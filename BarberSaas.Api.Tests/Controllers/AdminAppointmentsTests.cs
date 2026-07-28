using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// The barber no longer manually marks appointments "Completed" — the system computes that
// automatically once an appointment's end time has passed (AppointmentStatusHelper).
public class AdminAppointmentsTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    private async Task<string> RegisterAndLoginBarber(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Barber", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var body = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    private async Task<(string BarberId, string AppointmentId)> SeedPastConfirmedAppointment(string barberToken, string slug)
    {
        Authorize(Client, barberToken);
        var serviceResp = await Client.PostAsJsonAsync("/api/admin/services", new CreateServiceRequest("Cut", "Cut", "Cut", 30, 20m));
        var service = await serviceResp.Content.ReadFromJsonAsync<ServiceDto>();
        Client.DefaultRequestHeaders.Authorization = null;

        using var db = Db();
        var barber = db.Barbers.First(b => b.Slug == slug);
        var customer = new Customer { BarberId = barber.Id, Name = "Past Customer", Phone = "+15559990001" };
        db.Customers.Add(customer);
        db.Appointments.Add(new Appointment
        {
            BarberId = barber.Id,
            CustomerId = customer.Id,
            ServiceId = service!.Id,
            Date = DateTime.UtcNow.Date.AddDays(-1),
            StartTime = "10:00",
            EndTime = "10:30",
            Status = AppointmentStatus.CONFIRMED,
        });
        db.SaveChanges();
        var appt = db.Appointments.First(a => a.BarberId == barber.Id);
        return (barber.Id, appt.Id);
    }

    [Fact]
    public async Task PastConfirmedAppointment_ShowsAsCompletedInAppointmentsList()
    {
        var slug = "past-appt-shop";
        var token = await RegisterAndLoginBarber("past-appt@example.com", slug);
        await SeedPastConfirmedAppointment(token, slug);

        Authorize(Client, token);
        var appointments = await Client.GetFromJsonAsync<List<DashboardAppointmentDto>>("/api/admin/appointments?filter=all");

        var appt = Assert.Single(appointments!);
        Assert.Equal("COMPLETED", appt.Status);
    }

    [Fact]
    public async Task PastConfirmedAppointment_ShowsAsCompletedOnDashboard()
    {
        var slug = "past-dash-shop";
        var token = await RegisterAndLoginBarber("past-dash@example.com", slug);
        await SeedPastConfirmedAppointment(token, slug);

        Authorize(Client, token);
        // "Yesterday" is in week=0's Sun-Sat window unless today is Sunday, in which case it
        // falls in week=-1 instead — check both rather than assume which one.
        var thisWeek = await Client.GetFromJsonAsync<List<DashboardAppointmentDto>>("/api/admin/dashboard?week=0");
        var lastWeek = await Client.GetFromJsonAsync<List<DashboardAppointmentDto>>("/api/admin/dashboard?week=-1");

        Assert.Contains(thisWeek!.Concat(lastWeek!), a => a.Status == "COMPLETED");
    }

    [Fact]
    public async Task UpdateStatus_RejectsAnythingOtherThanCancelled()
    {
        var slug = "reject-complete-shop";
        var token = await RegisterAndLoginBarber("reject-complete@example.com", slug);
        var (_, appointmentId) = await SeedPastConfirmedAppointment(token, slug);

        Authorize(Client, token);
        var resp = await Client.PatchAsJsonAsync($"/api/admin/appointments/{appointmentId}", new { status = "COMPLETED" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_StillAllowsCancelling()
    {
        var slug = "allow-cancel-shop";
        var token = await RegisterAndLoginBarber("allow-cancel@example.com", slug);
        var (_, appointmentId) = await SeedPastConfirmedAppointment(token, slug);

        Authorize(Client, token);
        var resp = await Client.PatchAsJsonAsync($"/api/admin/appointments/{appointmentId}", new { status = "CANCELLED" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ─── Manual (owner-created) appointment booking ────────────────────────

    private async Task<(string BarberId, string ServiceId, DateTime Date)> SeedBarberWithServiceAndAvailability(string token, string slug)
    {
        Authorize(Client, token);
        var serviceResp = await Client.PostAsJsonAsync("/api/admin/services", new CreateServiceRequest("Cut", "Cut", "Cut", 30, 20m));
        var service = await serviceResp.Content.ReadFromJsonAsync<ServiceDto>();

        // Tomorrow, not today -- avoids the "isToday" cutoff in AvailabilityService rejecting
        // slots that fall before the current wall-clock time depending on when the test runs.
        var date = DateTime.Now.Date.AddDays(1);
        await Client.PostAsJsonAsync("/api/admin/schedule",
            new List<WorkingHoursDto> { new(null, (int)date.DayOfWeek, "09:00", "18:00", true) });

        using var db = Db();
        var barber = db.Barbers.First(b => b.Slug == slug);
        return (barber.Id, service!.Id, date);
    }

    [Fact]
    public async Task CreateAppointment_ExistingCustomer_Succeeds()
    {
        var slug = "admin-book-existing";
        var token = await RegisterAndLoginBarber("admin-book-existing@example.com", slug);
        var (barberId, serviceId, date) = await SeedBarberWithServiceAndAvailability(token, slug);

        string customerId;
        using (var db = Db())
        {
            var customer = new Customer { BarberId = barberId, Name = "Mohamed", Phone = "+15550001111" };
            db.Customers.Add(customer);
            db.SaveChanges();
            customerId = customer.Id;
        }

        Authorize(Client, token);
        var resp = await Client.PostAsJsonAsync("/api/admin/appointments", new CreateAdminAppointmentRequest(
            customerId, null, null, serviceId, date.ToString("yyyy-MM-dd"), "09:00", null));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<DashboardAppointmentDto>();
        Assert.Equal("Mohamed", dto!.Customer.Name);
    }

    [Fact]
    public async Task CreateAppointment_NewCustomer_UpsertsByPhone()
    {
        var slug = "admin-book-new";
        var token = await RegisterAndLoginBarber("admin-book-new@example.com", slug);
        var (barberId, serviceId, date) = await SeedBarberWithServiceAndAvailability(token, slug);

        Authorize(Client, token);
        var resp = await Client.PostAsJsonAsync("/api/admin/appointments", new CreateAdminAppointmentRequest(
            null, "Sara", "+15559998888", serviceId, date.ToString("yyyy-MM-dd"), "09:00", null));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        using var db = Db();
        Assert.Single(db.Customers.Where(c => c.BarberId == barberId && c.Phone == "+15559998888"));
    }

    [Fact]
    public async Task CreateAppointment_ConflictingSlotWithoutForce_ReturnsConflict()
    {
        var slug = "admin-book-conflict";
        var token = await RegisterAndLoginBarber("admin-book-conflict@example.com", slug);
        var (_, serviceId, date) = await SeedBarberWithServiceAndAvailability(token, slug);
        var dateStr = date.ToString("yyyy-MM-dd");

        Authorize(Client, token);
        await Client.PostAsJsonAsync("/api/admin/appointments", new CreateAdminAppointmentRequest(null, "A", "+15551110001", serviceId, dateStr, "09:00", null));
        var resp = await Client.PostAsJsonAsync("/api/admin/appointments", new CreateAdminAppointmentRequest(null, "B", "+15551110002", serviceId, dateStr, "09:00", null));

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task CreateAppointment_ForceTrue_OverridesUnavailableSlot()
    {
        var slug = "admin-book-force";
        var token = await RegisterAndLoginBarber("admin-book-force@example.com", slug);
        var (_, serviceId, date) = await SeedBarberWithServiceAndAvailability(token, slug);

        Authorize(Client, token);
        // 19:00 falls outside the 09:00-18:00 working hours seeded above, so a normal
        // (non-forced) booking at this time would be rejected as unavailable.
        var resp = await Client.PostAsJsonAsync("/api/admin/appointments", new CreateAdminAppointmentRequest(
            null, "Walkin", "+15551234567", serviceId, date.ToString("yyyy-MM-dd"), "19:00", null, Force: true));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task CreateAppointment_ForceTrue_StillRejectsExactOverlap()
    {
        var slug = "admin-book-force-overlap";
        var token = await RegisterAndLoginBarber("admin-book-force-overlap@example.com", slug);
        var (_, serviceId, date) = await SeedBarberWithServiceAndAvailability(token, slug);
        var dateStr = date.ToString("yyyy-MM-dd");

        Authorize(Client, token);
        await Client.PostAsJsonAsync("/api/admin/appointments", new CreateAdminAppointmentRequest(null, "A", "+15551110003", serviceId, dateStr, "09:00", null));
        var resp = await Client.PostAsJsonAsync("/api/admin/appointments", new CreateAdminAppointmentRequest(
            null, "B", "+15551110004", serviceId, dateStr, "09:00", null, Force: true));

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task CreateAppointment_DoesNotEnforcePerCustomerBookingLimits()
    {
        var slug = "admin-book-limits";
        var token = await RegisterAndLoginBarber("admin-book-limits@example.com", slug);
        var (_, serviceId, date) = await SeedBarberWithServiceAndAvailability(token, slug);
        var dateStr = date.ToString("yyyy-MM-dd");

        Authorize(Client, token);
        await Client.PatchAsJsonAsync("/api/admin/settings", new { maxBookingsPerDay = 1 });

        const string phone = "+15557778888";
        var first = await Client.PostAsJsonAsync("/api/admin/appointments", new CreateAdminAppointmentRequest(null, "Same Customer", phone, serviceId, dateStr, "09:00", null));
        var second = await Client.PostAsJsonAsync("/api/admin/appointments", new CreateAdminAppointmentRequest(null, "Same Customer", phone, serviceId, dateStr, "10:00", null));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }
}
