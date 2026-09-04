using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

// Opaque, DB-backed booking-link token issued once a customer picks a service in the WhatsApp
// chatbot flow. Deliberately not a JWT: a JWT's payload is base64-encoded, not encrypted, so the
// phone number embedded in it would be readable by anyone who received the link -- the spec
// requires the URL not expose the customer's phone number. Reusable for its whole lifetime (no
// Consumed/one-time flag) so the customer can reopen the WhatsApp message and land back in the
// booking wizard without asking the bot again.
public class WhatsAppBookingToken
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BarberId { get; set; } = "";
    public string ServiceId { get; set; } = "";
    public string Phone { get; set; } = "";
    // WhatsApp's inbound-message "ProfileName" field -- the sender's WhatsApp display name.
    // Null when Twilio doesn't supply one; the redemption endpoint falls back to a generic name.
    public string? ProfileName { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Barber Barber { get; set; } = null!;
    public Service Service { get; set; } = null!;
}
