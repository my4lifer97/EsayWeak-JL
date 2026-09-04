using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

public class WaitlistTests : IntegrationTestBase
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

    private async Task<string> GetCustomerToken(string phone, string name = "Waiting", string familyName = "Customer") =>
        (await LoginCustomerViaWhatsAppAsync(phone, name, familyName)).Token;

    // Registers a barber, opens working hours for tomorrow (avoids the "isToday" cutoff in
    // AvailabilityService), and turns on the waitlist with dummy Twilio creds so WaitlistService
    // actually attempts a send (captured by the test factory's FakeWhatsAppSender).
    private async Task<(string BarberId, string ServiceId, DateTime Date)> SeedWaitlistEnabledBarber(string token, string slug, bool waitlistEnabled = true)
    {
        Authorize(Client, token);
        var serviceResp = await Client.PostAsJsonAsync("/api/admin/services", new CreateServiceRequest("Cut", "Cut", "Cut", 30, 20m));
        var service = await serviceResp.Content.ReadFromJsonAsync<ServiceDto>();

        var date = DateTime.Now.Date.AddDays(1);
        await Client.PostAsJsonAsync("/api/admin/schedule",
            new List<WorkingHoursDto> { new(null, (int)date.DayOfWeek, "09:00", "18:00", true) });

        var settingsResp = await Client.PatchAsJsonAsync("/api/admin/settings", new UpdateSettingsRequest(
            null, null, null, null, "+15550009999", "AC_test_sid", "test_auth_token", null, null, WaitlistEnabled: waitlistEnabled));
        Assert.Equal(HttpStatusCode.OK, settingsResp.StatusCode);

        Client.DefaultRequestHeaders.Authorization = null;

        using var db = Db();
        var barber = db.Barbers.First(b => b.Slug == slug);
        return (barber.Id, service!.Id, date);
    }

    private Task<HttpResponseMessage> BookAs(string customerToken, string slug, string serviceId, string date, string startTime, string customerName = "Customer")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/{slug}/appointments")
        {
            Content = JsonContent.Create(new BookAppointmentRequest(serviceId, date, startTime, customerName, "", null)),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        return Client.SendAsync(req);
    }

    private Task<HttpResponseMessage> JoinWaitlistAs(string customerToken, string slug, string appointmentId)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/{slug}/waitlist/{appointmentId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        return Client.SendAsync(req);
    }

    [Fact]
    public async Task Join_NoAuth_ReturnsUnauthorized()
    {
        var token = await RegisterAndLoginBarber("wl-noauth@example.com", "wl-noauth-shop");
        var (_, serviceId, date) = await SeedWaitlistEnabledBarber(token, "wl-noauth-shop");
        var customerToken = await GetCustomerToken("+15551000001");
        var booked = await BookAs(customerToken, "wl-noauth-shop", serviceId, date.ToString("yyyy-MM-dd"), "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var resp = await Client.PostAsync($"/api/wl-noauth-shop/waitlist/{appt!.AppointmentId}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Join_WhenWaitlistDisabled_ReturnsBadRequest()
    {
        var token = await RegisterAndLoginBarber("wl-disabled@example.com", "wl-disabled-shop");
        var (_, serviceId, date) = await SeedWaitlistEnabledBarber(token, "wl-disabled-shop", waitlistEnabled: false);
        var bookerToken = await GetCustomerToken("+15551000002");
        var booked = await BookAs(bookerToken, "wl-disabled-shop", serviceId, date.ToString("yyyy-MM-dd"), "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var waiterToken = await GetCustomerToken("+15551000003");
        var resp = await JoinWaitlistAs(waiterToken, "wl-disabled-shop", appt!.AppointmentId);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Join_NonExistentAppointment_ReturnsNotFound()
    {
        var token = await RegisterAndLoginBarber("wl-404@example.com", "wl-404-shop");
        await SeedWaitlistEnabledBarber(token, "wl-404-shop");
        var customerToken = await GetCustomerToken("+15551000004");

        var resp = await JoinWaitlistAs(customerToken, "wl-404-shop", "does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Join_CrossTenant_ReturnsNotFound()
    {
        var tokenA = await RegisterAndLoginBarber("wl-tenant-a@example.com", "wl-tenant-a-shop");
        var (_, serviceIdA, dateA) = await SeedWaitlistEnabledBarber(tokenA, "wl-tenant-a-shop");
        var tokenB = await RegisterAndLoginBarber("wl-tenant-b@example.com", "wl-tenant-b-shop");
        await SeedWaitlistEnabledBarber(tokenB, "wl-tenant-b-shop");

        var bookerToken = await GetCustomerToken("+15551000005");
        var booked = await BookAs(bookerToken, "wl-tenant-a-shop", serviceIdA, dateA.ToString("yyyy-MM-dd"), "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var waiterToken = await GetCustomerToken("+15551000006");
        // Appointment belongs to tenant A, but we try to join through tenant B's slug.
        var resp = await JoinWaitlistAs(waiterToken, "wl-tenant-b-shop", appt!.AppointmentId);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Join_Duplicate_IsIdempotent()
    {
        var token = await RegisterAndLoginBarber("wl-dup@example.com", "wl-dup-shop");
        var (barberId, serviceId, date) = await SeedWaitlistEnabledBarber(token, "wl-dup-shop");
        var bookerToken = await GetCustomerToken("+15551000007");
        var booked = await BookAs(bookerToken, "wl-dup-shop", serviceId, date.ToString("yyyy-MM-dd"), "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var waiterToken = await GetCustomerToken("+15551000008");
        var first = await JoinWaitlistAs(waiterToken, "wl-dup-shop", appt!.AppointmentId);
        var second = await JoinWaitlistAs(waiterToken, "wl-dup-shop", appt.AppointmentId);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var db = Db();
        Assert.Single(db.WaitlistEntries.Where(w => w.AppointmentId == appt.AppointmentId));
    }

    [Fact]
    public async Task OwnerOffersToWaitlist_NotifiesWaitersAndResolvesOnRebook()
    {
        var token = await RegisterAndLoginBarber("wl-lifecycle@example.com", "wl-lifecycle-shop");
        var (barberId, serviceId, date) = await SeedWaitlistEnabledBarber(token, "wl-lifecycle-shop");
        var dateStr = date.ToString("yyyy-MM-dd");

        var bookerToken = await GetCustomerToken("+15551000009", "Original", "Booker");
        var booked = await BookAs(bookerToken, "wl-lifecycle-shop", serviceId, dateStr, "09:00", "Original Booker");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var waiter1Phone = "+15551000010";
        var waiter2Phone = "+15551000011";
        var waiter1Token = await GetCustomerToken(waiter1Phone, "First", "Waiter");
        var waiter2Token = await GetCustomerToken(waiter2Phone, "Second", "Waiter");
        Assert.Equal(HttpStatusCode.OK, (await JoinWaitlistAs(waiter1Token, "wl-lifecycle-shop", appt!.AppointmentId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await JoinWaitlistAs(waiter2Token, "wl-lifecycle-shop", appt.AppointmentId)).StatusCode);

        Authorize(Client, token);
        var cancelResp = await Client.PatchAsJsonAsync($"/api/admin/appointments/{appt.AppointmentId}",
            new { status = "CANCELLED", notifyWaitlist = true });
        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);
        Client.DefaultRequestHeaders.Authorization = null;

        var sent = Factory.WhatsAppSender.Sent.Where(s => s.BarberId == barberId).ToList();
        Assert.Equal(2, sent.Count);
        Assert.Contains(sent, s => s.Phone == waiter1Phone);
        Assert.Contains(sent, s => s.Phone == waiter2Phone);

        using (var db = Db())
        {
            var entries = db.WaitlistEntries.Where(w => w.AppointmentId == appt.AppointmentId).ToList();
            Assert.Equal(2, entries.Count);
            Assert.All(entries, e => Assert.Equal(WaitlistEntryStatus.NOTIFIED, e.Status));
        }

        var rebook = await BookAs(waiter1Token, "wl-lifecycle-shop", serviceId, dateStr, "09:00", "First Waiter");
        Assert.Equal(HttpStatusCode.Created, rebook.StatusCode);

        using (var db = Db())
        {
            var entries = db.WaitlistEntries.Where(w => w.AppointmentId == appt.AppointmentId).ToList();
            Assert.All(entries, e => Assert.Equal(WaitlistEntryStatus.RESOLVED, e.Status));
        }
    }

    [Fact]
    public async Task OwnerCancelsSilently_SendsNoNotification()
    {
        var token = await RegisterAndLoginBarber("wl-silent@example.com", "wl-silent-shop");
        var (barberId, serviceId, date) = await SeedWaitlistEnabledBarber(token, "wl-silent-shop");
        var dateStr = date.ToString("yyyy-MM-dd");

        var bookerToken = await GetCustomerToken("+15551000012");
        var booked = await BookAs(bookerToken, "wl-silent-shop", serviceId, dateStr, "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var waiterToken = await GetCustomerToken("+15551000013");
        await JoinWaitlistAs(waiterToken, "wl-silent-shop", appt!.AppointmentId);

        Authorize(Client, token);
        var cancelResp = await Client.PatchAsJsonAsync($"/api/admin/appointments/{appt.AppointmentId}",
            new { status = "CANCELLED", notifyWaitlist = false });
        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);

        Assert.DoesNotContain(Factory.WhatsAppSender.Sent, s => s.BarberId == barberId);
        using var db = Db();
        Assert.All(db.WaitlistEntries.Where(w => w.AppointmentId == appt.AppointmentId), e => Assert.Equal(WaitlistEntryStatus.WAITING, e.Status));
    }

    [Fact]
    public async Task ConcurrentBooking_ExactlyOneSucceedsForTheFreedSlot()
    {
        var token = await RegisterAndLoginBarber("wl-race@example.com", "wl-race-shop");
        var (_, serviceId, date) = await SeedWaitlistEnabledBarber(token, "wl-race-shop");
        var dateStr = date.ToString("yyyy-MM-dd");

        var bookerToken = await GetCustomerToken("+15551000014");
        var booked = await BookAs(bookerToken, "wl-race-shop", serviceId, dateStr, "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var cancel = await Client.DeleteAsync($"/api/wl-race-shop/appointments/{appt!.AppointmentId}?token={appt.CancelToken}");
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var raceToken1 = await GetCustomerToken("+15551000015");
        var raceToken2 = await GetCustomerToken("+15551000016");

        // Two concurrent requests for the exact same now-freed slot -- the partial unique index
        // on (BarberId, Date, StartTime) for CONFIRMED appointments must let exactly one win.
        // We only assert that invariant (no double-booking survives), not the loser's exact
        // HTTP status: this test harness's SQLite in-memory DB is a single shared connection
        // (TestWebApplicationFactory), so a truly concurrent second transaction can fail at
        // BeginTransaction itself with a raw SqliteException -- surfacing as 500, not the 409
        // our DbUpdateException catch produces -- before EF even gets a chance to wrap it. Real
        // Postgres gives each request its own pooled connection, so there the second writer's
        // unique-violation is reliably caught and turned into a 409 (see AvailabilityService.
        // TrySaveOrDetectConflict). What matters here, and what SQLite still proves, is that the
        // index itself prevents two CONFIRMED rows from ever coexisting for this slot.
        var results = await Task.WhenAll(
            BookAs(raceToken1, "wl-race-shop", serviceId, dateStr, "09:00"),
            BookAs(raceToken2, "wl-race-shop", serviceId, dateStr, "09:00"));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);

        using var db = Db();
        var confirmedAtSlot = db.Appointments.Where(a =>
            a.ServiceId == serviceId && a.StartTime == "09:00" && a.Status == AppointmentStatus.CONFIRMED);
        Assert.Single(confirmedAtSlot);
    }
}
