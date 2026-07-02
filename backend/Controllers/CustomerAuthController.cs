using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/customer/auth")]
public class CustomerAuthController(AppDbContext db, CustomerJwtService jwt, IOtpSender otpSender, IWebHostEnvironment env) : ControllerBase
{
    private const int OtpCooldownSeconds = 45;
    private const int OtpMaxPerHour = 5;
    private const int OtpMaxAttempts = 5;
    private const int OtpExpiryMinutes = 10;

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

        var code = Random.Shared.Next(100000, 999999).ToString();
        db.CustomerOtps.Add(new CustomerOtp
        {
            Phone = phone,
            CodeHash = BCrypt.Net.BCrypt.HashPassword(code),
            ExpiresAt = DateTime.UtcNow.AddMinutes(OtpExpiryMinutes),
        });
        await db.SaveChangesAsync();

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
}

public record RequestCustomerOtpRequest(string Phone);
public record VerifyCustomerOtpRequest(string Phone, string Otp, string? Name, string? FamilyName);
