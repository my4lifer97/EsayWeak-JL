using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Covers Business.RequireApprovalOnCustomerCancel: when on, a customer cancelling doesn't finalize
// the cancellation -- the slot freezes (still CONFIRMED, PendingCancellationApproval=true) and
// the owner gets a WhatsApp message to decide, instead of the appointment/waitlist being
// resolved immediately.
public class CustomerCancellationApprovalTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    private async Task<string> RegisterAndLoginBusiness(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var body = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    private async Task<string> GetCustomerToken(string phone, string name = "Jane", string familyName = "Doe") =>
        (await LoginCustomerViaWhatsAppAsync(phone, name, familyName)).Token;

    // Registers a business, opens working hours for tomorrow, and (unless overridden) fully
    // configures Twilio + a personal phone + RequireApprovalOnCustomerCancel so the approval
    // path is actually reachable -- individual tests override pieces of this to hit fallbacks.
    private async Task<(string BusinessId, string ItemId, DateTime Date)> SeedApprovalBusiness(
        string token, string slug, bool requireApproval = true, bool configureTwilio = true, bool setOwnerPhone = true)
    {
        Authorize(Client, token);
        var serviceResp = await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Cut", "Cut", "Cut", 30, 20m));
        var service = await serviceResp.Content.ReadFromJsonAsync<ItemDto>();

        var date = DateTime.Now.Date.AddDays(1);
        await Client.PostAsJsonAsync("/api/admin/schedule",
            new List<WorkingHoursDto> { new(null, (int)date.DayOfWeek, "09:00", "18:00", true) });

        var settingsResp = await Client.PatchAsJsonAsync("/api/admin/settings", new UpdateSettingsRequest(
            null, setOwnerPhone ? "+15559990000" : null, null, null,
            null, null, WaitlistEnabled: false, RequireApprovalOnCustomerCancel: requireApproval));
        Assert.Equal(HttpStatusCode.OK, settingsResp.StatusCode);

        Client.DefaultRequestHeaders.Authorization = null;

        using var db = Db();
        var business = db.Businesses.First(b => b.Slug == slug);
        // WhatsAppNumber is now assigned by the platform-admin link flow, not settable via /api/admin/settings.
        if (configureTwilio)
        {
            business.WhatsAppNumber = "+15550009999";
            db.SaveChanges();
        }
        return (business.Id, service!.Id, date);
    }

    private Task<HttpResponseMessage> BookAs(string customerToken, string slug, string itemId, string date, string startTime)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/{slug}/appointments")
        {
            Content = JsonContent.Create(new BookAppointmentRequest(itemId, date, startTime, "Customer", "", null)),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        return Client.SendAsync(req);
    }

    // QA run 3: parallel cancel/keep/replace requests all passed their checks on their own
    // snapshot. A cancel working from a row someone else changed since must do nothing.
    [Fact]
    public async Task Cancel_FromAStaleSnapshot_DoesNothing()
    {
        var token = await RegisterAndLoginBusiness("approval-stale@example.com", "approval-stale-shop");
        var (_, itemId, date) = await SeedApprovalBusiness(token, "approval-stale-shop");
        var customerToken = await GetCustomerToken("+15558880101");
        var booked = await (await BookAs(customerToken, "approval-stale-shop", itemId, date.ToString("yyyy-MM-dd"), "10:00"))
            .Content.ReadFromJsonAsync<BookAppointmentResponse>();

        using var scope = Factory.Services.CreateScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<BarberSaas.Api.Data.AppDbContext>();
        var cancellation = scope.ServiceProvider.GetRequiredService<BarberSaas.Api.Services.AppointmentCancellationService>();
        var stale = await scopedDb.Appointments.FirstAsync(a => a.Id == booked!.AppointmentId);

        // Another request (the customer asking to cancel) gets there first.
        using (var other = Db())
        {
            other.Appointments.First(a => a.Id == booked!.AppointmentId).PendingCancellationApproval = true;
            other.SaveChanges();
        }

        Assert.False(await cancellation.CancelAsync(stale, notifyWaitlist: true));
        Assert.False(await cancellation.CancelFromCustomerAsync(stale));
        using var check = Db();
        var row = check.Appointments.First(a => a.Id == booked!.AppointmentId);
        Assert.Equal(AppointmentStatus.CONFIRMED, row.Status);
        Assert.True(row.PendingCancellationApproval);
    }

    [Fact]
    public async Task CustomerCancel_WithApprovalRequired_FreezesSlotAndNotifiesOwner()
    {
        var token = await RegisterAndLoginBusiness("approval-freeze@example.com", "approval-freeze-shop");
        var (businessId, itemId, date) = await SeedApprovalBusiness(token, "approval-freeze-shop");
        var dateStr = date.ToString("yyyy-MM-dd");

        var customerToken = await GetCustomerToken("+15551110001");
        var booked = await BookAs(customerToken, "approval-freeze-shop", itemId, dateStr, "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/appointments/{appt!.AppointmentId}/cancel");
        cancelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        var customerCancel = await Client.SendAsync(cancelReq);
        Assert.Equal(HttpStatusCode.OK, customerCancel.StatusCode);

        // The owner was texted instead of the cancellation finalizing. Filtered to the owner's own
        // phone -- booking the appointment in the first place also sent the customer their own
        // "you're booked" confirmation (business.WhatsAppNumber is set for this test), which isn't
        // what this assertion is about.
        var sent = Factory.WhatsAppSender.Sent.Where(s => s.BusinessId == businessId && s.Phone == "+15559990000").ToList();
        Assert.Single(sent);

        using (var db = Db())
        {
            var stored = db.Appointments.First(a => a.Id == appt.AppointmentId);
            Assert.Equal(AppointmentStatus.CONFIRMED, stored.Status);
            Assert.True(stored.PendingCancellationApproval);
        }

        // The slot is still frozen -- nobody else can book over it while pending.
        var otherCustomerToken = await GetCustomerToken("+15551110002", "Someone", "Else");
        var conflicting = await BookAs(otherCustomerToken, "approval-freeze-shop", itemId, dateStr, "09:00");
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);

        // The customer's own view reads as "cancellation requested": not confirmed, and not
        // cancelled yet either, since the owner may still keep it.
        Authorize(Client, customerToken);
        var mine = await Client.GetFromJsonAsync<List<CustomerAppointmentDto>>("/api/customer/appointments?filter=upcoming");
        Assert.Equal("CANCELLATION_PENDING", mine!.Single(a => a.Id == appt.AppointmentId).Status);

        // And a second cancel attempt is rejected, not double-processed.
        var secondCancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/appointments/{appt.AppointmentId}/cancel");
        secondCancelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        var secondCancel = await Client.SendAsync(secondCancelReq);
        Assert.Equal(HttpStatusCode.Conflict, secondCancel.StatusCode);
    }

    [Fact]
    public async Task CustomerCancel_WithApprovalRequired_OwnerNotifyFails_StillFreezesSlotInsteadOf500()
    {
        var token = await RegisterAndLoginBusiness("approval-notify-fails@example.com", "approval-notify-fails-shop");
        var (businessId, itemId, date) = await SeedApprovalBusiness(token, "approval-notify-fails-shop");
        var dateStr = date.ToString("yyyy-MM-dd");

        var customerToken = await GetCustomerToken("+15551110020");
        var booked = await BookAs(customerToken, "approval-notify-fails-shop", itemId, dateStr, "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        // Simulates a WhatsApp bridge outage / expired session at the moment the owner would be
        // notified -- the booking confirmation above still went through fine (that send happened
        // before this was flipped on).
        Factory.WhatsAppSender.ShouldFail = true;

        var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/appointments/{appt!.AppointmentId}/cancel");
        cancelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        var customerCancel = await Client.SendAsync(cancelReq);

        // The send failing must not turn the customer's cancel request into an unhandled 500 with
        // nothing persisted -- the appointment still needs to freeze exactly as it would if the
        // notification had succeeded.
        Assert.Equal(HttpStatusCode.OK, customerCancel.StatusCode);

        using var db = Db();
        var stored = db.Appointments.First(a => a.Id == appt.AppointmentId);
        Assert.Equal(AppointmentStatus.CONFIRMED, stored.Status);
        Assert.True(stored.PendingCancellationApproval);
    }

    [Fact]
    public async Task OwnerFinalizesCancelSilently_AfterApprovalFreeze_FreesTheSlot()
    {
        var token = await RegisterAndLoginBusiness("approval-finalize@example.com", "approval-finalize-shop");
        var (_, itemId, date) = await SeedApprovalBusiness(token, "approval-finalize-shop");
        var dateStr = date.ToString("yyyy-MM-dd");

        var customerToken = await GetCustomerToken("+15551110003");
        var booked = await BookAs(customerToken, "approval-finalize-shop", itemId, dateStr, "09:00");
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
        var rebooked = await BookAs(otherCustomerToken, "approval-finalize-shop", itemId, dateStr, "09:00");
        Assert.Equal(HttpStatusCode.Created, rebooked.StatusCode);
    }

    [Fact]
    public async Task OwnerReplacesCustomer_AfterApprovalFreeze_KeepsSlotConfirmedForNewCustomer()
    {
        var token = await RegisterAndLoginBusiness("approval-replace@example.com", "approval-replace-shop");
        var (_, itemId, date) = await SeedApprovalBusiness(token, "approval-replace-shop");
        var dateStr = date.ToString("yyyy-MM-dd");

        var customerToken = await GetCustomerToken("+15551110005");
        var booked = await BookAs(customerToken, "approval-replace-shop", itemId, dateStr, "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/appointments/{appt!.AppointmentId}/cancel");
        cancelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        await Client.SendAsync(cancelReq);

        Authorize(Client, token);
        var replace = await Client.PatchAsJsonAsync($"/api/admin/appointments/{appt.AppointmentId}/customer",
            new { customerName = "Walk-in Replacement", customerPhone = "+15551110099" });
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        var newId = (await replace.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetString();
        Assert.NotEqual(appt.AppointmentId, newId);

        using (var db = Db())
        {
            // The slot is confirmed for the new customer on a fresh row...
            var replacement = db.Appointments.Include(a => a.Customer).First(a => a.Id == newId);
            Assert.Equal(AppointmentStatus.CONFIRMED, replacement.Status);
            Assert.Equal("Walk-in Replacement", replacement.Customer.Name);
            // ...and the original row stays with the original customer, cancelled.
            var original = db.Appointments.Include(a => a.Customer).First(a => a.Id == appt.AppointmentId);
            Assert.Equal(AppointmentStatus.CANCELLED, original.Status);
            Assert.False(original.PendingCancellationApproval);
            Assert.NotEqual("Walk-in Replacement", original.Customer.Name);
        }

        // QA run 2: it vanished from the original customer's list, and their magic link showed
        // the new customer's details.
        Client.DefaultRequestHeaders.Authorization = null;
        Authorize(Client, customerToken);
        var mine = await Client.GetFromJsonAsync<List<CustomerAppointmentDto>>("/api/customer/appointments");
        Assert.Equal("CANCELLED", Assert.Single(mine!).Status);
        Client.DefaultRequestHeaders.Authorization = null;
        var viaLink = await Client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/approval-replace-shop/appointments/{appt.AppointmentId}?token={appt.CancelToken}");
        Assert.Equal("CANCELLED", viaLink.GetProperty("status").GetString());
        Assert.DoesNotContain("Walk-in", viaLink.ToString());
    }

    [Fact]
    public async Task OwnerKeepsAppointment_AfterApprovalFreeze_CustomerSeesItConfirmedAgain()
    {
        var token = await RegisterAndLoginBusiness("approval-keep@example.com", "approval-keep-shop");
        var (_, itemId, date) = await SeedApprovalBusiness(token, "approval-keep-shop");

        var customerToken = await GetCustomerToken("+15551110006");
        var booked = await BookAs(customerToken, "approval-keep-shop", itemId, date.ToString("yyyy-MM-dd"), "09:00");
        var appt = (await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>())!;
        var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/appointments/{appt.AppointmentId}/cancel");
        cancelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        await Client.SendAsync(cancelReq);

        Authorize(Client, token);
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync($"/api/admin/appointments/{appt.AppointmentId}/keep", null)).StatusCode);
        // Nothing left to decline the second time.
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsync($"/api/admin/appointments/{appt.AppointmentId}/keep", null)).StatusCode);

        Client.DefaultRequestHeaders.Authorization = null;
        Authorize(Client, customerToken);
        var mine = await Client.GetFromJsonAsync<List<CustomerAppointmentDto>>("/api/customer/appointments");
        Assert.Equal("CONFIRMED", Assert.Single(mine!).Status);
    }

    private async Task SetApprovalChannels(string token, bool viaWhatsApp, string? whatsAppNumber, bool viaEmail, string? email, string? language = null)
    {
        Authorize(Client, token);
        var resp = await Client.PatchAsJsonAsync("/api/admin/settings", new
        {
            language,
            cancelApprovalNotifyViaWhatsApp = viaWhatsApp,
            cancelApprovalWhatsAppNumber = whatsAppNumber,
            cancelApprovalNotifyViaEmail = viaEmail,
            cancelApprovalEmail = email,
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Client.DefaultRequestHeaders.Authorization = null;
    }

    private async Task<(string AppointmentId, HttpStatusCode Status)> BookAndCustomerCancel(string slug, string itemId, DateTime date, string phone)
    {
        var customerToken = await GetCustomerToken(phone);
        var booked = await BookAs(customerToken, slug, itemId, date.ToString("yyyy-MM-dd"), "09:00");
        var appt = (await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>())!;
        var cancelReq = new HttpRequestMessage(HttpMethod.Post, $"/api/customer/appointments/{appt.AppointmentId}/cancel");
        cancelReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        return (appt.AppointmentId, (await Client.SendAsync(cancelReq)).StatusCode);
    }

    // Both channels on, each with its own contact -- the owner is asked on both, in the language
    // they picked in Settings (Hebrew here).
    [Fact]
    public async Task CustomerCancel_ApprovalViaWhatsAppAndEmail_AsksOwnerOnBothInTheirLanguage()
    {
        var token = await RegisterAndLoginBusiness("approval-both@example.com", "approval-both-shop");
        var (businessId, itemId, date) = await SeedApprovalBusiness(token, "approval-both-shop");
        await SetApprovalChannels(token, true, "+15559990077", true, "boss@example.com", language: "HE");

        var (appointmentId, status) = await BookAndCustomerCancel("approval-both-shop", itemId, date, "+15551110101");
        Assert.Equal(HttpStatusCode.OK, status);

        var whatsApp = Factory.WhatsAppSender.Sent.Single(s => s.BusinessId == businessId && s.Phone == "+15559990077");
        Assert.Contains("ביטל את התור", whatsApp.Message);
        var email = Factory.OwnerEmail.Sent.Single(e => e.Email == "boss@example.com");
        Assert.Contains("ביטול ממתין להחלטתך", email.Subject);
        Assert.Contains("ביטל את התור", email.Body);
        // Not also to the business phone -- the dedicated number replaces it.
        Assert.DoesNotContain(Factory.WhatsAppSender.Sent, s => s.BusinessId == businessId && s.Phone == "+15559990000");

        using var db = Db();
        Assert.True(db.Appointments.Single(a => a.Id == appointmentId).PendingCancellationApproval);
    }

    // Email only works even with no WhatsApp bot number linked at all; a blank address falls back
    // to the account email.
    [Fact]
    public async Task CustomerCancel_ApprovalViaEmailOnly_FreezesWithoutWhatsApp()
    {
        var token = await RegisterAndLoginBusiness("approval-email@example.com", "approval-email-shop");
        var (businessId, itemId, date) = await SeedApprovalBusiness(token, "approval-email-shop", configureTwilio: false);
        await SetApprovalChannels(token, false, null, true, null);

        var (appointmentId, status) = await BookAndCustomerCancel("approval-email-shop", itemId, date, "+15551110102");
        Assert.Equal(HttpStatusCode.OK, status);

        Assert.Single(Factory.OwnerEmail.Sent, e => e.Email == "approval-email@example.com" && e.Subject.Contains("waiting for your decision"));
        Assert.DoesNotContain(Factory.WhatsAppSender.Sent, s => s.BusinessId == businessId && s.Phone == "+15559990000");

        using var db = Db();
        var stored = db.Appointments.Single(a => a.Id == appointmentId);
        Assert.Equal(AppointmentStatus.CONFIRMED, stored.Status);
        Assert.True(stored.PendingCancellationApproval);
    }

    [Fact]
    public async Task CustomerCancel_ApprovalWithBothChannelsOff_CancelsImmediately()
    {
        var token = await RegisterAndLoginBusiness("approval-none@example.com", "approval-none-shop");
        var (_, itemId, date) = await SeedApprovalBusiness(token, "approval-none-shop");
        await SetApprovalChannels(token, false, null, false, null);

        var (appointmentId, status) = await BookAndCustomerCancel("approval-none-shop", itemId, date, "+15551110103");
        Assert.Equal(HttpStatusCode.OK, status);

        using var db = Db();
        var stored = db.Appointments.Single(a => a.Id == appointmentId);
        Assert.Equal(AppointmentStatus.CANCELLED, stored.Status);
        Assert.False(stored.PendingCancellationApproval);
    }

    [Fact]
    public async Task CustomerCancel_ApprovalRequiredButTwilioNotConfigured_FallsBackToImmediateCancel()
    {
        var token = await RegisterAndLoginBusiness("approval-notwilio@example.com", "approval-notwilio-shop");
        var (_, itemId, date) = await SeedApprovalBusiness(token, "approval-notwilio-shop", configureTwilio: false);
        var dateStr = date.ToString("yyyy-MM-dd");

        var customerToken = await GetCustomerToken("+15551110006");
        var booked = await BookAs(customerToken, "approval-notwilio-shop", itemId, dateStr, "09:00");
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
        var token = await RegisterAndLoginBusiness("no-approval@example.com", "no-approval-shop");
        var (_, itemId, date) = await SeedApprovalBusiness(token, "no-approval-shop", requireApproval: false);
        var dateStr = date.ToString("yyyy-MM-dd");

        var customerToken = await GetCustomerToken("+15551110007");
        var booked = await BookAs(customerToken, "no-approval-shop", itemId, dateStr, "09:00");
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
