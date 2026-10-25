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
    BookingRules bookingRules,
    ServiceListPrompt serviceList,
    ILogger<WhatsAppController> logger) : ControllerBase
{
    // Literal commands, not natural language -- deliberately simple so a business can explain them
    // to a customer in one sentence. Only meaningful when Business.ChatbotInquiryEnabled is on; a
    // business that doesn't use the feature sees zero behavior change (these never get checked).
    // "#1"/"#2" since 2026-10-23 (owner's choice); the old "$1"/"$2" still work, since earlier
    // messages in customers' chats name them (NormalizeDollarCommand).
    private const string BookingModeCommand = "#1";
    private const string InquiryModeCommand = "#2";
    private const string BookingMode = ServiceListPrompt.BookingMode;
    private const string InquiryMode = ServiceListPrompt.InquiryMode;
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
        "cancel", "cancelling", "canceling",
        "מבטל", "מבטלת", "אבטל",
        "ביטול", "בטל", "לבטל", "תבטל", "תבטלי", "תבטלו", "בטלו", "בטלי",
        "بطل", "إلغاء", "الغاء", "والغاء", "وإلغاء", "الغي", "ألغي", "إلغي", "الغيه", "ألغيه", "الغيلي", "تلغي", "ألغ", "الغ", "إلغ",
    ];
    // "غير" ("change!") is left out on purpose: it also means "other"/"not" and is very common.
    private static readonly string[] RescheduleKeywords =
    [
        "reschedule", "rescheduling", "change", "move", "postpone",
        "שינוי", "לשנות", "תשנה", "אשנה", "להזיז", "תזיז", "לדחות", "תדחה",
        "تغيير", "اغير", "أغير", "نغير", "تأجيل", "تاجيل",
    ];
    // A cancel keyword never cancels on its own: it asks "reply 1 to cancel / 2 to keep" and parks
    // the appointment id in WhatsAppConversationState.ChatbotMode as "ConfirmCancel:{id}" until the
    // customer answers (see TryHandleCancelConfirmation).
    private const string ConfirmCancelModePrefix = "ConfirmCancel:";
    private static readonly TimeSpan ConversationStateLifetime = ServiceListPrompt.ConversationStateLifetime;
    // An inquiry waits on the owner's answer, so it outlives the 10-minute booking prompt: a
    // follow-up an hour later used to land on the opening gate and was never logged (QA).
    private static readonly TimeSpan InquiryStateLifetime = ServiceListPrompt.InquiryStateLifetime;
    // After this many consecutive non-numeric/out-of-range replies to a "which service?" prompt,
    // the rule-based bot stops replying to anything except the literal "$" unlock keyword -- see
    // the lockout gate at the top of ProcessMessageRuleBasedAsync.
    private const int MaxInvalidAttempts = 3;
    // While locked out, the "$" reminder goes out at most this often; other messages get no reply.
    // It used to answer every message, which kept a customer typing at a wall of reminders. The
    // lock lasts LockoutLifetime from the last reminder (ExpiresAt), so ExpiresAt - LockoutLifetime
    // is when the last one was sent -- no extra column.
    private static readonly TimeSpan LockoutReminderInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LockoutLifetime = TimeSpan.FromHours(24);
    // What the AI path sends to OpenAI per message and keeps per history turn. A few very long
    // messages used to overflow the context window, and every later turn then failed (QA run 3).
    private const int MaxAiMessageChars = 1000;
    // An inquiry is stored, shown in the inbox and forwarded to the owner; 100,000 characters
    // went through in full (QA run 3).
    private const int MaxInquiryMessageChars = 4000;
    // Natural answers to the "reply 1 to cancel / 2 to keep" question, as whole words. "yes" went
    // to the model, which had no tool to act on it (QA run 3).
    private static readonly string[] YesWords = ["yes", "yeah", "yep", "yup", "y", "sure", "כן", "נכון", "בטח", "نعم", "ايوه", "أيوه", "اه", "أكيد", "اكيد"];
    // Words that may follow a yes/no without changing it ("yes please", "no thanks", "keep it").
    private static readonly string[] AnswerFillerWords =
    [
        "please", "pls", "plz", "it", "thanks", "thank", "you", "the", "my", "appointment", "ok", "okay",
        "בבקשה", "תודה", "את", "זה", "אותו", "התור", "תור",
        "من", "فضلك", "لو", "سمحت", "شكرا", "الموعد", "موعد", "اياه",
    ];
    private static readonly string[] NoWords = ["no", "nope", "n", "לא", "لا", "keep", "להשאיר", "השאר"];
    // "#" since 2026-10-23 (owner's choice, like the #1/#2 commands); "$" still works, since earlier
    // lockout messages in customers' chats name it.
    private const string UnlockKeyword = "#";
    private const string OldUnlockKeyword = "$";
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

            if (fromPhone.Length == 0) return Ok(new BridgeInboundResponse(null));

            // The owner's Chats page (ChatsController) shows only the part of a conversation where
            // the customer chose to talk to the owner (Inquiry mode, "#2"): their messages there,
            // and the bot's replies on the way in and out. Saved before the chatbot runs, so the
            // message is there even if it fails.
            var profileName = TruncateName(req.ProfileName);
            var wasInChat = await ChatLog.IsChatOpenAsync(db, business.Id, fromPhone);
            if (wasInChat)
            {
                db.ChatMessages.Add(ChatLog.NewMessage(business.Id, fromPhone, ChatSender.Customer, req.Message.Replace("\0", "").Trim(),
                    string.IsNullOrWhiteSpace(profileName) ? null : profileName));
                await db.SaveChangesAsync();
            }

            var appUrl = config["AppUrl"] ?? "";
            var reply = await ProcessMessageLockedAsync(business, appUrl, fromPhone, profileName, req.Message.Trim());
            if (reply is not null && (wasInChat || await ChatLog.IsChatOpenAsync(db, business.Id, fromPhone)))
            {
                // With the profile name too: a chat that has only this message yet already shows who it is.
                db.ChatMessages.Add(ChatLog.NewMessage(business.Id, fromPhone, ChatSender.Bot, reply,
                    string.IsNullOrWhiteSpace(profileName) ? null : profileName));
                await db.SaveChangesAsync();
            }
            return Ok(new BridgeInboundResponse(reply));
        }
        finally
        {
            conversationLock.Release();
        }
    }

    public record BridgeOutgoingRequest(string BusinessId, string ToPhone, string Message);

    // The owner wrote to a customer on the linked phone itself (the bridge reports those; it skips
    // what it sent for us). Kept for the Chats page when that customer's chat is open; other
    // messages from the phone aren't the platform's business.
    [HttpPost("bridge/outgoing")]
    [RequireBridgeSecret]
    public async Task<IActionResult> BridgeOutgoing([FromBody] BridgeOutgoingRequest req)
    {
        var toPhone = PhoneNormalizer.Normalize(req.ToPhone);
        var message = req.Message.Replace("\0", "").Trim();
        if (toPhone.Length == 0 || message.Length == 0) return BadRequest(new { error = "Invalid request" });
        if (!await db.Businesses.AnyAsync(b => b.Id == req.BusinessId)) return NotFound();
        if (!await ChatLog.IsChatOpenAsync(db, req.BusinessId, toPhone)) return Ok(new { ok = true, recorded = false });

        db.ChatMessages.Add(ChatLog.NewMessage(req.BusinessId, toPhone, ChatSender.OwnerPhone, message));
        await ChatLog.OwnerAnsweredAsync(db, req.BusinessId, toPhone);
        await db.SaveChangesAsync();
        return Ok(new { ok = true, recorded = true });
    }

    public record BridgeSessionEventRequest(string BusinessId, string Event, string? PhoneNumber);

    // The bridge reports a session WhatsApp itself ended ("loggedOut": the device was removed on
    // the phone, or WhatsApp dropped it). The bridge deletes the saved session then, so the bot
    // stays silent until someone links it again -- on 2026-10-20 that went unnoticed overnight.
    // An admin's own Unlink isn't reported. Clears Business.WhatsAppNumber (the admin page then
    // offers "Link WhatsApp" again, QA run 4), logs it on the business's activity feed and emails
    // the platform admin (Brevo, not Gmail). A repeat while the number is still cleared and within
    // 10 minutes (a bridge retry whose first attempt did get through) only answers 200; a logout
    // after a re-link is reported again. One at a time per business, so parallel retries can't
    // both get through the check.
    [HttpPost("bridge/session-event")]
    [RequireBridgeSecret]
    public async Task<IActionResult> BridgeSessionEvent([FromBody] BridgeSessionEventRequest req, [FromServices] PlatformAdminAlerts alerts)
    {
        if (req.Event is not ("loggedOut" or "connected")) return BadRequest(new { error = "Unknown event" });
        var eventLock = LockFor(req.BusinessId, "session-event");
        await eventLock.WaitAsync();
        try
        {
            return req.Event == "connected" ? await HandleConnectedAsync(req) : await HandleLoggedOutAsync(req, alerts);
        }
        finally
        {
            eventLock.Release();
        }
    }

    // The bridge's session is up (after a scan, a restart or a reconnect): the number is saved
    // here too. It used to be written only by the admin page's status poll during a QR link, so a
    // session that came up with nobody watching (2026-10-25: after a logout had cleared it) left
    // WhatsAppNumber empty -- no confirmations or reminders, and Chats said "not linked".
    private async Task<IActionResult> HandleConnectedAsync(BridgeSessionEventRequest req)
    {
        var business = await db.Businesses.FindAsync(req.BusinessId);
        if (business is null) return NotFound();
        if (!PhoneNormalizer.IsValid(req.PhoneNumber)) return BadRequest(new { error = "Invalid request" });

        var number = PhoneNormalizer.Normalize(req.PhoneNumber!);
        if (business.WhatsAppNumber == number) return Ok();
        var old = business.WhatsAppNumber;
        business.WhatsAppNumber = number;
        db.ActivityLogs.Add(new ActivityLog
        {
            BusinessId = business.Id,
            Action = "WhatsAppLink.Connected",
            Description = $"WhatsApp number: \"{old}\" → \"{number}\" (the bot connected)",
            Method = "POST",
            Path = Request.Path.ToString(),
            StatusCode = 200,
        });
        await db.SaveChangesAsync();
        return Ok();
    }

    private async Task<IActionResult> HandleLoggedOutAsync(BridgeSessionEventRequest req, PlatformAdminAlerts alerts)
    {
        var business = await db.Businesses.FindAsync(req.BusinessId);
        if (business is null) return NotFound();

        const string action = "WhatsAppLink.LoggedOut";
        var since = DateTime.UtcNow.AddMinutes(-10);
        if (business.WhatsAppNumber is null
            && await db.ActivityLogs.AnyAsync(a => a.BusinessId == business.Id && a.Action == action && a.CreatedAt > since))
            return Ok();

        // Shown in the row and the email, so only a real phone number.
        var reported = PhoneNormalizer.IsValid(req.PhoneNumber) ? PhoneNormalizer.Normalize(req.PhoneNumber!) : null;
        var number = reported ?? business.WhatsAppNumber;
        business.WhatsAppNumber = null;
        db.ActivityLogs.Add(new ActivityLog
        {
            BusinessId = business.Id,
            Action = action,
            Description = $"WhatsApp bot logged out by WhatsApp: {number ?? "(unknown number)"}. The bot is off until it's linked again",
            Method = "POST",
            Path = Request.Path.ToString(),
            StatusCode = 200,
        });
        await db.SaveChangesAsync();

        var appUrl = config["AppUrl"] ?? "";
        try
        {
            await alerts.SendAsync(
                $"Action needed: WhatsApp bot for {business.Name} was logged out",
                $"WhatsApp logged out the bot number {number ?? "(unknown)"} of \"{business.Name}\" (/{business.Slug}) " +
                $"at {AppClock.Now:yyyy-MM-dd HH:mm} (Israel time).\n\n" +
                "Until it's linked again, the chatbot doesn't answer customers, and reminders, waitlist " +
                "offers and booking confirmations from that number aren't sent.\n\n" +
                "This usually means the device was removed under WhatsApp > Linked devices on the phone, " +
                "or WhatsApp ended the session. To fix it, open the business in the platform admin and " +
                $"click Link WhatsApp, then scan the QR code with that phone:\n{appUrl}/platform-admin/businesses/{business.Id}");
        }
        catch (Exception ex)
        {
            // The activity row is saved, so it still shows on the business page.
            logger.LogError(ex, "Failed to email the WhatsApp logout alert for business {BusinessId}", business.Id);
        }
        return Ok();
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
        // A business the platform disabled gets no replies either (QA run 4: it still handed out
        // its service list).
        if (!business.ChatbotEnabled || business.IsDisabled)
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
                Message = Cut(incomingMsg, MaxInquiryMessageChars),
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
                    Cut(incomingMsg, MaxInquiryMessageChars));

            // No reply: the owner is talking to the customer now, and an automatic answer to every
            // message was spam (owner, 2026-10-23). The one message on entering this mode
            // (whatsapp.inquiryStarted) already says how to get back to booking.
            return (true, null);
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
            var gateChoice = ParseNumber(incomingMsg);
            if (gateChoice == 1)
            {
                // Cleared, not just flipped to Booking -- same reasoning as the $1 branch above:
                // TryHandleServiceSelectionReply must see no open state and let PromptServiceSelection
                // start a genuinely fresh service list, not try to parse this gate reply as one.
                if (isLive) await ClearConversationState(business.Id, fromPhone);
                return (false, null);
            }
            if (gateChoice == 2)
            {
                await EnterInquiryModeAsync(business.Id, fromPhone, lang);
                return (true, I18nService.T(lang, "whatsapp.inquiryStarted", new() { ["businessName"] = business.Name }));
            }

            // A reply to the gate that isn't a choice counts toward the lockout, like one to the
            // service list: it used to re-show the gate forever (owner, 2026-10-25).
            if (isLive)
            {
                state!.InvalidAttempts++;
                state.Language = lang;
                if (state.InvalidAttempts >= MaxInvalidAttempts)
                {
                    var lockout = StartLockout(state, lang);
                    await db.SaveChangesAsync();
                    return (true, lockout);
                }
                state.ExpiresAt = DateTime.UtcNow.Add(ConversationStateLifetime);
                await db.SaveChangesAsync();
                return (true, BuildModeGateMessage(business, lang));
            }
            return (true, await ShowModeGate(business, fromPhone, lang, state));
        }

        return (false, null);
    }

    // Starts a conversation on the Book/Ask gate, with a new count of unclear replies.
    private async Task<string> ShowModeGate(Business business, string fromPhone, string lang, WhatsAppConversationState? state)
    {
        if (state is null)
        {
            state = new WhatsAppConversationState { BusinessId = business.Id, Phone = fromPhone };
            db.WhatsAppConversationStates.Add(state);
        }
        // An expired row keeps its old counters: an ended lockout came straight back after the
        // gate (QA run 4).
        else ResetIfExpired(state);
        state.ChatbotMode = AwaitingModeChoiceMode;
        state.Language = lang;
        state.InvalidAttempts = 0;
        state.ExpiresAt = DateTime.UtcNow.Add(ConversationStateLifetime);
        await db.SaveChangesAsync();
        return BuildModeGateMessage(business, lang);
    }

    // An RTL keyboard often puts the "#" after the digit ("1#"), which is also how "#1" can display
    // inside Hebrew/Arabic text, so both orders work, and the old "$1"/"$2" become "#1"/"#2".
    // Direction marks (our own HE/AR texts wrap "#2" in them, so a copy-paste carries them),
    // spaces, a trailing "." and the fullwidth signs are ignored too (QA run 4). Also used for the
    // "$" unlock.
    private static string NormalizeDollarCommand(string message)
    {
        var text = new string(message.Where(c => !IsDirectionMark(c) && !char.IsWhiteSpace(c))
            .Select(c => c switch { '＄' or '﹩' => '$', '＃' or '﹟' => '#', _ => c }).ToArray()).TrimEnd('.');
        if (text.Length != 2) return text;
        static bool IsSign(char c) => c is '#' or '$';
        if (IsSign(text[0]) && char.IsAsciiDigit(text[1])) return $"#{text[1]}";
        if (IsSign(text[1]) && char.IsAsciiDigit(text[0])) return $"#{text[0]}";
        return text;
    }

    private static bool IsDirectionMark(char c) =>
        c is '‎' or '‏' or '؜' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩');

    private static bool IsUnlock(string message) => NormalizeDollarCommand(message) is UnlockKeyword or OldUnlockKeyword;

    // A numbered reply: "2", "٢", "２", "2.", "#2", "2)" or the keycap emoji "2️⃣" (QA run 4: those
    // counted toward the lockout). Null for anything else.
    private static int? ParseNumber(string message)
    {
        var text = new string(NormalizeDigits(message.Trim())
            .Where(c => c is not ('️' or '⃣') && !IsDirectionMark(c))
            .Select(c => c is >= '０' and <= '９' ? (char)('0' + (c - '０')) : c).ToArray());
        var match = Regex.Match(text, @"^#?(\d{1,3})[.)]?$");
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

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

        // A blank contact falls back to the business's own phone/email, like the cancel-approval
        // channels: switched on with an empty field, nothing was ever sent (QA runs 3 and 4).
        var whatsAppTo = string.IsNullOrWhiteSpace(business.InquiryWhatsAppNumber) ? business.Phone : business.InquiryWhatsAppNumber;
        var emailTo = string.IsNullOrWhiteSpace(business.InquiryEmail) ? business.Email : business.InquiryEmail;

        if (business.InquiryNotifyViaWhatsApp && !string.IsNullOrWhiteSpace(whatsAppTo))
            notifications.AddWhatsApp(business, whatsAppTo,
                $"📩 New inquiry from {fromLabel}:\n\n{message}", $"WhatsApp inquiry notification for business {business.Id}");

        if (business.InquiryNotifyViaEmail && !string.IsNullOrWhiteSpace(emailTo))
            notifications.AddOwnerEmail(emailTo, $"New customer inquiry — {business.Name}",
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
            // A customer who's sent 3 replies in a row that don't answer the bot's last message
            // (service list, Book/Ask gate, cancel question) gets locked out of automated replies
            // entirely -- including cancel/reschedule keywords -- until they send the literal
            // unlock keyword, which restarts the conversation from the opening prompt.
            // While locked out the "#"-unlock reminder is re-sent at most every 30 minutes
            // (LockoutReminderIfDue), other messages get no reply: a reminder on every message was
            // spam, none at all looked like a broken bot. Same in the AI path below.
            // Checked before the pending-link quiet mode: a cancel question asked while a link is
            // pending can end in a lockout too.
            if (conversationState.InvalidAttempts >= MaxInvalidAttempts)
            {
                if (!IsUnlock(incomingMsg))
                    return await LockoutReminderIfDue(conversationState, lang);
                db.WhatsAppConversationStates.Remove(conversationState);
                await db.SaveChangesAsync();
                return business.ChatbotInquiryEnabled
                    ? await ShowModeGate(business, fromPhone, lang, null)
                    : await PromptServiceSelection(business.Id, business.Name, fromPhone, lang, welcomeMessage, business.ChatbotInquiryEnabled);
            }

            // A booking link was already issued and not yet completed -- stay quiet instead of
            // re-sending the opening prompt for every message the customer sends while they finish
            // booking on the web page the link opened. BookingController clears this once the
            // appointment is actually created (or it just expires with the row).
            // Cancel and reschedule still get through (QA: for 24 h after a link the bot ignored
            // even "cancel"), as does the answer to the cancel confirmation they start.
            if (conversationState.AwaitingBookingCompletion
                && !IsCancelPrompt(conversationState.ChatbotMode)
                && !ContainsKeyword(incomingMsg, CancelKeywords)
                && !ContainsKeyword(incomingMsg, RescheduleKeywords))
            {
                if (await PendingLinkStillUsable(business, fromPhone)) return null;
                // The owner turned the service off, or bookings stopped, after the link went out:
                // the link can't work, so staying quiet for 24 h left the customer stuck (QA run 4).
                db.WhatsAppConversationStates.Remove(conversationState);
                await db.SaveChangesAsync();
                var intro = I18nService.T(lang, "whatsapp.serviceNoLongerAvailable");
                var list = await PromptServiceSelection(business.Id, business.Name, fromPhone, lang, welcomeMessage, business.ChatbotInquiryEnabled);
                return bookingRules.AcceptsBookings(business) ? $"{intro}\n\n{list}" : list;
            }
        }

        if (conversationState is not null && IsCancelPrompt(conversationState.ChatbotMode))
        {
            var (outcome, promptReply) = await AnswerCancelPrompt(business.Id, conversationState, fromPhone, incomingMsg, lang, reaskAnyUnclear: true);
            if (outcome == CancelPromptOutcome.Unclear)
            {
                conversationState.InvalidAttempts++;
                if (conversationState.InvalidAttempts >= MaxInvalidAttempts)
                {
                    // The question is gone: after "#" a "1" must not cancel anything.
                    conversationState.ChatbotMode = BookingMode;
                    promptReply = StartLockout(conversationState, lang);
                }
                await db.SaveChangesAsync();
                return promptReply;
            }
            if (outcome == CancelPromptOutcome.StillOpen)
            {
                // "Which one?" answered: a new question, so a new count.
                conversationState.InvalidAttempts = 0;
                await db.SaveChangesAsync();
                return promptReply;
            }
            // Answered or dropped. With a booking link pending the row stays (the bot goes back to
            // quiet mode); otherwise the conversation is over.
            if (conversationState.AwaitingBookingCompletion) await db.SaveChangesAsync();
            else await ClearConversationState(business.Id, fromPhone);
            if (outcome == CancelPromptOutcome.Done)
                return promptReply ?? $"{I18nService.T(lang, "whatsapp.noAppointment")}\n\n{await PromptServiceSelection(business.Id, business.Name, fromPhone, lang, welcomeMessage, business.ChatbotInquiryEnabled)}";
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
            // With several, each gets its link (QA run 4: only the nearest one could be reached).
            var (toMove, _) = await FindUpcomingAppointments(business.Id, fromPhone);
            string MoveUrl(Appointment a) => $"{appUrl}/{business.Slug}/appointments/{a.Id}?token={a.CancelToken}";
            if (toMove.Count == 1)
            {
                var moveArgs = DateTimeArgs(toMove[0]);
                moveArgs["service"] = ItemName(toMove[0].Item, lang);
                moveArgs["url"] = MoveUrl(toMove[0]);
                return I18nService.T(lang, "whatsapp.rescheduleLink", moveArgs);
            }
            if (toMove.Count > 1)
                return I18nService.T(lang, "whatsapp.rescheduleLinks", new()
                {
                    ["list"] = string.Join("\n", toMove.Select(a => $"• {AppointmentLine(a, lang)}: {MoveUrl(a)}")),
                });
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
            if (!IsUnlock(incomingMsg))
                return await LockoutReminderIfDue(state, lang);
            await ClearConversationState(business.Id, fromPhone);
            return I18nService.T(lang, "whatsapp.aiConversationRestarted");
        }

        var history = state?.HistoryJson is not null
            ? JsonSerializer.Deserialize<List<OpenAiTurn>>(state.HistoryJson) ?? []
            : [];
        incomingMsg = Cut(incomingMsg, MaxAiMessageChars);

        // The answer to a "reply 1 to cancel / 2 to keep" question the cancel tool asked last turn.
        // Handled here without OpenAI, like the rule-based path; any other reply drops the question
        // (nothing cancelled) and goes to the model as a normal message. The row and its history
        // are kept, unlike the rule-based path's ClearConversationState.
        // Restored if the daily cap hands this message to the rule-based path, which answers the
        // open question itself (it would otherwise read a bare number against an old service list).
        // A short unclear answer is asked again here too, so the next "1" still answers it (QA run
        // 4: the model re-asked in free text with nothing parked).
        var modeBeforeTurn = state?.ChatbotMode;
        if (state is not null && IsCancelPrompt(state.ChatbotMode))
        {
            var (outcome, promptReply) = await AnswerCancelPrompt(business.Id, state, fromPhone, incomingMsg, lang);
            if (outcome != CancelPromptOutcome.NotAnswer)
            {
                var confirmReply = promptReply ?? I18nService.T(lang, "whatsapp.noAppointment");
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
        if (!aiUsage.TryUse(business.Id, fromPhone, DateTime.UtcNow))
        {
            if (state is not null) state.ChatbotMode = modeBeforeTurn!;
            throw new AiDailyCapReachedException();
        }

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
        string? cancelPromptMode = null; // set when the cancel tool asked its question this turn
        string? toolMessage = null; // text of the last tool that actually did something
        string? cancelQuestion = null; // the cancel tool's 1/2 question, which the customer must see as-is
        string? cancelNotice = null; // its "already requested" text: nothing to confirm, so a link may follow
        var linkIssued = false;
        // Every tool result this turn, for the URL check below, plus the business's own links the
        // prompt gives the model (map, storefront).
        var toolTexts = new List<string> { $"{appUrl}/{business.Slug}" };
        if (business.MapUrl is not null) toolTexts.Add(business.MapUrl);
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
                var (cancelResult, promptMode) = await ExecuteCancelUpcomingAppointment(business.Id, fromPhone, lang);
                result = cancelResult;
                cancelPromptMode = promptMode ?? cancelPromptMode;
                using var cancelParsed = JsonDocument.Parse(cancelResult);
                // "Already requested" has a message but nothing to park, so it doesn't block a booking link.
                if (cancelParsed.RootElement.TryGetProperty("message", out var question))
                {
                    if (promptMode is not null) cancelQuestion = question.GetString();
                    else cancelNotice = question.GetString();
                }
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
        else if (cancelNotice is not null && !linkIssued)
            reply = cancelNotice;
        else
            reply = StripUnknownUrls(reply, toolTexts);
        // A reply that was nothing but a made-up link: the tool's own text, else the rule-based bot.
        if (reply.Length == 0)
            reply = toolMessage ?? throw new InvalidOperationException("The AI reply was empty once unknown links were removed");

        history.Add(new OpenAiTurn("user", incomingMsg));
        history.Add(new OpenAiTurn("assistant", Cut(reply, MaxAiMessageChars)));
        if (history.Count > 12) history = history[^12..];

        if (state is null)
        {
            // The lookup above skips expired rows, but they are never deleted, so reuse one rather
            // than inserting a second row for this phone (unique index).
            state = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == business.Id && s.Phone == fromPhone);
            if (state is null)
            {
                state = new WhatsAppConversationState { BusinessId = business.Id, Phone = fromPhone };
                db.WhatsAppConversationStates.Add(state);
            }
            else ResetIfExpired(state);
        }
        state.Language = lang;
        state.HistoryJson = JsonSerializer.Serialize(history);
        state.ExpiresAt = DateTime.UtcNow.Add(ConversationStateLifetime);
        // A list the rule-based fallback showed earlier is no longer what the customer is looking
        // at, so a later fallback must not read a bare number against it.
        state.ShownItemIds = null;
        if (cancelPromptMode is not null)
            state.ChatbotMode = cancelPromptMode;

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
                reply = StartLockout(state, lang);
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
        // The literal #2 command itself is intercepted before this path ever runs (see
        // HandleInquiryModeAsync) -- the model never needs to act on it, just know to mention it
        // exists, since (unlike the rule-based path's PromptServiceSelection) nothing else here
        // surfaces that note to the customer.
        var inquiryNote = business.ChatbotInquiryEnabled
            ? "\n\nIf the customer asks something you can't help with, or wants to reach the owner directly, tell them to reply \"#2\" to talk to the owner directly."
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
            Never say an appointment was cancelled, booked or changed unless a tool's message in this turn says so. A cancellation only happens when the customer answers the confirmation question; if their message isn't an answer to it, the question is closed and nothing was cancelled.{inquiryNote}
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
        if (!bookingRules.AcceptsBookings(business))
            return JsonSerializer.Serialize(new { error = "This business isn't taking online bookings right now. Tell the customer to contact the business directly." });
        var services = await ActiveServices(business.Id, lang);
        var chosen = services.FirstOrDefault(s => s.Id == itemId);
        if (chosen.Id is null)
            return JsonSerializer.Serialize(new { error = "unknown itemId" });

        var message = await IssueBookingLink(business.Id, business.Slug, appUrl, fromPhone, profileName, lang, chosen, business.ResolveChatbotConfirmationMessage(lang));
        return JsonSerializer.Serialize(new { message });
    }

    // Asks the same question as the rule-based HandleCancel (1/2, or "which one?" for several); the
    // caller parks the returned mode in ChatbotMode for the answer. Cancels nothing itself.
    private async Task<(string Result, string? PromptMode)> ExecuteCancelUpcomingAppointment(string businessId, string fromPhone, string lang)
    {
        var (upcoming, pending) = await FindUpcomingAppointments(businessId, fromPhone);
        if (upcoming.Count == 0)
        {
            // Everything left is already waiting on the owner -- say so instead of "no appointment".
            return pending is null
                ? (JsonSerializer.Serialize(new { found = false }), null)
                : (JsonSerializer.Serialize(new { found = true, message = I18nService.T(lang, "whatsapp.cancellationAlreadyRequested", DateTimeArgs(pending)) }), null);
        }

        var (question, mode) = CancelQuestion(upcoming, lang);
        return (JsonSerializer.Serialize(new { found = true, message = question }), mode);
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
    // waits for "1" (cancel) / "2" (keep), or with several upcoming ones asks which first -- see
    // AnswerCancelPrompt.
    private async Task<string> HandleCancel(string businessId, string businessName, string fromPhone, string lang, string? welcomeMessage, bool inquiryEnabled)
    {
        var (upcoming, pending) = await FindUpcomingAppointments(businessId, fromPhone);

        if (upcoming.Count == 0)
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
        else ResetIfExpired(state);
        var (reply, mode) = CancelQuestion(upcoming, lang);
        state.ChatbotMode = mode;
        state.Language = lang;
        state.InvalidAttempts = 0;
        // A pending booking link stays pending: the bot stays quiet after the question is answered
        // (QA run 3: it re-sent the list and later chatter counted toward the lockout). The row then
        // lives on for the link, but the question itself only ConversationStateLifetime (its own
        // clock in the mode, see CancelPrompt).
        var questionExpiresAt = DateTime.UtcNow.Add(ConversationStateLifetime);
        if (!state.AwaitingBookingCompletion || state.ExpiresAt < questionExpiresAt)
            state.ExpiresAt = questionExpiresAt;
        await db.SaveChangesAsync();
        return reply;
    }

    // An open cancel question lives in ChatbotMode: "ConfirmCancel:{id}:{asked}:{reasked}" (reply
    // 1 or 2) or "PickCancel:{id,id,...}:{asked}:{reasked}" (which appointment?). asked is unix
    // seconds: with a booking link pending the row lives 24 h, the question only
    // ConversationStateLifetime (QA run 4: "yes see you" cancelled an appointment 20 hours later).
    // reasked is 1 once a short unclear answer was asked again. "ConfirmCancel:{id}" rows from
    // before this format still parse (no clock).
    private const string PickCancelModePrefix = "PickCancel:";
    private const int MaxPickedAppointments = 5;
    private enum CancelPromptOutcome { NotAnswer, StillOpen, Unclear, Done }
    private sealed record CancelPrompt(bool Pick, List<string> Ids, DateTime? AskedAt, bool Reasked);

    private static bool IsCancelPrompt(string mode) =>
        mode.StartsWith(ConfirmCancelModePrefix) || mode.StartsWith(PickCancelModePrefix);

    private static string CancelPromptMode(bool pick, IEnumerable<string> ids, bool reasked) =>
        $"{(pick ? PickCancelModePrefix : ConfirmCancelModePrefix)}{string.Join(",", ids)}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:{(reasked ? 1 : 0)}";

    private static CancelPrompt ParseCancelPrompt(string mode)
    {
        var pick = mode.StartsWith(PickCancelModePrefix);
        var parts = mode[(pick ? PickCancelModePrefix : ConfirmCancelModePrefix).Length..].Split(':');
        DateTime? askedAt = parts.Length > 1 && long.TryParse(parts[1], out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
        return new CancelPrompt(pick, parts[0].Split(',').ToList(), askedAt, parts.Length > 2 && parts[2] == "1");
    }

    // The question for the customer's cancellable appointments: 1/2 for one, "which one?" for
    // several (QA run 4: only the nearest one could be cancelled). Returns the mode to park.
    private static (string Reply, string Mode) CancelQuestion(List<Appointment> upcoming, string lang, bool reask = false)
    {
        if (upcoming.Count == 1)
        {
            var args = DateTimeArgs(upcoming[0]);
            args["service"] = ItemName(upcoming[0].Item, lang);
            return (I18nService.T(lang, reask ? "whatsapp.confirmCancelReask" : "whatsapp.confirmCancel", args),
                CancelPromptMode(false, [upcoming[0].Id], reask));
        }
        var shown = upcoming.Take(MaxPickedAppointments).ToList();
        var list = string.Join("\n", shown.Select((a, i) => $"{i + 1}. {AppointmentLine(a, lang)}"));
        return (I18nService.T(lang, reask ? "whatsapp.pickCancelReask" : "whatsapp.pickCancel", new() { ["list"] = list }),
            CancelPromptMode(true, shown.Select(a => a.Id), reask));
    }

    private static string AppointmentLine(Appointment a, string lang) => $"{ItemName(a.Item, lang)}, {a.Date:yyyy-MM-dd} {a.StartTime}";

    // A message while a cancel question is open, shared by both paths. Done: answered (the reply,
    // or null when the appointment can't be cancelled any more). StillOpen: asked again, or "which
    // one?" answered with the 1/2 question for it. Unclear: not an answer, asked again. NotAnswer:
    // the question is dropped, nothing is cancelled, and the message is handled as a normal one.
    // Sets state.ChatbotMode; the caller saves.
    // reaskAnyUnclear (rule-based path): every unclear reply is asked again, so it counts toward
    // the lockout like one to any other question (owner, 2026-10-25); only a cancel/reschedule
    // request drops the question. The AI path drops a longer message for the model to answer.
    private async Task<(CancelPromptOutcome Outcome, string? Reply)> AnswerCancelPrompt(string businessId, WhatsAppConversationState state, string fromPhone, string message, string lang, bool reaskAnyUnclear = false)
    {
        var prompt = ParseCancelPrompt(state.ChatbotMode);
        state.ChatbotMode = BookingMode;
        if (prompt.AskedAt is { } asked && DateTime.UtcNow - asked >= ConversationStateLifetime)
            return (CancelPromptOutcome.NotAnswer, null);

        if (prompt.Pick)
        {
            var number = ParseNumber(message);
            if (number == 0 || (number is null && ConfirmationAnswer(message) == "2"))
                return (CancelPromptOutcome.Done, I18nService.T(lang, "whatsapp.cancelKeptAll"));
            if (number >= 1 && number <= prompt.Ids.Count)
            {
                var picked = await CancellableAppointments(businessId, fromPhone, [prompt.Ids[number.Value - 1]]);
                if (picked.Count == 0) return (CancelPromptOutcome.Done, null);
                var (question, mode) = CancelQuestion(picked, lang);
                state.ChatbotMode = mode;
                return (CancelPromptOutcome.StillOpen, question);
            }
        }
        else
        {
            var answer = ConfirmationAnswer(message);
            if (answer == "2") return (CancelPromptOutcome.Done, I18nService.T(lang, "whatsapp.cancelKept"));
            if (answer == "1") return (CancelPromptOutcome.Done, await CancelConfirmedAppointment(businessId, prompt.Ids[0], fromPhone, lang));
        }

        // A short reply that isn't an answer ("ok", "تمام") is asked once more: dropped, a
        // customer who wrote "ok" believed it was cancelled (QA run 4). A longer message, a
        // question or a reschedule request is about something else.
        if (ContainsKeyword(message, RescheduleKeywords) || ContainsKeyword(message, CancelKeywords)
            || !reaskAnyUnclear && (prompt.Reasked || Words(message).Count > 4 || message.Contains('?') || message.Contains('؟')))
            return (CancelPromptOutcome.NotAnswer, null);
        var still = await CancellableAppointments(businessId, fromPhone, prompt.Ids);
        if (still.Count == 0) return (CancelPromptOutcome.NotAnswer, null);
        var (reask, reaskMode) = CancelQuestion(still, lang, reask: true);
        state.ChatbotMode = reaskMode;
        return (CancelPromptOutcome.Unclear, reask);
    }

    // The reply that locks the conversation: the "$" reminder, with the lock running LockoutLifetime.
    private static string StartLockout(WhatsAppConversationState state, string lang)
    {
        state.ExpiresAt = DateTime.UtcNow.Add(LockoutLifetime);
        return I18nService.T(lang, "whatsapp.tooManyInvalidReplies", new() { ["unlockKeyword"] = UnlockKeyword });
    }

    // A message while locked out: the reminder again once LockoutReminderInterval has passed since
    // the last one, else no reply (null).
    private async Task<string?> LockoutReminderIfDue(WhatsAppConversationState state, string lang)
    {
        var lastReminder = state.ExpiresAt - LockoutLifetime;
        if (DateTime.UtcNow - lastReminder < LockoutReminderInterval) return null;
        var reply = StartLockout(state, lang);
        await db.SaveChangesAsync();
        return reply;
    }

    // Expired rows stay in the table, so one found without the ExpiresAt filter must not carry its
    // old conversation into a new one (a stale pending link would keep the bot quiet, an old
    // question or list would be answered).
    private static void ResetIfExpired(WhatsAppConversationState state)
    {
        if (state.ExpiresAt > DateTime.UtcNow) return;
        state.ChatbotMode = BookingMode;
        state.AwaitingBookingCompletion = false;
        state.InquiryNotified = false;
        state.InvalidAttempts = 0;
        state.ShownItemIds = null;
        state.HistoryJson = null;
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

    // The customer's upcoming appointments that can still be cancelled, nearest first (date, then
    // start time), plus the nearest one already waiting on owner approval, so callers can tell
    // "nothing booked" apart from "already asked".
    private async Task<(List<Appointment> Upcoming, Appointment? PendingApproval)> FindUpcomingAppointments(string businessId, string fromPhone)
    {
        var all = await UpcomingAppointments(businessId, fromPhone);
        return (all.Where(a => !a.PendingCancellationApproval).ToList(), all.FirstOrDefault(a => a.PendingCancellationApproval));
    }

    private async Task<List<Appointment>> CancellableAppointments(string businessId, string fromPhone, List<string> ids) =>
        (await UpcomingAppointments(businessId, fromPhone)).Where(a => !a.PendingCancellationApproval && ids.Contains(a.Id)).ToList();

    private async Task<List<Appointment>> UpcomingAppointments(string businessId, string fromPhone)
    {
        var customer = await db.Customers
            // a.Date is a calendar date (local wall-clock, never UTC-converted), so compare
            // against local "today" as a date — not DateTime.UtcNow, which is both the wrong
            // clock and, being a timestamp rather than a date, would already exclude today's
            // appointments as soon as any time had passed since UTC midnight.
            .Include(c => c.Appointments.Where(a => a.Status == AppointmentStatus.CONFIRMED && a.Date >= AppClock.Now.Date))
                .ThenInclude(a => a.Item)
            .FirstOrDefaultAsync(c => c.BusinessId == businessId && c.Phone == fromPhone);

        return (customer?.Appointments ?? [])
            .Where(a => AppointmentStatusHelper.EffectiveStatus(a.Status, a.Date, a.EndTime) == "CONFIRMED")
            .OrderBy(a => a.Date).ThenBy(a => a.StartTime)
            .ToList();
    }

    // "1"/"2" for an answer to the cancel question: the digit itself, or a yes/no word, alone or
    // with words that don't change it ("yes please", "כן, תבטל"). Null for anything else: "yes
    // see you" or "yes but tomorrow" isn't a yes (QA run 4).
    private static string? ConfirmationAnswer(string message)
    {
        var number = ParseNumber(message);
        if (number is 1 or 2) return number.ToString();
        var words = Words(message);
        if (words.Count == 0 || words.Count > 4) return null;
        if (YesWords.Contains(words[0]) && words.Skip(1).All(w => YesWords.Contains(w) || AnswerFillerWords.Contains(w) || CancelKeywords.Contains(w)))
            return "1";
        if (NoWords.Contains(words[0]) && words.Skip(1).All(w => NoWords.Contains(w) || AnswerFillerWords.Contains(w)))
            return "2";
        return null;
    }

    // Defence in depth: a link in the model's own text must be one a tool returned this turn, so a
    // prompt-injected or made-up URL never reaches the customer (QA run 3).
    // ASCII only (\P{IsBasicLatin} excluded): an emoji or a Hebrew/Arabic word written right after a
    // real link used to make the whole match unknown, and the valid link was removed.
    // Any case ("HTTPS://"), and bare domains too ("www.x.com/pay", "x.com"), which WhatsApp
    // turns into links as well (QA run 4).
    private static string StripUnknownUrls(string reply, List<string> toolTexts)
    {
        bool Known(string url) => toolTexts.Any(t => t.Contains(url, StringComparison.OrdinalIgnoreCase));
        var withoutUrls = Regex.Replace(reply, @"https?://[^\s)\]>""'\P{IsBasicLatin}]*[^\s)\]>""'.,!?:;\P{IsBasicLatin}]",
            m => Known(m.Value) ? m.Value : "", RegexOptions.IgnoreCase);
        return Regex.Replace(withoutUrls,
            @"(?<![\w@./-])(?:www\.)?(?:[a-z0-9-]+\.)+(?:com|net|org|io|co|il|me|app|info|biz|xyz|link|ly|to|ru|cn|top|site|online|shop|store)\b(?:/[^\s)\]>""'\P{IsBasicLatin}]*[^\s)\]>""'.,!?:;\P{IsBasicLatin}])?",
            m => Known(m.Value) || IsInsideKnownUrl(withoutUrls, m) ? m.Value : "", RegexOptions.IgnoreCase).Trim();
    }

    // The host part of a full URL the first pass kept ("https://frontend.up.railway.app/x").
    private static bool IsInsideKnownUrl(string text, Match m) =>
        m.Index >= 3 && text[..m.Index].EndsWith("://", StringComparison.Ordinal);

    private sealed class AiDailyCapReachedException : Exception;

    private static string TruncateName(string? name) => name is null ? "" : Cut(name, Filters.ValidateInputFilter.MaxNameLength);

    // Never splits an emoji (surrogate pair): Npgsql refuses to save a lone surrogate, so the
    // message would 500 and the customer get no reply.
    private static string Cut(string text, int max)
    {
        if (text.Length <= max) return text;
        return char.IsHighSurrogate(text[max - 1]) ? text[..(max - 1)] : text[..max];
    }

    // Lowercased words, with combining marks (Arabic harakat, Hebrew niqqud) removed: "ألغِ" kept
    // its kasra and missed the keyword list (QA run 4).
    private static List<string> Words(string message) =>
        Regex.Split(Regex.Replace(message.ToLowerInvariant(), @"\p{Mn}", "").Replace("re-schedul", "reschedul"), @"[^\p{L}\p{Nd}]+")
            .Where(w => w.Length > 0).ToList();

    // Whole words only. An Arabic word also matches without its "و" (and) or "ال" (the) prefix:
    // "والغاء", "الإلغاء".
    private static bool ContainsKeyword(string message, string[] keywords) =>
        Words(message).Any(w => keywords.Contains(w)
            || (w.Length > 2 && w[0] == 'و' && keywords.Contains(w[1..]))
            || (w.Length > 3 && w.StartsWith("ال") && keywords.Contains(w[2..])));

    // Whether the booking link the bot is staying quiet for can still be used: bookings are open
    // and its service is still active and bookable.
    private async Task<bool> PendingLinkStillUsable(Business business, string phone)
    {
        if (!bookingRules.AcceptsBookings(business)) return false;
        var itemId = await db.WhatsAppBookingTokens
            .Where(t => t.BusinessId == business.Id && t.Phone == phone && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt).Select(t => t.ItemId).FirstOrDefaultAsync();
        return itemId is null || await db.Items.AnyAsync(i => i.Id == itemId && i.IsActive && i.IsBookable);
    }

    private async Task ClearConversationState(string businessId, string phone)
    {
        var existing = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == businessId && s.Phone == phone);
        if (existing is null) return;
        db.WhatsAppConversationStates.Remove(existing);
        await db.SaveChangesAsync();
    }

    private Task<List<(string Id, string Name)>> ActiveServices(string businessId, string lang) =>
        serviceList.ActiveServicesAsync(businessId, lang);

    // See ServiceListPrompt.PromptAsync.
    private Task<string> PromptServiceSelection(string businessId, string businessName, string phone, string lang, string? welcomeMessage, bool inquiryEnabled) =>
        serviceList.PromptAsync(businessId, businessName, phone, lang, welcomeMessage, inquiryEnabled);

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
        var index = ParseNumber(message) ?? 0;
        if (index < 1 || index > shownIds.Count)
        {
            state.InvalidAttempts++;
            // So a following bare "1" stays in the language this reply was answered in (QA run 3).
            state.Language = lang;
            var invalidReply = state.InvalidAttempts >= MaxInvalidAttempts
                ? StartLockout(state, lang)
                : I18nService.T(lang, "whatsapp.invalidServiceSelection");
            await db.SaveChangesAsync();
            return invalidReply;
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
        var owner = (await db.Businesses.FindAsync(businessId))!;
        if (!bookingRules.AcceptsBookings(owner))
        {
            db.WhatsAppConversationStates.Remove(state);
            await db.SaveChangesAsync();
            return I18nService.T(lang, "whatsapp.bookingUnavailable", new() { ["businessName"] = owner.Name });
        }
        state.AwaitingBookingCompletion = true;
        // Answered: the link is a new message, so earlier unclear replies no longer count.
        state.InvalidAttempts = 0;
        state.ExpiresAt = DateTime.UtcNow.Add(BookingLinkPendingLifetime);
        // The booking confirmation BookingController sends later uses this (QA run 4: a choice
        // made in Arabic after an English list left it English).
        state.Language = lang;
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
