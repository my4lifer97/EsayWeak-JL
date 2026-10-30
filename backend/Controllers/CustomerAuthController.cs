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
public class CustomerAuthController(AppDbContext db, CustomerJwtService jwt, WhatsAppBookingTokenService bookingTokens, IOtpSender otpSender, IWebHostEnvironment env, IConfiguration config) : ControllerBase
{
    private const int OtpCooldownSeconds = 45;
    private const int OtpMaxPerHour = 5;
    private const int OtpMaxAttempts = 5;
    private const int OtpExpiryMinutes = 10;

    // Fixed phone/code pair that always succeeds without sending a real SMS -- needed for app
    // store reviewer logins once the mobile app exists (a reviewer can't receive a real SMS to an
    // unknown number). Off unless both CustomerAuth:ReviewerPhone and ReviewerCode are set (QA
    // run 2: a hard-coded 0501234567 / 123456 let anyone into that account in production).
    private string? ReviewerPhone =>
        string.IsNullOrWhiteSpace(config["CustomerAuth:ReviewerPhone"]) || string.IsNullOrWhiteSpace(config["CustomerAuth:ReviewerCode"])
            ? null
            : PhoneNormalizer.Normalize(config["CustomerAuth:ReviewerPhone"]!);

    [HttpPost("otp")]
    public async Task<IActionResult> RequestOtp([FromBody] RequestCustomerOtpRequest req, [FromServices] RequestQuota quota)
    {
        if (string.IsNullOrWhiteSpace(req.Phone))
            return BadRequest(new { error = "Phone is required" });
        if (!PhoneNormalizer.IsValid(req.Phone))
            return BadRequest(new { error = "Please enter a valid phone number" });
        // Per IP too, not only per phone: each SMS costs money (QA run 6).
        var (allowed, retryAfter) = quota.TryTake($"otp|{HttpContext.Connection.RemoteIpAddress}", quota.OtpPerIpPerHour, TimeSpan.FromHours(1));
        if (!allowed)
        {
            Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
            return StatusCode(429, new { error = "Too many codes were requested from here. Please try again later." });
        }

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

        var isReviewer = phone == ReviewerPhone;
        var code = isReviewer
            ? config["CustomerAuth:ReviewerCode"]!
            : Random.Shared.Next(100000, 999999).ToString();
        db.CustomerOtps.Add(new CustomerOtp
        {
            Phone = phone,
            CodeHash = BCrypt.Net.BCrypt.HashPassword(code),
            ExpiresAt = DateTime.UtcNow.AddMinutes(OtpExpiryMinutes),
        });
        await db.SaveChangesAsync();

        // The reviewer phone always gets the fixed ReviewerCode -- sending it a real SMS on every
        // request would text a real phone number that has nothing to do with app review.
        if (!isReviewer)
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

        var account = await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Phone == phone);
        if (account is null && (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.FamilyName)))
            return BadRequest(new { error = "Name and family name required for new customers" });

        // Claim the code in one conditional UPDATE: two requests with the same code at the same
        // moment both pass the check above, but only one of them flips Consumed (QA run 2).
        var claimed = await db.CustomerOtps
            .Where(o => o.Id == entry.Id && !o.Consumed)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Consumed, true));
        if (claimed == 0)
            return BadRequest(new { error = "Invalid or expired code" });

        account ??= await CreateAccountAsync(new CustomerAccount { Phone = phone, Name = req.Name!, FamilyName = req.FamilyName! });

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

        // Errors carry the chat language too (null for a token we never issued), so the landing
        // page doesn't fall back to Hebrew for an English or Arabic chat.
        var tokenRow = await bookingTokens.TryResolveAsync(req.Token);
        if (tokenRow is null)
            return BadRequest(new { error = "This link has expired or is invalid", language = await bookingTokens.FindLanguageAsync(req.Token) });

        var business = await db.Businesses.Where(b => b.Id == tokenRow.BusinessId).Select(b => new { b.Slug }).FirstOrDefaultAsync();
        // Items are soft-deleted (IsActive = false), never hard-deleted -- so this also covers
        // the business deactivating the item after the WhatsApp link was already sent.
        var item = await db.Items.Where(s => s.Id == tokenRow.ItemId && s.IsActive).Select(s => new { s.Id }).FirstOrDefaultAsync();
        if (business is null || item is null)
            return NotFound(new { error = "This business or item is no longer available", language = tokenRow.Language });

        var phone = PhoneNormalizer.Normalize(tokenRow.Phone);
        var account = await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Phone == phone);
        if (account is null)
        {
            var (name, familyName) = SplitProfileName(tokenRow.ProfileName);
            account = await CreateAccountAsync(new CustomerAccount { Phone = phone, Name = name, FamilyName = familyName });
        }

        await db.Customers
            .Where(c => c.Phone == phone && c.CustomerAccountId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.CustomerAccountId, account.Id));

        // A correction the customer typed into a previous booking with THIS business
        // (BookingController.BookAppointment always saves whatever's submitted) is what should
        // come back here -- not the account's own Name/FamilyName, which is only ever auto-filled
        // from a WhatsApp profile name once, on the account's first-ever creation, and never
        // updated after that. Falls back to the account's name when there's no booking history
        // with this business yet (a true first-time-with-this-business customer).
        var existingCustomer = await db.Customers
            .Where(c => c.BusinessId == tokenRow.BusinessId && c.Phone == phone)
            .Select(c => new { c.Name, c.FamilyName })
            .FirstOrDefaultAsync();
        var displayName = existingCustomer?.Name ?? account.Name;
        var displayFamilyName = existingCustomer?.FamilyName ?? account.FamilyName;

        var token = jwt.Generate(account.Id, account.Phone, $"{displayName} {displayFamilyName}".Trim());
        return Ok(new
        {
            token,
            customerId = account.Id,
            name = displayName,
            familyName = displayFamilyName,
            phone = account.Phone,
            businessSlug = business.Slug,
            itemId = item.Id,
            language = tokenRow.Language,
        });
    }

    // Phone is unique, so two logins for a new phone at the same moment (the same link opened
    // twice, React StrictMode's double effect in dev) both try to insert it; the loser used to 500
    // (QA run 4). Same idea as FollowService.EnsureFollowed: take the row that won.
    private async Task<CustomerAccount> CreateAccountAsync(CustomerAccount account)
    {
        db.CustomerAccounts.Add(account);
        try
        {
            await db.SaveChangesAsync();
            return account;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Phone == account.Phone);
            if (winner is null) throw;
            return winner;
        }
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
