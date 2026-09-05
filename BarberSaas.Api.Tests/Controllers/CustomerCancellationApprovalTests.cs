using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Covers Barber.RequireApprovalOnCustomerCancel: when on, a customer cancelling doesn't finalize
// the cancellation -- the slot freezes (still CONFIRMED, PendingCancellationApproval=true) and
// the owner gets a WhatsApp message to decide, instead of the appointment/waitlist being
// resolved immediately.
public class CustomerCancellationApprovalTests : IntegrationTestBase
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

    private async Task<string> GetCustomerToken(string phone, string name = "Jane", string familyName = "Doe") =>
        (await LoginCustomerViaWhatsAppAsync(phone, name, familyName)).Token;

    // Registers a barber, opens working hours for tomorrow, and (unless overridden) fully
    // configures Twilio + a personal phone + RequireApprovalOnCustomerCancel so the approval
    // path is actually reachable -- individual tests override pieces of this to hit fallbacks.
    private async Task<(string BarberId, string ServiceId, DateTime Date)> SeedApprovalBarber(
        string token, string slug, bool requireApproval = true, bool configureTwilio = true, bool setOwnerPhone = true)
    {
        Authorize(Client, token);
        var serviceResp = await Client.PostAsJsonAsync("/api/admin/services", new CreateServiceRequest("Cut", "Cut", "Cut", 30, 20m));
        var service = await serviceResp.Content.ReadFromJsonAsync<ServiceDto>();

        var date = DateTime.Now.Date.AddDays(1);
        await Client.PostAsJsonAsync("/api/admin/schedule",
            new List<WorkingHoursDto> { new(null, (int)date.DayOfWeek, "09:00", "18:00", true) });

        var settingsResp = await Client.PatchAsJsonAsync("/api/admin/settings", new UpdateSettingsRequest(
            null, setOwnerPhone ? "+15559990000" : null, null, null,
            null, null, WaitlistEnabled: false, RequireApprovalOnCustomerCancel: requireApproval));
        Assert.Equal(HttpStatusCode.OK, settingsResp.StatusCode);

        Client.DefaultRequestHeaders.Authorization = null;

        using var db = Db();
        var barber = db.Barbers.First(b => b.Slug == slug);
        // TwilioNumber is now platform-admin-assigned, not settable via /api/admin/settings.
        if (configureTwilio)
        {
            barber.TwilioNumber = "+15550009999";
            db.SaveChanges();
        }
        return (barber.Id, service!.Id, date);
    }

    private Task<HttpResponseMessage> BookAs(string customerToken, string slug, string serviceId, string date, string startTime)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/{slug}/appointments")
        {
            Content = JsonContent.Create(new BookAppointmentRequest(serviceId, date, startTime, "Customer", "", null)),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        return Client.SendAsync(req);
    }

    [Fact]
    public async Task CustomerCancel_WithApprovalRequired_FreezesSlotAndNotifiesOwner()
    {
        var token = await RegisterAndLoginBarber("approval-freeze@example.com", "approval-freeze-shop");
        var (barberId, serviceId, date) = await SeedApprovalBarber(token, "approval-freeze-shop");
        var dateStr = date.ToString("yyyy-MM-dd");

        var customerToken = await GetCustomerToken("+15551110001");
        var booked = await BookAs(customerToken, "approval-freeze-shop", serviceId, dateStr, "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/appointments/{appt!.AppointmentId}/cancel");
        cancelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        var customerCancel = await Client.SendAsync(cancelReq);
        Assert.Equal(HttpStatusCode.OK, customerCancel.StatusCode);

        // The owner was texted instead of the cancellation finalizing.
        var sent = Factory.WhatsAppSender.Sent.Where(s => s.BarberId == barberId).ToList();
        Assert.Single(sent);
        Assert.Equal("+15559990000", sent[0].Phone);

        using (var db = Db())
        {
            var stored = db.Appointments.First(a => a.Id == appt.AppointmentId);
            Assert.Equal(AppointmentStatus.CONFIRMED, stored.Status);
            Assert.True(stored.PendingCancellationApproval);
        }

        // The slot is still frozen -- nobody else can book over it while pending.
        var otherCustomerToken = await GetCustomerToken("+15551110002", "Someone", "Else");
        var conflicting = await BookAs(otherCustomerToken, "approval-freeze-shop", serviceId, dateStr, "09:00");
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);

        // The customer's own view already reads as cancelled, even though it's admin-CONFIRMED.
        Authorize(Client, customerToken);
        var mine = await Client.GetFromJsonAsync<List<CustomerAppointmentDto>>("/api/customer/appointments?filter=upcoming");
        Assert.Equal("CANCELLED", mine!.Single(a => a.Id == appt.AppointmentId).Status);

        // And a second cancel attempt is rejected, not double-processed.
        var secondCancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/appointments/{appt.AppointmentId}/cancel");
        secondCancelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        var secondCancel = await Client.SendAsync(secondCancelReq);
        Assert.Equal(HttpStatusCode.Conflict, secondCancel.StatusCode);
    }

    [Fact]
    public async Task OwnerFinalizesCancelSilently_AfterApprovalFreeze_FreesTheSlot()
    {
        var token = await RegisterAndLoginBarber("approval-finalize@example.com", "approval-finalize-shop");
        var (_, serviceId, date) = await SeedApprovalBarber(token, "approval-finalize-shop");
        var dateStr = date.ToString("yyyy-MM-dd");

        var customerToken = await GetCustomerToken("+15551110003");
        var booked = await BookAs(customerToken, "approval-finalize-shop", serviceId, dateStr, "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/appointments/{appt!.AppointmentId}/cancel");
        cancelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        await Client.SendAsync(cancelReq);

        Authorize(Client, token);
        var finalize = await Client.PatchAsJsonAsync($"/api/admin/appointments/{appt.AppointmentId}",
            new { status = "CANCELLED", notifyWaitlist = false });
        Assert.Equal(HttpStatusCode.OK, finalize.StatusCode);

        using (var db = Db())
        {
            var stored = db.Appointments.First(a => a.Id == appt.AppointmentId);
            Assert.Equal(AppointmentStatus.CANCELLED, stored.Status);
            Assert.False(stored.PendingCancellationApproval);
        }
        Client.DefaultRequestHeaders.Authorization = null;

        var otherCustomerToken = await GetCustomerToken("+15551110004", "New", "Booker");
        var rebooked = await BookAs(otherCustomerToken, "approval-finalize-shop", serviceId, dateStr, "09:00");
        Assert.Equal(HttpStatusCode.Created, rebooked.StatusCode);
    }

    [Fact]
    public async Task OwnerReplacesCustomer_AfterApprovalFreeze_KeepsSlotConfirmedForNewCustomer()
    {
        var token = await RegisterAndLoginBarber("approval-replace@example.com", "approval-replace-shop");
        var (_, serviceId, date) = await SeedApprovalBarber(token, "approval-replace-shop");
        var dateStr = date.ToString("yyyy-MM-dd");

        var customerToken = await GetCustomerToken("+15551110005");
        var booked = await BookAs(customerToken, "approval-replace-shop", serviceId, dateStr, "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/appointments/{appt!.AppointmentId}/cancel");
        cancelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        await Client.SendAsync(cancelReq);

        Authorize(Client, token);
        var replace = await Client.PatchAsJsonAsync($"/api/admin/appointments/{appt.AppointmentId}/customer",
            new { customerName = "Walk-in Replacement", customerPhone = "+15551110099" });
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);

        using var db = Db();
        var stored = db.Appointments.Include(a => a.Customer).First(a => a.Id == appt.AppointmentId);
        Assert.Equal(AppointmentStatus.CONFIRMED, stored.Status);
        Assert.False(stored.PendingCancellationApproval);
        Assert.Equal("Walk-in Replacement", stored.Customer.Name);
    }

    [Fact]
    public async Task CustomerCancel_ApprovalRequiredButTwilioNotConfigured_FallsBackToImmediateCancel()
    {
        var token = await RegisterAndLoginBarber("approval-notwilio@example.com", "approval-notwilio-shop");
        var (_, serviceId, date) = await SeedApprovalBarber(token, "approval-notwilio-shop", configureTwilio: false);
        var dateStr = date.ToString("yyyy-MM-dd");

        var customerToken = await GetCustomerToken("+15551110006");
        var booked = await BookAs(customerToken, "approval-notwilio-shop", serviceId, dateStr, "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/appointments/{appt!.AppointmentId}/cancel");
        cancelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        var cancelResp = await Client.SendAsync(cancelReq);
        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);

        using var db = Db();
        var stored = db.Appointments.First(a => a.Id == appt.AppointmentId);
        Assert.Equal(AppointmentStatus.CANCELLED, stored.Status);
        Assert.False(stored.PendingCancellationApproval);
    }

    [Fact]
    public async Task CustomerCancel_WithoutApprovalRequired_CancelsImmediatelyAsBefore()
    {
        var token = await RegisterAndLoginBarber("no-approval@example.com", "no-approval-shop");
        var (_, serviceId, date) = await SeedApprovalBarber(token, "no-approval-shop", requireApproval: false);
        var dateStr = date.ToString("yyyy-MM-dd");

        var customerToken = await GetCustomerToken("+15551110007");
        var booked = await BookAs(customerToken, "no-approval-shop", serviceId, dateStr, "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/appointments/{appt!.AppointmentId}/cancel");
        cancelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        var cancelResp = await Client.SendAsync(cancelReq);
        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);

        using var db = Db();
        var stored = db.Appointments.First(a => a.Id == appt.AppointmentId);
        Assert.Equal(AppointmentStatus.CANCELLED, stored.Status);
        Assert.False(stored.PendingCancellationApproval);
    }
}
