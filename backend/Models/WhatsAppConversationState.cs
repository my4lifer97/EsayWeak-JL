using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

// Twilio's webhook is a stateless HTTP call per inbound message -- this row is how the bot
// remembers "this phone is mid service-selection with this barber" between the "which service?"
// prompt and the customer's numeric reply. Short-lived scratch state, not an identity/credential
// (see WhatsAppBookingToken for that) -- one row per (BarberId, Phone), replaced on each new
// prompt, and ignored/deleted once expired or once the selection is resolved.
public class WhatsAppConversationState
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BarberId { get; set; } = "";
    public string Phone { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Barber Barber { get; set; } = null!;
}
