using System.Security.Claims;
using System.Security.Cryptography;
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
    AppDbContext db, PlatformAdminJwtService adminJwt, JwtService businessJwt, CustomerJwtService customerJwt,
    IEmailSender emailSender, IConfiguration config, ILogger<PlatformAdminController> logger) : ControllerBase
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

    // ─── Businesses ────────────────────────────────────────────────────────────

    [HttpGet("businesses")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> SearchBusinesses([FromQuery] string? search)
    {
        var query = db.Businesses.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(b => b.Name.Contains(search) || b.Email.Contains(search) || b.Slug.Contains(search));

        var businesses = await query.OrderByDescending(b => b.CreatedAt).Take(50)
            .Select(b => new PlatformAdminBusinessSummaryDto(b.Id, b.Name, b.Email, b.Slug, b.SubscriptionStatus.ToString()))
            .ToListAsync();
        return Ok(businesses);
    }

    [HttpGet("businesses/{id}")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> GetBusiness(string id)
    {
        var b = await db.Businesses.FindAsync(id);
        if (b is null) return NotFound();
        return Ok(new PlatformAdminBusinessDetailDto(
            b.Id, b.Name, b.Email, b.Slug, b.Phone, b.TrialEndsAt, b.SubscriptionStatus.ToString(), b.CreatedAt, b.TwilioNumber));
    }

    // Assigns (or clears, with a null body value) which of the platform's own Twilio WhatsApp
    // numbers this business's chatbot uses -- see Business.TwilioNumber and TwilioWhatsAppSender.
    [HttpPatch("businesses/{id}/twilio-number")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> SetTwilioNumber(string id, [FromBody] SetTwilioNumberRequest req)
    {
        var b = await db.Businesses.FindAsync(id);
        if (b is null) return NotFound();

        var old = b.TwilioNumber;
        b.TwilioNumber = req.TwilioNumber;
        await db.SaveChangesAsync();

        db.ActivityLogs.Add(new ActivityLog
        {
            BusinessId = b.Id,
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

    [HttpGet("businesses/{id}/activity")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> GetBusinessActivity(string id)
    {
        var logs = await db.ActivityLogs.Where(a => a.BusinessId == id)
            .OrderByDescending(a => a.CreatedAt).Take(200)
            .Select(a => new PlatformAdminActivityLogDto(
                a.Id, a.Action, a.Description, a.Method, a.Path, a.StatusCode, a.IpAddress, a.CreatedAt,
                a.ImpersonatedByPlatformAdminId != null))
            .ToListAsync();
        return Ok(logs);
    }

    [HttpPost("businesses/{id}/impersonate")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> ImpersonateBusiness(string id)
    {
        var b = await db.Businesses.FindAsync(id);
        if (b is null) return NotFound();

        var token = businessJwt.GenerateImpersonation(b.Id, b.Email, b.Name, b.Slug, AdminId);
        await LogImpersonation(businessId: b.Id, customerAccountId: null, "ImpersonateBusiness");

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
        await LogImpersonation(businessId: null, customerAccountId: c.Id, "ImpersonateCustomer");

        return Ok(new PlatformAdminImpersonateResponse(token));
    }

    // ─── Business owner requests ───────────────────────────────────────────

    // status: "Pending" | "Approved" | "Rejected" | "All" (or omitted) -- omitted/unrecognized
    // defaults to All so the dashboard can show the full history, not just the queue.
    [HttpGet("business-owner-requests")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> ListBusinessOwnerRequests([FromQuery] string? status)
    {
        var query = db.BusinessOwnerRequests.AsQueryable();
        if (Enum.TryParse<BusinessOwnerRequestStatus>(status, ignoreCase: true, out var parsedStatus))
            query = query.Where(r => r.Status == parsedStatus);

        var requests = await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(ToDto)
            .ToListAsync();

        return Ok(requests);
    }

    [HttpGet("business-owner-requests/{id}")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> GetBusinessOwnerRequest(string id)
    {
        var dto = await db.BusinessOwnerRequests
            .Where(x => x.Id == id)
            .Select(ToDto)
            .FirstOrDefaultAsync();
        if (dto is null) return NotFound();

        return Ok(dto);
    }

    private static readonly System.Linq.Expressions.Expression<Func<BusinessOwnerRequest, BusinessOwnerRequestDto>> ToDto = r => new BusinessOwnerRequestDto(
        r.Id, r.BusinessName, r.OwnerFirstName, r.OwnerFamilyName, r.Email, r.Phone,
        r.BusinessTypeId, r.BusinessType != null ? r.BusinessType.DisplayNameEn : null,
        r.BusinessDescription, r.SystemNeeds,
        r.Status.ToString(), r.RejectionNote, r.CreatedAt, r.ReviewedAt,
        r.CreatedBusiness != null ? r.CreatedBusiness.Slug : null,
        r.CreatedBusiness != null ? r.CreatedBusiness.Username : null);

    [HttpPost("business-owner-requests/{id}/approve")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> ApproveBusinessOwnerRequest(string id, [FromBody] ApproveBusinessOwnerRequestRequest req)
    {
        var request = await db.BusinessOwnerRequests.FindAsync(id);
        if (request is null) return NotFound();
        if (request.Status != BusinessOwnerRequestStatus.Pending)
            return BadRequest(new { error = "This request has already been reviewed." });

        if (!SlugValidator.IsValidFormat(req.Slug))
            return BadRequest(new { error = "Slug must be lowercase letters, numbers and hyphens (min 3 chars)" });
        if (SlugValidator.IsReserved(req.Slug))
            return BadRequest(new { error = "This URL is reserved" });
        if (await db.Businesses.AnyAsync(b => b.Slug == req.Slug))
            return BadRequest(new { error = "URL already taken" });
        // Re-check at approval time too -- the requester's email could have registered a real
        // account through the self-service flow in the time since this request was submitted.
        if (await db.Businesses.AnyAsync(b => b.Email == request.Email))
            return BadRequest(new { error = "This email already has an account." });

        // Username is generated here (never by the client) from the owner's name and made unique
        // against every existing Business.Username, guarded further by the DB unique index.
        var username = await UsernameGenerator.GenerateUniqueAsync(
            request.OwnerFirstName, request.OwnerFamilyName,
            candidate => db.Businesses.AnyAsync(b => b.Username == candidate));

        var tempPassword = GenerateTempPassword();
        var business = new Business
        {
            Name = request.BusinessName,
            Email = request.Email,
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword),
            Slug = req.Slug,
            TrialEndsAt = DateTime.UtcNow.AddDays(30),
            // The admin already vetted this request directly -- no self-service OTP loop needed.
            EmailVerified = true,
            BusinessTypeId = request.BusinessTypeId,
            MustChangePassword = true,
        };
        db.Businesses.Add(business);

        // Same default-schedule seeding as AuthController.Register -- new businesses default to
        // BusinessModel.Appointment, so this always runs today, but stays guarded for when this
        // flow can accept a Showcase business type too.
        if (business.BusinessModel != BusinessModel.Showcase)
        {
            var defaultHours = new[] { 1, 2, 3, 4, 5 }.Select(day => new WorkingHours
            {
                BusinessId = business.Id,
                DayOfWeek = day,
                StartTime = "09:00",
                EndTime = "18:00",
                IsActive = true,
            });
            db.WorkingHours.AddRange(defaultHours);
        }

        request.Status = BusinessOwnerRequestStatus.Approved;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewedByPlatformAdminId = AdminId;
        request.CreatedBusinessId = business.Id;
        await db.SaveChangesAsync();

        db.ActivityLogs.Add(new ActivityLog
        {
            BusinessId = business.Id,
            ImpersonatedByPlatformAdminId = AdminId,
            Action = $"{nameof(PlatformAdminController)}.{nameof(ApproveBusinessOwnerRequest)}",
            Description = $"Account created from request by {request.OwnerFirstName} {request.OwnerFamilyName} ({request.Email}), username {username}",
            Method = "POST",
            Path = Request.Path.ToString(),
            StatusCode = 200,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });
        await db.SaveChangesAsync();

        var emailSent = await SendApprovalEmailAsync(request, username, tempPassword);

        // The temp password is still returned even when the email went out, so the admin can send
        // it by hand if delivery failed (bad address, provider limit). Never logged, never stored
        // (only the bcrypt hash is persisted).
        return Ok(new ApproveBusinessOwnerRequestResponse(business.Id, business.Slug, username, tempPassword, emailSent));
    }

    // Best-effort -- a failed send (SMTP hiccup, unverified sending domain, typo'd address) must
    // never fail the approval itself; the admin always still has the credentials in the response.
    private async Task<bool> SendApprovalEmailAsync(BusinessOwnerRequest request, string username, string tempPassword)
    {
        var appUrl = (config["AppUrl"] ?? "").TrimEnd('/');
        var loginUrl = string.IsNullOrEmpty(appUrl) ? "the EsayWeek sign-in page" : $"{appUrl}/admin/login";
        try
        {
            await emailSender.SendAsync(request.Email, "Your EsayWeek account is ready",
                $"Hi {request.OwnerFirstName},\n\n" +
                $"Your business account for \"{request.BusinessName}\" has been approved.\n\n" +
                $"Sign in: {loginUrl}\n" +
                $"Username: {username}\n" +
                $"Temporary password: {tempPassword}\n\n" +
                "You'll be asked to choose your own password the first time you sign in.\n");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send approval email for business-owner request {RequestId}", request.Id);
            return false;
        }
    }

    [HttpPost("business-owner-requests/{id}/reject")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> RejectBusinessOwnerRequest(string id, [FromBody] RejectBusinessOwnerRequestRequest req)
    {
        var request = await db.BusinessOwnerRequests.FindAsync(id);
        if (request is null) return NotFound();
        if (request.Status != BusinessOwnerRequestStatus.Pending)
            return BadRequest(new { error = "This request has already been reviewed." });

        request.Status = BusinessOwnerRequestStatus.Rejected;
        request.RejectionNote = req.Note;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewedByPlatformAdminId = AdminId;
        await db.SaveChangesAsync();

        return Ok(new { ok = true });
    }

    // 12 characters from an alphabet that excludes visually-ambiguous characters (0/O, 1/l/I) --
    // this is read once by a human off a screen and typed/pasted elsewhere, not entered
    // programmatically, so ambiguity costs real support friction.
    private static string GenerateTempPassword()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(12);
        return new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
    }

    private async Task LogImpersonation(string? businessId, string? customerAccountId, string action)
    {
        db.ActivityLogs.Add(new ActivityLog
        {
            BusinessId = businessId,
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
