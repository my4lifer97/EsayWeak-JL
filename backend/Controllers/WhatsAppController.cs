using System.Text.Json;
using System.Text.RegularExpressions;
using BarberSaas.Api.Data;
using BarberSaas.Api.Filters;
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
    AiUsageLimiter aiUsage,
    AvailabilityService availability,
    NotificationOutbox notifications,
    ILogger<WhatsAppController> logger) : ControllerBase
{
    // Literal commands, not natural language -- deliberately simple so a business can explain them
    // to a customer in one sentence. Only meaningful when Business.ChatbotInquiryEnabled is on; a
    // business that doesn't use the feature sees zero behavior change (these never get checked).
    private const string BookingModeCommand = "$1";
    private const string InquiryModeCommand = "$2";
    private const string BookingMode = "Booking";
    private const string InquiryMode = "Inquiry";
    // A fresh conversation (or one that gave an unrecognized reply to the gate below) sits here
    // until it resolves to "1" or "2" -- distinct from BookingMode so TryHandleServiceSelectionReply
    // never mistakes the gate's own "1"/"2" reply for an actual service-list numeric selection.
    private const string AwaitingModeChoiceMode = "AwaitingModeChoice";

    // Matched as whole words only (see ContainsKeyword) -- a substring match cancelled real
    // appointments on ordinary sentences ("בטל" inside "בטלפון", "بطل" inside "بطلب"). "שנה" was
    // dropped from the reschedule list: on its own it far more often means "year" ("שנה טובה").
    // Whole words, so inflected forms are listed one by one (QA run 3: "תבטל לי את התור" and the
    // colloquial "بدي الغي الموعد" weren't recognised).
    private static readonly string[] CancelKeywords =
    [
        "cancel",
        "ביטול", "בטל", "לבטל", "תבטל", "תבטלי", "תבטלו", "בטלו", "בטלי",
        "بطل", "إلغاء", "الغاء", "والغاء", "وإلغاء", "الغي", "ألغي", "إلغي", "الغيه", "ألغيه", "الغيلي",
    ];
    private static readonly string[] RescheduleKeywords = ["reschedule", "שינוי", "לשנות", "تغيير"];
    // A cancel keyword never cancels on its own: it asks "reply 1 to cancel / 2 to keep" and parks
    // the appointment id in WhatsAppConversationState.ChatbotMode as "ConfirmCancel:{id}" until the
    // customer answers (see TryHandleCancelConfirmation).
    private const string ConfirmCancelModePrefix = "ConfirmCancel:";
    private static readonly TimeSpan ConversationStateLifetime = TimeSpan.FromMinutes(10);
    // An inquiry waits on the owner's answer, so it outlives the 10-minute booking prompt: a
    // follow-up an hour later used to land on the opening gate and was never logged (QA).
    private static readonly TimeSpan InquiryStateLifetime = TimeSpan.FromHours(24);
    // After this many consecutive non-numeric/out-of-range replies to a "which service?" prompt,
    // the rule-based bot stops replying to anything except the literal "$" unlock keyword -- see
    // the lockout gate at the top of ProcessMessageRuleBasedAsync.
    private const int MaxInvalidAttempts = 3;
    // What the AI path sends to OpenAI per message and keeps per history turn. A few very long
    // messages used to overflow the context window, and every later turn then failed (QA run 3).
    private const int MaxAiMessageChars = 1000;
    // An inquiry is stored, shown in the inbox and forwarded to the owner; 100,000 characters
    // went through in full (QA run 3).
    private const int MaxInquiryMessageChars = 4000;
    // Natural answers to the "reply 1 to cancel / 2 to keep" question, as whole words. "yes" went
    // to the model, which had no tool to act on it (QA run 3).
    private static readonly string[] YesWords = ["yes", "yeah", "yep", "y", "כן", "נכון", "نعم", "ايوه", "أيوه", "اه"];
    private static readonly string[] NoWords = ["no", "nope", "n", "לא", "لا", "keep", "להשאיר", "השאר"];
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
    // ProfileName is the sender's WhatsApp display name: cut to length below rather than rejected,
    // since a 400 here means the customer gets no reply at all (QA run 3).
    public record BridgeInboundRequest(string BusinessId, string FromPhone, [property: Filters.SkipLengthCheck] string? ProfileName, string Message);
    public record BridgeInboundResponse(string? Reply);

    [HttpPost("bridge/inbound")]
    [RequireBridgeSecret]
    public async Task<IActionResult> BridgeInbound([FromBody] BridgeInboundRequest req)
    {
        // The bridge names the business up front, so the per-customer lock (see ConversationLocks)
        // can cover this whole request, business lookup included.
        var fromPhone = PhoneNormalizer.Normalize(req.FromPhone);
        var conversationLock = LockFor(req.BusinessId, fromPhone);
        await conversationLock.WaitAsync();
        try
        {
            var business = await db.Businesses.FindAsync(req.BusinessId);
            if (business is null) return NotFound();

            var appUrl = config["AppUrl"] ?? "";
            var reply = fromPhone.Length == 0
                ? null
                : await ProcessMessageLockedAsync(business, appUrl, fromPhone, TruncateName(req.ProfileName), req.Message.Trim());
            return Ok(new BridgeInboundResponse(reply));
        }
        finally
        {
            conversationLock.Release();
        }
    }

    // Shared by both inbound transports (Twilio webhook + bridge inbound). Dispatches to the
    // OpenAI-driven path when configured, falling back to the rule-based flow on any failure (or
    // whenever OpenAI isn't configured at all) -- see ProcessMessageWithAiAsync's doc comment for
    // why this fallback matters. Returns null to mean "send nothing" (chatbot disabled), mirroring
    // the old empty-TwiML-response behavior.
    // Two messages from the same customer arriving together each created the conversation row,
    // and the second hit its unique (BusinessId, Phone) index: a 500 and no reply (QA). One
    // backend instance, so an in-memory lock per (business, phone) handles them one at a time.
    // Striped so memory stays bounded; an unrelated customer sharing a stripe just waits briefly.
    private static readonly SemaphoreSlim[] ConversationLocks =
        Enumerable.Range(0, 256).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static SemaphoreSlim LockFor(string businessId, string normalizedPhone) =>
        ConversationLocks[(uint)HashCode.Combine(businessId, normalizedPhone) % ConversationLocks.Length];

    private async Task<string?> ProcessMessageAsync(Business business, string appUrl, string fromPhone, string profileName, string incomingMsg)
    {
        // Both inbound paths (Twilio, bridge) land here: canonicalize once so every lookup and
        // write below matches bookings made on the website (see PhoneNormalizer).
        fromPhone = PhoneNormalizer.Normalize(fromPhone);
        if (fromPhone.Length == 0) return null;

        var conversationLock = LockFor(business.Id, fromPhone);
        await conversationLock.WaitAsync();
        try
        {
            return await ProcessMessageLockedAsync(business, appUrl, fromPhone, profileName, incomingMsg);
        }
        finally
        {
            conversationLock.Release();
        }
    }

    private async Task<string?> ProcessMessageLockedAsync(Business business, string appUrl, string fromPhone, string profileName, string incomingMsg)
    {
        // Postgres text columns can't hold a NUL character: one in a message or WhatsApp profile
        // name failed the save below with a 500 and the customer got no reply (QA run 2).
        incomingMsg = incomingMsg.Replace("\0", "");
        profileName = profileName.Replace("\0", "");

        // The business wants to reply themselves instead of the automated flow -- send no message
        // at all, rather than a fixed "not available" reply.
        if (!business.ChatbotEnabled)
            return null;

        // Resolved exactly once per message and threaded through everything below, rather than
        // re-resolved independently inside each path. This matters specifically for a signal-less
        // message (e.g. a bare "1"): HandleInquiryModeAsync's "$1"/"1" (back to booking) branches
        // call ClearConversationState and fall through (Handled=false) to the booking dispatch below
        // -- if that dispatch re-resolved language on its own, it would find no conversation state
        // left to fall back on (just deleted) and wrongly use the business's default language for a
        // message that, moments earlier in this same call, clearly resolved to the customer's actual
        // language.
        var lang = await ResolveLanguage(business.Id, fromPhone, incomingMsg, (business.ChatbotDefaultLanguage ?? business.Language).ToString());
        // Whatever language the bot is about to reply in is the one our own later messages to this
        // customer (waitlist) use too.
        await RememberCustomerLanguage(fromPhone, lang);

        if (business.ChatbotInquiryEnabled)
        {
            var (handled, inquiryReply) = await HandleInquiryModeAsync(business, fromPhone, profileName, incomingMsg, lang);
            if (handled) return inquiryReply;
        }

        // Past the per-phone daily cap (AiUsageLimiter, checked right before the OpenAI call) the
        // free rule-based flow answers instead.
        if (!string.IsNullOrEmpty(config["OpenAI:ApiKey"]))
        {
            try
            {
                return await ProcessMessageWithAiAsync(business, appUrl, fromPhone, profileName, incomingMsg, lang);
            }
            catch (AiDailyCapReachedException) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "OpenAI chatbot path failed for business {BusinessId}, falling back to rule-based", business.Id);
            }
        }

        return await ProcessMessageRuleBasedAsync(business, appUrl, fromPhone, profileName, incomingMsg, lang);
    }

    // Checked before either chatbot path (rule-based or AI) runs, for any business with
    // ChatbotInquiryEnabled on -- lets a customer step out of the normal booking flow to ask a
    // free-form question / reach the owner directly. A brand-new conversation opens on a plain
    // "1"/"2" gate (Book a service / Talk to the owner) rather than the service list directly;
    // once past the gate, "$1"/"$2" (dollar-prefixed, to stay unambiguous against a plain numeric
    // service-selection reply) switch between the two sections for the rest of the conversation.
    // Returns Handled=true with the reply to send when this message was about inquiry-mode
    // navigation or content; Handled=false means "not mine, let the normal booking dispatch handle
    // this message" (a customer already past the gate, answering the real service list).
    private async Task<(bool Handled, string? Reply)> HandleInquiryModeAsync(
        Business business, string fromPhone, string profileName, string incomingMsg, string lang)
    {
        // Normalized so a customer replying in Arabic-Indic digits (e.g. "١" for the gate, or "$١"
        // for the mode-switch command) matches just as well as ASCII -- same reasoning as
        // TryHandleServiceSelectionReply's own NormalizeDigits call below.
        var trimmed = NormalizeDollarCommand(NormalizeDigits(incomingMsg.Trim()));
        var state = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == business.Id && s.Phone == fromPhone);
        var isLive = state is not null && state.ExpiresAt > DateTime.UtcNow;

        // Locked out: only "$" unlocks, which the booking paths below handle. "$2" used to
        // escape the lockout here.
        if (isLive && state!.InvalidAttempts >= MaxInvalidAttempts) return (false, null);

        if (trimmed == BookingModeCommand)
        {
            // Removed outright, not just flipped back to Booking mode -- a merely-updated row
            // would still look "open" to TryHandleServiceSelectionReply below, which would then
            // try to parse the literal text "$1" as a numeric service choice (fails) instead of
            // falling through to a fresh prompt. Same idiom as the reschedule keyword's own
            // ClearConversationState call.
            // Also at the opening Book/Ask gate: left in place, the gate's state row made the
            // booking dispatch read "$1" as a service number and answer "didn't recognize" (QA).
            // And on the service list itself, where the leftover row made "$1" an invalid service
            // number that counted toward the lockout (QA). Also with a booking link pending: that
            // row used to be kept, so "$1" got silence and a customer who had gone into Inquiry
            // mode after the link stayed there for 24 h (QA run 3). The link itself still works.
            if (isLive)
                await ClearConversationState(business.Id, fromPhone);
            // Falls through to the normal booking dispatch, which starts a fresh prompt for a
            // phone with no (or an expired) conversation state -- exactly what "back to booking"
            // should feel like, with no separate reply of its own needed here. Since
            // ChatbotInquiryEnabled is on, that fresh prompt is this same method's own gate below,
            // reached on the customer's *next* message (this call returns unhandled).
            return (false, null);
        }

        if (trimmed == InquiryModeCommand)
        {
            await EnterInquiryModeAsync(business.Id, fromPhone, lang);
            return (true, I18nService.T(lang, "whatsapp.inquiryStarted", new() { ["businessName"] = business.Name }));
        }

        if (isLive && state!.ChatbotMode == InquiryMode)
        {
            db.ChatbotInquiries.Add(new ChatbotInquiry
            {
                BusinessId = business.Id,
                CustomerPhone = fromPhone,
                CustomerName = string.IsNullOrWhiteSpace(profileName) ? null : profileName,
                Message = incomingMsg.Length > MaxInquiryMessageChars ? incomingMsg[..MaxInquiryMessageChars] : incomingMsg,
            });

            // Only the first message of a session pings the owner -- InquiryNotified is reset
            // whenever the customer returns to Booking mode (above), so a later, separate inquiry
            // still notifies again.
            var shouldNotify = !state.InquiryNotified;
            state.InquiryNotified = true;
            state.ExpiresAt = DateTime.UtcNow.Add(InquiryStateLifetime);
            await db.SaveChangesAsync();

            if (shouldNotify)
                QueueInquiryNotifications(business, fromPhone, profileName,
                    incomingMsg.Length > MaxInquiryMessageChars ? incomingMsg[..MaxInquiryMessageChars] : incomingMsg);

            return (true, I18nService.T(lang, "whatsapp.inquiryAck"));
        }

        // Not in Inquiry mode and not a $-command -- either a brand-new conversation, or one still
        // sitting at the gate (an unrecognized first reply re-shows it). Once past the gate
        // (ChatbotMode == Booking), fall through and let normal booking dispatch handle everything,
        // including plain numeric service-selection replies.
        if (!isLive || state!.ChatbotMode == AwaitingModeChoiceMode)
        {
            // Cancelling or rescheduling isn't "booking" or "asking": the gate used to stand in the
            // way until the customer picked "Book a service" (QA run 3).
            if (ContainsKeyword(incomingMsg, CancelKeywords) || ContainsKeyword(incomingMsg, RescheduleKeywords))
            {
                if (isLive) await ClearConversationState(business.Id, fromPhone);
                return (false, null);
            }
            if (trimmed == "1")
            {
                // Cleared, not just flipped to Booking -- same reasoning as the $1 branch above:
                // TryHandleServiceSelectionReply must see no open state and let PromptServiceSelection
                // start a genuinely fresh service list, not try to parse this gate reply as one.
                if (isLive) await ClearConversationState(business.Id, fromPhone);
                return (false, null);
            }
            if (trimmed == "2")
            {
                await EnterInquiryModeAsync(business.Id, fromPhone, lang);
                return (true, I18nService.T(lang, "whatsapp.inquiryStarted", new() { ["businessName"] = business.Name }));
            }

            if (state is null)
            {
                state = new WhatsAppConversationState { BusinessId = business.Id, Phone = fromPhone };
                db.WhatsAppConversationStates.Add(state);
            }
            state.ChatbotMode = AwaitingModeChoiceMode;
            state.Language = lang;
            state.ExpiresAt = DateTime.UtcNow.Add(ConversationStateLifetime);
            await db.SaveChangesAsync();
            return (true, BuildModeGateMessage(business, lang));
        }

        return (false, null);
    }

    // An RTL keyboard often puts the "$" after the digit ("1$"), which is also how "$1" used to
    // display inside Hebrew/Arabic text, so both orders work.
    private static string NormalizeDollarCommand(string message) =>
        message.Length == 2 && message[1] == '$' && char.IsAsciiDigit(message[0]) ? $"${message[0]}" : message;

    private async Task EnterInquiryModeAsync(string businessId, string phone, string lang)
    {
        var state = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == businessId && s.Phone == phone);
        if (state is null)
        {
            state = new WhatsAppConversationState { BusinessId = businessId, Phone = phone };
            db.WhatsAppConversationStates.Add(state);
        }
        state.ChatbotMode = InquiryMode;
        state.Language = lang;
        state.InquiryNotified = false;
        state.InvalidAttempts = 0;
        state.AwaitingBookingCompletion = false;
        state.ExpiresAt = DateTime.UtcNow.Add(InquiryStateLifetime);
        await db.SaveChangesAsync();
    }

    // The very first thing a customer sees when Business.ChatbotInquiryEnabled is on -- a plain
    // "1"/"2" choice, not the service list yet. A custom welcome message replaces just the
    // greeting, same pattern as PromptServiceSelection's own welcomeMessage handling.
    private static string BuildModeGateMessage(Business business, string lang)
    {
        var welcomeMessage = business.ResolveChatbotWelcomeMessage(lang);
        var tail = I18nService.T(lang, "whatsapp.modeGatePrompt");
        return string.IsNullOrWhiteSpace(welcomeMessage)
            ? I18nService.T(lang, "whatsapp.modeGate", new() { ["businessName"] = business.Name })
            : $"{welcomeMessage}\n\n{tail}";
    }

    // Best-effort on both channels -- a failed notification must never break the customer's own
    // reply, and the inquiry itself is always saved to ChatbotInquiry regardless (the "in-app"
    // channel, which is always on). WhatsApp goes out from the business's own bot number to
    // whatever number the owner registered for inquiries -- a different recipient than the
    // customer, not a reply in the same thread. Email goes through the platform's own Gmail
    // (IOwnerEmailSender), like every other email to a business owner -- not the Brevo chain.
    // Both go out after the reply is returned (NotificationQueue).
    private void QueueInquiryNotifications(Business business, string fromPhone, string profileName, string message)
    {
        var fromLabel = string.IsNullOrWhiteSpace(profileName) ? fromPhone : $"{profileName} ({fromPhone})";

        if (business.InquiryNotifyViaWhatsApp && !string.IsNullOrWhiteSpace(business.InquiryWhatsAppNumber))
            notifications.AddWhatsApp(business, business.InquiryWhatsAppNumber,
                $"📩 New inquiry from {fromLabel}:\n\n{message}", $"WhatsApp inquiry notification for business {business.Id}");

        if (business.InquiryNotifyViaEmail && !string.IsNullOrWhiteSpace(business.InquiryEmail))
            notifications.AddOwnerEmail(business.InquiryEmail, $"New customer inquiry — {business.Name}",
                $"From: {fromLabel}\n\nMessage:\n{message}", $"email inquiry notification for business {business.Id}");
    }

    // Everything from cancel/reschedule keyword matching through numbered service-selection
    // dispatch -- now the fallback path when OpenAI isn't configured or fails. Gated by the
    // too-many-invalid-replies lockout below (new behavior; the rest is otherwise unchanged).
    private async Task<string?> ProcessMessageRuleBasedAsync(Business business, string appUrl, string fromPhone, string profileName, string incomingMsg, string lang)
    {
        var welcomeMessage = business.ResolveChatbotWelcomeMessage(lang);
        var confirmationMessage = business.ResolveChatbotConfirmationMessage(lang);

        var conversationState = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s =>
            s.BusinessId == business.Id && s.Phone == fromPhone && s.ExpiresAt > DateTime.UtcNow);
        if (conversationState is not null)
        {
            // A booking link was already issued and not yet completed -- stay quiet instead of
            // re-sending the opening prompt for every message the customer sends while they finish
            // booking on the web page the link opened. BookingController clears this once the
            // appointment is actually created (or it just expires with the row).
            // Cancel and reschedule still get through (QA: for 24 h after a link the bot ignored
            // even "cancel"), as does the answer to the cancel confirmation they start.
            if (conversationState.AwaitingBookingCompletion
                && !conversationState.ChatbotMode.StartsWith(ConfirmCancelModePrefix)
                && !ContainsKeyword(incomingMsg, CancelKeywords)
                && !ContainsKeyword(incomingMsg, RescheduleKeywords))
                return null;

            // A customer who's sent 3+ non-numeric/out-of-range replies in a row gets locked out of
            // automated replies entirely -- including cancel/reschedule keywords -- until they send
            // the literal unlock keyword, which restarts the conversation from the opening prompt.
            // Every message they send while locked out re-sends the "$"-unlock reminder (rather than
            // going silent) so a customer who missed/forgot the first one doesn't just keep texting
            // into a bot that looks broken -- see the AI path's identical mirror below.
            if (conversationState.InvalidAttempts >= MaxInvalidAttempts)
            {
                if (incomingMsg.Trim() != UnlockKeyword)
                    return I18nService.T(lang, "whatsapp.tooManyInvalidReplies", new() { ["unlockKeyword"] = UnlockKeyword });
                db.WhatsAppConversationStates.Remove(conversationState);
                await db.SaveChangesAsync();
                return await PromptServiceSelection(business.Id, business.Name, fromPhone, lang, welcomeMessage, business.ChatbotInquiryEnabled);
            }
        }

        if (conversationState is not null && conversationState.ChatbotMode.StartsWith(ConfirmCancelModePrefix))
        {
            var confirmationReply = await TryHandleCancelConfirmation(business, conversationState, fromPhone, incomingMsg, lang, welcomeMessage);
            if (confirmationReply is not null) return confirmationReply;
            // Not an answer, and a booking link is still pending: back to quiet mode.
            if (conversationState.AwaitingBookingCompletion
                && !ContainsKeyword(incomingMsg, CancelKeywords) && !ContainsKeyword(incomingMsg, RescheduleKeywords))
                return null;
        }

        if (ContainsKeyword(incomingMsg, CancelKeywords))
            return await HandleCancel(business.Id, business.Name, fromPhone, lang, welcomeMessage, business.ChatbotInquiryEnabled);

        if (ContainsKeyword(incomingMsg, RescheduleKeywords))
        {
            await ClearConversationState(business.Id, fromPhone);
            // An upcoming appointment is moved on its own page (the reminder link), not replaced by
            // a second booking: the customer used to end up holding both slots (QA run 3).
            var (toMove, _) = await FindUpcomingAppointments(business.Id, fromPhone);
            if (toMove is not null)
            {
                var moveArgs = DateTimeArgs(toMove);
                moveArgs["service"] = ItemName(toMove.Item, lang);
                moveArgs["url"] = $"{appUrl}/{business.Slug}/appointments/{toMove.Id}?token={toMove.CancelToken}";
                return I18nService.T(lang, "whatsapp.rescheduleLink", moveArgs);
            }
            var intro = I18nService.T(lang, "whatsapp.rescheduleIntro");
            return $"{intro}\n\n{await PromptServiceSelection(business.Id, business.Name, fromPhone, lang, welcomeMessage, business.ChatbotInquiryEnabled)}";
        }

        // Either a fresh conversation (no state row yet -- falls through to the prompt below)
        // or a reply to an already-open "which service?" prompt (a numeric selection or junk).
        var selectionReply = await TryHandleServiceSelectionReply(business.Id, business.Slug, appUrl, fromPhone, profileName, lang, incomingMsg, confirmationMessage);
        return selectionReply ?? await PromptServiceSelection(business.Id, business.Name, fromPhone, lang, welcomeMessage, business.ChatbotInquiryEnabled);
    }

    // The customer's message is understood by an LLM instead of fixed keywords/numeric replies --
    // e.g. "actually can we move it to Thursday" works, not just the literal word "reschedule".
    // Design guardrail: the model decides WHEN to call a tool, but never composes the text for a
    // completed action itself -- IssueBookingLink/FindAndCancelUpcomingAppointment build the exact
    // same I18nService-templated text the rule-based path uses, and the system prompt instructs the
    // model to relay a tool's "message" field verbatim. This is what prevents a hallucinated URL,
    // price, or date from ever reaching a customer; the model only freely composes text for
    // open-ended Q&A grounded in the business data injected into the system prompt.
    private async Task<string?> ProcessMessageWithAiAsync(Business business, string appUrl, string fromPhone, string profileName, string incomingMsg, string lang)
    {
        var state = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == business.Id && s.Phone == fromPhone && s.ExpiresAt > DateTime.UtcNow);

        // Same "$"-unlock lockout the rule-based path uses (see ProcessMessageRuleBasedAsync) --
        // reuses the same InvalidAttempts field, just counted differently: here a turn counts as
        // "invalid" only when the model flags the message as one it couldn't understand (the
        // mark_unclear tool). Questions it answers don't count (QA: three plain questions locked
        // the customer out); AiUsageLimiter caps the cost of chatting instead.
        if (state is not null && state.InvalidAttempts >= MaxInvalidAttempts)
        {
            if (incomingMsg.Trim() != UnlockKeyword)
                return I18nService.T(lang, "whatsapp.tooManyInvalidReplies", new() { ["unlockKeyword"] = UnlockKeyword });
            await ClearConversationState(business.Id, fromPhone);
            return I18nService.T(lang, "whatsapp.aiConversationRestarted");
        }

        var history = state?.HistoryJson is not null
            ? JsonSerializer.Deserialize<List<OpenAiTurn>>(state.HistoryJson) ?? []
            : [];
        if (incomingMsg.Length > MaxAiMessageChars) incomingMsg = incomingMsg[..MaxAiMessageChars];

        // The answer to a "reply 1 to cancel / 2 to keep" question the cancel tool asked last turn.
        // Handled here without OpenAI, like the rule-based path; any other reply drops the question
        // (nothing cancelled) and goes to the model as a normal message. The row and its history
        // are kept, unlike the rule-based path's ClearConversationState.
        if (state is not null && state.ChatbotMode.StartsWith(ConfirmCancelModePrefix))
        {
            var appointmentId = state.ChatbotMode[ConfirmCancelModePrefix.Length..];
            state.ChatbotMode = BookingMode;
            var answer = ConfirmationAnswer(incomingMsg);
            if (answer is "1" or "2")
            {
                var confirmReply = answer == "2"
                    ? I18nService.T(lang, "whatsapp.cancelKept")
                    : await CancelConfirmedAppointment(business.Id, appointmentId, fromPhone, lang)
                        ?? I18nService.T(lang, "whatsapp.noAppointment");
                history.Add(new OpenAiTurn("user", incomingMsg));
                history.Add(new OpenAiTurn("assistant", confirmReply));
                if (history.Count > 12) history = history[^12..];
                state.HistoryJson = JsonSerializer.Serialize(history);
                state.InvalidAttempts = 0;
                state.ExpiresAt = DateTime.UtcNow.Add(ConversationStateLifetime);
                await db.SaveChangesAsync();
                return confirmReply;
            }
        }

        // Counted here, after the turns answered without OpenAI (lockout reminder, "$", the 1/2
        // answer), which used to use up the daily allowance too (QA run 3).
        if (!aiUsage.TryUse(business.Id, fromPhone, DateTime.UtcNow)) throw new AiDailyCapReachedException();

        var systemPrompt = await BuildAiSystemPrompt(business, lang);
        List<OpenAiToolDefinition> tools =
        [
            new("create_booking_link",
                "Create a booking link once the customer has chosen a specific service.",
                """{"type":"object","properties":{"itemId":{"type":"string"}},"required":["itemId"]}"""),
            new("cancel_upcoming_appointment",
                "Ask the customer to confirm cancelling their next upcoming appointment with this business. Nothing is cancelled until they reply 1.",
                """{"type":"object","properties":{}}"""),
            new("mark_unclear",
                "Call this when you can't understand the customer's message or it has nothing to do with this business, then ask them to rephrase.",
                """{"type":"object","properties":{}}"""),
        ];

        var unclear = false;
        string? confirmCancelId = null; // set when the cancel tool asked its 1/2 question this turn
        string? toolMessage = null; // text of the last tool that actually did something
        string? cancelQuestion = null; // the cancel tool's own text, which the customer must see as-is
        var linkIssued = false;
        var toolTexts = new List<string>(); // every tool result this turn, for the URL check below
        async Task<string> ExecuteTool(string name, string argsJson)
        {
            if (name == "mark_unclear")
            {
                unclear = true;
                return JsonSerializer.Serialize(new { ok = true });
            }
            // The 1/2 question has to be the last thing the customer reads this turn.
            if (name == "create_booking_link" && cancelQuestion is not null)
                return JsonSerializer.Serialize(new { error = "Wait until the customer confirms the cancellation before creating a booking link." });
            // The other order: the forced 1/2 question would hide the link just issued (QA run 3).
            if (name == "cancel_upcoming_appointment" && linkIssued)
                return JsonSerializer.Serialize(new { error = "A booking link was just sent. Ask about cancelling in your next message." });

            string result;
            if (name == "cancel_upcoming_appointment")
            {
                var (cancelResult, appointmentId) = await ExecuteCancelUpcomingAppointment(business.Id, fromPhone, lang);
                result = cancelResult;
                confirmCancelId = appointmentId ?? confirmCancelId;
                using var cancelParsed = JsonDocument.Parse(cancelResult);
                // Also "already requested", which has a message but no id to park.
                if (cancelParsed.RootElement.TryGetProperty("message", out var question)) cancelQuestion = question.GetString();
            }
            else
            {
                result = name == "create_booking_link"
                    ? await ExecuteCreateBookingLink(business, appUrl, fromPhone, profileName, lang, argsJson)
                    : JsonSerializer.Serialize(new { error = "unknown tool" });
            }
            using var parsed = JsonDocument.Parse(result);
            if (parsed.RootElement.TryGetProperty("message", out var message))
            {
                toolMessage = message.GetString();
                if (name == "create_booking_link") linkIssued = true;
                toolTexts.Add(toolMessage ?? ""); // unescaped, unlike the JSON
            }
            toolTexts.Add(result);
            return result;
        }

        string reply;
        try
        {
            reply = await openAi.GetReplyAsync(systemPrompt, history, incomingMsg, tools, ExecuteTool);
        }
        catch (Exception ex) when (toolMessage is not null)
        {
            // A tool already acted (link issued or appointment cancelled) before OpenAI failed.
            // Falling back to the rule-based path would answer as if nothing happened, so send the
            // tool's own text, which is what the model was told to relay anyway.
            logger.LogError(ex, "OpenAI failed after a tool ran for business {BusinessId}; sending the tool's message", business.Id);
            reply = toolMessage;
        }

        // Whatever the model wrote, the customer must see the exact 1/2 question.
        if (cancelQuestion is not null)
            reply = cancelQuestion;
        else
            reply = StripUnknownUrls(reply, toolTexts);

        history.Add(new OpenAiTurn("user", incomingMsg));
        history.Add(new OpenAiTurn("assistant", reply.Length > MaxAiMessageChars ? reply[..MaxAiMessageChars] : reply));
        if (history.Count > 12) history = history[^12..];

        if (state is null)
        {
            state = new WhatsAppConversationState { BusinessId = business.Id, Phone = fromPhone };
            db.WhatsAppConversationStates.Add(state);
        }
        state.Language = lang;
        state.HistoryJson = JsonSerializer.Serialize(history);
        state.ExpiresAt = DateTime.UtcNow.Add(ConversationStateLifetime);
        // A list the rule-based fallback showed earlier is no longer what the customer is looking
        // at, so a later fallback must not read a bare number against it.
        state.ShownItemIds = null;
        if (confirmCancelId is not null)
            state.ChatbotMode = ConfirmCancelModePrefix + confirmCancelId;

        // A turn where a tool really acted isn't an unclear one, even if the model also flagged it
        // (QA run 3: the lockout text could replace a link that had just been issued).
        if (!unclear || toolMessage is not null)
        {
            state.InvalidAttempts = 0;
        }
        else
        {
            state.InvalidAttempts++;
            if (state.InvalidAttempts >= MaxInvalidAttempts)
                reply = I18nService.T(lang, "whatsapp.tooManyInvalidReplies", new() { ["unlockKeyword"] = UnlockKeyword });
        }

        await db.SaveChangesAsync();

        return reply;
    }

    private async Task<string> BuildAiSystemPrompt(Business business, string lang)
    {
        var services = await ActiveServices(business.Id, lang);
        // Price and length too: without them every price question was refused or made up (QA run 3).
        var details = await db.Items.Where(i => i.BusinessId == business.Id && i.IsActive && i.IsBookable)
            .ToDictionaryAsync(i => i.Id, i => (i.Price, i.DurationMinutes));
        var itemsList = services.Count == 0
            ? "(no bookable services configured yet)"
            : string.Join("\n", services.Select(s =>
            {
                var (price, minutes) = details.GetValueOrDefault(s.Id);
                var priceText = price is null ? "price on request" : $"₪{price:0.##}";
                return $"- id={s.Id}: {s.Name} ({priceText}{(minutes is null ? "" : $", {minutes} min")})";
            }));
        var hours = await availability.GetUpcomingHours(business.Id, 7);
        var hoursList = string.Join("\n", hours.Select(h =>
            $"- {h.Date} ({(DayOfWeek)h.DayOfWeek}): {(h.StartTime is null ? "closed" : $"{h.StartTime}-{h.EndTime}")}"));
        var address = string.Join(", ", new[] { business.AddressLine, business.City }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var locationText = address.Length == 0 && business.MapUrl is null ? "(not provided)"
            : $"{(address.Length == 0 ? "" : address)}{(business.MapUrl is null ? "" : $" {business.MapUrl}")}".Trim();
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

            Opening hours, next 7 days (exact free times are chosen on the booking page):
            {hoursList}

            Address: {locationText}

            Tools:
            - create_booking_link(itemId): call this once the customer has picked a specific service. Its result includes a "message" field -- output that text to the customer VERBATIM, do not reword it or invent your own link, price, or service name.
            - cancel_upcoming_appointment(): call this when the customer wants to cancel their appointment. It doesn't cancel anything yet: it asks the customer to reply 1 to cancel or 2 to keep it. If the result's "found" field is true, it also has a "message" field -- output that text VERBATIM. If "found" is false, tell the customer yourself that you couldn't find an upcoming appointment and offer to help them book one.
            - mark_unclear(): call this only when you can't understand the message or it has nothing to do with this business, then ask the customer to rephrase. Don't call it for questions you can answer.
            To reschedule: call cancel_upcoming_appointment first; once the customer has confirmed, help them book a new time via create_booking_link.

            Keep replies short and friendly. Never invent prices, links, dates, or services that aren't listed above or returned by a tool.
            Never say an appointment was cancelled, booked or changed unless a tool's message in this turn says so. A cancellation only happens when the customer replies 1 to the confirmation question; if they answer it any other way, ask them to reply 1 or 2.{inquiryNote}
            """;
    }

    private async Task<string> ExecuteCreateBookingLink(Business business, string appUrl, string fromPhone, string profileName, string lang, string argsJson)
    {
        // Read leniently: {"itemId":123} or null used to throw and drop the whole turn (QA run 3).
        string itemId;
        try
        {
            using var args = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
            itemId = args.RootElement.ValueKind == JsonValueKind.Object && args.RootElement.TryGetProperty("itemId", out var id)
                ? (id.ValueKind == JsonValueKind.String ? id.GetString() ?? "" : id.ToString())
                : "";
        }
        catch (JsonException) { itemId = ""; }
        var services = await ActiveServices(business.Id, lang);
        var chosen = services.FirstOrDefault(s => s.Id == itemId);
        if (chosen.Id is null)
            return JsonSerializer.Serialize(new { error = "unknown itemId" });

        var message = await IssueBookingLink(business.Id, business.Slug, appUrl, fromPhone, profileName, lang, chosen, business.ResolveChatbotConfirmationMessage(lang));
        return JsonSerializer.Serialize(new { message });
    }

    // Names the appointment and asks 1/2 (same text as the rule-based HandleCancel); the caller
    // parks the returned id in ChatbotMode for the answer. Cancels nothing itself.
    private async Task<(string Result, string? AppointmentId)> ExecuteCancelUpcomingAppointment(string businessId, string fromPhone, string lang)
    {
        var (upcoming, pending) = await FindUpcomingAppointments(businessId, fromPhone);
        if (upcoming is null)
        {
            // Everything left is already waiting on the owner -- say so instead of "no appointment".
            return pending is null
                ? (JsonSerializer.Serialize(new { found = false }), null)
                : (JsonSerializer.Serialize(new { found = true, message = I18nService.T(lang, "whatsapp.cancellationAlreadyRequested", DateTimeArgs(pending)) }), null);
        }

        var args = DateTimeArgs(upcoming);
        args["service"] = ItemName(upcoming.Item, lang);
        return (JsonSerializer.Serialize(new { found = true, message = I18nService.T(lang, "whatsapp.confirmCancel", args) }), upcoming.Id);
    }

    // Detects the language from the incoming message's script (Hebrew/Arabic Unicode blocks --
    // Arabic-Indic digits included, see below -- or Latin letters -> English) so the bot always
    // replies in whatever language the customer just wrote in, regardless of the business's own
    // configured storefront language. A message with no script signal at all (e.g. a bare ASCII "1"
    // reply, punctuation, or an emoji) carries no signal of its own, so it falls back to whatever
    // language the open conversation was already using, and only falls back to the business's
    // default when there's no open conversation either (a fresh, signal-less first message).
    private const char HebrewBlockStart = (char)0x0590;
    private const char HebrewBlockEnd = (char)0x05FF;
    private const char ArabicBlockStart = (char)0x0600;
    private const char ArabicBlockEnd = (char)0x06FF;

    private static string? DetectLanguage(string text)
    {
        if (text.Any(c => c >= HebrewBlockStart && c <= HebrewBlockEnd)) return "HE";
        // Arabic-Indic/Extended Arabic-Indic digits (0x0660-0x0669 / 0x06F0-0x06F9) sit inside this
        // same Unicode block and DO count as an Arabic signal here -- confirmed with the business
        // owner: unlike ASCII digits, a customer only sends these because their keyboard/locale is
        // actually set to Arabic, so a bare Arabic-Indic-digit reply (e.g. "١" to a numbered prompt)
        // should switch/keep the conversation in Arabic, not fall through to the default language.
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

    private static bool IsArabicIndicDigit(char c) =>
        (c >= ArabicIndicDigitStart && c <= ArabicIndicDigitEnd)
        || (c >= ExtendedArabicIndicDigitStart && c <= ExtendedArabicIndicDigitEnd);

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

    private async Task RememberCustomerLanguage(string fromPhone, string replyLanguage)
    {
        var phone = PhoneNormalizer.Normalize(fromPhone);
        var account = await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Phone == phone);
        if (account is null || account.LastMessageLanguage == replyLanguage) return;

        account.LastMessageLanguage = replyLanguage;
        await db.SaveChangesAsync();
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

    // Rule-based "cancel" keyword: never cancels directly. Names the appointment it would cancel and
    // waits for "1" (cancel) / "2" (keep) -- see TryHandleCancelConfirmation.
    private async Task<string> HandleCancel(string businessId, string businessName, string fromPhone, string lang, string? welcomeMessage, bool inquiryEnabled)
    {
        var (upcoming, pending) = await FindUpcomingAppointments(businessId, fromPhone);

        if (upcoming is null)
        {
            await ClearConversationState(businessId, fromPhone);
            // Everything left is already waiting on the owner -- say so instead of "no appointment".
            if (pending is not null)
                return I18nService.T(lang, "whatsapp.cancellationAlreadyRequested", DateTimeArgs(pending));
            var intro = I18nService.T(lang, "whatsapp.noAppointment");
            return $"{intro}\n\n{await PromptServiceSelection(businessId, businessName, fromPhone, lang, welcomeMessage, inquiryEnabled)}";
        }

        var state = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == businessId && s.Phone == fromPhone);
        if (state is null)
        {
            state = new WhatsAppConversationState { BusinessId = businessId, Phone = fromPhone };
            db.WhatsAppConversationStates.Add(state);
        }
        state.ChatbotMode = ConfirmCancelModePrefix + upcoming.Id;
        state.Language = lang;
        state.InvalidAttempts = 0;
        // A pending booking link stays pending: the bot stays quiet after the question is answered
        // (QA run 3: it re-sent the list and later chatter counted toward the lockout).
        if (!state.AwaitingBookingCompletion)
            state.ExpiresAt = DateTime.UtcNow.Add(ConversationStateLifetime);
        await db.SaveChangesAsync();

        var args = DateTimeArgs(upcoming);
        args["service"] = ItemName(upcoming.Item, lang);
        return I18nService.T(lang, "whatsapp.confirmCancel", args);
    }

    // Answers an open "reply 1 to cancel / 2 to keep" question. Returns null for any other reply:
    // the question is dropped (nothing cancelled) and the message is handled as a normal one.
    private async Task<string?> TryHandleCancelConfirmation(Business business, WhatsAppConversationState state, string fromPhone, string incomingMsg, string lang, string? welcomeMessage)
    {
        var appointmentId = state.ChatbotMode[ConfirmCancelModePrefix.Length..];
        var answer = ConfirmationAnswer(incomingMsg);
        if (state.AwaitingBookingCompletion)
        {
            state.ChatbotMode = BookingMode;
            await db.SaveChangesAsync();
        }
        else await ClearConversationState(business.Id, fromPhone);

        if (answer == "2") return I18nService.T(lang, "whatsapp.cancelKept");
        if (answer != "1") return null;

        var cancelReply = await CancelConfirmedAppointment(business.Id, appointmentId, fromPhone, lang);
        if (cancelReply is not null) return cancelReply;
        var intro = I18nService.T(lang, "whatsapp.noAppointment");
        return $"{intro}\n\n{await PromptServiceSelection(business.Id, business.Name, fromPhone, lang, welcomeMessage, business.ChatbotInquiryEnabled)}";
    }

    // A "1" to the cancel question, shared by both paths. Null when the appointment changed since
    // the question was asked (already cancelled, moved into the past, or already waiting on the
    // owner) -- nothing left to cancel.
    private async Task<string?> CancelConfirmedAppointment(string businessId, string appointmentId, string fromPhone, string lang)
    {
        var appointment = await db.Appointments
            .Include(a => a.Customer)
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.BusinessId == businessId && a.Customer.Phone == fromPhone);
        if (appointment is null || appointment.Status != AppointmentStatus.CONFIRMED || appointment.PendingCancellationApproval
            || AppointmentStatusHelper.EffectiveStatus(appointment.Status, appointment.Date, appointment.EndTime) != "CONFIRMED")
            return null;

        if (!await cancellationService.CancelFromCustomerAsync(appointment)) return null;
        await db.SaveChangesAsync();
        return CancelResultMessage(appointment, lang);
    }

    // Shared by the rule-based cancel and the AI path's cancel_upcoming_appointment tool. With
    // owner approval required, CancelFromCustomerAsync only freezes the appointment
    // (PendingCancellationApproval) -- the reply must say "request sent", not "cancelled".
    private static string CancelResultMessage(Appointment appointment, string lang) =>
        I18nService.T(lang, appointment.PendingCancellationApproval ? "whatsapp.cancellationRequested" : "whatsapp.cancelled", DateTimeArgs(appointment));

    private static Dictionary<string, string> DateTimeArgs(Appointment appointment) => new()
    {
        ["date"] = appointment.Date.ToString("yyyy-MM-dd"),
        ["time"] = appointment.StartTime,
    };

    private static string ItemName(Item item, string lang) => lang switch
    {
        "AR" => item.NameAr,
        "HE" => item.NameHe,
        _ => item.NameEn,
    };

    // The customer's nearest upcoming appointment that can still be cancelled, plus (separately)
    // the nearest one already waiting on owner approval, so callers can tell "nothing booked" apart
    // from "already asked". Ordered by date then start time, so the earlier of two same-day
    // appointments comes first.
    private async Task<(Appointment? Upcoming, Appointment? PendingApproval)> FindUpcomingAppointments(string businessId, string fromPhone)
    {
        var customer = await db.Customers
            // a.Date is a calendar date (local wall-clock, never UTC-converted), so compare
            // against local "today" as a date — not DateTime.UtcNow, which is both the wrong
            // clock and, being a timestamp rather than a date, would already exclude today's
            // appointments as soon as any time had passed since UTC midnight.
            .Include(c => c.Appointments.Where(a => a.Status == AppointmentStatus.CONFIRMED && a.Date >= DateTime.Now.Date))
                .ThenInclude(a => a.Item)
            .FirstOrDefaultAsync(c => c.BusinessId == businessId && c.Phone == fromPhone);

        var upcoming = (customer?.Appointments ?? [])
            .Where(a => AppointmentStatusHelper.EffectiveStatus(a.Status, a.Date, a.EndTime) == "CONFIRMED")
            .OrderBy(a => a.Date).ThenBy(a => a.StartTime)
            .ToList();
        return (upcoming.FirstOrDefault(a => !a.PendingCancellationApproval), upcoming.FirstOrDefault(a => a.PendingCancellationApproval));
    }

    // "1"/"2" for an answer to the cancel question: the digit itself, or a yes/no word (alone or
    // with a cancel word, "כן, תבטל"). Null for anything else.
    private static string? ConfirmationAnswer(string message)
    {
        var answer = NormalizeDigits(message.Trim());
        if (answer is "1" or "2") return answer;
        var words = Regex.Split(answer.ToLowerInvariant(), @"[^\p{L}\p{Mn}\p{Nd}]+").Where(w => w.Length > 0).ToList();
        if (words.Count == 0 || words.Count > 4) return null;
        if (YesWords.Contains(words[0]) && !words.Any(NoWords.Contains)) return "1";
        if (NoWords.Contains(words[0]) && !words.Any(YesWords.Contains)) return "2";
        return null;
    }

    // Defence in depth: a link in the model's own text must be one a tool returned this turn, so a
    // prompt-injected or made-up URL never reaches the customer (QA run 3).
    private static string StripUnknownUrls(string reply, List<string> toolTexts) =>
        Regex.Replace(reply, @"https?://[^\s)\]>""']+", m => toolTexts.Any(t => t.Contains(m.Value)) ? m.Value : "").Trim();

    private sealed class AiDailyCapReachedException : Exception;

    private static string TruncateName(string? name) =>
        name is null ? "" : name.Length > Filters.ValidateInputFilter.MaxNameLength ? name[..Filters.ValidateInputFilter.MaxNameLength] : name;

    private static bool ContainsKeyword(string message, string[] keywords) =>
        Regex.Split(message.ToLowerInvariant(), @"[^\p{L}\p{Mn}\p{Nd}]+").Any(word => keywords.Contains(word));

    private async Task ClearConversationState(string businessId, string phone)
    {
        var existing = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == businessId && s.Phone == phone);
        if (existing is null) return;
        db.WhatsAppConversationStates.Remove(existing);
        await db.SaveChangesAsync();
    }

    // Only bookable items make sense to offer through the booking chatbot -- a showcase-only
    // item has nothing for this flow to schedule.
    // Alphabetical in the customer's language -- ordering by Id looked random to owners.
    private async Task<List<(string Id, string Name)>> ActiveServices(string businessId, string lang)
    {
        var services = await db.Items
            .Where(s => s.BusinessId == businessId && s.IsActive && s.IsBookable)
            .ToListAsync();
        return services
            .Select(s => (s.Id, Name: lang switch
            {
                "AR" => s.NameAr,
                "HE" => s.NameHe,
                _ => s.NameEn,
            }))
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.Id)
            .ToList();
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
        existing.ShownItemIds = string.Join(",", services.Select(s => s.Id));
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
        // Only a row that actually showed a service list is waiting for a number. An AI
        // conversation (daily cap reached or OpenAI down), the Book/Ask gate or Inquiry mode
        // (the owner turned Inquiry off meanwhile) used to be read as one: free text counted
        // toward the lockout and a bare "2" booked a service the customer never saw (QA run 3).
        // Returning null starts a fresh list instead.
        if (state is null || state.ChatbotMode != BookingMode || string.IsNullOrEmpty(state.ShownItemIds)) return null;

        var services = await ActiveServices(businessId, lang);
        // The number refers to the list this customer was shown (QA: after the owner turned off
        // "Kids Cut", replying "2" for Haircut booked Beard Trim).
        var shownIds = state.ShownItemIds.Split(',').ToList();
        if (!int.TryParse(NormalizeDigits(message.Trim()), out var index) || index < 1 || index > shownIds.Count)
        {
            state.InvalidAttempts++;
            // So a following bare "1" stays in the language this reply was answered in (QA run 3).
            state.Language = lang;
            await db.SaveChangesAsync();
            return state.InvalidAttempts >= MaxInvalidAttempts
                ? I18nService.T(lang, "whatsapp.tooManyInvalidReplies", new() { ["unlockKeyword"] = UnlockKeyword })
                : I18nService.T(lang, "whatsapp.invalidServiceSelection");
        }

        var chosen = services.FirstOrDefault(s => s.Id == shownIds[index - 1]);
        if (chosen.Id is null)
        {
            // Turned off or no longer bookable since the list was sent -- show the current one.
            var business = (await db.Businesses.FindAsync(businessId))!;
            var intro = I18nService.T(lang, "whatsapp.serviceNoLongerAvailable");
            return $"{intro}\n\n{await PromptServiceSelection(businessId, business.Name, phone, lang, null, business.ChatbotInquiryEnabled)}";
        }
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
