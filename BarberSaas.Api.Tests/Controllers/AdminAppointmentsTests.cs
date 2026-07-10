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
}
