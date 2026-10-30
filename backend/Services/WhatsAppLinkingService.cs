using BarberSaas.Api.Data;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

// Issues/resolves the shareable WhatsAppLinkToken (see that model), and holds the "record a
// completed link" side effect (write Business.WhatsAppNumber + log it) shared by
// PlatformAdminController's authenticated status poll and WhatsAppLinkController's token-based
// one, so the two don't drift out of sync.
public class WhatsAppLinkingService(AppDbContext db, IWhatsAppBridgeClient bridge, PlatformAdminAlerts alerts)
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

    public async Task<WhatsAppLinkToken> CreateTokenAsync(string businessId, string adminId)
    {
        var token = new WhatsAppLinkToken
        {
            BusinessId = businessId,
            CreatedByPlatformAdminId = adminId,
            ExpiresAt = DateTime.UtcNow.Add(TokenLifetime),
        };
        db.WhatsAppLinkTokens.Add(token);
        await db.SaveChangesAsync();
        return token;
    }

    public Task<WhatsAppLinkToken?> TryResolveTokenAsync(string token) =>
        db.WhatsAppLinkTokens.Include(t => t.Business)
            .FirstOrDefaultAsync(t => t.Id == token && t.ExpiresAt > DateTime.UtcNow);

    public async Task<WhatsAppLinkStatusDto> GetStatusAndRecordAsync(
        Business b, string actingPlatformAdminId, string method, string path, string? ip)
    {
        var status = await bridge.GetStatusAsync(b.Id);
        return await RecordIfConnectedAsync(b, status, actingPlatformAdminId, method, path, ip);
    }

    // actingPlatformAdminId null: the owner connected through the wa-link page themselves; it was
    // logged as done by the admin who made the link (QA run 6).
    public async Task<WhatsAppLinkStatusDto> RecordIfConnectedAsync(
        Business b, WhatsAppLinkStatusDto status, string? actingPlatformAdminId, string method, string path, string? ip)
    {
        if (status.State == "connected" && status.PhoneNumber is not null && b.WhatsAppNumber != status.PhoneNumber)
        {
            var old = b.WhatsAppNumber;
            b.WhatsAppNumber = status.PhoneNumber;
            db.ActivityLogs.Add(new ActivityLog
            {
                BusinessId = b.Id,
                ImpersonatedByPlatformAdminId = actingPlatformAdminId,
                Action = "WhatsAppLink.Connected",
                Description = $"WhatsApp number: \"{old}\" → \"{b.WhatsAppNumber}\"" + (actingPlatformAdminId is null ? " (the owner's link page)" : ""),
                Method = method,
                Path = path,
                StatusCode = 200,
                IpAddress = ip,
            });
            await db.SaveChangesAsync();
            await alerts.WarnIfNumberSharedAsync(b);
        }
        return status;
    }
}
