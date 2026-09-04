using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Controllers;

// Replaces the old phone+OTP login: a customer session now starts by redeeming the opaque
// booking-link token the WhatsApp bot sent them (see WhatsAppController + WhatsAppBookingTokenService)
// instead of typing a phone number and a code. No manual sign-up/sign-in step remains.
[ApiController]
[Route("api/customer/auth")]
public class CustomerAuthController(AppDbContext db, CustomerJwtService jwt, WhatsAppBookingTokenService bookingTokens) : ControllerBase
{
    [HttpPost("whatsapp")]
    public async Task<IActionResult> LoginWithWhatsApp([FromBody] WhatsAppLoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Token))
            return BadRequest(new { error = "Token is required" });

        var tokenRow = await bookingTokens.TryResolveAsync(req.Token);
        if (tokenRow is null)
            return BadRequest(new { error = "This link has expired or is invalid" });

        var barber = await db.Barbers.Where(b => b.Id == tokenRow.BarberId).Select(b => new { b.Slug }).FirstOrDefaultAsync();
        // Services are soft-deleted (IsActive = false), never hard-deleted -- so this also covers
        // the barber deactivating the service after the WhatsApp link was already sent.
        var service = await db.Services.Where(s => s.Id == tokenRow.ServiceId && s.IsActive).Select(s => new { s.Id }).FirstOrDefaultAsync();
        if (barber is null || service is null)
            return NotFound(new { error = "This barber or service is no longer available" });

        var phone = PhoneNormalizer.Normalize(tokenRow.Phone);
        var account = await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Phone == phone);
        if (account is null)
        {
            var (name, familyName) = SplitProfileName(tokenRow.ProfileName);
            account = new CustomerAccount { Phone = phone, Name = name, FamilyName = familyName };
            db.CustomerAccounts.Add(account);
        }
        await db.SaveChangesAsync();

        await db.Customers
            .Where(c => c.Phone == phone && c.CustomerAccountId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CustomerAccountId, account.Id));

        var token = jwt.Generate(account.Id, account.Phone, $"{account.Name} {account.FamilyName}".Trim());
        return Ok(new
        {
            token,
            customerId = account.Id,
            name = account.Name,
            familyName = account.FamilyName,
            phone = account.Phone,
            barberSlug = barber.Slug,
            serviceId = service.Id,
        });
    }

    // WhatsApp only exposes a single display name, not separate given/family names -- best-effort
    // split on the first space. Falls back to a generic name (never blocks login) when the
    // customer has no WhatsApp profile name set; they can still correct it in the booking form.
    private static (string Name, string FamilyName) SplitProfileName(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            return ("Customer", "");
        var parts = profileName.Trim().Split(' ', 2);
        return parts.Length == 2 ? (parts[0], parts[1]) : (parts[0], "");
    }
}

public record WhatsAppLoginRequest(string Token);
