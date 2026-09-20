using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

// Opaque, DB-backed link a platform admin can hand off to a business owner so they can scan their
// own WhatsApp-link QR code themselves, instead of the admin relaying a screenshot that goes stale
// within seconds (Baileys QR codes refresh that often). The admin still initiates every link
// session (see WhatsAppLinkController) -- this only removes the admin as a manual relay for the
// scan step itself. Short-lived; not one-time-use, since the owner might need a couple of tries.
public class WhatsAppLinkToken
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BusinessId { get; set; } = "";
    // Which admin generated this link -- carried onto the ActivityLog row once the phone connects,
    // since that request comes from the owner's own browser, not an authenticated admin session.
    public string CreatedByPlatformAdminId { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
}
