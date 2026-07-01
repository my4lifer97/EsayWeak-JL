using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Twilio;
using Twilio.Rest.Api.V2010.Account;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/{slug}/portal")]
public class CustomerPortalController(AppDbContext db, IConfiguration config) : ControllerBase
{
    private static readonly ConcurrentDictionary<string, (string Otp, DateTime Expiry)> _otps = new();

    [HttpPost("otp")]
    public async Task<IActionResult> RequestOtp(string slug, [FromBody] RequestOtpRequest req)
    {
        var barber = await db.Barbers.FirstOrDefaultAsync(b => b.Slug == slug);
        if (barber is null) return NotFound(new { error = "Not found" });

        var isNew = !await db.Customers.AnyAsync(c => c.BarberId == barber.Id && c.Phone == req.Phone);

        var otp = Random.Shared.Next(100000, 999999).ToString();
        _otps[$"{slug}:{req.Phone}"] = (otp, DateTime.UtcNow.AddMinutes(10));

        string? devOtp = null;

        if (!string.IsNullOrEmpty(barber.TwilioSid) && !string.IsNullOrEmpty(barber.TwilioToken) && !string.IsNullOrEmpty(barber.TwilioNumber))
        {
            try
            {
                TwilioClient.Init(barber.TwilioSid, barber.TwilioToken);
                await MessageResource.CreateAsync(
                    to: new Twilio.Types.PhoneNumber($"whatsapp:{req.Phone}"),
                    from: new Twilio.Types.PhoneNumber($"whatsapp:{barber.TwilioNumber}"),
                    body: $"Your {barber.Name} login code: {otp}. Valid for 10 minutes.");
            }
            catch { devOtp = otp; }
        }
        else
        {
            devOtp = otp;
        }

        return Ok(new { isNewCustomer = isNew, devOtp });
    }

    [HttpPost("verify")]
    public async Task<IActionResult> VerifyOtp(string slug, [FromBody] VerifyOtpRequest req)
    {
        var barber = await db.Barbers.FirstOrDefaultAsync(b => b.Slug == slug);
        if (barber is null) return NotFound(new { error = "Not found" });

        var key = $"{slug}:{req.Phone}";
        if (!_otps.TryGetValue(key, out var entry) || entry.Expiry < DateTime.UtcNow || entry.Otp != req.Otp)
            return BadRequest(new { error = "Invalid or expired code" });

        _otps.TryRemove(key, out _);

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.BarberId == barber.Id && c.Phone == req.Phone);
        if (customer is null)
        {
            if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.FamilyName))
                return BadRequest(new { error = "Name and family name required for new customers" });
            customer = new Customer { Name = req.Name, FamilyName = req.FamilyName, Phone = req.Phone, BarberId = barber.Id };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
        }

        var token = GeneratePortalToken(customer.Id, req.Phone, slug);
        return Ok(new { token, customerName = $"{customer.Name} {customer.FamilyName}".Trim() });
    }

    [HttpGet("appointments")]
    public async Task<IActionResult> GetMyAppointments(string slug)
    {
        var (customerId, portalSlug) = ValidatePortalToken();
        if (customerId is null || portalSlug != slug) return Unauthorized();

        var appointments = await db.Appointments
            .Include(a => a.Service)
            .Where(a => a.CustomerId == customerId && a.Barber.Slug == slug)
            .OrderByDescending(a => a.Date).ThenByDescending(a => a.StartTime)
            .Select(a => new
            {
                a.Id,
                Date = a.Date.ToString("yyyy-MM-dd"),
                a.StartTime,
                a.EndTime,
                Status = a.Status.ToString(),
                a.CancelToken,
                Service = new { a.Service.NameEn, a.Service.NameAr, a.Service.NameHe },
            })
            .ToListAsync();

        return Ok(appointments);
    }

    private string GeneratePortalToken(string customerId, string phone, string slug)
    {
        var secret = config["Jwt:Secret"]!;
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim("sub", customerId),
            new Claim("phone", phone),
            new Claim("slug", slug),
            new Claim("type", "portal"),
        };
        var jwt = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private (string? CustomerId, string? Slug) ValidatePortalToken()
    {
        var header = HttpContext.Request.Headers["Authorization"].FirstOrDefault();
        if (header is null || !header.StartsWith("Bearer ")) return (null, null);
        var tokenStr = header[7..];
        try
        {
            var secret = config["Jwt:Secret"]!;
            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(tokenStr, new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = config["Jwt:Issuer"],
                ValidateAudience = true, ValidAudience = config["Jwt:Audience"],
                ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey,
                ValidateLifetime = true,
            }, out _);
            if (principal.FindFirst("type")?.Value != "portal") return (null, null);
            return (principal.FindFirst("sub")?.Value, principal.FindFirst("slug")?.Value);
        }
        catch { return (null, null); }
    }
}

public record RequestOtpRequest(string Phone);
public record VerifyOtpRequest(string Phone, string Otp, string? Name, string? FamilyName);
