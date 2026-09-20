using System.Text.Json;
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
    WhatsAppBookingTokenService bookingTokens,
    IOpenAiChatClient openAi,
    IWhatsAppSender whatsAppSender,
    IEmailSender emailSender,
    ILogger<WhatsAppController> logger) : ControllerBase
{
    // Literal commands, not natural language -- deliberately simple so a business can explain them
    // to a customer in one sentence. Only meaningful when Business.ChatbotInquiryEnabled is on; a
    // business that doesn't use the feature sees zero behavior change (these never get checked).
    private const string BookingModeCommand = "$1";
    private const string InquiryModeCommand = "$2";
    private const string BookingMode = "Booking";
    private const string InquiryMode = "Inquiry";

    private static readonly string[] CancelKeywords = ["cancel", "ביטול", "بطل", "إلغاء", "בטל"];
    private static readonly string[] RescheduleKeywords = ["reschedule", "שינוי", "تغيير", "שנה"];
    private static readonly TimeSpan ConversationStateLifetime = TimeSpan.FromMinutes(10);
    // After this many consecutive non-numeric/out-of-range replies to a "which service?" prompt,
    // the rule-based bot stops replying to anything except the literal "$" unlock keyword -- see
    // the lockout gate at the top of ProcessMessageRuleBasedAsync.
    private const int MaxInvalidAttempts = 3;
    private const string UnlockKeyword = "$";
    // Matches WhatsAppBookingTokenService's own token lifetime -- the "stay quiet" window should
    // never outlast the link it's protecting.
    private static readonly TimeSpan BookingLinkPendingLifetime = TimeSpan.FromHours(24);

    // Legacy Twilio WhatsApp Business API webhook. Kept but dormant -- no business currently has a
    // real Twilio WhatsApp number (Meta/Trust Hub verification was rejected, see project history),
    // so nothing calls this in production. Left in place as the fallback path if a real registered
    // business + Trust Hub approval ever happens later. The active inbound path is
    // BridgeInbound below, via the self-hosted whatsapp-bridge (Baileys) service.
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync();
        var form = System.Web.HttpUtility.ParseQueryString(rawBody);
        var parms = form.AllKeys.Where(k => k is not null).ToDictionary(k => k!, k => form[k] ?? "");

        var toNumber = parms.GetValueOrDefault("To", "").Replace("whatsapp:", "");
        var business = await db.Businesses
            .Where(b => b.WhatsAppNumber == toNumber)
            .FirstOrDefaultAsync();

        if (business is null)
            return NotFound("Not configured");

        var signature = Request.Headers["X-Twilio-Signature"].FirstOrDefault() ?? "";
        var appUrl = config["AppUrl"] ?? "";
        // AppUrl is the frontend's public URL, reused here on the assumption frontend and backend
        // share a domain in production. WebhookPublicUrl overrides just the signature-check base
        // URL for setups where that's not true (e.g. a local ngrok tunnel pointed at the backend
        // only, while AppUrl keeps pointing at the frontend for booking links in reply text).
        var webhookUrl = $"{config["WebhookPublicUrl"] ?? appUrl}/api/whatsapp/webhook";

        // One platform-owned Twilio account handles every business's WhatsApp number now (see
        // TwilioWhatsAppSender) -- the signature is always checked against that single account's
        // Auth Token, not a per-business one.
        var validator = new RequestValidator(config["Twilio:AuthToken"] ?? "");
        if (!validator.Validate(webhookUrl, parms, signature))
            return StatusCode(403, "Invalid signature");

        var incomingMsg = parms.GetValueOrDefault("Body", "").Trim();
        var fromPhone = parms.GetValueOrDefault("From", "").Replace("whatsapp:", "");
        // Twilio's inbound WhatsApp webhook includes the sender's WhatsApp display name here --
        // that's the "name automatically taken from the WhatsApp API" the booking link identifies
        // the customer with, no separate profile lookup needed.
        var profileName = parms.GetValueOrDefault("ProfileName", "");

        var reply = await ProcessMessageAsync(business, appUrl, fromPhone, profileName, incomingMsg);
        if (reply is null)
            return Content("""<?xml version="1.0" encoding="UTF-8"?><Response></Response>""", "text/xml");

        var twiml = $"""<?xml version="1.0" encoding="UTF-8"?><Response><Message>{System.Net.WebUtility.HtmlEncode(reply)}</Message></Response>""";
        return Content(twiml, "text/xml");
    }

    // Active inbound path: the self-hosted whatsapp-bridge (Baileys) service posts here for every
    // message on a business's linked WhatsApp session. Unlike the Twilio webhook, the bridge
    // already knows which business a session belongs to (one linked number per business), so
    // there's no "To number" lookup -- it just tells us the BusinessId directly. Auth is a shared
    // secret (same pattern as CronSecret) instead of Twilio's per-request HMAC signature, since
    // this is a private server-to-server call, not a public webhook Twilio signs.
    public record BridgeInboundRequest(string BusinessId, string FromPhone, string? ProfileName, string Message);
    public record BridgeInboundResponse(string? Reply);

    [HttpPost("bridge/inbound")]
    public async Task<IActionResult> BridgeInbound([FromBody] BridgeInboundRequest req)
    {
        var secret = config["WhatsAppBridge:Secret"];
        var header = Request.Headers["X-Bridge-Secret"].FirstOrDefault();
        if (string.IsNullOrEmpty(secret) || header != secret)
            return Unauthorized();

        var business = await db.Businesses.FindAsync(req.BusinessId);
        if (business is null) return NotFound();

        var appUrl = config["AppUrl"] ?? "";
        var reply = await ProcessMessageAsync(business, appUrl, req.FromPhone, req.ProfileName ?? "", req.Message.Trim());
        return Ok(new BridgeInboundResponse(reply));
    }

    // Shared by both inbound transports (Twilio webhook + bridge inbound). Dispatches to the
    // OpenAI-driven path when configured, falling back to the rule-based flow on any failure (or
    // whenever OpenAI isn't configured at all) -- see ProcessMessageWithAiAsync's doc comment for
    // why this fallback matters. Returns null to mean "send nothing" (chatbot disabled), mirroring
    // the old empty-TwiML-response behavior.
    private async Task<string?> ProcessMessageAsync(Business business, string appUrl, string fromPhone, string profileName, string incomingMsg)
    {
        // The business wants to reply themselves instead of the automated flow -- send no message
        // at all, rather than a fixed "not available" reply.
        if (!business.ChatbotEnabled)
            return null;

        if (business.ChatbotInquiryEnabled)
        {
            var lang = await ResolveLanguage(business.Id, fromPhone, incomingMsg, business.Language.ToString());
            var (handled, inquiryReply) = await HandleInquiryModeAsync(business, fromPhone, profileName, incomingMsg, lang);
            if (handled) return inquiryReply;
        }

        if (!string.IsNullOrEmpty(config["OpenAI:ApiKey"]))
        {
            try
            {
                return await ProcessMessageWithAiAsync(business, appUrl, fromPhone, profileName, incomingMsg);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OpenAI chatbot path failed for business {BusinessId}, falling back to rule-based", business.Id);
            }
        }

        return await ProcessMessageRuleBasedAsync(business, appUrl, fromPhone, profileName, incomingMsg);
    }

    // Checked before either chatbot path (rule-based or AI) runs, for any business with
    // ChatbotInquiryEnabled on -- lets a customer step out of the normal booking flow to ask a
    // free-form question / reach the owner directly, via the literal $1 (booking) / $2 (inquiry)
    // commands. Returns Handled=true with the reply to send when this message was about
    // inquiry-mode navigation or content; Handled=false means "not mine, let the normal booking
    // dispatch handle this message" (including a bare $1 from someone already in Booking mode,
    // which just falls through to whatever booking already does with unrecognized text).
    private async Task<(bool Handled, string? Reply)> HandleInquiryModeAsync(
        Business business, string fromPhone, string profileName, string incomingMsg, string lang)
    {
        var trimmed = incomingMsg.Trim();
        var state = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == business.Id && s.Phone == fromPhone);

        if (trimmed == BookingModeCommand)
        {
            // Removed outright, not just flipped back to Booking mode -- a merely-updated row
            // would still look "open" to TryHandleServiceSelectionReply below, which would then
            // try to parse the literal text "$1" as a numeric service choice (fails) instead of
            // falling through to a fresh prompt. Same idiom as the reschedule keyword's own
            // ClearConversationState call.
            if (state is not null && state.ChatbotMode == InquiryMode)
                await ClearConversationState(business.Id, fromPhone);
            // Falls through to the normal booking dispatch, which starts a fresh prompt for a
            // phone with no (or an expired) conversation state -- exactly what "back to booking"
            // should feel like, with no separate reply of its own needed here.
            return (false, null);
        }

        if (trimmed == InquiryModeCommand)
        {
            if (state is null)
            {
                state = new WhatsAppConversationState { BusinessId = business.Id, Phone = fromPhone };
                db.WhatsAppConversationStates.Add(state);
            }
            state.ChatbotMode = InquiryMode;
            state.Language = lang;
            state.InquiryNotified = false;
            state.ExpiresAt = DateTime.UtcNow.Add(ConversationStateLifetime);
            await db.SaveChangesAsync();
            return (true, I18nService.T(lang, "whatsapp.inquiryStarted", new() { ["businessName"] = business.Name }));
        }

        if (state is not null && state.ChatbotMode == InquiryMode && state.ExpiresAt > DateTime.UtcNow)
        {
            db.ChatbotInquiries.Add(new ChatbotInquiry
            {
                BusinessId = business.Id,
                CustomerPhone = fromPhone,
                CustomerName = string.IsNullOrWhiteSpace(profileName) ? null : profileName,
                Message = incomingMsg,
            });

            // Only the first message of a session pings the owner -- InquiryNotified is reset
            // whenever the customer returns to Booking mode (above), so a later, separate inquiry
            // still notifies again.
            var shouldNotify = !state.InquiryNotified;
            state.InquiryNotified = true;
            state.ExpiresAt = DateTime.UtcNow.Add(ConversationStateLifetime);
            await db.SaveChangesAsync();

            if (shouldNotify)
                await SendInquiryNotificationsAsync(business, fromPhone, profileName, incomingMsg);

            return (true, I18nService.T(lang, "whatsapp.inquiryAck"));
        }

        return (false, null);
    }

    // Best-effort on both channels -- a failed notification must never break the customer's own
    // reply, and the inquiry itself is always saved to ChatbotInquiry regardless (the "in-app"
    // channel, which is always on). WhatsApp goes out from the business's own bot number to
    // whatever number the owner registered for inquiries -- a different recipient than the
    // customer, not a reply in the same thread.
    private async Task SendInquiryNotificationsAsync(Business business, string fromPhone, string profileName, string message)
    {
        var fromLabel = string.IsNullOrWhiteSpace(profileName) ? fromPhone : $"{profileName} ({fromPhone})";

        if (business.InquiryNotifyViaWhatsApp && !string.IsNullOrWhiteSpace(business.InquiryWhatsAppNumber))
        {
            try
            {
                await whatsAppSender.SendAsync(business, business.InquiryWhatsAppNumber,
                    $"📩 New inquiry from {fromLabel}:\n\n{message}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send WhatsApp inquiry notification for business {BusinessId}", business.Id);
            }
        }

        if (business.InquiryNotifyViaEmail && !string.IsNullOrWhiteSpace(business.InquiryEmail))
        {
            try
            {
                await emailSender.SendAsync(business.InquiryEmail, $"New customer inquiry — {business.Name}",
                    $"From: {fromLabel}\n\nMessage:\n{message}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send email inquiry notification for business {BusinessId}", business.Id);
            }
        }
    }

    // Everything from cancel/reschedule keyword matching through numbered service-selection
    // dispatch -- now the fallback path when OpenAI isn't configured or fails. Gated by the
    // too-many-invalid-replies lockout below (new behavior; the rest is otherwise unchanged).
    private async Task<string?> ProcessMessageRuleBasedAsync(Business business, string appUrl, string fromPhone, string profileName, string incomingMsg)
    {
        var lang = await ResolveLanguage(business.Id, fromPhone, incomingMsg, business.Language.ToString());

        var conversationState = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s =>
            s.BusinessId == business.Id && s.Phone == fromPhone && s.ExpiresAt > DateTime.UtcNow);
        if (conversationState is not null)
        {
            // A booking link was already issued and not yet completed -- stay quiet instead of
            // re-sending the opening prompt for every message the customer sends while they finish
            // booking on the web page the link opened. BookingController clears this once the
            // appointment is actually created (or it just expires with the row).
            if (conversationState.AwaitingBookingCompletion) return null;

            // A customer who's sent 3+ non-numeric/out-of-range replies in a row gets locked out of
            // automated replies entirely -- including cancel/reschedule keywords -- until they send
            // the literal unlock keyword, which restarts the conversation from the opening prompt.
            // Prevents the bot replying indefinitely to someone just sending random text.
            if (conversationState.InvalidAttempts >= MaxInvalidAttempts)
            {
                if (incomingMsg.Trim() != UnlockKeyword) return null;
                db.WhatsAppConversationStates.Remove(conversationState);
                await db.SaveChangesAsync();
                return await PromptServiceSelection(business.Id, business.Name, fromPhone, lang, business.ChatbotWelcomeMessage, business.ChatbotInquiryEnabled);
            }
        }

        var lowerMsg = incomingMsg.ToLowerInvariant();

        if (CancelKeywords.Any(k => lowerMsg.Contains(k)))
            return await HandleCancel(business.Id, business.Name, fromPhone, lang, business.ChatbotWelcomeMessage, business.ChatbotInquiryEnabled);

        if (RescheduleKeywords.Any(k => lowerMsg.Contains(k)))
        {
            await ClearConversationState(business.Id, fromPhone);
            var intro = I18nService.T(lang, "whatsapp.rescheduleIntro");
            return $"{intro}\n\n{await PromptServiceSelection(business.Id, business.Name, fromPhone, lang, business.ChatbotWelcomeMessage, business.ChatbotInquiryEnabled)}";
        }

        // Either a fresh conversation (no state row yet -- falls through to the prompt below)
        // or a reply to an already-open "which service?" prompt (a numeric selection or junk).
        var selectionReply = await TryHandleServiceSelectionReply(business.Id, business.Slug, appUrl, fromPhone, profileName, lang, incomingMsg, business.ChatbotConfirmationMessage);
        return selectionReply ?? await PromptServiceSelection(business.Id, business.Name, fromPhone, lang, business.ChatbotWelcomeMessage, business.ChatbotInquiryEnabled);
    }

    // The customer's message is understood by an LLM instead of fixed keywords/numeric replies --
    // e.g. "actually can we move it to Thursday" works, not just the literal word "reschedule".
    // Design guardrail: the model decides WHEN to call a tool, but never composes the text for a
    // completed action itself -- IssueBookingLink/FindAndCancelUpcomingAppointment build the exact
    // same I18nService-templated text the rule-based path uses, and the system prompt instructs the
    // model to relay a tool's "message" field verbatim. This is what prevents a hallucinated URL,
    // price, or date from ever reaching a customer; the model only freely composes text for
    // open-ended Q&A grounded in the business data injected into the system prompt.
    private async Task<string> ProcessMessageWithAiAsync(Business business, string appUrl, string fromPhone, string profileName, string incomingMsg)
    {
        var lang = await ResolveLanguage(business.Id, fromPhone, incomingMsg, business.Language.ToString());

        var state = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == business.Id && s.Phone == fromPhone && s.ExpiresAt > DateTime.UtcNow);
        var history = state?.HistoryJson is not null
            ? JsonSerializer.Deserialize<List<OpenAiTurn>>(state.HistoryJson) ?? []
            : [];

        var systemPrompt = await BuildAiSystemPrompt(business, lang);
        List<OpenAiToolDefinition> tools =
        [
            new("create_booking_link",
                "Create a booking link once the customer has chosen a specific service.",
                """{"type":"object","properties":{"itemId":{"type":"string"}},"required":["itemId"]}"""),
            new("cancel_upcoming_appointment",
                "Cancel the customer's next upcoming appointment with this business.",
                """{"type":"object","properties":{}}"""),
        ];

        async Task<string> ExecuteTool(string name, string argsJson) => name switch
        {
            "create_booking_link" => await ExecuteCreateBookingLink(business, appUrl, fromPhone, profileName, lang, argsJson),
            "cancel_upcoming_appointment" => await ExecuteCancelUpcomingAppointment(business.Id, fromPhone, lang),
            _ => JsonSerializer.Serialize(new { error = "unknown tool" }),
        };

        var reply = await openAi.GetReplyAsync(systemPrompt, history, incomingMsg, tools, ExecuteTool);

        history.Add(new OpenAiTurn("user", incomingMsg));
        history.Add(new OpenAiTurn("assistant", reply));
        if (history.Count > 12) history = history[^12..];

        if (state is null)
        {
            state = new WhatsAppConversationState { BusinessId = business.Id, Phone = fromPhone };
            db.WhatsAppConversationStates.Add(state);
        }
        state.Language = lang;
        state.HistoryJson = JsonSerializer.Serialize(history);
        state.ExpiresAt = DateTime.UtcNow.Add(ConversationStateLifetime);
        await db.SaveChangesAsync();

        return reply;
    }

    private async Task<string> BuildAiSystemPrompt(Business business, string lang)
    {
        var services = await ActiveServices(business.Id, lang);
        var itemsList = services.Count == 0
            ? "(no bookable services configured yet)"
            : string.Join("\n", services.Select(s => $"- id={s.Id}: {s.Name}"));
        var languageName = lang switch { "AR" => "Arabic", "HE" => "Hebrew", _ => "English" };
        // The literal $2 command itself is intercepted before this path ever runs (see
        // HandleInquiryModeAsync) -- the model never needs to act on it, just know to mention it
        // exists, since (unlike the rule-based path's PromptServiceSelection) nothing else here
        // surfaces that note to the customer.
        var inquiryNote = business.ChatbotInquiryEnabled
            ? "\n\nIf the customer asks something you can't help with, or wants to reach the owner directly, tell them to reply \"$2\" to talk to the owner directly."
            : "";

        return $"""
            You are {business.Name}'s WhatsApp booking assistant. Always reply in {languageName}, in plain WhatsApp-friendly text (no markdown).

            Bookable services (use the id when calling create_booking_link):
            {itemsList}

            Tools:
            - create_booking_link(itemId): call this once the customer has picked a specific service. Its result includes a "message" field -- output that text to the customer VERBATIM, do not reword it or invent your own link, price, or service name.
            - cancel_upcoming_appointment(): call this when the customer wants to cancel their appointment. If the result's "found" field is true, it also has a "message" field -- output that text VERBATIM. If "found" is false, tell the customer yourself that you couldn't find an upcoming appointment and offer to help them book one.
            To reschedule: call cancel_upcoming_appointment first, then help the customer book a new time via create_booking_link.

            Keep replies short and friendly. Never invent prices, links, dates, or services that aren't listed above or returned by a tool.{inquiryNote}
            """;
    }

    private async Task<string> ExecuteCreateBookingLink(Business business, string appUrl, string fromPhone, string profileName, string lang, string argsJson)
    {
        var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argsJson) ?? [];
        var itemId = args.GetValueOrDefault("itemId", "");
        var services = await ActiveServices(business.Id, lang);
        var chosen = services.FirstOrDefault(s => s.Id == itemId);
        if (chosen.Id is null)
            return JsonSerializer.Serialize(new { error = "unknown itemId" });

        var message = await IssueBookingLink(business.Id, business.Slug, appUrl, fromPhone, profileName, lang, chosen, business.ChatbotConfirmationMessage);
        return JsonSerializer.Serialize(new { message });
    }

    private async Task<string> ExecuteCancelUpcomingAppointment(string businessId, string fromPhone, string lang)
    {
        var cancelled = await FindAndCancelUpcomingAppointment(businessId, fromPhone);
        if (cancelled is null)
            return JsonSerializer.Serialize(new { found = false });

        var message = I18nService.T(lang, "whatsapp.cancelled", new()
        {
            ["date"] = cancelled.Date.ToString("yyyy-MM-dd"),
            ["time"] = cancelled.StartTime,
        });
        return JsonSerializer.Serialize(new { found = true, message });
    }

    // Detects the language from the incoming message's script (Hebrew/Arabic Unicode blocks, or
    // Latin letters -> English) so the bot always replies in whatever language the customer just
    // wrote in, regardless of the business's own configured storefront language. A message with no
    // letters at all (e.g. a bare "1" reply) carries no signal of its own, so it falls back to
    // whatever language the open conversation was already using, and only falls back to the
    // business's default when there's no open conversation either (a fresh, signal-less first message).
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

    // A customer replying in Arabic script naturally types the service number in Arabic-Indic
    // digits (٠-٩) rather than switching their keyboard to Western ones -- int.TryParse only
    // understands ASCII digits, so TryHandleServiceSelectionReply would otherwise treat "١" as an
    // invalid reply. Also covers the Extended Arabic-Indic/Persian variant (۰-۹) some keyboards use.
    private const char ArabicIndicDigitStart = (char)0x0660;
    private const char ArabicIndicDigitEnd = (char)0x0669;
    private const char ExtendedArabicIndicDigitStart = (char)0x06F0;
    private const char ExtendedArabicIndicDigitEnd = (char)0x06F9;

    private static string NormalizeDigits(string text)
    {
        var chars = text.Select(c => c switch
        {
            >= ArabicIndicDigitStart and <= ArabicIndicDigitEnd => (char)('0' + (c - ArabicIndicDigitStart)),
            >= ExtendedArabicIndicDigitStart and <= ExtendedArabicIndicDigitEnd => (char)('0' + (c - ExtendedArabicIndicDigitStart)),
            _ => c,
        }).ToArray();
        return new string(chars);
    }

    private async Task<string> ResolveLanguage(string businessId, string phone, string incomingMsg, string businessDefault)
    {
        var detected = DetectLanguage(incomingMsg);
        if (detected is not null) return detected;

        var pendingLang = await db.WhatsAppConversationStates
            .Where(s => s.BusinessId == businessId && s.Phone == phone && s.ExpiresAt > DateTime.UtcNow)
            .Select(s => s.Language)
            .FirstOrDefaultAsync();
        return pendingLang ?? businessDefault;
    }

    private async Task<string> HandleCancel(string businessId, string businessName, string fromPhone, string lang, string? welcomeMessage, bool inquiryEnabled)
    {
        await ClearConversationState(businessId, fromPhone);
        var cancelled = await FindAndCancelUpcomingAppointment(businessId, fromPhone);

        if (cancelled is null)
        {
            var intro = I18nService.T(lang, "whatsapp.noAppointment");
            return $"{intro}\n\n{await PromptServiceSelection(businessId, businessName, fromPhone, lang, welcomeMessage, inquiryEnabled)}";
        }

        return I18nService.T(lang, "whatsapp.cancelled", new()
        {
            ["date"] = cancelled.Date.ToString("yyyy-MM-dd"),
            ["time"] = cancelled.StartTime,
        });
    }

    // Shared by the rule-based "cancel" keyword and the AI path's cancel_upcoming_appointment tool.
    // Deliberately does NOT touch WhatsAppConversationState -- the rule-based caller clears it
    // itself (its "awaiting numbered reply" semantics), while the AI path's conversation history
    // in that same row must survive a cancellation instead of being wiped.
    private async Task<Appointment?> FindAndCancelUpcomingAppointment(string businessId, string fromPhone)
    {
        var customer = await db.Customers
            // a.Date is a calendar date (local wall-clock, never UTC-converted), so compare
            // against local "today" as a date — not DateTime.UtcNow, which is both the wrong
            // clock and, being a timestamp rather than a date, would already exclude today's
            // appointments as soon as any time had passed since UTC midnight.
            .Include(c => c.Appointments.Where(a => a.Status == AppointmentStatus.CONFIRMED && !a.PendingCancellationApproval && a.Date >= DateTime.Now.Date))
            .FirstOrDefaultAsync(c => c.BusinessId == businessId && c.Phone == fromPhone);

        var upcoming = customer?.Appointments
            .Where(a => AppointmentStatusHelper.EffectiveStatus(a.Status, a.Date, a.EndTime) == "CONFIRMED")
            .OrderBy(a => a.Date)
            .FirstOrDefault();
        if (upcoming is null) return null;

        await cancellationService.CancelFromCustomerAsync(upcoming);
        await db.SaveChangesAsync();
        return upcoming;
    }

    private async Task ClearConversationState(string businessId, string phone)
    {
        var existing = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == businessId && s.Phone == phone);
        if (existing is null) return;
        db.WhatsAppConversationStates.Remove(existing);
        await db.SaveChangesAsync();
    }

    // Only bookable items make sense to offer through the booking chatbot -- a showcase-only
    // item has nothing for this flow to schedule.
    private async Task<List<(string Id, string Name)>> ActiveServices(string businessId, string lang)
    {
        var services = await db.Items
            .Where(s => s.BusinessId == businessId && s.IsActive && s.IsBookable)
            .OrderBy(s => s.Id)
            .ToListAsync();
        return services.Select(s => (s.Id, lang switch
        {
            "AR" => s.NameAr,
            "HE" => s.NameHe,
            _ => s.NameEn,
        })).ToList();
    }

    // Upserts the (BusinessId, Phone) conversation-state row (unique index guarantees at most one,
    // and records the resolved language on it for TryHandleServiceSelectionReply / future
    // signal-less replies to reuse) and replies with the numbered service list. Reused by the
    // conversation-start path and by the reschedule/no-appointment paths, which prefix their own
    // intro line first. A business's custom welcome message replaces the default greeting -- the
    // "which service + list + instructions" tail always stays in the detected language.
    private async Task<string> PromptServiceSelection(string businessId, string businessName, string phone, string lang, string? welcomeMessage, bool inquiryEnabled)
    {
        var services = await ActiveServices(businessId, lang);
        if (services.Count == 0)
            return I18nService.T(lang, "whatsapp.noServices", new() { ["businessName"] = businessName });

        var existing = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == businessId && s.Phone == phone);
        if (existing is null)
        {
            existing = new WhatsAppConversationState { BusinessId = businessId, Phone = phone };
            db.WhatsAppConversationStates.Add(existing);
        }
        existing.ExpiresAt = DateTime.UtcNow.Add(ConversationStateLifetime);
        existing.Language = lang;
        existing.InvalidAttempts = 0;
        existing.AwaitingBookingCompletion = false;
        // A fresh booking prompt always means "back in Booking mode" -- without this, a row left
        // over from an expired Inquiry session (see HandleInquiryModeAsync) would still read as
        // Inquiry mode once its ExpiresAt gets refreshed below, silently swallowing the customer's
        // next numeric service-selection reply as if it were inquiry content.
        existing.ChatbotMode = BookingMode;
        existing.InquiryNotified = false;
        await db.SaveChangesAsync();

        var list = string.Join("\n", services.Select((s, i) => $"{i + 1}. {s.Name}"));
        var escapeHatch = inquiryEnabled ? I18nService.T(lang, "whatsapp.inquiryEscapeHatch") : "";
        if (!string.IsNullOrWhiteSpace(welcomeMessage))
        {
            var tail = I18nService.T(lang, "whatsapp.selectServicePrompt", new() { ["list"] = list });
            return $"{welcomeMessage}\n\n{tail}{escapeHatch}";
        }
        return I18nService.T(lang, "whatsapp.selectService", new() { ["businessName"] = businessName, ["list"] = list }) + escapeHatch;
    }

    // Returns null when there's no open "which service?" prompt for this phone -- the caller then
    // falls through to starting a fresh one. Otherwise resolves the numeric reply: valid -> issues
    // the booking link and clears the state; invalid -> reprompts and keeps the state so the
    // customer can retry within the window. A business's custom confirmation message is used as-is
    // (with {url} substituted if present, else the link is appended on its own line) in place of
    // the default confirmation text.
    private async Task<string?> TryHandleServiceSelectionReply(string businessId, string slug, string appUrl, string phone, string profileName, string lang, string message, string? confirmationMessage)
    {
        var state = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == businessId && s.Phone == phone && s.ExpiresAt > DateTime.UtcNow);
        if (state is null) return null;

        var services = await ActiveServices(businessId, lang);
        if (!int.TryParse(NormalizeDigits(message.Trim()), out var index) || index < 1 || index > services.Count)
        {
            state.InvalidAttempts++;
            await db.SaveChangesAsync();
            return state.InvalidAttempts >= MaxInvalidAttempts
                ? I18nService.T(lang, "whatsapp.tooManyInvalidReplies", new() { ["unlockKeyword"] = UnlockKeyword })
                : I18nService.T(lang, "whatsapp.invalidServiceSelection");
        }

        var chosen = services[index - 1];
        // Kept alive (not removed) with AwaitingBookingCompletion set -- see the lockout gate in
        // ProcessMessageRuleBasedAsync for why: it's what stops the bot re-sending the opening
        // prompt while the customer finishes booking on the web page this link opens.
        state.AwaitingBookingCompletion = true;
        state.ExpiresAt = DateTime.UtcNow.Add(BookingLinkPendingLifetime);
        await db.SaveChangesAsync();

        return await IssueBookingLink(businessId, slug, appUrl, phone, profileName, lang, chosen, confirmationMessage);
    }

    // Shared by the rule-based numbered-selection reply and the AI path's create_booking_link
    // tool. A business's custom confirmation message is used as-is (with {url} substituted if
    // present, else the link is appended on its own line) in place of the default confirmation text.
    private async Task<string> IssueBookingLink(string businessId, string slug, string appUrl, string phone, string profileName, string lang, (string Id, string Name) item, string? confirmationMessage)
    {
        var token = await bookingTokens.CreateAsync(businessId, item.Id, phone, string.IsNullOrWhiteSpace(profileName) ? null : profileName, lang);
        var url = $"{appUrl}/{slug}/w/{token.Id}";

        if (!string.IsNullOrWhiteSpace(confirmationMessage))
        {
            return confirmationMessage.Contains("{url}")
                ? confirmationMessage.Replace("{url}", url)
                : $"{confirmationMessage}\n\n{url}";
        }
        return I18nService.T(lang, "whatsapp.serviceLinkSent", new() { ["service"] = item.Name, ["url"] = url });
    }
}
