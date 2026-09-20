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
        // Same ordering WhatsAppController uses (OrderBy Id) -- so tests can pick "the Nth item
        // the bot listed" without depending on service-creation order.
        var idsInBotOrder = await db.Items.Where(s => s.BusinessId == businessId).OrderBy(s => s.Id).Select(s => s.Id).ToListAsync();
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

        // Locked out now -- even a cancel keyword gets no automated reply at all.
        var lockedReply = await SendWhatsAppMessage(phone, "cancel");
        Assert.DoesNotContain("<Message>", lockedReply);

        using var db = Db();
        Assert.Equal(3, await db.WhatsAppConversationStates.Where(s => s.BusinessId == businessId && s.Phone == phone).Select(s => s.InvalidAttempts).FirstAsync());
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

    [Fact]
    public async Task CancelKeyword_MidSelection_WithUpcomingAppointment_CancelsAndClearsState()
    {
        // "No upcoming appointment" deliberately re-prompts for a fresh selection instead of
        // dead-ending the conversation (see HandleCancel), which would itself recreate a state
        // row -- so the clean "state is gone, nothing pending" case is the successful-cancel path.
        var (businessId, _, serviceIds) = await SeedBusinessWithServices("wa-webhook-4@example.com", "wa-webhook-4");
        var phone = "+15558880004";
        await SendWhatsAppMessage(phone, "hi"); // opens a pending selection state

        using (var db = Db())
        {
            var customer = new Customer { BusinessId = businessId, Phone = phone, Name = "Test", FamilyName = "Customer" };
            db.Customers.Add(customer);
            db.Appointments.Add(new Appointment
            {
                BusinessId = businessId,
                CustomerId = customer.Id,
                ItemId = serviceIds[0],
                Date = DateTime.Now.Date.AddDays(1),
                StartTime = "10:00",
                EndTime = "10:30",
                Status = AppointmentStatus.CONFIRMED,
            });
            await db.SaveChangesAsync();
        }

        var reply = await SendWhatsAppMessage(phone, "cancel");

        Assert.Contains("cancelled", reply, StringComparison.OrdinalIgnoreCase);
        using var verifyDb = Db();
        Assert.False(await verifyDb.WhatsAppConversationStates.AnyAsync(s => s.BusinessId == businessId && s.Phone == phone));
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
    public async Task ArabicIndicDigitReply_DoesNotFlipAnEnglishConversationToArabic()
    {
        // Arabic-Indic digits (٠-٩) sit inside the same Unicode block as Arabic letters, but a
        // customer whose keyboard defaults numeric input to Arabic-Indic digits may be chatting in
        // English (or Hebrew) the whole time -- DetectLanguage must treat the digit as carrying no
        // language signal, not as an Arabic-language signal.
        await SeedBusinessWithServices("wa-webhook-lang-arabic-digit-sticky@example.com", "wa-webhook-lang-arabic-digit-sticky");
        var phone = "+15558880014";
        await SendWhatsAppMessage(phone, "hi"); // opens the conversation in English

        await SendWhatsAppMessage(phone, "١"); // Arabic-Indic "1" (U+0661)

        using var db = Db();
        var token = await db.WhatsAppBookingTokens.FirstAsync(t => t.Phone == phone);
        Assert.Equal("EN", token.Language);
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
}
