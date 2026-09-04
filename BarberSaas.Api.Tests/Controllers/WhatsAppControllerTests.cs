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

    private async Task<(string BarberId, string Slug, List<string> ServiceIdsInBotOrder)> SeedBarberWithServices(string email, string slug, int serviceCount = 2)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Barber", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var barberBody = await verify.Content.ReadFromJsonAsync<LoginResponse>();

        Authorize(Client, barberBody!.Token);
        for (var i = 0; i < serviceCount; i++)
            await Client.PostAsJsonAsync("/api/admin/services", new CreateServiceRequest($"Service {i}", $"Service {i}", $"Service {i}", 30, 50m));

        await Client.PatchAsJsonAsync("/api/admin/settings", new UpdateSettingsRequest(
            null, null, null, null, TwilioNumber, "AC_test_sid", TwilioToken, null, null));
        Client.DefaultRequestHeaders.Authorization = null;

        using var db = Db();
        var barberId = await db.Barbers.Where(b => b.Slug == slug).Select(b => b.Id).FirstAsync();
        // Same ordering WhatsAppController uses (OrderBy Id) -- so tests can pick "the Nth item
        // the bot listed" without depending on service-creation order.
        var idsInBotOrder = await db.Services.Where(s => s.BarberId == barberId).OrderBy(s => s.Id).Select(s => s.Id).ToListAsync();
        return (barberId, slug, idsInBotOrder);
    }

    private async Task SetChatbotConfig(string barberId, bool enabled = true, string? welcome = null, string? confirmation = null)
    {
        using var db = Db();
        var barber = await db.Barbers.FirstAsync(b => b.Id == barberId);
        barber.ChatbotEnabled = enabled;
        barber.ChatbotWelcomeMessage = welcome;
        barber.ChatbotConfirmationMessage = confirmation;
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
        var (barberId, _, _) = await SeedBarberWithServices("wa-webhook-1@example.com", "wa-webhook-1");
        var phone = "+15558880001";

        var reply = await SendWhatsAppMessage(phone, "hi");

        Assert.Contains("1.", reply);
        Assert.Contains("2.", reply);
        using var db = Db();
        Assert.True(await db.WhatsAppConversationStates.AnyAsync(s => s.BarberId == barberId && s.Phone == phone));
    }

    [Fact]
    public async Task ValidNumericReply_SendsBookingLinkAndClearsState()
    {
        var (barberId, slug, serviceIds) = await SeedBarberWithServices("wa-webhook-2@example.com", "wa-webhook-2");
        var phone = "+15558880002";
        await SendWhatsAppMessage(phone, "hi");

        var reply = await SendWhatsAppMessage(phone, "1", profileName: "Jane Doe");

        Assert.Contains($"/{slug}/w/", reply);
        using var db = Db();
        Assert.False(await db.WhatsAppConversationStates.AnyAsync(s => s.BarberId == barberId && s.Phone == phone));
        var token = await db.WhatsAppBookingTokens.SingleAsync(t => t.BarberId == barberId && t.Phone == phone);
        Assert.Equal(serviceIds[0], token.ServiceId);
        Assert.Equal("Jane Doe", token.ProfileName);
    }

    [Fact]
    public async Task InvalidNumericReply_RepromptsAndKeepsState()
    {
        var (barberId, _, _) = await SeedBarberWithServices("wa-webhook-3@example.com", "wa-webhook-3");
        var phone = "+15558880003";
        await SendWhatsAppMessage(phone, "hi");

        var reply = await SendWhatsAppMessage(phone, "99");

        Assert.Contains("didn", reply, StringComparison.OrdinalIgnoreCase);
        using var db = Db();
        Assert.True(await db.WhatsAppConversationStates.AnyAsync(s => s.BarberId == barberId && s.Phone == phone));
    }

    [Fact]
    public async Task CancelKeyword_MidSelection_WithUpcomingAppointment_CancelsAndClearsState()
    {
        // "No upcoming appointment" deliberately re-prompts for a fresh selection instead of
        // dead-ending the conversation (see HandleCancel), which would itself recreate a state
        // row -- so the clean "state is gone, nothing pending" case is the successful-cancel path.
        var (barberId, _, serviceIds) = await SeedBarberWithServices("wa-webhook-4@example.com", "wa-webhook-4");
        var phone = "+15558880004";
        await SendWhatsAppMessage(phone, "hi"); // opens a pending selection state

        using (var db = Db())
        {
            var customer = new Customer { BarberId = barberId, Phone = phone, Name = "Test", FamilyName = "Customer" };
            db.Customers.Add(customer);
            db.Appointments.Add(new Appointment
            {
                BarberId = barberId,
                CustomerId = customer.Id,
                ServiceId = serviceIds[0],
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
        Assert.False(await verifyDb.WhatsAppConversationStates.AnyAsync(s => s.BarberId == barberId && s.Phone == phone));
    }

    [Fact]
    public async Task ArabicFirstMessage_RepliesInArabic()
    {
        await SeedBarberWithServices("wa-webhook-lang-ar@example.com", "wa-webhook-lang-ar");
        var phone = "+15558880010";

        // "مرحبا" (hello) -- Arabic-script text with no cancel/reschedule keyword.
        var reply = await SendWhatsAppMessage(phone, "مرحبا");

        Assert.Contains("الخدمة", reply); // "service" -- only appears in the Arabic template
    }

    [Fact]
    public async Task HebrewFirstMessage_RepliesInHebrew()
    {
        await SeedBarberWithServices("wa-webhook-lang-he@example.com", "wa-webhook-lang-he");
        var phone = "+15558880011";

        // "שלום" (hello) -- Hebrew-script text.
        var reply = await SendWhatsAppMessage(phone, "שלום");

        Assert.Contains("שירות", reply); // "service" -- only appears in the Hebrew template
    }

    [Fact]
    public async Task NumericReply_KeepsThePreviouslyDetectedLanguage()
    {
        await SeedBarberWithServices("wa-webhook-lang-sticky@example.com", "wa-webhook-lang-sticky");
        var phone = "+15558880012";
        await SendWhatsAppMessage(phone, "مرحبا"); // opens the conversation in Arabic

        // "1" alone carries no language signal -- must still reply in Arabic, not fall back to
        // the barber's own default (English, since RegisterRequest doesn't set a language).
        await SendWhatsAppMessage(phone, "1");

        using var db = Db();
        var token = await db.WhatsAppBookingTokens.FirstAsync(t => t.Phone == phone);
        Assert.Equal("AR", token.Language);
    }

    [Fact]
    public async Task ChatbotDisabled_SendsNoAutomatedReply()
    {
        var (barberId, _, _) = await SeedBarberWithServices("wa-webhook-disabled@example.com", "wa-webhook-disabled");
        await SetChatbotConfig(barberId, enabled: false);

        var reply = await SendWhatsAppMessage("+15558880013", "hi");

        Assert.DoesNotContain("<Message>", reply);
        using var db = Db();
        Assert.False(await db.WhatsAppConversationStates.AnyAsync(s => s.BarberId == barberId));
    }

    [Fact]
    public async Task CustomWelcomeMessage_ReplacesDefaultGreetingButKeepsServiceList()
    {
        var (barberId, _, _) = await SeedBarberWithServices("wa-webhook-welcome@example.com", "wa-webhook-welcome");
        await SetChatbotConfig(barberId, welcome: "Yo! Welcome to the shop.");

        var reply = await SendWhatsAppMessage("+15558880014", "hi");

        Assert.Contains("Yo! Welcome to the shop.", reply);
        // Services are listed in Id order, not creation order, so don't assume which one is #1.
        Assert.Matches(@"1\. Service \d", reply);
        Assert.Matches(@"2\. Service \d", reply);
        Assert.DoesNotContain("booking assistant", reply); // the default greeting text
    }

    [Fact]
    public async Task CustomConfirmationMessage_WithUrlPlaceholder_SubstitutesInPlace()
    {
        var (barberId, _, _) = await SeedBarberWithServices("wa-webhook-confirm-1@example.com", "wa-webhook-confirm-1");
        await SetChatbotConfig(barberId, confirmation: "Thanks! Tap here to finish booking: {url} See you soon.");
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
        var (barberId, _, _) = await SeedBarberWithServices("wa-webhook-confirm-2@example.com", "wa-webhook-confirm-2");
        // No apostrophe -- the reply is HTML-encoded as XML content, so a literal "'" would come
        // back as "&#39;" and this'd need to assert against the encoded form instead.
        await SetChatbotConfig(barberId, confirmation: "Almost done! Here is your link:");
        var phone = "+15558880016";
        await SendWhatsAppMessage(phone, "hi");

        var reply = await SendWhatsAppMessage(phone, "1");

        Assert.Contains("Almost done! Here is your link:", reply);
        Assert.Contains("/w/", reply);
    }

    [Fact]
    public async Task InvalidSignature_IsRejected()
    {
        var (_, _, _) = await SeedBarberWithServices("wa-webhook-5@example.com", "wa-webhook-5");
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
