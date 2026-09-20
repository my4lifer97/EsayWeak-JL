using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

// Twilio's webhook is a stateless HTTP call per inbound message -- this row is how the bot
// remembers "this phone is mid service-selection with this business" between the "which service?"
// prompt and the customer's numeric reply. Short-lived scratch state, not an identity/credential
// (see WhatsAppBookingToken for that) -- one row per (BusinessId, Phone), replaced on each new
// prompt, and ignored/deleted once expired or once the selection is resolved.
public class WhatsAppConversationState
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BusinessId { get; set; } = "";
    public string Phone { get; set; } = "";
    // The language detected from whichever message opened/last touched this conversation (see
    // WhatsAppController.DetectLanguage) -- lets a numeric-only reply like "1" (no language
    // signal of its own) keep replying in the language the customer was already using.
    public string Language { get; set; } = "EN";
    // JSON-serialized List<OpenAiTurn> -- rolling chat history for the OpenAI-driven chatbot path
    // (see WhatsAppController.ProcessMessageWithAiAsync), capped to the last ~12 turns. Null when
    // the AI path isn't configured/used, or before the first AI-driven exchange for this
    // conversation. Reuses this row's existing (BusinessId, Phone) + ExpiresAt session boundary
    // rather than a separate table.
    public string? HistoryJson { get; set; }
    // Consecutive non-numeric/out-of-range replies to a "which service?" prompt (rule-based path
    // only). Reset to 0 whenever a fresh prompt is (re)issued (see PromptServiceSelection); once it
    // reaches WhatsAppController.MaxInvalidAttempts, the bot stops auto-replying entirely until the
    // customer sends the unlock keyword.
    public int InvalidAttempts { get; set; }
    // Set once a booking link has been issued for this phone (see WhatsAppController.IssueBookingLink)
    // -- while true, the rule-based bot goes silent for any further message instead of re-sending the
    // opening prompt, so the customer isn't double-messaged while they finish booking on the web page
    // the link opened. Cleared by BookingController once the appointment is actually created (or just
    // expires naturally with the row, same as everything else here).
    public bool AwaitingBookingCompletion { get; set; }
    // "Booking" (default) or "Inquiry" -- which section of the chatbot this phone is currently in,
    // when Business.ChatbotInquiryEnabled is on. Switched only by the literal "$1"/"$2" commands
    // (checked before either chatbot path runs -- see WhatsAppController.HandleInquiryModeAsync);
    // meaningless/unused while the feature is off for this business.
    public string ChatbotMode { get; set; } = "Booking";
    // Whether the owner has already been notified (WhatsApp/email) for the CURRENT inquiry
    // session -- a back-and-forth conversation only pings once, not per message. Reset to false
    // whenever the customer returns to Booking mode, so a later inquiry session notifies again.
    public bool InquiryNotified { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
}
