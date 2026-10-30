using BarberSaas.Api.Services;
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

    private async Task<string> RegisterAndLoginBusiness(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var body = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    private async Task<string> GetCustomerToken(string phone, string name = "Waiting", string familyName = "Customer") =>
        (await LoginCustomerViaWhatsAppAsync(phone, name, familyName)).Token;

    // Registers a business, opens working hours for tomorrow (avoids the "isToday" cutoff in
    // AvailabilityService), and turns on the waitlist with dummy Twilio creds so WaitlistService
    // actually attempts a send (captured by the test factory's FakeWhatsAppSender).
    private async Task<(string BusinessId, string ItemId, DateTime Date)> SeedWaitlistEnabledBusiness(string token, string slug, bool waitlistEnabled = true)
    {
        Authorize(Client, token);
        var serviceResp = await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Cut", "Cut", "Cut", 30, 20m));
        var service = await serviceResp.Content.ReadFromJsonAsync<ItemDto>();

        var date = AppClock.Now.Date.AddDays(1);
        await Client.PostAsJsonAsync("/api/admin/schedule",
            new List<WorkingHoursDto> { new(null, (int)date.DayOfWeek, "09:00", "18:00", true) });

        var settingsResp = await Client.PatchAsJsonAsync("/api/admin/settings", new UpdateSettingsRequest(
            null, null, null, null, null, null, WaitlistEnabled: waitlistEnabled));
        Assert.Equal(HttpStatusCode.OK, settingsResp.StatusCode);

        Client.DefaultRequestHeaders.Authorization = null;

        // WhatsAppNumber is now assigned by the platform-admin link flow, not settable via /api/admin/settings.
        using var db = Db();
        var business = db.Businesses.First(b => b.Slug == slug);
        business.WhatsAppNumber = "+15550009999";
        db.SaveChanges();
        return (business.Id, service!.Id, date);
    }

    private Task<HttpResponseMessage> BookAs(string customerToken, string slug, string itemId, string date, string startTime, string customerName = "Customer")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/{slug}/appointments")
        {
            Content = JsonContent.Create(new BookAppointmentRequest(itemId, date, startTime, customerName, "", null)),
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

    private Task<HttpResponseMessage> JoinWaitlistAt(string customerToken, string slug, string appointmentId, string date, string startTime, string? itemId)
    {
        var url = $"/api/{slug}/waitlist/{appointmentId}?date={date}&startTime={startTime}" + (itemId is null ? "" : $"&itemId={itemId}");
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        return Client.SendAsync(req);
    }

    // QA run 6 ST-1: a 60-minute booking at 09:00 also shows as the booked 09:30 slot, but joining
    // from there was "This time has changed".
    [Theory]
    [InlineData("09:30", true, HttpStatusCode.OK)]
    [InlineData("09:30", false, HttpStatusCode.OK)] // a page without itemId: inside the appointment
    [InlineData("10:30", true, HttpStatusCode.Conflict)] // past its end
    public async Task Join_FromAnySlotTheAppointmentCovers(string startTime, bool sendItemId, HttpStatusCode expected)
    {
        var slug = $"wl-cover-{startTime.Replace(":", "")}-{sendItemId}".ToLowerInvariant();
        var token = await RegisterAndLoginBusiness($"{slug}@example.com", slug);
        var (_, cutId, date) = await SeedWaitlistEnabledBusiness(token, slug);
        Authorize(Client, token);
        var longItem = await (await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Long", "Long", "Long", 60, 50m)))
            .Content.ReadFromJsonAsync<ItemDto>();
        Client.DefaultRequestHeaders.Authorization = null;
        var day = date.ToString("yyyy-MM-dd");
        var booker = await GetCustomerToken($"+1555700{startTime.Replace(":", "")}{(sendItemId ? 1 : 2)}");
        var appt = await (await BookAs(booker, slug, longItem!.Id, day, "09:00")).Content.ReadFromJsonAsync<BookAppointmentResponse>();
        var waiter = await GetCustomerToken($"+1555701{startTime.Replace(":", "")}{(sendItemId ? 1 : 2)}");

        var resp = await JoinWaitlistAt(waiter, slug, appt!.AppointmentId, day, startTime, sendItemId ? cutId : null);

        Assert.Equal(expected, resp.StatusCode);
    }

    // A 30-minute slot at 09:00 is booked by an appointment that starts at 09:15 (a gap time).
    [Fact]
    public async Task Join_FromASlotThatStartsBeforeTheAppointment_WithTheItem()
    {
        var token = await RegisterAndLoginBusiness("wl-gap@example.com", "wl-gap-shop");
        var (_, cutId, date) = await SeedWaitlistEnabledBusiness(token, "wl-gap-shop");
        var day = date.ToString("yyyy-MM-dd");
        string appointmentId;
        using (var db = Db())
        {
            var business = db.Businesses.First(b => b.Slug == "wl-gap-shop");
            var customer = new Customer { BusinessId = business.Id, Name = "Gap", Phone = "+15557020001" };
            var appointment = new Appointment
            {
                BusinessId = business.Id, CustomerId = customer.Id, ItemId = cutId, Date = DateTime.Parse(day + "T00:00:00Z").ToUniversalTime(),
                StartTime = "09:15", EndTime = "09:30", Status = AppointmentStatus.CONFIRMED,
            };
            db.AddRange(customer, appointment);
            db.SaveChanges();
            appointmentId = appointment.Id;
        }
        var waiter = await GetCustomerToken("+15557020002");

        var resp = await JoinWaitlistAt(waiter, "wl-gap-shop", appointmentId, day, "09:00", cutId);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Join_NoAuth_ReturnsUnauthorized()
    {
        var token = await RegisterAndLoginBusiness("wl-noauth@example.com", "wl-noauth-shop");
        var (_, itemId, date) = await SeedWaitlistEnabledBusiness(token, "wl-noauth-shop");
        var customerToken = await GetCustomerToken("+15551000001");
        var booked = await BookAs(customerToken, "wl-noauth-shop", itemId, date.ToString("yyyy-MM-dd"), "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var resp = await Client.PostAsync($"/api/wl-noauth-shop/waitlist/{appt!.AppointmentId}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Join_WhenWaitlistDisabled_ReturnsBadRequest()
    {
        var token = await RegisterAndLoginBusiness("wl-disabled@example.com", "wl-disabled-shop");
        var (_, itemId, date) = await SeedWaitlistEnabledBusiness(token, "wl-disabled-shop", waitlistEnabled: false);
        var bookerToken = await GetCustomerToken("+15551000002");
        var booked = await BookAs(bookerToken, "wl-disabled-shop", itemId, date.ToString("yyyy-MM-dd"), "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var waiterToken = await GetCustomerToken("+15551000003");
        var resp = await JoinWaitlistAs(waiterToken, "wl-disabled-shop", appt!.AppointmentId);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Join_NonExistentAppointment_ReturnsNotFound()
    {
        var token = await RegisterAndLoginBusiness("wl-404@example.com", "wl-404-shop");
        await SeedWaitlistEnabledBusiness(token, "wl-404-shop");
        var customerToken = await GetCustomerToken("+15551000004");

        var resp = await JoinWaitlistAs(customerToken, "wl-404-shop", "does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Join_CrossTenant_ReturnsNotFound()
    {
        var tokenA = await RegisterAndLoginBusiness("wl-tenant-a@example.com", "wl-tenant-a-shop");
        var (_, itemIdA, dateA) = await SeedWaitlistEnabledBusiness(tokenA, "wl-tenant-a-shop");
        var tokenB = await RegisterAndLoginBusiness("wl-tenant-b@example.com", "wl-tenant-b-shop");
        await SeedWaitlistEnabledBusiness(tokenB, "wl-tenant-b-shop");

        var bookerToken = await GetCustomerToken("+15551000005");
        var booked = await BookAs(bookerToken, "wl-tenant-a-shop", itemIdA, dateA.ToString("yyyy-MM-dd"), "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var waiterToken = await GetCustomerToken("+15551000006");
        // Appointment belongs to tenant A, but we try to join through tenant B's slug.
        var resp = await JoinWaitlistAs(waiterToken, "wl-tenant-b-shop", appt!.AppointmentId);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Join_Duplicate_IsIdempotent()
    {
        var token = await RegisterAndLoginBusiness("wl-dup@example.com", "wl-dup-shop");
        var (businessId, itemId, date) = await SeedWaitlistEnabledBusiness(token, "wl-dup-shop");
        var bookerToken = await GetCustomerToken("+15551000007");
        var booked = await BookAs(bookerToken, "wl-dup-shop", itemId, date.ToString("yyyy-MM-dd"), "09:00");
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
        var token = await RegisterAndLoginBusiness("wl-lifecycle@example.com", "wl-lifecycle-shop");
        var (businessId, itemId, date) = await SeedWaitlistEnabledBusiness(token, "wl-lifecycle-shop");
        var dateStr = date.ToString("yyyy-MM-dd");

        var bookerToken = await GetCustomerToken("+15551000009", "Original", "Booker");
        var booked = await BookAs(bookerToken, "wl-lifecycle-shop", itemId, dateStr, "09:00", "Original Booker");
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

        // Queue order: only the first to join is messaged right away -- the second waits their turn.
        // Filtered to the two waiters specifically -- booking the original appointment above also
        // sent its own "you're booked" confirmation to the booker's phone.
        var sent = Factory.WhatsAppSender.Sent.Where(s => s.BusinessId == businessId && (s.Phone == waiter1Phone || s.Phone == waiter2Phone)).ToList();
        Assert.Single(sent);
        Assert.Equal(waiter1Phone, sent[0].Phone);

        var rebook = await BookAs(waiter1Token, "wl-lifecycle-shop", itemId, dateStr, "09:00", "First Waiter");
        Assert.Equal(HttpStatusCode.Created, rebook.StatusCode);

        using (var db = Db())
        {
            var entries = db.WaitlistEntries.Where(w => w.AppointmentId == appt.AppointmentId).ToList();
            Assert.All(entries, e => Assert.Equal(WaitlistEntryStatus.RESOLVED, e.Status));
        }
    }

    private async Task<HttpResponseMessage> AdvanceQueues()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/cron/retry-waitlist-notifications");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestWebApplicationFactory.CronSecret);
        return await Client.SendAsync(req);
    }

    // Pretends the queue's last notification went out `minutes` ago, instead of the test waiting.
    private void BackdateNotifications(string appointmentId, int minutes)
    {
        using var db = Db();
        foreach (var e in db.WaitlistEntries.Where(w => w.AppointmentId == appointmentId && w.NotifiedAt != null))
            e.NotifiedAt = e.NotifiedAt!.Value.AddMinutes(-minutes);
        db.SaveChanges();
    }

    // The real-life scenario: every slot of the day is booked, three customers join the
    // waitlist for the 12:00 appointment in order, and its customer cancels. The first to
    // join gets the message immediately, the next one only after 5 minutes pass with the slot
    // still free, and once someone rebooks nobody else is messaged.
    [Fact]
    public async Task FullyBookedDay_CustomerCancels_WaitlistIsMessagedOneAtATimeInJoinOrder()
    {
        var token = await RegisterAndLoginBusiness("wl-queue@example.com", "wl-queue-shop");
        var (businessId, itemId, date) = await SeedWaitlistEnabledBusiness(token, "wl-queue-shop");
        var dateStr = date.ToString("yyyy-MM-dd");

        // Book the whole day 09:00-18:00 (18 half-hour slots), one customer per slot.
        var appointmentsByTime = new Dictionary<string, BookAppointmentResponse>();
        for (var t = TimeSpan.FromHours(9); t < TimeSpan.FromHours(18); t += TimeSpan.FromMinutes(30))
        {
            var time = t.ToString(@"hh\:mm");
            var bookerToken = await GetCustomerToken($"+1555200{(int)t.TotalMinutes:0000}", "Booker", time);
            var resp = await BookAs(bookerToken, "wl-queue-shop", itemId, dateStr, time, $"Booker {time}");
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            appointmentsByTime[time] = (await resp.Content.ReadFromJsonAsync<BookAppointmentResponse>())!;
        }
        var availability = await Client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/wl-queue-shop/availability?itemId={itemId}&date={dateStr}");
        Assert.Equal(0, availability.GetProperty("slots").GetArrayLength());

        var target = appointmentsByTime["12:00"];
        string[] waiterPhones = ["+15553000001", "+15553000002", "+15553000003"];
        var waiterTokens = new List<string>();
        foreach (var (phone, i) in waiterPhones.Select((p, i) => (p, i)))
        {
            var waiterToken = await GetCustomerToken(phone, $"Waiter{i + 1}", "Queue");
            waiterTokens.Add(waiterToken);
            Assert.Equal(HttpStatusCode.OK, (await JoinWaitlistAs(waiterToken, "wl-queue-shop", target.AppointmentId)).StatusCode);
            await Task.Delay(20); // distinct CreatedAt per join
        }

        // Only the "slot opened up" messages -- rebooking below also sends that waiter a normal
        // booking confirmation.
        List<string> WaiterMessages() => Factory.WhatsAppSender.Sent
            .Where(s => s.BusinessId == businessId && waiterPhones.Contains(s.Phone) && s.Message.Contains("just opened up"))
            .Select(s => s.Phone).ToList();

        // The 12:00 customer cancels via their magic link.
        var cancel = await Client.DeleteAsync($"/api/wl-queue-shop/appointments/{target.AppointmentId}?token={target.CancelToken}");
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal([waiterPhones[0]], WaiterMessages());

        // Before 5 minutes pass, the queue doesn't move.
        BackdateNotifications(target.AppointmentId, 4);
        Assert.Equal(HttpStatusCode.OK, (await AdvanceQueues()).StatusCode);
        Assert.Equal([waiterPhones[0]], WaiterMessages());

        // 5 minutes later and the slot is still free -> the second in line is messaged.
        BackdateNotifications(target.AppointmentId, 1);
        await AdvanceQueues();
        Assert.Equal(waiterPhones[0..2], WaiterMessages().Order());

        // The second waiter books it -> the third is never messaged, and every entry is closed.
        var rebook = await BookAs(waiterTokens[1], "wl-queue-shop", itemId, dateStr, "12:00", "Waiter2 Queue");
        Assert.Equal(HttpStatusCode.Created, rebook.StatusCode);
        BackdateNotifications(target.AppointmentId, 10);
        await AdvanceQueues();
        Assert.Equal(2, WaiterMessages().Count);
        Assert.DoesNotContain(waiterPhones[2], WaiterMessages());

        using var db = Db();
        Assert.All(db.WaitlistEntries.Where(w => w.AppointmentId == target.AppointmentId),
            e => Assert.Equal(WaitlistEntryStatus.RESOLVED, e.Status));
    }

    // A freed 09:00-10:00 slot with two waiters, the first already messaged. Returns what the
    // overlap tests below need.
    private async Task<(string BusinessId, string ItemId, string DateStr, string FreedId, string[] WaiterPhones)> FreeAnHourWithTwoWaiters(string slug)
    {
        var token = await RegisterAndLoginBusiness($"{slug}@example.com", slug);
        var (businessId, shortItemId, date) = await SeedWaitlistEnabledBusiness(token, slug);
        Authorize(Client, token);
        var longItem = await (await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Long", "Long", "Long", 60, 40m)))
            .Content.ReadFromJsonAsync<ItemDto>();
        Client.DefaultRequestHeaders.Authorization = null;
        var dateStr = date.ToString("yyyy-MM-dd");

        var booked = await BookAs(await GetCustomerToken("+15554000001", "Hour", "Booker"), slug, longItem!.Id, dateStr, "09:00", "Hour Booker");
        var appt = (await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>())!;

        string[] waiterPhones = ["+15554000002", "+15554000003"];
        foreach (var phone in waiterPhones)
        {
            Assert.Equal(HttpStatusCode.OK, (await JoinWaitlistAs(await GetCustomerToken(phone, "Waiter", "Overlap"), slug, appt.AppointmentId)).StatusCode);
            await Task.Delay(20); // distinct CreatedAt per join
        }

        var cancel = await Client.DeleteAsync($"/api/{slug}/appointments/{appt.AppointmentId}?token={appt.CancelToken}");
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        return (businessId, shortItemId, dateStr, appt.AppointmentId, waiterPhones);
    }

    private List<string> SlotOpenMessages(string businessId, string[] phones) => Factory.WhatsAppSender.Sent
        .Where(s => s.BusinessId == businessId && phones.Contains(s.Phone) && s.Message.Contains("just opened up"))
        .Select(s => s.Phone).ToList();

    // QA run 2: a booking that overlaps the freed slot without starting at exactly its time
    // (09:30 inside 09:00-10:00) left the queue running, and the next waiter's link said "not available".
    [Fact]
    public async Task OverlappingRebook_EndsTheQueue()
    {
        var (businessId, shortItemId, dateStr, freedId, waiterPhones) = await FreeAnHourWithTwoWaiters("wl-overlap-shop");
        Assert.Equal([waiterPhones[0]], SlotOpenMessages(businessId, waiterPhones));

        var overlap = await BookAs(await GetCustomerToken("+15554000004", "Other", "Customer"), "wl-overlap-shop", shortItemId, dateStr, "09:30", "Other Customer");
        Assert.Equal(HttpStatusCode.Created, overlap.StatusCode);

        BackdateNotifications(freedId, 10);
        await AdvanceQueues();
        Assert.Equal([waiterPhones[0]], SlotOpenMessages(businessId, waiterPhones));
        using var db = Db();
        Assert.All(db.WaitlistEntries.Where(w => w.AppointmentId == freedId), e => Assert.Equal(WaitlistEntryStatus.RESOLVED, e.Status));
    }

    // Paths that never call ResolveForRebooking (recurring generation, say) can fill the slot
    // too -- the queue checks the slot is still free before each message.
    [Fact]
    public async Task SlotFilledWithoutResolve_NextWaiterIsNotMessaged()
    {
        var (businessId, shortItemId, dateStr, freedId, waiterPhones) = await FreeAnHourWithTwoWaiters("wl-filled-shop");

        using (var db = Db())
        {
            var freed = db.Appointments.First(a => a.Id == freedId);
            db.Appointments.Add(new Appointment
            {
                BusinessId = businessId, CustomerId = freed.CustomerId, ItemId = shortItemId,
                Date = freed.Date, StartTime = "09:30", EndTime = "10:00", Status = AppointmentStatus.CONFIRMED,
            });
            db.SaveChanges();
        }

        BackdateNotifications(freedId, 10);
        await AdvanceQueues();
        Assert.Equal([waiterPhones[0]], SlotOpenMessages(businessId, waiterPhones));
        using var db2 = Db();
        Assert.All(db2.WaitlistEntries.Where(w => w.AppointmentId == freedId && w.Status != WaitlistEntryStatus.NOTIFIED),
            e => Assert.Equal(WaitlistEntryStatus.RESOLVED, e.Status));
    }

    private Task<HttpResponseMessage> SendWhatsAppAs(string businessId, string fromPhone, string message)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/bridge/inbound")
        {
            Content = JsonContent.Create(new WhatsAppController.BridgeInboundRequest(businessId, fromPhone, null, message)),
        };
        req.Headers.Add("X-Bridge-Secret", TestWebApplicationFactory.BridgeSecret);
        return Client.SendAsync(req);
    }

    // Each waiter gets the slot-opened message in the language the chatbot last replied to them
    // in (a bare "1" keeps the conversation's language); one who never wrote gets the business's
    // language (English here).
    [Fact]
    public async Task WaitlistMessage_UsesLanguageOfCustomersLastWhatsAppMessage()
    {
        var token = await RegisterAndLoginBusiness("wl-lang@example.com", "wl-lang-shop");
        var (businessId, itemId, date) = await SeedWaitlistEnabledBusiness(token, "wl-lang-shop");
        var dateStr = date.ToString("yyyy-MM-dd");

        var bookerToken = await GetCustomerToken("+15554000001");
        var booked = await BookAs(bookerToken, "wl-lang-shop", itemId, dateStr, "09:00");
        var appt = (await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>())!;

        string arabicPhone = "+15554000002", hebrewPhone = "+15554000003", silentPhone = "+15554000004";
        foreach (var phone in new[] { arabicPhone, hebrewPhone, silentPhone })
        {
            await JoinWaitlistAs(await GetCustomerToken(phone), "wl-lang-shop", appt.AppointmentId);
            await Task.Delay(20);
        }

        await SendWhatsAppAs(businessId, hebrewPhone, "hello");
        await SendWhatsAppAs(businessId, arabicPhone, "مرحبا");
        await SendWhatsAppAs(businessId, arabicPhone, "1");
        await SendWhatsAppAs(businessId, hebrewPhone, "שלום");

        var cancel = await Client.DeleteAsync($"/api/wl-lang-shop/appointments/{appt.AppointmentId}?token={appt.CancelToken}");
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        BackdateNotifications(appt.AppointmentId, 5);
        await AdvanceQueues();
        BackdateNotifications(appt.AppointmentId, 5);
        await AdvanceQueues();

        string SlotMessageTo(string phone) => Factory.WhatsAppSender.Sent
            .Single(m => m.BusinessId == businessId && m.Phone == phone && m.Message.Contains("/wl-lang-shop/book?")).Message;

        Assert.Contains("أصبح هناك موعد متاح", SlotMessageTo(arabicPhone));
        Assert.Contains("התפנה תור", SlotMessageTo(hebrewPhone));
        Assert.Contains("just opened up", SlotMessageTo(silentPhone));
    }

    [Fact]
    public async Task Join_OwnAppointment_ReturnsBadRequest()
    {
        var token = await RegisterAndLoginBusiness("wl-own@example.com", "wl-own-shop");
        var (_, itemId, date) = await SeedWaitlistEnabledBusiness(token, "wl-own-shop");
        var customerToken = await GetCustomerToken("+15555000001");
        var booked = await BookAs(customerToken, "wl-own-shop", itemId, date.ToString("yyyy-MM-dd"), "09:00");
        var appt = (await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>())!;

        var resp = await JoinWaitlistAs(customerToken, "wl-own-shop", appt.AppointmentId);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var db = Db();
        Assert.Empty(db.WaitlistEntries.Where(w => w.AppointmentId == appt.AppointmentId));
    }

    // Moving an appointment to another time frees the old slot, so its waitlist is messaged just
    // like on a cancel -- while the moved appointment keeps its Id (the customer's links still work).
    // QA run 4 (ST-13): only the queue worker skipped a slot that had already started, so a cancel
    // during the appointment still sent the first waiter a link to a time no longer offered.
    [Fact]
    public async Task CancelAfterStart_DoesNotOfferTheSlot()
    {
        var token = await RegisterAndLoginBusiness("wl-started@example.com", "wl-started-shop");
        var (businessId, itemId, date) = await SeedWaitlistEnabledBusiness(token, "wl-started-shop");
        var booked = await BookAs(await GetCustomerToken("+15555100001"), "wl-started-shop", itemId, date.ToString("yyyy-MM-dd"), "10:00");
        var appt = (await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>())!;
        var waiterPhone = "+15555100002";
        Assert.Equal(HttpStatusCode.OK, (await JoinWaitlistAs(await GetCustomerToken(waiterPhone), "wl-started-shop", appt.AppointmentId)).StatusCode);

        // Under way now: started at midnight, ends at the end of the day.
        using (var db = Db())
        {
            var row = db.Appointments.Single(a => a.Id == appt.AppointmentId);
            row.Date = DateTime.SpecifyKind(AppClock.Now.Date, DateTimeKind.Utc);
            row.StartTime = "00:00";
            row.EndTime = "23:59";
            db.SaveChanges();
        }

        var cancel = await Client.DeleteAsync($"/api/wl-started-shop/appointments/{appt.AppointmentId}?token={appt.CancelToken}");

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.DoesNotContain(Factory.WhatsAppSender.Sent, m => m.BusinessId == businessId && m.Phone == waiterPhone);
        using var check = Db();
        Assert.Equal(WaitlistEntryStatus.RESOLVED, check.WaitlistEntries.Single(w => w.AppointmentId == appt.AppointmentId).Status);
    }

    // QA run 4 (ST-14): a join from a page loaded before the appointment moved queued the customer
    // for its new time while the slot they saw stood free.
    [Fact]
    public async Task Join_SlotMovedSincePageLoaded_ReturnsConflict()
    {
        var token = await RegisterAndLoginBusiness("wl-stale@example.com", "wl-stale-shop");
        var (_, itemId, date) = await SeedWaitlistEnabledBusiness(token, "wl-stale-shop");
        var dateStr = date.ToString("yyyy-MM-dd");
        var bookerToken = await GetCustomerToken("+15555100003");
        var appt = (await (await BookAs(bookerToken, "wl-stale-shop", itemId, dateStr, "10:00")).Content.ReadFromJsonAsync<BookAppointmentResponse>())!;

        var move = new HttpRequestMessage(HttpMethod.Patch, $"/api/customer/appointments/{appt.AppointmentId}/reschedule")
        {
            Content = JsonContent.Create(new { date = dateStr, startTime = "15:00" }),
        };
        move.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bookerToken);
        Assert.Equal(HttpStatusCode.OK, (await Client.SendAsync(move)).StatusCode);

        var waiterToken = await GetCustomerToken("+15555100004");
        HttpRequestMessage Join(string time)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/wl-stale-shop/waitlist/{appt.AppointmentId}?date={dateStr}&startTime={time}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", waiterToken);
            return req;
        }

        var stale = await Client.SendAsync(Join("10:00"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.True((await stale.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("slotChanged").GetBoolean());
        using (var db = Db()) Assert.Empty(db.WaitlistEntries.Where(w => w.AppointmentId == appt.AppointmentId));

        Assert.Equal(HttpStatusCode.OK, (await Client.SendAsync(Join("15:00"))).StatusCode);
    }

    [Fact]
    public async Task CustomerReschedules_WaitlistForOldSlotIsNotified()
    {
        var token = await RegisterAndLoginBusiness("wl-resched@example.com", "wl-resched-shop");
        var (businessId, itemId, date) = await SeedWaitlistEnabledBusiness(token, "wl-resched-shop");
        var dateStr = date.ToString("yyyy-MM-dd");

        var bookerToken = await GetCustomerToken("+15555000002");
        var booked = await BookAs(bookerToken, "wl-resched-shop", itemId, dateStr, "10:00");
        var appt = (await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>())!;

        var waiterPhone = "+15555000003";
        var waiterToken = await GetCustomerToken(waiterPhone);
        Assert.Equal(HttpStatusCode.OK, (await JoinWaitlistAs(waiterToken, "wl-resched-shop", appt.AppointmentId)).StatusCode);

        var req = new HttpRequestMessage(HttpMethod.Patch, $"/api/customer/appointments/{appt.AppointmentId}/reschedule")
        {
            Content = JsonContent.Create(new { date = dateStr, startTime = "15:00" }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bookerToken);
        Assert.Equal(HttpStatusCode.OK, (await Client.SendAsync(req)).StatusCode);

        var slotMessages = Factory.WhatsAppSender.Sent
            .Where(m => m.BusinessId == businessId && m.Phone == waiterPhone && m.Message.Contains("time=10:00")).ToList();
        Assert.Single(slotMessages);

        using var db = Db();
        var moved = db.Appointments.Single(a => a.Id == appt.AppointmentId);
        Assert.Equal("15:00", moved.StartTime);
        Assert.Equal(AppointmentStatus.CONFIRMED, moved.Status);

        var entry = db.WaitlistEntries.Single(w => w.CustomerAccount.Phone == waiterPhone);
        Assert.Equal(WaitlistEntryStatus.NOTIFIED, entry.Status);
        var oldSlot = db.Appointments.Single(a => a.Id == entry.AppointmentId);
        Assert.Equal(AppointmentStatus.CANCELLED, oldSlot.Status);
        Assert.Equal("10:00", oldSlot.StartTime);
        Assert.True(oldSlot.IsWaitlistCarrier);

        // QA run 2: that copy showed up in the customer's own list as a phantom "Cancelled" booking.
        var mineReq = new HttpRequestMessage(HttpMethod.Get, "/api/customer/appointments");
        mineReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bookerToken);
        var mine = await (await Client.SendAsync(mineReq)).Content.ReadFromJsonAsync<List<System.Text.Json.JsonElement>>();
        Assert.Equal([appt.AppointmentId], mine!.Select(a => a.GetProperty("id").GetString()));

        // 10:00 is bookable again.
        var rebook = await BookAs(waiterToken, "wl-resched-shop", itemId, dateStr, "10:00");
        Assert.Equal(HttpStatusCode.Created, rebook.StatusCode);
    }

    [Fact]
    public async Task OwnerCancelsWithoutOfferingToWaitlist_MessagesNoWaiter()
    {
        var token = await RegisterAndLoginBusiness("wl-silent@example.com", "wl-silent-shop");
        var (businessId, itemId, date) = await SeedWaitlistEnabledBusiness(token, "wl-silent-shop");
        var dateStr = date.ToString("yyyy-MM-dd");

        var bookerToken = await GetCustomerToken("+15551000012");
        var booked = await BookAs(bookerToken, "wl-silent-shop", itemId, dateStr, "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var waiterToken = await GetCustomerToken("+15551000013");
        await JoinWaitlistAs(waiterToken, "wl-silent-shop", appt!.AppointmentId);

        Authorize(Client, token);
        var cancelResp = await Client.PatchAsJsonAsync($"/api/admin/appointments/{appt.AppointmentId}",
            new { status = "CANCELLED", notifyWaitlist = false });
        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);

        // The booker is told their appointment was cancelled (OwnerActionCustomerNoticeTests);
        // nobody on the waitlist hears anything -- not later from the queue worker either, which
        // used to pick the still-WAITING entry up within one poll (QA run 3).
        Client.DefaultRequestHeaders.Authorization = null;
        await AdvanceQueues();
        Assert.DoesNotContain(Factory.WhatsAppSender.Sent, s => s.Phone == "+15551000013");
        using var db = Db();
        Assert.All(db.WaitlistEntries.Where(w => w.AppointmentId == appt.AppointmentId), e => Assert.Equal(WaitlistEntryStatus.RESOLVED, e.Status));
    }

    [Fact]
    public async Task RetryEndpoint_NotificationInitiallyFailed_SendsOnRetryAndFlipsToNotified()
    {
        var token = await RegisterAndLoginBusiness("wl-retry@example.com", "wl-retry-shop");
        var (businessId, itemId, date) = await SeedWaitlistEnabledBusiness(token, "wl-retry-shop");
        var dateStr = date.ToString("yyyy-MM-dd");

        var bookerToken = await GetCustomerToken("+15551000020");
        var booked = await BookAs(bookerToken, "wl-retry-shop", itemId, dateStr, "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var waiterPhone = "+15551000021";
        var waiterToken = await GetCustomerToken(waiterPhone);
        await JoinWaitlistAs(waiterToken, "wl-retry-shop", appt!.AppointmentId);

        // Simulates a bridge outage at the moment the owner cancels -- the notification attempt
        // fails, leaving the entry WAITING with nothing else retrying it on its own.
        Factory.WhatsAppSender.ShouldFail = true;
        Authorize(Client, token);
        await Client.PatchAsJsonAsync($"/api/admin/appointments/{appt.AppointmentId}", new { status = "CANCELLED", notifyWaitlist = true });
        Client.DefaultRequestHeaders.Authorization = null;

        using (var db = Db())
        {
            var entry = db.WaitlistEntries.Single(w => w.AppointmentId == appt.AppointmentId);
            Assert.Equal(WaitlistEntryStatus.WAITING, entry.Status);
        }

        // The outage clears; the periodic retry endpoint should now succeed for that same entry.
        Factory.WhatsAppSender.ShouldFail = false;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestWebApplicationFactory.CronSecret);
        var retryResp = await Client.GetAsync("/api/cron/retry-waitlist-notifications");

        Assert.Equal(HttpStatusCode.OK, retryResp.StatusCode);
        Assert.Contains(Factory.WhatsAppSender.Sent, s => s.BusinessId == businessId && s.Phone == waiterPhone);

        using (var db = Db())
        {
            var entry = db.WaitlistEntries.Single(w => w.AppointmentId == appt.AppointmentId);
            Assert.Equal(WaitlistEntryStatus.NOTIFIED, entry.Status);
        }
    }

    // QA run 7 ST-1: two overlapping retry runs both sent the same offer. The second run starts
    // while the first is still sending (SQLite can't run them truly in parallel).
    [Fact]
    public async Task RetryEndpoint_OverlappingRuns_SendTheOfferOnce()
    {
        var token = await RegisterAndLoginBusiness("wl-retry-twice@example.com", "wl-retry-twice");
        var (businessId, itemId, date) = await SeedWaitlistEnabledBusiness(token, "wl-retry-twice");
        var bookerToken = await GetCustomerToken("+15551000030");
        var booked = await BookAs(bookerToken, "wl-retry-twice", itemId, date.ToString("yyyy-MM-dd"), "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();
        var waiterPhone = "+15551000031";
        await JoinWaitlistAs(await GetCustomerToken(waiterPhone), "wl-retry-twice", appt!.AppointmentId);
        Factory.WhatsAppSender.ShouldFail = true;
        Authorize(Client, token);
        await Client.PatchAsJsonAsync($"/api/admin/appointments/{appt.AppointmentId}", new { status = "CANCELLED", notifyWaitlist = true });
        Client.DefaultRequestHeaders.Authorization = null;
        Factory.WhatsAppSender.ShouldFail = false;

        using var second = Factory.CreateClient();
        second.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestWebApplicationFactory.CronSecret);
        Factory.WhatsAppSender.OnSend = async () =>
        {
            Factory.WhatsAppSender.OnSend = null;
            await second.GetAsync("/api/cron/retry-waitlist-notifications");
        };
        try
        {
            await AdvanceQueues();
        }
        finally
        {
            Factory.WhatsAppSender.OnSend = null;
        }

        Assert.Single(Factory.WhatsAppSender.Sent, s => s.BusinessId == businessId && s.Phone == waiterPhone);
    }

    [Fact]
    public async Task RetryEndpoint_NoAuth_ReturnsUnauthorized()
    {
        var resp = await Client.GetAsync("/api/cron/retry-waitlist-notifications");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ConcurrentBooking_ExactlyOneSucceedsForTheFreedSlot()
    {
        var token = await RegisterAndLoginBusiness("wl-race@example.com", "wl-race-shop");
        var (_, itemId, date) = await SeedWaitlistEnabledBusiness(token, "wl-race-shop");
        var dateStr = date.ToString("yyyy-MM-dd");

        var bookerToken = await GetCustomerToken("+15551000014");
        var booked = await BookAs(bookerToken, "wl-race-shop", itemId, dateStr, "09:00");
        var appt = await booked.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var cancel = await Client.DeleteAsync($"/api/wl-race-shop/appointments/{appt!.AppointmentId}?token={appt.CancelToken}");
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var raceToken1 = await GetCustomerToken("+15551000015");
        var raceToken2 = await GetCustomerToken("+15551000016");

        // Two concurrent requests for the exact same now-freed slot -- the partial unique index
        // on (BusinessId, Date, StartTime) for CONFIRMED appointments must let exactly one win.
        // We only assert that invariant (no double-booking survives), not the loser's exact
        // HTTP status: this test harness's SQLite in-memory DB is a single shared connection
        // (TestWebApplicationFactory), so a truly concurrent second transaction can fail at
        // BeginTransaction itself with a raw SqliteException -- surfacing as 500, not the 409
        // our DbUpdateException catch produces -- before EF even gets a chance to wrap it. Real
        // Postgres gives each request its own pooled connection, so there the second writer's
        // unique-violation is reliably caught and turned into a 409 (see AvailabilityService.
        // TrySaveOrDetectConflict). What matters here, and what SQLite still proves, is that the
        // index itself prevents two CONFIRMED rows from ever coexisting for this slot.
        // The losing request can occasionally blow up mid-response-stream instead of cleanly
        // returning a status code -- e.g. ActivityLogFilter's own post-response SaveChangesAsync
        // hitting the shared connection's pending transaction from the other concurrent request
        // (a test-harness-only artifact of the single shared SQLite connection, per the comment
        // above; Postgres doesn't share connections this way). That crashes the HttpClient call
        // with an HttpRequestException rather than yielding a response the test could inspect --
        // tolerated here as "this one didn't win", since the real invariant under test is the DB
        // state asserted below, not which HTTP status the loser happened to surface.
        async Task<HttpStatusCode> SafeBook(string customerToken)
        {
            try
            {
                var r = await BookAs(customerToken, "wl-race-shop", itemId, dateStr, "09:00");
                return r.StatusCode;
            }
            catch (HttpRequestException)
            {
                return HttpStatusCode.InternalServerError;
            }
        }

        var results = await Task.WhenAll(SafeBook(raceToken1), SafeBook(raceToken2));

        Assert.Single(results, r => r == HttpStatusCode.Created);

        using var db = Db();
        var confirmedAtSlot = db.Appointments.Where(a =>
            a.ItemId == itemId && a.StartTime == "09:00" && a.Status == AppointmentStatus.CONFIRMED);
        Assert.Single(confirmedAtSlot);
    }
}
