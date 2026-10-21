using BarberSaas.Api.Services;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Exercises the WhatsApp chatbot's service-selection conversation end-to-end through the real
// signed webhook, rather than calling WhatsAppController's internals directly -- this is the
// only place the request-signature validation and the raw TwiML reply text get covered at all.
public class WhatsAppControllerTests : IntegrationTestBase
{
    private const string TwilioToken = "test_auth_token";
    private const string TwilioNumber = "+15550009999";
    // Matches TestWebApplicationFactory's AppUrl env var -- WhatsAppController signs against
    // {WebhookPublicUrl ?? AppUrl}/api/whatsapp/webhook, and WebhookPublicUrl isn't set in tests.
    private const string WebhookUrl = "http://localhost:5173/api/whatsapp/webhook";

    private record RegisterResponse(string? DevCode);
    private record ServiceDto(string Id);

    private async Task<(string BusinessId, string Slug, List<string> ServiceIdsInBotOrder)> SeedBusinessWithServices(string email, string slug, int serviceCount = 2)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var businessBody = await verify.Content.ReadFromJsonAsync<LoginResponse>();

        Authorize(Client, businessBody!.Token);
        for (var i = 0; i < serviceCount; i++)
            await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest($"Service {i}", $"Service {i}", $"Service {i}", 30, 50m));
        Client.DefaultRequestHeaders.Authorization = null;

        using var db = Db();
        var businessId = await db.Businesses.Where(b => b.Slug == slug).Select(b => b.Id).FirstAsync();
        // WhatsAppNumber is now assigned by the platform-admin link flow, not settable via
        // /api/admin/settings -- set it directly, same as SetChatbotConfig below does for chatbot fields.
        var businessForTwilio = await db.Businesses.FirstAsync(b => b.Id == businessId);
        businessForTwilio.WhatsAppNumber = TwilioNumber;
        await db.SaveChangesAsync();
        // Same ordering WhatsAppController uses (by name, "Service 0", "Service 1", ...) -- so tests
        // can pick "the Nth item the bot listed".
        var idsInBotOrder = (await db.Items.Where(s => s.BusinessId == businessId).ToListAsync())
            .OrderBy(s => s.NameEn).ThenBy(s => s.Id).Select(s => s.Id).ToList();
        return (businessId, slug, idsInBotOrder);
    }

    private async Task SetChatbotConfig(string businessId, bool enabled = true, string? welcome = null, string? confirmation = null)
    {
        using var db = Db();
        var business = await db.Businesses.FirstAsync(b => b.Id == businessId);
        business.ChatbotEnabled = enabled;
        business.ChatbotWelcomeMessageEn = welcome;
        business.ChatbotConfirmationMessageEn = confirmation;
        await db.SaveChangesAsync();
    }

    private static string ComputeTwilioSignature(string url, string authToken, IReadOnlyDictionary<string, string> parms)
    {
        var data = url + string.Concat(parms.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + kv.Value));
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(authToken));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }

    private async Task<string> SendWhatsAppMessage(string fromPhone, string body, string? profileName = null)
    {
        var parms = new Dictionary<string, string>
        {
            ["To"] = $"whatsapp:{TwilioNumber}",
            ["From"] = $"whatsapp:{fromPhone}",
            ["Body"] = body,
        };
        if (profileName is not null) parms["ProfileName"] = profileName;

        var signature = ComputeTwilioSignature(WebhookUrl, TwilioToken, parms);
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/webhook") { Content = new FormUrlEncodedContent(parms) };
        req.Headers.Add("X-Twilio-Signature", signature);

        var resp = await Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await resp.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task FirstMessage_ReplyListsServicesAndCreatesConversationState()
    {
        var (businessId, _, _) = await SeedBusinessWithServices("wa-webhook-1@example.com", "wa-webhook-1");
        var phone = "+15558880001";

        var reply = await SendWhatsAppMessage(phone, "hi");

        Assert.Contains("1.", reply);
        Assert.Contains("2.", reply);
        using var db = Db();
        Assert.True(await db.WhatsAppConversationStates.AnyAsync(s => s.BusinessId == businessId && s.Phone == phone));
    }

    [Fact]
    public async Task ValidNumericReply_SendsBookingLinkAndMarksAwaitingCompletion()
    {
        var (businessId, slug, serviceIds) = await SeedBusinessWithServices("wa-webhook-2@example.com", "wa-webhook-2");
        var phone = "+15558880002";
        await SendWhatsAppMessage(phone, "hi");

        var reply = await SendWhatsAppMessage(phone, "1", profileName: "Jane Doe");

        Assert.Contains($"/{slug}/w/", reply);
        using var db = Db();
        // The state row is kept alive (not removed) with AwaitingBookingCompletion set -- it's what
        // stops the bot re-sending the opening prompt while the customer finishes booking on the
        // web page this link opens (see the next test).
        var state = await db.WhatsAppConversationStates.SingleAsync(s => s.BusinessId == businessId && s.Phone == phone);
        Assert.True(state.AwaitingBookingCompletion);
        var token = await db.WhatsAppBookingTokens.SingleAsync(t => t.BusinessId == businessId && t.Phone == phone);
        Assert.Equal(serviceIds[0], token.ItemId);
        Assert.Equal("Jane Doe", token.ProfileName);
    }

    [Fact]
    public async Task ArabicIndicAndExtendedArabicIndicNumerals_AreAcceptedAsValidSelections()
    {
        var (businessId, slug, serviceIds) = await SeedBusinessWithServices("wa-webhook-arabic-digit@example.com", "wa-webhook-arabic-digit");

        var phone1 = "+15558880023";
        await SendWhatsAppMessage(phone1, "hi");
        var reply1 = await SendWhatsAppMessage(phone1, "١"); // Arabic-Indic "1" (U+0661)
        Assert.Contains($"/{slug}/w/", reply1);

        var phone2 = "+15558880024";
        await SendWhatsAppMessage(phone2, "hi");
        var reply2 = await SendWhatsAppMessage(phone2, "۲"); // Extended Arabic-Indic/Persian "2" (U+06F2)
        Assert.Contains($"/{slug}/w/", reply2);

        using var db = Db();
        var token1 = await db.WhatsAppBookingTokens.SingleAsync(t => t.BusinessId == businessId && t.Phone == phone1);
        Assert.Equal(serviceIds[0], token1.ItemId);
        var token2 = await db.WhatsAppBookingTokens.SingleAsync(t => t.BusinessId == businessId && t.Phone == phone2);
        Assert.Equal(serviceIds[1], token2.ItemId);
    }

    [Fact]
    public async Task AfterBookingLinkIssued_FurtherMessagesGetNoAutomatedReply()
    {
        await SeedBusinessWithServices("wa-webhook-pending-1@example.com", "wa-webhook-pending-1");
        var phone = "+15558880022";
        await SendWhatsAppMessage(phone, "hi");
        await SendWhatsAppMessage(phone, "1");

        // Anything sent while a booking link is pending gets silence, not the opening prompt again.
        var reply = await SendWhatsAppMessage(phone, "hello?");

        Assert.DoesNotContain("<Message>", reply);
    }

    [Fact]
    public async Task InvalidNumericReply_RepromptsAndKeepsState()
    {
        var (businessId, _, _) = await SeedBusinessWithServices("wa-webhook-3@example.com", "wa-webhook-3");
        var phone = "+15558880003";
        await SendWhatsAppMessage(phone, "hi");

        var reply = await SendWhatsAppMessage(phone, "99");

        Assert.Contains("didn", reply, StringComparison.OrdinalIgnoreCase);
        using var db = Db();
        Assert.True(await db.WhatsAppConversationStates.AnyAsync(s => s.BusinessId == businessId && s.Phone == phone));
    }

    [Fact]
    public async Task ThreeInvalidReplies_LocksOutFurtherAutomatedReplies()
    {
        var (businessId, _, _) = await SeedBusinessWithServices("wa-webhook-lockout-1@example.com", "wa-webhook-lockout-1");
        var phone = "+15558880020";
        await SendWhatsAppMessage(phone, "hi");

        await SendWhatsAppMessage(phone, "99"); // attempt 1 -- normal reprompt
        await SendWhatsAppMessage(phone, "99"); // attempt 2 -- normal reprompt
        var thirdReply = await SendWhatsAppMessage(phone, "99"); // attempt 3 -- lockout warning
        Assert.Contains("$", thirdReply);

        // Locked out now -- even a cancel keyword gets no reply, and the reminder isn't repeated
        // right away (it used to answer every message).
        var lockedReply = await SendWhatsAppMessage(phone, "cancel");
        Assert.DoesNotContain("$", lockedReply);
        Assert.DoesNotContain("cancel", lockedReply, StringComparison.OrdinalIgnoreCase);

        using var db = Db();
        Assert.Equal(3, await db.WhatsAppConversationStates.Where(s => s.BusinessId == businessId && s.Phone == phone).Select(s => s.InvalidAttempts).FirstAsync());
    }

    [Fact]
    public async Task Lockout_RemindsAgainOnlyAfterThirtyMinutes()
    {
        var (businessId, _, _) = await SeedBusinessWithServices("wa-webhook-lockout-3@example.com", "wa-webhook-lockout-3");
        var phone = "+15558880022";
        await SendWhatsAppMessage(phone, "hi");
        for (var i = 0; i < 3; i++) await SendWhatsAppMessage(phone, "99"); // locked, reminder sent

        Assert.DoesNotContain("$", await SendWhatsAppMessage(phone, "hello?"));

        // 31 minutes since the reminder (the lock runs 24 h from it).
        using (var db = Db())
        {
            var state = await db.WhatsAppConversationStates.SingleAsync(s => s.BusinessId == businessId && s.Phone == phone);
            state.ExpiresAt = DateTime.UtcNow.AddHours(24).AddMinutes(-31);
            await db.SaveChangesAsync();
        }

        Assert.Contains("$", await SendWhatsAppMessage(phone, "hello?"));
        Assert.DoesNotContain("$", await SendWhatsAppMessage(phone, "hello again"));
    }

    [Fact]
    public async Task UnlockKeyword_AfterLockout_RestartsWithTheOpeningPrompt()
    {
        await SeedBusinessWithServices("wa-webhook-lockout-2@example.com", "wa-webhook-lockout-2");
        var phone = "+15558880021";
        await SendWhatsAppMessage(phone, "hi");
        await SendWhatsAppMessage(phone, "99");
        await SendWhatsAppMessage(phone, "99");
        await SendWhatsAppMessage(phone, "99"); // now locked out

        var unlockReply = await SendWhatsAppMessage(phone, "$");

        Assert.Contains("1.", unlockReply);
        Assert.Contains("2.", unlockReply);
    }

    // Seeds one CONFIRMED appointment tomorrow for this phone (creating the customer on first use).
    private async Task<string> AddUpcomingAppointment(string businessId, string phone, string itemId, string startTime = "10:00", string endTime = "10:30")
    {
        using var db = Db();
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.BusinessId == businessId && c.Phone == phone);
        if (customer is null)
        {
            customer = new Customer { BusinessId = businessId, Phone = phone, Name = "Test", FamilyName = "Customer" };
            db.Customers.Add(customer);
        }
        var appointment = new Appointment
        {
            BusinessId = businessId,
            CustomerId = customer.Id,
            ItemId = itemId,
            Date = AppClock.Now.Date.AddDays(1),
            StartTime = startTime,
            EndTime = endTime,
            Status = AppointmentStatus.CONFIRMED,
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }

    private async Task<Appointment> LoadAppointment(string id)
    {
        using var db = Db();
        return await db.Appointments.FirstAsync(a => a.Id == id);
    }

    [Fact]
    public async Task CancelKeyword_MidSelection_AsksFirst_ThenCancelsOnOneAndClearsState()
    {
        // "No upcoming appointment" deliberately re-prompts for a fresh selection instead of
        // dead-ending the conversation (see HandleCancel), which would itself recreate a state
        // row -- so the clean "state is gone, nothing pending" case is the successful-cancel path.
        var (businessId, _, serviceIds) = await SeedBusinessWithServices("wa-webhook-4@example.com", "wa-webhook-4");
        var phone = "+15558880004";
        await SendWhatsAppMessage(phone, "hi"); // opens a pending selection state
        var appointmentId = await AddUpcomingAppointment(businessId, phone, serviceIds[0]);

        var question = await SendWhatsAppMessage(phone, "cancel");
        Assert.Contains("Reply 1 to cancel", question);
        Assert.Contains("10:00", question);
        Assert.Equal(AppointmentStatus.CONFIRMED, (await LoadAppointment(appointmentId)).Status);

        var reply = await SendWhatsAppMessage(phone, "1");

        Assert.Contains("cancelled", reply, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AppointmentStatus.CANCELLED, (await LoadAppointment(appointmentId)).Status);
        using var verifyDb = Db();
        Assert.False(await verifyDb.WhatsAppConversationStates.AnyAsync(s => s.BusinessId == businessId && s.Phone == phone));
    }

    // QA run 3: "reschedule" started a second booking and the old one stayed, so the customer
    // held two slots. It now sends the existing appointment's own change-time link.
    [Fact]
    public async Task RescheduleKeyword_WithAnUpcomingAppointment_SendsItsChangeTimeLink()
    {
        var (businessId, slug, serviceIds) = await SeedBusinessWithServices("wa-resched@example.com", "wa-resched");
        var phone = "+15558880090";
        var appointmentId = await AddUpcomingAppointment(businessId, phone, serviceIds[0]);
        var token = (await LoadAppointment(appointmentId)).CancelToken;

        var reply = await SendWhatsAppMessage(phone, "I need to reschedule");

        Assert.Contains($"/{slug}/appointments/{appointmentId}?token={token}", reply);
        Assert.DoesNotContain("Service 1", reply); // no service list
        Assert.Equal(AppointmentStatus.CONFIRMED, (await LoadAppointment(appointmentId)).Status);
    }

    [Fact]
    public async Task RescheduleKeyword_WithNothingBooked_OffersANewBooking()
    {
        await SeedBusinessWithServices("wa-resched-none@example.com", "wa-resched-none");

        var reply = await SendWhatsAppMessage("+15558880091", "reschedule");

        Assert.Contains("book a new appointment", reply);
        Assert.Contains("Service 1", reply);
    }

    // WhatsApp always sends E.164; a booking the owner typed in locally ("050...") must still be
    // the same customer's, or "cancel" finds nothing.
    [Fact]
    public async Task CancelKeyword_FindsAppointmentBookedWithLocalFormatPhone()
    {
        var (businessId, _, serviceIds) = await SeedBusinessWithServices("wa-cancel-local@example.com", "wa-cancel-local");
        var login = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("wa-cancel-local@example.com", "password123"));
        var ownerToken = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        var date = AppClock.Now.Date.AddDays(1);
        Authorize(Client, ownerToken);
        await Client.PostAsJsonAsync("/api/admin/schedule",
            new List<WorkingHoursDto> { new(null, (int)date.DayOfWeek, "09:00", "18:00", true) });
        var booked = await Client.PostAsJsonAsync("/api/admin/appointments", new CreateAdminAppointmentRequest(
            null, "Rami", "050-111-2255", serviceIds[0], date.ToString("yyyy-MM-dd"), "10:00", null));
        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        Client.DefaultRequestHeaders.Authorization = null;

        var question = await SendWhatsAppMessage("+972501112255", "cancel");
        Assert.Contains("Reply 1 to cancel", question);
        await SendWhatsAppMessage("+972501112255", "1");

        using var db = Db();
        Assert.Equal(AppointmentStatus.CANCELLED, (await db.Appointments.SingleAsync(a => a.BusinessId == businessId)).Status);
    }

    [Theory]
    [InlineData("he", "אני בטלפון עכשיו")]         // "I'm on the phone now" -- contains בטל
    [InlineData("ar", "بطلب من صديقي")]             // "at my friend's request" -- contains بطل
    [InlineData("en", "I'll be there, no cancellation needed")]
    public async Task CancelKeyword_InsideAnotherWord_DoesNotTriggerCancel(string caseId, string message)
    {
        var (businessId, _, serviceIds) = await SeedBusinessWithServices($"wa-substr-{caseId}@example.com", $"wa-substr-{caseId}");
        var phone = "+15558880040";
        var appointmentId = await AddUpcomingAppointment(businessId, phone, serviceIds[0]);

        var reply = await SendWhatsAppMessage(phone, message);
        // Even if "1" follows, there was never a cancel question to answer.
        await SendWhatsAppMessage(phone, "1");

        Assert.DoesNotContain("10:00", reply);
        Assert.Equal(AppointmentStatus.CONFIRMED, (await LoadAppointment(appointmentId)).Status);
    }

    [Fact]
    public async Task CancelKeyword_InSentence_AsksAndKeepsOnTwo()
    {
        var (businessId, _, serviceIds) = await SeedBusinessWithServices("wa-cancel-keep@example.com", "wa-cancel-keep");
        var phone = "+15558880041";
        var appointmentId = await AddUpcomingAppointment(businessId, phone, serviceIds[0]);

        var question = await SendWhatsAppMessage(phone, "Don't cancel, I'm just confirming I'll come");
        Assert.Contains("Reply 1 to cancel", question);

        var reply = await SendWhatsAppMessage(phone, "2");

        Assert.Contains("stays as it is", reply);
        Assert.Equal(AppointmentStatus.CONFIRMED, (await LoadAppointment(appointmentId)).Status);
    }

    [Fact]
    public async Task CancelQuestion_AnsweredWithSomethingElse_CancelsNothing()
    {
        var (businessId, _, serviceIds) = await SeedBusinessWithServices("wa-cancel-other@example.com", "wa-cancel-other");
        var phone = "+15558880042";
        var appointmentId = await AddUpcomingAppointment(businessId, phone, serviceIds[0]);

        await SendWhatsAppMessage(phone, "ביטול");
        var reply = await SendWhatsAppMessage(phone, "מה השעות שלכם?");
        await SendWhatsAppMessage(phone, "1"); // now a service choice, not a cancel confirmation

        Assert.Contains("1.", reply); // handled as a normal message: fresh service list
        Assert.Equal(AppointmentStatus.CONFIRMED, (await LoadAppointment(appointmentId)).Status);
    }

    [Fact]
    public async Task CancelKeyword_TwoSameDayAppointments_OffersTheEarlierOne()
    {
        var (businessId, _, serviceIds) = await SeedBusinessWithServices("wa-cancel-order@example.com", "wa-cancel-order");
        var phone = "+15558880043";
        var later = await AddUpcomingAppointment(businessId, phone, serviceIds[0], "15:00", "15:30");
        var earlier = await AddUpcomingAppointment(businessId, phone, serviceIds[0], "10:00", "10:30");

        var pick = await SendWhatsAppMessage(phone, "cancel");
        var question = await SendWhatsAppMessage(phone, "1");
        await SendWhatsAppMessage(phone, "1");

        Assert.Contains("1. Service", pick); // numbered, nearest first
        Assert.True(pick.IndexOf("10:00") < pick.IndexOf("15:00"));
        Assert.Contains("10:00", question);
        Assert.Equal(AppointmentStatus.CANCELLED, (await LoadAppointment(earlier)).Status);
        Assert.Equal(AppointmentStatus.CONFIRMED, (await LoadAppointment(later)).Status);
    }

    [Fact]
    public async Task CancelKeyword_WithOwnerApprovalRequired_SaysRequestSent_NotCancelled()
    {
        var (businessId, _, serviceIds) = await SeedBusinessWithServices("wa-cancel-approval@example.com", "wa-cancel-approval");
        using (var db = Db())
        {
            var business = await db.Businesses.FirstAsync(b => b.Id == businessId);
            business.RequireApprovalOnCustomerCancel = true;
            business.CancelApprovalNotifyViaWhatsApp = true;
            business.CancelApprovalWhatsAppNumber = "+15550001111";
            await db.SaveChangesAsync();
        }
        var phone = "+15558880044";
        var appointmentId = await AddUpcomingAppointment(businessId, phone, serviceIds[0]);

        await SendWhatsAppMessage(phone, "cancel");
        var reply = await SendWhatsAppMessage(phone, "1");
        var again = await SendWhatsAppMessage(phone, "cancel");

        Assert.Contains("needs to approve", reply);
        Assert.DoesNotContain("has been cancelled", reply);
        var appointment = await LoadAppointment(appointmentId);
        Assert.True(appointment.PendingCancellationApproval);
        Assert.Equal(AppointmentStatus.CONFIRMED, appointment.Status);
        Assert.Contains("already asked", again);
    }

    [Fact]
    public async Task ArabicFirstMessage_RepliesInArabic()
    {
        await SeedBusinessWithServices("wa-webhook-lang-ar@example.com", "wa-webhook-lang-ar");
        var phone = "+15558880010";

        // "مرحبا" (hello) -- Arabic-script text with no cancel/reschedule keyword.
        var reply = await SendWhatsAppMessage(phone, "مرحبا");

        Assert.Contains("الخدمة", reply); // "service" -- only appears in the Arabic template
    }

    [Fact]
    public async Task HebrewFirstMessage_RepliesInHebrew()
    {
        await SeedBusinessWithServices("wa-webhook-lang-he@example.com", "wa-webhook-lang-he");
        var phone = "+15558880011";

        // "שלום" (hello) -- Hebrew-script text.
        var reply = await SendWhatsAppMessage(phone, "שלום");

        Assert.Contains("שירות", reply); // "service" -- only appears in the Hebrew template
    }

    [Fact]
    public async Task SignalLessFirstMessage_UsesChatbotDefaultLanguageOverBusinessLanguage()
    {
        var (businessId, _, _) = await SeedBusinessWithServices("wa-webhook-lang-default@example.com", "wa-webhook-lang-default");
        using (var db = Db())
        {
            var business = await db.Businesses.FirstAsync(b => b.Id == businessId);
            business.ChatbotDefaultLanguage = Language.AR;
            await db.SaveChangesAsync();
        }

        // No letters and no digits -- carries no language signal of its own, so this exercises the
        // business-default fallback specifically. Business.Language is still English (RegisterRequest's
        // default) -- ChatbotDefaultLanguage must take priority over it, not the other way around.
        var reply = await SendWhatsAppMessage("+15558880099", "👋");

        Assert.Contains("الخدمة", reply);
    }

    [Fact]
    public async Task NumericReply_KeepsThePreviouslyDetectedLanguage()
    {
        await SeedBusinessWithServices("wa-webhook-lang-sticky@example.com", "wa-webhook-lang-sticky");
        var phone = "+15558880012";
        await SendWhatsAppMessage(phone, "مرحبا"); // opens the conversation in Arabic

        // "1" alone carries no language signal -- must still reply in Arabic, not fall back to
        // the business's own default (English, since RegisterRequest doesn't set a language).
        await SendWhatsAppMessage(phone, "1");

        using var db = Db();
        var token = await db.WhatsAppBookingTokens.FirstAsync(t => t.Phone == phone);
        Assert.Equal("AR", token.Language);
    }

    [Fact]
    public async Task ArabicIndicDigitReply_FlipsAnEnglishConversationToArabic()
    {
        // Arabic-Indic digits (٠-٩) sit inside the same Unicode block as Arabic letters -- confirmed
        // with the business owner that, unlike ASCII digits, a customer only sends these because
        // their keyboard/locale is actually Arabic, so DetectLanguage must treat them as a genuine
        // Arabic-language signal, switching the conversation even mid-flow.
        await SeedBusinessWithServices("wa-webhook-lang-arabic-digit-sticky@example.com", "wa-webhook-lang-arabic-digit-sticky");
        var phone = "+15558880014";
        await SendWhatsAppMessage(phone, "hi"); // opens the conversation in English

        await SendWhatsAppMessage(phone, "١"); // Arabic-Indic "1" (U+0661)

        using var db = Db();
        var token = await db.WhatsAppBookingTokens.FirstAsync(t => t.Phone == phone);
        Assert.Equal("AR", token.Language);
    }

    [Fact]
    public async Task ChatbotDisabled_SendsNoAutomatedReply()
    {
        var (businessId, _, _) = await SeedBusinessWithServices("wa-webhook-disabled@example.com", "wa-webhook-disabled");
        await SetChatbotConfig(businessId, enabled: false);

        var reply = await SendWhatsAppMessage("+15558880013", "hi");

        Assert.DoesNotContain("<Message>", reply);
        using var db = Db();
        Assert.False(await db.WhatsAppConversationStates.AnyAsync(s => s.BusinessId == businessId));
    }

    [Fact]
    public async Task CustomWelcomeMessage_ReplacesDefaultGreetingButKeepsServiceList()
    {
        var (businessId, _, _) = await SeedBusinessWithServices("wa-webhook-welcome@example.com", "wa-webhook-welcome");
        await SetChatbotConfig(businessId, welcome: "Yo! Welcome to the shop.");

        var reply = await SendWhatsAppMessage("+15558880014", "hi");

        Assert.Contains("Yo! Welcome to the shop.", reply);
        // Services are listed in Id order, not creation order, so don't assume which one is #1.
        Assert.Matches(@"1\. Service \d", reply);
        Assert.Matches(@"2\. Service \d", reply);
        Assert.DoesNotContain("booking assistant", reply); // the default greeting text
    }

    [Fact]
    public async Task WelcomeMessage_OnlySetForOneLanguage_OnlyAppliesToThatLanguagesConversation()
    {
        var (businessId, _, _) = await SeedBusinessWithServices("wa-webhook-welcome-lang@example.com", "wa-webhook-welcome-lang");
        using (var db = Db())
        {
            var business = await db.Businesses.FirstAsync(b => b.Id == businessId);
            business.ChatbotWelcomeMessageAr = "أهلاً بك في المحل!";
            await db.SaveChangesAsync();
        }

        var arabicReply = await SendWhatsAppMessage("+15558880020", "مرحبا");
        Assert.Contains("أهلاً بك في المحل!", arabicReply);

        // No English welcome message was set, so an English conversation must fall back to the
        // built-in default text -- never borrow the Arabic custom text.
        var englishReply = await SendWhatsAppMessage("+15558880021", "hi");
        Assert.DoesNotContain("أهلاً بك في المحل!", englishReply);
        Assert.Contains("booking assistant", englishReply);
    }

    [Fact]
    public async Task CustomConfirmationMessage_WithUrlPlaceholder_SubstitutesInPlace()
    {
        var (businessId, _, _) = await SeedBusinessWithServices("wa-webhook-confirm-1@example.com", "wa-webhook-confirm-1");
        await SetChatbotConfig(businessId, confirmation: "Thanks! Tap here to finish booking: {url} See you soon.");
        var phone = "+15558880015";
        await SendWhatsAppMessage(phone, "hi");

        var reply = await SendWhatsAppMessage(phone, "1");

        Assert.Contains("Tap here to finish booking: http", reply);
        Assert.Contains("See you soon.", reply);
        Assert.DoesNotContain("{url}", reply);
    }

    [Fact]
    public async Task CustomConfirmationMessage_WithoutUrlPlaceholder_AppendsLinkAtTheEnd()
    {
        var (businessId, _, _) = await SeedBusinessWithServices("wa-webhook-confirm-2@example.com", "wa-webhook-confirm-2");
        // No apostrophe -- the reply is HTML-encoded as XML content, so a literal "'" would come
        // back as "&#39;" and this'd need to assert against the encoded form instead.
        await SetChatbotConfig(businessId, confirmation: "Almost done! Here is your link:");
        var phone = "+15558880016";
        await SendWhatsAppMessage(phone, "hi");

        var reply = await SendWhatsAppMessage(phone, "1");

        Assert.Contains("Almost done! Here is your link:", reply);
        Assert.Contains("/w/", reply);
    }

    [Fact]
    public async Task InvalidSignature_IsRejected()
    {
        var (_, _, _) = await SeedBusinessWithServices("wa-webhook-5@example.com", "wa-webhook-5");
        var parms = new Dictionary<string, string>
        {
            ["To"] = $"whatsapp:{TwilioNumber}",
            ["From"] = "whatsapp:+15558880005",
            ["Body"] = "hi",
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/webhook") { Content = new FormUrlEncodedContent(parms) };
        req.Headers.Add("X-Twilio-Signature", "not-a-real-signature");

        var resp = await Client.SendAsync(req);

        Assert.Equal((HttpStatusCode)403, resp.StatusCode);
    }

    [Theory]
    [InlineData("כן")]
    [InlineData("yes")]
    public async Task CancelQuestion_YesWord_Cancels(string answer)
    {
        var (businessId, _, serviceIds) = await SeedBusinessWithServices($"wa-yes-{answer.Length}@example.com", $"wa-yes-{answer.Length}");
        var phone = $"+1555888012{answer.Length}";
        var appointmentId = await AddUpcomingAppointment(businessId, phone, serviceIds[0]);
        await SendWhatsAppMessage(phone, "cancel");

        await SendWhatsAppMessage(phone, answer);

        Assert.Equal(AppointmentStatus.CANCELLED, (await LoadAppointment(appointmentId)).Status);
    }

    // QA run 3 BOT-2: with Inquiry on, "cancel" as the first message got the Book/Ask gate.
    [Fact]
    public async Task InquiryEnabled_CancelAsFirstMessage_AsksTheCancelQuestion()
    {
        var (businessId, _, serviceIds) = await SeedBusinessWithServices("wa-gate-cancel@example.com", "wa-gate-cancel");
        using (var db = Db())
        {
            (await db.Businesses.FirstAsync(b => b.Id == businessId)).ChatbotInquiryEnabled = true;
            await db.SaveChangesAsync();
        }
        var phone = "+15558880130";
        await AddUpcomingAppointment(businessId, phone, serviceIds[0]);

        var reply = await SendWhatsAppMessage(phone, "I want to cancel my appointment");

        Assert.Contains("Reply 1 to cancel", reply);
    }
}
