using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// QA run 11 fixes that need the API.
public class QaRun11Tests : IntegrationTestBase
{
    private const string Phone = "+15551110001";
    private record RegisterResponse(string? DevCode);

    private async Task<(string BusinessId, string Slug, string Token, string ItemId)> SeedBusiness(string slug, Action<Business>? configure = null)
    {
        var email = $"{slug}@example.com";
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Shop", email, "password123", slug, AcceptedTerms: true));
        var code = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.DevCode!;
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code));
        var token = (await verify.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        using var db = Db();
        var business = await db.Businesses.SingleAsync(b => b.Slug == slug);
        business.WhatsAppNumber = "+15559990000";
        configure?.Invoke(business);
        var item = new Item { BusinessId = business.Id, NameEn = "Haircut", NameAr = "قص شعر", NameHe = "תספורת", DurationMinutes = 30 };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return (business.Id, slug, token, item.Id);
    }

    private async Task<string> AddAppointment(string businessId, string itemId, string start, int days = 1, Action<Appointment>? configure = null)
    {
        using var db = Db();
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.BusinessId == businessId && c.Phone == Phone);
        if (customer is null)
        {
            customer = new Customer { BusinessId = businessId, Name = "Dana", FamilyName = "Cohen", Phone = Phone };
            db.Customers.Add(customer);
        }
        var appointment = new Appointment
        {
            BusinessId = businessId, CustomerId = customer.Id, ItemId = itemId, Date = AppClock.Now.AddDays(days).Date,
            StartTime = start, EndTime = AvailabilityService.AddMinutes(start, 30), Status = AppointmentStatus.CONFIRMED,
        };
        configure?.Invoke(appointment);
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }

    private async Task<string?> Inbound(string businessId, string message, string fromPhone = Phone)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/bridge/inbound")
        {
            Content = JsonContent.Create(new WhatsAppController.BridgeInboundRequest(businessId, fromPhone, "Dana", message)),
        };
        req.Headers.Add("X-Bridge-Secret", TestWebApplicationFactory.BridgeSecret);
        var resp = await Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>())!.Reply;
    }

    private async Task<Appointment> Load(string id)
    {
        using var db = Db();
        return await db.Appointments.SingleAsync(a => a.Id == id);
    }

    // SEC-60
    [Theory]
    [InlineData("admin\n")]
    [InlineData("qa11-shop\n")]
    public async Task Register_SlugWithATrailingNewline_IsRefused(string slug)
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Shop", "nl@example.com", "password123", slug, AcceptedTerms: true));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var db = Db();
        Assert.False(await db.Businesses.AnyAsync(b => b.Email == "nl@example.com"));
    }

    // PA-60 / OWN-60
    [Fact]
    public async Task Register_WithoutAcceptingTheTerms_IsRefused()
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/register", new { name = "Shop", email = "noterms@example.com", password = "password123", slug = "no-terms-shop" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var db = Db();
        Assert.False(await db.Businesses.AnyAsync(b => b.Email == "noterms@example.com"));
    }

    [Fact]
    public async Task Register_StoresWhenAndWhichTermsWereAccepted()
    {
        var (businessId, _, _, _) = await SeedBusiness("q11-terms");
        using var db = Db();
        var business = await db.Businesses.SingleAsync(b => b.Id == businessId);
        Assert.NotNull(business.TermsAcceptedAt);
        Assert.Equal(LegalTerms.Version, business.TermsVersion);
    }

    // ST-61
    [Fact]
    public async Task Waitlist_NoOfferForASlotOnADayTheOwnerBlocked()
    {
        var (businessId, slug, token, itemId) = await SeedBusiness("q11-wl-block");
        var date = AppClock.Now.Date.AddDays(1);
        Authorize(Client, token);
        await Client.PostAsJsonAsync("/api/admin/schedule", new List<WorkingHoursDto> { new(null, (int)date.DayOfWeek, "09:00", "18:00", true) });
        await Client.PatchAsJsonAsync("/api/admin/settings", new UpdateSettingsRequest(null, null, null, null, null, null, WaitlistEnabled: true));
        Client.DefaultRequestHeaders.Authorization = null;
        var day = date.ToString("yyyy-MM-dd");

        var booker = (await LoginCustomerViaWhatsAppAsync("+15551110002")).Token;
        var bookReq = new HttpRequestMessage(HttpMethod.Post, $"/api/{slug}/appointments")
        {
            Content = JsonContent.Create(new BookAppointmentRequest(itemId, day, "10:00", "Booker", "", null)),
        };
        bookReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", booker);
        var appt = await (await Client.SendAsync(bookReq)).Content.ReadFromJsonAsync<BookAppointmentResponse>();

        const string waiterPhone = "+15551110003";
        var waiter = (await LoginCustomerViaWhatsAppAsync(waiterPhone)).Token;
        var joinReq = new HttpRequestMessage(HttpMethod.Post, $"/api/{slug}/waitlist/{appt!.AppointmentId}");
        joinReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", waiter);
        Assert.Equal(HttpStatusCode.OK, (await Client.SendAsync(joinReq)).StatusCode);

        Authorize(Client, token);
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("/api/admin/schedule/blocked", new CreateBlockedSlotRequest(day, null, null, null))).StatusCode);
        var cancel = await Client.PatchAsJsonAsync($"/api/admin/appointments/{appt.AppointmentId}", new { status = "CANCELLED", notifyWaitlist = true });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        Assert.DoesNotContain(Factory.WhatsAppSender.Sent, s => s.Phone == waiterPhone);
        using var db = Db();
        Assert.All(db.WaitlistEntries.Where(w => w.BusinessId == businessId), e => Assert.Equal(WaitlistEntryStatus.RESOLVED, e.Status));
    }

    // ST-60
    [Fact]
    public async Task Keep_OnAnAppointmentThatAlreadyHappened_SendsNothing()
    {
        var (businessId, _, token, itemId) = await SeedBusiness("q11-keep-past");
        var id = await AddAppointment(businessId, itemId, "09:00", days: -2, a => a.PendingCancellationApproval = true);
        Authorize(Client, token);

        var resp = await Client.PostAsync($"/api/admin/appointments/{id}/keep", null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.DoesNotContain(Factory.WhatsAppSender.Sent, s => s.Phone == Phone);
        Assert.False((await Load(id)).PendingCancellationApproval);
    }

    // BOT-60
    [Theory]
    [InlineData("לביטול התור")]
    [InlineData("ולבטל")]
    [InlineData("שתבטל את התור")]
    [InlineData("שנבטל")]
    public async Task HebrewCancelWords_WithAPrefixLetter_AskTheCancelQuestion(string message)
    {
        var (businessId, _, _, itemId) = await SeedBusiness("q11-he-cancel");
        var id = await AddAppointment(businessId, itemId, "10:00", days: 3);

        var reply = await Inbound(businessId, message);

        Assert.Contains("1", reply);
        Assert.DoesNotContain("1. תספורת", reply);
        Assert.DoesNotContain("איזה שירות", reply);
        Assert.Equal(AppointmentStatus.CONFIRMED, (await Load(id)).Status);
    }

    [Theory]
    [InlineData("בקשה לשינוי תור")]
    [InlineData("להקדים את התור")]
    [InlineData("הזזה של התור")]
    [InlineData("can I rebook it?")]
    [InlineData("ممكن تقديم الموعد")]
    public async Task RescheduleWordings_SendTheChangeTimeLink(string message)
    {
        var (businessId, _, _, itemId) = await SeedBusiness("q11-he-move");
        var id = await AddAppointment(businessId, itemId, "10:00", days: 3);

        var reply = await Inbound(businessId, message);

        Assert.Contains($"/appointments/{id}?token=", reply);
    }

    [Fact]
    public async Task Telephone_IsStillNotACancelWord()
    {
        var (businessId, _, _, itemId) = await SeedBusiness("q11-phone-word");
        await AddAppointment(businessId, itemId, "10:00", days: 3);

        var reply = await Inbound(businessId, "התקשרתי בטלפון");

        Assert.Contains("1. תספורת", reply);
    }

    // BOT-66
    [Theory]
    [InlineData("keep my appointment, do not cancel")]
    [InlineData("don't cancel, I'm coming")]
    [InlineData("לא לבטל")]
    public async Task DontCancel_IsAnsweredWithStays(string message)
    {
        var (businessId, _, _, itemId) = await SeedBusiness("q11-dont-cancel");
        var id = await AddAppointment(businessId, itemId, "10:00", days: 3);

        var reply = await Inbound(businessId, message);

        Assert.True(reply!.Contains("stays as it is") || reply.Contains("נשאר כמו שהוא"), reply);
        var follow = await Inbound(businessId, "1");
        Assert.Equal(AppointmentStatus.CONFIRMED, (await Load(id)).Status);
        Assert.DoesNotContain("cancelled", follow ?? "");
    }

    // BOT-62
    [Fact]
    public async Task Reschedule_WhileNotTakingBookings_SaysSoInsteadOfALink()
    {
        var (businessId, _, _, itemId) = await SeedBusiness("q11-expired", b => b.SubscriptionStatus = SubStatus.EXPIRED);
        await AddAppointment(businessId, itemId, "10:00", days: 3);

        var reply = await Inbound(businessId, "reschedule");

        Assert.Contains("isn't taking online bookings", reply);
        Assert.DoesNotContain("token=", reply);
    }

    // BOT-63
    [Fact]
    public async Task WelcomeMessage_FillsBusinessName()
    {
        var (businessId, _, _, _) = await SeedBusiness("q11-welcome", b => b.ChatbotWelcomeMessageEn = "Welcome to {businessName}!");

        var reply = await Inbound(businessId, "hi");

        Assert.Contains("Welcome to Shop!", reply);
    }

    // BOT-64
    [Theory]
    [InlineData("12345678901234567890@lid")]
    [InlineData("abc")]
    public async Task Inbound_FromSomethingThatIsNotAPhone_IsIgnored(string fromPhone)
    {
        var (businessId, _, _, _) = await SeedBusiness("q11-bad-phone");

        Assert.Null(await Inbound(businessId, "hi", fromPhone));
        using var db = Db();
        Assert.False(await db.WhatsAppConversationStates.AnyAsync(s => s.BusinessId == businessId));
    }

    // BOT-67
    [Fact]
    public async Task ProfileName_IsOneLineWithoutDirectionMarks()
    {
        var (businessId, _, _, _) = await SeedBusiness("q11-profile");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/bridge/inbound")
        {
            Content = JsonContent.Create(new WhatsAppController.BridgeInboundRequest(businessId, Phone, "Dana\n\nPaid\u202E", "hi")),
        };
        req.Headers.Add("X-Bridge-Secret", TestWebApplicationFactory.BridgeSecret);
        await Client.SendAsync(req);
        await Inbound(businessId, "1");

        using var db = Db();
        var names = await db.WhatsAppBookingTokens.Where(t => t.BusinessId == businessId).Select(t => t.ProfileName).ToListAsync();
        Assert.All(names, n => Assert.DoesNotContain('\n', n ?? ""));
        Assert.All(names, n => Assert.DoesNotContain('\u202E', n ?? ""));
    }

    // CHAT-60
    [Fact]
    public async Task OwnerReplyFromThePhone_KeepsTheBotQuiet()
    {
        var (businessId, _, _, _) = await SeedBusiness("q11-owner-talks");
        Assert.NotNull(await Inbound(businessId, "hello"));

        var outgoing = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/bridge/outgoing")
        {
            Content = JsonContent.Create(new WhatsAppController.BridgeOutgoingRequest(businessId, Phone, "Hi, owner here, what time suits you?")),
        };
        outgoing.Headers.Add("X-Bridge-Secret", TestWebApplicationFactory.BridgeSecret);
        Assert.Equal(HttpStatusCode.OK, (await Client.SendAsync(outgoing)).StatusCode);

        Assert.Null(await Inbound(businessId, "tomorrow at 10 please"));
        // Another customer still gets the bot.
        Assert.NotNull(await Inbound(businessId, "hello", "+15551110099"));
    }

    // CHAT-61
    [Fact]
    public async Task InquiryTurnedOff_EndsOpenChats()
    {
        var (businessId, _, token, _) = await SeedBusiness("q11-inquiry-off");
        Authorize(Client, token);
        await Client.PatchAsJsonAsync("/api/admin/settings", new { chatbotInquiryEnabled = true });
        Client.DefaultRequestHeaders.Authorization = null;
        await Inbound(businessId, "hi");
        await Inbound(businessId, "2");
        using (var db = Db())
            Assert.True(await ChatLog.IsChatOpenAsync(db, businessId, Phone));

        Authorize(Client, token);
        await Client.PatchAsJsonAsync("/api/admin/settings", new { chatbotInquiryEnabled = false });

        using (var db = Db())
            Assert.False(await ChatLog.IsChatOpenAsync(db, businessId, Phone));
    }

    // CHAT-63 / OWN-63
    [Theory]
    [InlineData("\u200B")]
    [InlineData("\u200E\u200F")]
    [InlineData("\u2800")]
    public async Task QuickReply_WithOnlyInvisibleText_IsRefused(string text)
    {
        var (_, _, token, _) = await SeedBusiness("q11-quick");
        Authorize(Client, token);

        var resp = await Client.PostAsJsonAsync("/api/admin/chats/quick-replies", new { title = text, text });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task BlockedReason_AndQuickReplyTitle_AreCleaned()
    {
        var (businessId, _, token, _) = await SeedBusiness("q11-clean");
        Authorize(Client, token);
        var day = AppClock.Now.Date.AddDays(5).ToString("yyyy-MM-dd");

        await Client.PostAsJsonAsync("/api/admin/schedule/blocked", new CreateBlockedSlotRequest(day, null, null, "a\u200Bb\nc"));
        await Client.PostAsJsonAsync("/api/admin/chats/quick-replies", new { title = "a\u200Bb", text = "hello" });

        using var db = Db();
        Assert.Equal("ab c", (await db.BlockedSlots.SingleAsync(b => b.BusinessId == businessId)).Reason);
        Assert.Equal("ab", (await db.QuickReplies.SingleAsync(q => q.BusinessId == businessId)).Title);
    }

    // OWN-62
    [Fact]
    public async Task Breaks_DuplicateOrOverlapping_AreRefused()
    {
        var (_, _, token, _) = await SeedBusiness("q11-breaks");
        Authorize(Client, token);

        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("/api/admin/schedule/breaks", new CreateBreakRequest(2, "12:00", "12:30"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("/api/admin/schedule/breaks", new CreateBreakRequest(2, "12:00", "12:30"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("/api/admin/schedule/breaks", new CreateBreakRequest(2, "12:15", "12:45"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("/api/admin/schedule/breaks", new CreateBreakRequest(2, "12:30", "13:00"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("/api/admin/schedule/breaks", new CreateBreakRequest(3, "12:00", "12:30"))).StatusCode);
    }

    [Fact]
    public async Task WorkingHours_TheSameDayTwice_IsRefused()
    {
        var (_, _, token, _) = await SeedBusiness("q11-hours");
        Authorize(Client, token);

        var resp = await Client.PostAsJsonAsync("/api/admin/schedule",
            new List<WorkingHoursDto> { new(null, 1, "09:00", "18:00", true), new(null, 1, "10:00", "12:00", true) });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // OWN-64
    [Fact]
    public async Task ItemPrice_IsRoundedToAgorot()
    {
        var (_, _, token, _) = await SeedBusiness("q11-price");
        Authorize(Client, token);

        var item = await (await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Cut", "Cut", "Cut", 30, 10.555m)))
            .Content.ReadFromJsonAsync<ItemDto>();

        Assert.Equal(10.56m, item!.Price);
    }

    // OWN-67
    [Fact]
    public async Task SchedulePreset_DuplicateName_IsRefused()
    {
        var (_, _, token, _) = await SeedBusiness("q11-presets");
        Authorize(Client, token);
        var days = Enumerable.Range(0, 7).Select(d => new SchedulePresetDayDto(d, "09:00", "18:00", true)).ToList();

        Assert.Equal(HttpStatusCode.Created, (await Client.PostAsJsonAsync("/api/admin/schedule/presets", new SaveSchedulePresetRequest("P1", days, []))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Client.PostAsJsonAsync("/api/admin/schedule/presets", new SaveSchedulePresetRequest("p1 ", days, []))).StatusCode);
    }

    // OWN-68
    [Fact]
    public async Task ResetPassword_WrongCode_DoesNotRevealADisabledAccount()
    {
        var (_, _, _, _) = await SeedBusiness("q11-disabled", b => b.IsDisabled = true);

        var resp = await Client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest("q11-disabled@example.com", "123456", "abcdef"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ST-64
    [Fact]
    public async Task Reminders_DoNotCountBusinessesWithoutALinkedNumber()
    {
        var (businessId, _, _, itemId) = await SeedBusiness("q11-unlinked", b => b.WhatsAppNumber = null);
        await AddAppointment(businessId, itemId, "15:00");

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestWebApplicationFactory.CronSecret);
        var body = await (await Client.GetAsync("/api/cron/reminders")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, body.GetProperty("dayBefore").GetProperty("total").GetInt32());
    }
}

// PA-63 / PA-64
public class QaRun11PaidUntilTests : IntegrationTestBase
{
    private async Task<string> Setup()
    {
        var resp = await Client.PostAsJsonAsync("/api/platform-admin/bootstrap",
            new PlatformAdminBootstrapRequest("admin@example.com", "supersecret123", "Admin"));
        Authorize(Client, (await resp.Content.ReadFromJsonAsync<PlatformAdminLoginResponse>())!.Token);
        using var db = Db();
        var business = new Business
        {
            Name = "Paid Shop", Email = "q11-paid@example.com", Slug = "q11-paid", PasswordHash = "x", Phone = "+972500000000",
            TrialEndsAt = DateTime.UtcNow.AddDays(30), EmailVerified = true,
        };
        db.Businesses.Add(business);
        await db.SaveChangesAsync();
        return business.Id;
    }

    [Fact]
    public async Task TheSameDateAgain_RevivesAnExpiredBusiness()
    {
        var id = await Setup();
        var date = DateOnly.FromDateTime(AppClock.Now).AddDays(30).ToString("yyyy-MM-dd");
        await Client.PutAsJsonAsync($"/api/platform-admin/businesses/{id}/paid-until", new SetPaidUntilRequest(date));
        using (var db = Db())
        {
            var b = await db.Businesses.SingleAsync(x => x.Id == id);
            b.SubscriptionStatus = SubStatus.EXPIRED;
            await db.SaveChangesAsync();
        }

        await Client.PutAsJsonAsync($"/api/platform-admin/businesses/{id}/paid-until", new SetPaidUntilRequest(date));

        using var check = Db();
        Assert.Equal(SubStatus.ACTIVE, (await check.Businesses.SingleAsync(x => x.Id == id)).SubscriptionStatus);
    }

    [Fact]
    public async Task ADateYearsAhead_IsRefused()
    {
        var id = await Setup();
        var resp = await Client.PutAsJsonAsync($"/api/platform-admin/businesses/{id}/paid-until",
            new SetPaidUntilRequest(DateOnly.FromDateTime(AppClock.Now).AddYears(5).ToString("yyyy-MM-dd")));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
