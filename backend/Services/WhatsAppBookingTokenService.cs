using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

// Issues and resolves the opaque booking-link token sent to a customer once they pick a service
// in the WhatsApp chatbot. Centralized here so WhatsAppController (issues) and
// CustomerAuthController (redeems) share the same expiry rule instead of duplicating it.
public class WhatsAppBookingTokenService(AppDbContext db)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public async Task<WhatsAppBookingToken> CreateAsync(string barberId, string serviceId, string phone, string? profileName, string language = "EN")
    {
        var token = new WhatsAppBookingToken
        {
            BarberId = barberId,
            ServiceId = serviceId,
            Phone = phone,
            ProfileName = profileName,
            Language = language,
            ExpiresAt = DateTime.UtcNow.Add(Lifetime),
        };
        db.WhatsAppBookingTokens.Add(token);
        await db.SaveChangesAsync();
        return token;
    }

    public Task<WhatsAppBookingToken?> TryResolveAsync(string token) =>
        db.WhatsAppBookingTokens.FirstOrDefaultAsync(t => t.Id == token && t.ExpiresAt > DateTime.UtcNow);
}
