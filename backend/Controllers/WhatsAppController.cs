using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Twilio.Security;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/whatsapp")]
public class WhatsAppController(
    AppDbContext db,
    IConfiguration config,
    AppointmentCancellationService cancellationService,
    WhatsAppBookingTokenService bookingTokens) : ControllerBase
{
    private static readonly string[] CancelKeywords = ["cancel", "ביטול", "بطل", "إلغاء", "בטל"];
    private static readonly string[] RescheduleKeywords = ["reschedule", "שינוי", "تغيير", "שנה"];
    private static readonly TimeSpan ConversationStateLifetime = TimeSpan.FromMinutes(10);

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync();
        var form = System.Web.HttpUtility.ParseQueryString(rawBody);
        var parms = form.AllKeys.Where(k => k is not null).ToDictionary(k => k!, k => form[k] ?? "");

        var toNumber = parms.GetValueOrDefault("To", "").Replace("whatsapp:", "");
        var barber = await db.Barbers
            .Where(b => b.TwilioNumber == toNumber)
            .Select(b => new { b.Id, b.Name, b.Slug, b.Language, b.ChatbotEnabled, b.ChatbotWelcomeMessage, b.ChatbotConfirmationMessage })
            .FirstOrDefaultAsync();

        if (barber is null)
            return NotFound("Not configured");

        var signature = Request.Headers["X-Twilio-Signature"].FirstOrDefault() ?? "";
        var appUrl = config["AppUrl"] ?? "";
        // AppUrl is the frontend's public URL, reused here on the assumption frontend and backend
        // share a domain in production. WebhookPublicUrl overrides just the signature-check base
        // URL for setups where that's not true (e.g. a local ngrok tunnel pointed at the backend
        // only, while AppUrl keeps pointing at the frontend for booking links in reply text).
        var webhookUrl = $"{config["WebhookPublicUrl"] ?? appUrl}/api/whatsapp/webhook";

        // One platform-owned Twilio account handles every barber's WhatsApp number now (see
        // TwilioWhatsAppSender) -- the signature is always checked against that single account's
        // Auth Token, not a per-barber one.
        var validator = new RequestValidator(config["Twilio:AuthToken"] ?? "");
        if (!validator.Validate(webhookUrl, parms, signature))
            return StatusCode(403, "Invalid signature");

        // The barber wants to reply themselves instead of the automated flow -- send no message
        // at all (an empty TwiML response), rather than a fixed "not available" reply.
        if (!barber.ChatbotEnabled)
            return Content("""<?xml version="1.0" encoding="UTF-8"?><Response></Response>""", "text/xml");

        var incomingMsg = parms.GetValueOrDefault("Body", "").Trim();
        var fromPhone = parms.GetValueOrDefault("From", "").Replace("whatsapp:", "");
        // Twilio's inbound WhatsApp webhook includes the sender's WhatsApp display name here --
        // that's the "name automatically taken from the WhatsApp API" the booking link identifies
        // the customer with, no separate profile lookup needed.
        var profileName = parms.GetValueOrDefault("ProfileName", "");
        var lang = await ResolveLanguage(barber.Id, fromPhone, incomingMsg, barber.Language.ToString());
        var lowerMsg = incomingMsg.ToLowerInvariant();

        string reply;
        if (CancelKeywords.Any(k => lowerMsg.Contains(k)))
        {
            reply = await HandleCancel(barber.Id, barber.Name, fromPhone, lang, barber.ChatbotWelcomeMessage);
        }
        else if (RescheduleKeywords.Any(k => lowerMsg.Contains(k)))
        {
            await ClearConversationState(barber.Id, fromPhone);
            var intro = I18nService.T(lang, "whatsapp.rescheduleIntro");
            reply = $"{intro}\n\n{await PromptServiceSelection(barber.Id, barber.Name, fromPhone, lang, barber.ChatbotWelcomeMessage)}";
        }
        else
        {
            // Either a fresh conversation (no state row yet -- falls through to the prompt below)
            // or a reply to an already-open "which service?" prompt (a numeric selection or junk).
            var selectionReply = await TryHandleServiceSelectionReply(barber.Id, barber.Slug, appUrl, fromPhone, profileName, lang, incomingMsg, barber.ChatbotConfirmationMessage);
            reply = selectionReply ?? await PromptServiceSelection(barber.Id, barber.Name, fromPhone, lang, barber.ChatbotWelcomeMessage);
        }

        var twiml = $"""<?xml version="1.0" encoding="UTF-8"?><Response><Message>{System.Net.WebUtility.HtmlEncode(reply)}</Message></Response>""";
        return Content(twiml, "text/xml");
    }

    // Detects the language from the incoming message's script (Hebrew/Arabic Unicode blocks, or
    // Latin letters -> English) so the bot always replies in whatever language the customer just
    // wrote in, regardless of the barber's own configured storefront language. A message with no
    // letters at all (e.g. a bare "1" reply) carries no signal of its own, so it falls back to
    // whatever language the open conversation was already using, and only falls back to the
    // barber's default when there's no open conversation either (a fresh, signal-less first message).
    private const char HebrewBlockStart = (char)0x0590;
    private const char HebrewBlockEnd = (char)0x05FF;
    private const char ArabicBlockStart = (char)0x0600;
    private const char ArabicBlockEnd = (char)0x06FF;

    private static string? DetectLanguage(string text)
    {
        if (text.Any(c => c >= HebrewBlockStart && c <= HebrewBlockEnd)) return "HE";
        if (text.Any(c => c >= ArabicBlockStart && c <= ArabicBlockEnd)) return "AR";
        if (text.Any(char.IsLetter)) return "EN";
        return null;
    }

    private async Task<string> ResolveLanguage(string barberId, string phone, string incomingMsg, string barberDefault)
    {
        var detected = DetectLanguage(incomingMsg);
        if (detected is not null) return detected;

        var pendingLang = await db.WhatsAppConversationStates
            .Where(s => s.BarberId == barberId && s.Phone == phone && s.ExpiresAt > DateTime.UtcNow)
            .Select(s => s.Language)
            .FirstOrDefaultAsync();
        return pendingLang ?? barberDefault;
    }

    private async Task<string> HandleCancel(string barberId, string barberName, string fromPhone, string lang, string? welcomeMessage)
    {
        var customer = await db.Customers
            // a.Date is a calendar date (local wall-clock, never UTC-converted), so compare
            // against local "today" as a date — not DateTime.UtcNow, which is both the wrong
            // clock and, being a timestamp rather than a date, would already exclude today's
            // appointments as soon as any time had passed since UTC midnight.
            .Include(c => c.Appointments.Where(a => a.Status == AppointmentStatus.CONFIRMED && !a.PendingCancellationApproval && a.Date >= DateTime.Now.Date))
            .FirstOrDefaultAsync(c => c.BarberId == barberId && c.Phone == fromPhone);

        var upcoming = customer?.Appointments
            .Where(a => AppointmentStatusHelper.EffectiveStatus(a.Status, a.Date, a.EndTime) == "CONFIRMED")
            .OrderBy(a => a.Date)
            .FirstOrDefault();

        if (upcoming is null)
        {
            await ClearConversationState(barberId, fromPhone);
            var intro = I18nService.T(lang, "whatsapp.noAppointment");
            return $"{intro}\n\n{await PromptServiceSelection(barberId, barberName, fromPhone, lang, welcomeMessage)}";
        }

        await ClearConversationState(barberId, fromPhone);
        await cancellationService.CancelFromCustomerAsync(upcoming);
        await db.SaveChangesAsync();
        return I18nService.T(lang, "whatsapp.cancelled", new()
        {
            ["date"] = upcoming.Date.ToString("yyyy-MM-dd"),
            ["time"] = upcoming.StartTime,
        });
    }

    private async Task ClearConversationState(string barberId, string phone)
    {
        var existing = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BarberId == barberId && s.Phone == phone);
        if (existing is null) return;
        db.WhatsAppConversationStates.Remove(existing);
        await db.SaveChangesAsync();
    }

    private async Task<List<(string Id, string Name)>> ActiveServices(string barberId, string lang)
    {
        var services = await db.Services
            .Where(s => s.BarberId == barberId && s.IsActive)
            .OrderBy(s => s.Id)
            .ToListAsync();
        return services.Select(s => (s.Id, lang switch
        {
            "AR" => s.NameAr,
            "HE" => s.NameHe,
            _ => s.NameEn,
        })).ToList();
    }

    // Upserts the (BarberId, Phone) conversation-state row (unique index guarantees at most one,
    // and records the resolved language on it for TryHandleServiceSelectionReply / future
    // signal-less replies to reuse) and replies with the numbered service list. Reused by the
    // conversation-start path and by the reschedule/no-appointment paths, which prefix their own
    // intro line first. A barber's custom welcome message replaces the default greeting -- the
    // "which service + list + instructions" tail always stays in the detected language.
    private async Task<string> PromptServiceSelection(string barberId, string barberName, string phone, string lang, string? welcomeMessage)
    {
        var services = await ActiveServices(barberId, lang);
        if (services.Count == 0)
            return I18nService.T(lang, "whatsapp.noServices", new() { ["barberName"] = barberName });

        var existing = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BarberId == barberId && s.Phone == phone);
        if (existing is null)
        {
            existing = new WhatsAppConversationState { BarberId = barberId, Phone = phone };
            db.WhatsAppConversationStates.Add(existing);
        }
        existing.ExpiresAt = DateTime.UtcNow.Add(ConversationStateLifetime);
        existing.Language = lang;
        await db.SaveChangesAsync();

        var list = string.Join("\n", services.Select((s, i) => $"{i + 1}. {s.Name}"));
        if (!string.IsNullOrWhiteSpace(welcomeMessage))
        {
            var tail = I18nService.T(lang, "whatsapp.selectServicePrompt", new() { ["list"] = list });
            return $"{welcomeMessage}\n\n{tail}";
        }
        return I18nService.T(lang, "whatsapp.selectService", new() { ["barberName"] = barberName, ["list"] = list });
    }

    // Returns null when there's no open "which service?" prompt for this phone -- the caller then
    // falls through to starting a fresh one. Otherwise resolves the numeric reply: valid -> issues
    // the booking link and clears the state; invalid -> reprompts and keeps the state so the
    // customer can retry within the window. A barber's custom confirmation message is used as-is
    // (with {url} substituted if present, else the link is appended on its own line) in place of
    // the default confirmation text.
    private async Task<string?> TryHandleServiceSelectionReply(string barberId, string slug, string appUrl, string phone, string profileName, string lang, string message, string? confirmationMessage)
    {
        var state = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BarberId == barberId && s.Phone == phone && s.ExpiresAt > DateTime.UtcNow);
        if (state is null) return null;

        var services = await ActiveServices(barberId, lang);
        if (!int.TryParse(message.Trim(), out var index) || index < 1 || index > services.Count)
            return I18nService.T(lang, "whatsapp.invalidServiceSelection");

        var chosen = services[index - 1];
        db.WhatsAppConversationStates.Remove(state);
        await db.SaveChangesAsync();

        var token = await bookingTokens.CreateAsync(barberId, chosen.Id, phone, string.IsNullOrWhiteSpace(profileName) ? null : profileName, lang);
        var url = $"{appUrl}/{slug}/w/{token.Id}";

        if (!string.IsNullOrWhiteSpace(confirmationMessage))
        {
            return confirmationMessage.Contains("{url}")
                ? confirmationMessage.Replace("{url}", url)
                : $"{confirmationMessage}\n\n{url}";
        }
        return I18nService.T(lang, "whatsapp.serviceLinkSent", new() { ["service"] = chosen.Name, ["url"] = url });
    }
}
