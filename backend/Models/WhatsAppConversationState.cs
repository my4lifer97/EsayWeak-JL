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
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
}
