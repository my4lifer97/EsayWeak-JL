using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Controllers;

// Two ways for a customer session to start: redeeming the opaque booking-link token the WhatsApp
// bot sent them (see WhatsAppController + WhatsAppBookingTokenService), or a direct phone+OTP
// login below -- both just mint the same kind of CustomerAccount-backed JWT via CustomerJwtService,
// so Follow/Waitlist/Review and everything else keyed off CustomerAccountId don't care which path
// was used. Phone+OTP exists mainly so a future native mobile app (which can't rely on "the
// customer already messaged us on WhatsApp") has a normal sign-in of its own.
[ApiController]
[Route("api/customer/auth")]
public class CustomerAuthController(AppDbContext db, CustomerJwtService jwt, WhatsAppBookingTokenService bookingTokens, IOtpSender otpSender, IWebHostEnvironment env) : ControllerBase
{
    private const int OtpCooldownSeconds = 45;
    private const int OtpMaxPerHour = 5;
    private const int OtpMaxAttempts = 5;
    private const int OtpExpiryMinutes = 10;

    // Fixed phone/code pair that always succeeds without sending a real SMS -- needed for app
    // store reviewer logins once the mobile app exists (a reviewer can't receive a real SMS to an
    // unknown number), same pattern as the old pre-WhatsApp OTP flow used.
    private const string TestCustomerCode = "123456";
    private static readonly string TestCustomerPhone = PhoneNormalizer.Normalize("0501234567");

    [HttpPost("otp")]
    public async Task<IActionResult> RequestOtp([FromBody] RequestCustomerOtpRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Phone))
            return BadRequest(new { error = "Phone is required" });

        var phone = PhoneNormalizer.Normalize(req.Phone);
        var since = DateTime.UtcNow.AddHours(-1);

        var recent = await db.CustomerOtps
            .Where(o => o.Phone == phone && o.CreatedAt > since)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var last = recent.FirstOrDefault();
        if (last is not null && (DateTime.UtcNow - last.CreatedAt).TotalSeconds < OtpCooldownSeconds)
            return StatusCode(429, new { error = "Please wait before requesting another code" });

        if (recent.Count >= OtpMaxPerHour)
            return StatusCode(429, new { error = "Too many requests. Try again later" });

        var isNew = !await db.CustomerAccounts.AnyAsync(a => a.Phone == phone);

        var code = phone == TestCustomerPhone
            ? TestCustomerCode
            : Random.Shared.Next(100000, 999999).ToString();
        db.CustomerOtps.Add(new CustomerOtp
        {
            Phone = phone,
            CodeHash = BCrypt.Net.BCrypt.HashPassword(code),
            ExpiresAt = DateTime.UtcNow.AddMinutes(OtpExpiryMinutes),
        });
        await db.SaveChangesAsync();

        // The fixed reviewer/test phone always resolves to the fixed TestCustomerCode above --
        // sending it a real SMS on every request would text a real phone number that has nothing
        // to do with app review/testing.
        if (phone != TestCustomerPhone)
            await otpSender.SendAsync(phone, code);

        string? devOtp = env.IsDevelopment() ? code : null;
        return Ok(new { isNewCustomer = isNew, devOtp });
    }

    [HttpPost("verify")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyCustomerOtpRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Phone) || string.IsNullOrWhiteSpace(req.Otp))
            return BadRequest(new { error = "Phone and code are required" });

        var phone = PhoneNormalizer.Normalize(req.Phone);

        var entry = await db.CustomerOtps
            .Where(o => o.Phone == phone && !o.Consumed && o.ExpiresAt > DateTime.UtcNow && o.Attempts < OtpMaxAttempts)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (entry is null || !BCrypt.Net.BCrypt.Verify(req.Otp, entry.CodeHash))
        {
            if (entry is not null)
            {
                entry.Attempts++;
                await db.SaveChangesAsync();
            }
            return BadRequest(new { error = "Invalid or expired code" });
        }

        entry.Consumed = true;

        var account = await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Phone == phone);
        if (account is null)
        {
            if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.FamilyName))
                return BadRequest(new { error = "Name and family name required for new customers" });
            account = new CustomerAccount { Phone = phone, Name = req.Name, FamilyName = req.FamilyName };
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
        });
    }

    [HttpPost("whatsapp")]
    public async Task<IActionResult> LoginWithWhatsApp([FromBody] WhatsAppLoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Token))
            return BadRequest(new { error = "Token is required" });

        var tokenRow = await bookingTokens.TryResolveAsync(req.Token);
        if (tokenRow is null)
            return BadRequest(new { error = "This link has expired or is invalid" });

        var business = await db.Businesses.Where(b => b.Id == tokenRow.BusinessId).Select(b => new { b.Slug }).FirstOrDefaultAsync();
        // Items are soft-deleted (IsActive = false), never hard-deleted -- so this also covers
        // the business deactivating the item after the WhatsApp link was already sent.
        var item = await db.Items.Where(s => s.Id == tokenRow.ItemId && s.IsActive).Select(s => new { s.Id }).FirstOrDefaultAsync();
        if (business is null || item is null)
            return NotFound(new { error = "This business or item is no longer available" });

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
            businessSlug = business.Slug,
            itemId = item.Id,
            language = tokenRow.Language,
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
public record RequestCustomerOtpRequest(string Phone);
public record VerifyCustomerOtpRequest(string Phone, string Otp, string? Name, string? FamilyName);
