using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

// Written automatically by ActivityLogFilter for every authenticated write request, plus
// explicitly by PlatformAdminController for impersonation events. Deliberately holds only
// request metadata (never bodies) so nothing sensitive (passwords, Twilio tokens, ...) ever
// ends up in a log row -- see ActivityLogFilter for the full write path.
public class ActivityLog
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? BarberId { get; set; }
    public string? CustomerAccountId { get; set; }
    // Set when the acting token was a platform-admin impersonation token, so an account's
    // activity log can distinguish "the owner did this" from "support did this on their behalf".
    public string? ImpersonatedByPlatformAdminId { get; set; }
    public string Action { get; set; } = "";
    public string Description { get; set; } = "";
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public int StatusCode { get; set; }
    public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Barber? Barber { get; set; }
    public CustomerAccount? CustomerAccount { get; set; }
    public PlatformAdmin? ImpersonatedByPlatformAdmin { get; set; }
}
