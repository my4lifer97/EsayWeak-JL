using System.Security.Claims;
using BarberSaas.Api.Data;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/platform-admin")]
public class PlatformAdminController(
    AppDbContext db, PlatformAdminJwtService adminJwt, JwtService barberJwt, CustomerJwtService customerJwt) : ControllerBase
{
    private string AdminId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // ─── Bootstrap & login ──────────────────────────────────────────────────

    [HttpGet("bootstrap-available")]
    public async Task<IActionResult> BootstrapAvailable()
    {
        var available = !await db.PlatformAdmins.AnyAsync();
        return Ok(new { available });
    }

    // Only ever succeeds once -- creates the first (and, for now, only) platform admin account.
    // Always 403s once one exists, so this can stay a public endpoint without becoming an
    // open door.
    [HttpPost("bootstrap")]
    public async Task<IActionResult> Bootstrap([FromBody] PlatformAdminBootstrapRequest req)
    {
        if (await db.PlatformAdmins.AnyAsync())
            return StatusCode(403, new { error = "An admin account already exists." });
        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
            return BadRequest(new { error = "Invalid email" });
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 8)
            return BadRequest(new { error = "Password must be at least 8 characters" });
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { error = "Name is required" });

        var admin = new PlatformAdmin
        {
            Email = req.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Name = req.Name,
        };
        db.PlatformAdmins.Add(admin);
        await db.SaveChangesAsync();

        var token = adminJwt.Generate(admin.Id, admin.Email, admin.Name);
        return StatusCode(201, new PlatformAdminLoginResponse(token, admin.Id, admin.Name, admin.Email));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] PlatformAdminLoginRequest req)
    {
        var admin = await db.PlatformAdmins.FirstOrDefaultAsync(a => a.Email == req.Email);
        if (admin is null || !BCrypt.Net.BCrypt.Verify(req.Password, admin.PasswordHash))
            return Unauthorized(new { error = "Invalid email or password" });

        var token = adminJwt.Generate(admin.Id, admin.Email, admin.Name);
        return Ok(new PlatformAdminLoginResponse(token, admin.Id, admin.Name, admin.Email));
    }

    // ─── Barbers ────────────────────────────────────────────────────────────

    [HttpGet("barbers")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> SearchBarbers([FromQuery] string? search)
    {
        var query = db.Barbers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(b => b.Name.Contains(search) || b.Email.Contains(search) || b.Slug.Contains(search));

        var barbers = await query.OrderByDescending(b => b.CreatedAt).Take(50)
            .Select(b => new PlatformAdminBarberSummaryDto(b.Id, b.Name, b.Email, b.Slug, b.SubscriptionStatus.ToString()))
            .ToListAsync();
        return Ok(barbers);
    }

    [HttpGet("barbers/{id}")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> GetBarber(string id)
    {
        var b = await db.Barbers.FindAsync(id);
        if (b is null) return NotFound();
        return Ok(new PlatformAdminBarberDetailDto(
            b.Id, b.Name, b.Email, b.Slug, b.Phone, b.TrialEndsAt, b.SubscriptionStatus.ToString(), b.CreatedAt, b.TwilioNumber));
    }

    // Assigns (or clears, with a null body value) which of the platform's own Twilio WhatsApp
    // numbers this barber's chatbot uses -- see Barber.TwilioNumber and TwilioWhatsAppSender.
    [HttpPatch("barbers/{id}/twilio-number")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> SetTwilioNumber(string id, [FromBody] SetTwilioNumberRequest req)
    {
        var b = await db.Barbers.FindAsync(id);
        if (b is null) return NotFound();

        var old = b.TwilioNumber;
        b.TwilioNumber = req.TwilioNumber;
        await db.SaveChangesAsync();

        db.ActivityLogs.Add(new ActivityLog
        {
            BarberId = b.Id,
            ImpersonatedByPlatformAdminId = AdminId,
            Action = $"{nameof(PlatformAdminController)}.{nameof(SetTwilioNumber)}",
            Description = $"WhatsApp number: \"{old}\" → \"{b.TwilioNumber}\"",
            Method = "PATCH",
            Path = Request.Path.ToString(),
            StatusCode = 200,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });
        await db.SaveChangesAsync();

        return Ok(new { b.Id, b.TwilioNumber });
    }

    [HttpGet("barbers/{id}/activity")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> GetBarberActivity(string id)
    {
        var logs = await db.ActivityLogs.Where(a => a.BarberId == id)
            .OrderByDescending(a => a.CreatedAt).Take(200)
            .Select(a => new PlatformAdminActivityLogDto(
                a.Id, a.Action, a.Description, a.Method, a.Path, a.StatusCode, a.IpAddress, a.CreatedAt,
                a.ImpersonatedByPlatformAdminId != null))
            .ToListAsync();
        return Ok(logs);
    }

    [HttpPost("barbers/{id}/impersonate")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> ImpersonateBarber(string id)
    {
        var b = await db.Barbers.FindAsync(id);
        if (b is null) return NotFound();

        var token = barberJwt.GenerateImpersonation(b.Id, b.Email, b.Name, b.Slug, AdminId);
        await LogImpersonation(barberId: b.Id, customerAccountId: null, "ImpersonateBarber");

        return Ok(new PlatformAdminImpersonateResponse(token));
    }

    // ─── Customers ──────────────────────────────────────────────────────────

    [HttpGet("customers")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> SearchCustomers([FromQuery] string? search)
    {
        var query = db.CustomerAccounts.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            // Match against the concatenated "First Last" rather than Name/FamilyName
            // separately -- a full-name search like "Waitlist Customer" spans both columns, and
            // this alone still matches a first-name-only or last-name-only query too.
            query = query.Where(c => (c.Name + " " + c.FamilyName).Contains(search) || c.Phone.Contains(search));

        var customers = await query.OrderByDescending(c => c.CreatedAt).Take(50)
            .Select(c => new PlatformAdminCustomerSummaryDto(c.Id, c.Name, c.FamilyName, c.Phone))
            .ToListAsync();
        return Ok(customers);
    }

    [HttpGet("customers/{id}")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> GetCustomer(string id)
    {
        var c = await db.CustomerAccounts.FindAsync(id);
        if (c is null) return NotFound();
        return Ok(new PlatformAdminCustomerDetailDto(c.Id, c.Name, c.FamilyName, c.Phone, c.CreatedAt));
    }

    [HttpGet("customers/{id}/activity")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> GetCustomerActivity(string id)
    {
        var logs = await db.ActivityLogs.Where(a => a.CustomerAccountId == id)
            .OrderByDescending(a => a.CreatedAt).Take(200)
            .Select(a => new PlatformAdminActivityLogDto(
                a.Id, a.Action, a.Description, a.Method, a.Path, a.StatusCode, a.IpAddress, a.CreatedAt,
                a.ImpersonatedByPlatformAdminId != null))
            .ToListAsync();
        return Ok(logs);
    }

    [HttpPost("customers/{id}/impersonate")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> ImpersonateCustomer(string id)
    {
        var c = await db.CustomerAccounts.FindAsync(id);
        if (c is null) return NotFound();

        var name = $"{c.Name} {c.FamilyName}".Trim();
        var token = customerJwt.GenerateImpersonation(c.Id, c.Phone, name, AdminId);
        await LogImpersonation(barberId: null, customerAccountId: c.Id, "ImpersonateCustomer");

        return Ok(new PlatformAdminImpersonateResponse(token));
    }

    private async Task LogImpersonation(string? barberId, string? customerAccountId, string action)
    {
        db.ActivityLogs.Add(new ActivityLog
        {
            BarberId = barberId,
            CustomerAccountId = customerAccountId,
            ImpersonatedByPlatformAdminId = AdminId,
            Action = $"{nameof(PlatformAdminController)}.{action}",
            Description = "Impersonation started by platform admin",
            Method = "POST",
            Path = Request.Path.ToString(),
            StatusCode = 200,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });
        await db.SaveChangesAsync();
    }
}
