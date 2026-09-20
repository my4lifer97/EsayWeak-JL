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
    IEmailSender emailSender, IConfiguration config, ILogger<PlatformAdminController> logger,
    ReviewService reviews, IWhatsAppBridgeClient whatsAppBridge, WhatsAppLinkingService whatsAppLinking,
    IOwnerEmailSender ownerEmailSender) : ControllerBase
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
            b.Id, b.Name, b.Email, b.Slug, b.Phone, b.Username,
            b.TrialEndsAt, b.SubscriptionStatus.ToString(), b.CreatedAt, b.WhatsAppNumber, b.IsDisabled));
    }

    // "Active Directory"-style account lock -- blocks only AuthController.Login (see
    // Business.IsDisabled); storefront/WhatsApp bot/appointments are unaffected.
    [HttpPost("businesses/{id}/disable")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> DisableBusiness(string id)
    {
        var b = await db.Businesses.FindAsync(id);
        if (b is null) return NotFound();

        b.IsDisabled = true;
        db.ActivityLogs.Add(new ActivityLog
        {
            BusinessId = b.Id,
            ImpersonatedByPlatformAdminId = AdminId,
            Action = $"{nameof(PlatformAdminController)}.{nameof(DisableBusiness)}",
            Description = "Account: enabled → disabled",
            Method = "POST",
            Path = Request.Path.ToString(),
            StatusCode = 200,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });
        await db.SaveChangesAsync();

        return Ok(new { b.Id, b.IsDisabled });
    }

    [HttpPost("businesses/{id}/enable")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> EnableBusiness(string id)
    {
        var b = await db.Businesses.FindAsync(id);
        if (b is null) return NotFound();

        b.IsDisabled = false;
        db.ActivityLogs.Add(new ActivityLog
        {
            BusinessId = b.Id,
            ImpersonatedByPlatformAdminId = AdminId,
            Action = $"{nameof(PlatformAdminController)}.{nameof(EnableBusiness)}",
            Description = "Account: disabled → enabled",
            Method = "POST",
            Path = Request.Path.ToString(),
            StatusCode = 200,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });
        await db.SaveChangesAsync();

        return Ok(new { b.Id, b.IsDisabled });
    }

    // Temporary mode mirrors ApproveBusinessOwnerRequest's temp-password mechanics exactly
    // (generate + hash + MustChangePassword=true); custom mode sets an exact password with no
    // forced change, since the admin picked it deliberately. Never logs the actual password --
    // ActivityLog rows here are metadata only, matching every other action in this controller.
    [HttpPost("businesses/{id}/reset-password")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> ResetBusinessPassword(string id, [FromBody] PlatformAdminResetPasswordRequest req)
    {
        var b = await db.Businesses.FindAsync(id);
        if (b is null) return NotFound();

        string? tempPassword = null;
        bool emailSent = false;

        if (req.Temporary)
        {
            tempPassword = GenerateTempPassword();
            b.PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);
            b.MustChangePassword = true;
            // Silent: the owner-email composer just wants the generated value to include in its
            // own admin-composed message -- the system's own generic reset email would otherwise
            // fire in parallel and undercut the whole point of the composer's "choose exactly what
            // gets sent" promise.
            if (!req.Silent)
                emailSent = await SendPasswordResetEmailAsync(b, tempPassword);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 6)
                return BadRequest(new { error = "Password must be at least 6 characters" });

            b.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
            b.MustChangePassword = false;
        }

        db.ActivityLogs.Add(new ActivityLog
        {
            BusinessId = b.Id,
            ImpersonatedByPlatformAdminId = AdminId,
            Action = $"{nameof(PlatformAdminController)}.{nameof(ResetBusinessPassword)}",
            Description = req.Temporary ? "Password reset (temporary)" : "Password reset (admin-set)",
            Method = "POST",
            Path = Request.Path.ToString(),
            StatusCode = 200,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });
        await db.SaveChangesAsync();

        return Ok(new PlatformAdminResetPasswordResponse(tempPassword, emailSent));
    }

    // Best-effort, same reasoning as SendApprovalEmailAsync -- a failed send must never fail the
    // reset itself; the admin always still has the temp password in the response to relay by hand.
    private async Task<bool> SendPasswordResetEmailAsync(Business business, string tempPassword)
    {
        var appUrl = (config["AppUrl"] ?? "").TrimEnd('/');
        var loginUrl = string.IsNullOrEmpty(appUrl) ? "the EsayWeek sign-in page" : $"{appUrl}/admin/login";
        try
        {
            await emailSender.SendAsync(business.Email, "Your EsayWeek password was reset",
                $"Hi {business.Name},\n\n" +
                "Your password was reset by an administrator.\n\n" +
                $"Sign in: {loginUrl}\n" +
                $"Temporary password: {tempPassword}\n\n" +
                "You'll be asked to choose your own password the next time you sign in.\n");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send password-reset email for business {BusinessId}", business.Id);
            return false;
        }
    }

    // Links a business's WhatsApp chatbot to a real, self-hosted (Baileys) session via
    // whatsapp-bridge -- see Business.WhatsAppNumber and BridgeWhatsAppSender. Business.WhatsAppNumber
    // itself is only ever written by WhatsAppLinkingService.GetStatusAndRecordAsync, once the bridge
    // reports a successful link -- these endpoints (plus the token-based ones below) just proxy the
    // bridge's linking flow. See CreateWhatsAppLinkToken for the shareable-link variant of this.
    [HttpPost("businesses/{id}/whatsapp/link")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> StartWhatsAppLink(string id)
    {
        var exists = await db.Businesses.AnyAsync(b => b.Id == id);
        if (!exists) return NotFound();

        var status = await whatsAppBridge.StartLinkAsync(id);
        return Ok(status);
    }

    [HttpGet("businesses/{id}/whatsapp/status")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> GetWhatsAppLinkStatus(string id)
    {
        var b = await db.Businesses.FindAsync(id);
        if (b is null) return NotFound();

        var status = await whatsAppLinking.GetStatusAndRecordAsync(
            b, AdminId, "GET", Request.Path.ToString(), HttpContext.Connection.RemoteIpAddress?.ToString());
        return Ok(status);
    }

    // Generates a short-lived, opaque link the admin can hand off (over any channel) to the
    // business owner, so the owner can complete the QR scan themselves on their own screen instead
    // of the admin relaying a screenshot that goes stale within seconds -- see WhatsAppLinkToken
    // and WhatsAppLinkController. The admin still initiates every session; this only removes the
    // admin as a manual relay for the scan step.
    [HttpPost("businesses/{id}/whatsapp/link-token")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> CreateWhatsAppLinkToken(string id)
    {
        var exists = await db.Businesses.AnyAsync(b => b.Id == id);
        if (!exists) return NotFound();

        var token = await whatsAppLinking.CreateTokenAsync(id, AdminId);
        var appUrl = config["AppUrl"];
        return Ok(new { token = token.Id, url = $"{appUrl}/wa-link/{token.Id}" });
    }

    [HttpDelete("businesses/{id}/whatsapp/link")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> UnlinkWhatsApp(string id)
    {
        var b = await db.Businesses.FindAsync(id);
        if (b is null) return NotFound();

        await whatsAppBridge.UnlinkAsync(id);

        var old = b.WhatsAppNumber;
        b.WhatsAppNumber = null;
        db.ActivityLogs.Add(new ActivityLog
        {
            BusinessId = b.Id,
            ImpersonatedByPlatformAdminId = AdminId,
            Action = $"{nameof(PlatformAdminController)}.{nameof(UnlinkWhatsApp)}",
            Description = $"WhatsApp number: \"{old}\" → (unlinked)",
            Method = "DELETE",
            Path = Request.Path.ToString(),
            StatusCode = 200,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });
        await db.SaveChangesAsync();

        return Ok(new { b.Id, b.WhatsAppNumber });
    }

    // Backs the platform-admin panel's owner-email composer: the admin picks exactly which pieces
    // (username, password, chatbot link, any combination) to include, previews the exact text, and
    // this just sends whatever was composed -- no server-side templating, so the preview the admin
    // saw is exactly the email that goes out. Sent via the admin's own Gmail (IOwnerEmailSender),
    // not the system IEmailSender chain -- see IOwnerEmailSender for why.
    [HttpPost("businesses/{id}/email")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> EmailOwner(string id, [FromBody] EmailOwnerRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Subject) || string.IsNullOrWhiteSpace(req.Body))
            return BadRequest(new { error = "Subject and body are required" });

        var gmailConfigured = !string.IsNullOrEmpty(config["Gmail:ClientId"])
            && !string.IsNullOrEmpty(config["Gmail:ClientSecret"])
            && !string.IsNullOrEmpty(config["Gmail:RefreshToken"]);
        if (!gmailConfigured)
            return StatusCode(503, new { error = "Gmail isn't configured yet -- set Gmail:ClientId/ClientSecret/RefreshToken/FromEmail." });

        var b = await db.Businesses.FindAsync(id);
        if (b is null) return NotFound();

        try
        {
            await ownerEmailSender.SendAsync(b.Email, req.Subject, req.Body);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to email business owner {BusinessId}", id);
            return StatusCode(502, new { error = "Could not send the email. Please try again shortly." });
        }

        db.ActivityLogs.Add(new ActivityLog
        {
            BusinessId = b.Id,
            ImpersonatedByPlatformAdminId = AdminId,
            Action = $"{nameof(PlatformAdminController)}.{nameof(EmailOwner)}",
            Description = $"Emailed owner: \"{req.Subject}\"",
            Method = "POST",
            Path = Request.Path.ToString(),
            StatusCode = 200,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        });
        await db.SaveChangesAsync();

        return Ok(new { ok = true });
    }

    [HttpGet("businesses/{id}/activity")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> GetBusinessActivity(string id)
    {
        var logs = await db.ActivityLogs.Where(a => a.BusinessId == id)
            .OrderByDescending(a => a.CreatedAt).Take(200)
            .Select(a => new PlatformAdminActivityLogDto(
                a.Id, a.Action, a.Description, a.Method, a.Path, a.StatusCode, a.IpAddress, a.UserAgent, a.CreatedAt,
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
                a.Id, a.Action, a.Description, a.Method, a.Path, a.StatusCode, a.IpAddress, a.UserAgent, a.CreatedAt,
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

        // Silent: the owner-email composer wants to present the generated credentials for the
        // admin to review/edit before anything goes out -- the automatic approval email would
        // otherwise fire immediately and undercut that choice.
        var emailSent = req.Silent ? false : await SendApprovalEmailAsync(request, username, tempPassword);

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

    // ─── Review moderation ──────────────────────────────────────────────────

    [HttpGet("reviews")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public async Task<IActionResult> ListReviews([FromQuery] string? businessId)
    {
        var q = db.Reviews.AsQueryable();
        if (!string.IsNullOrWhiteSpace(businessId))
            q = q.Where(r => r.BusinessId == businessId);

        var rows = await q
            .OrderByDescending(r => r.CreatedAt)
            .Take(200)
            .Select(r => new
            {
                r.Id, r.Rating, r.Comment, r.CreatedAt, r.UpdatedAt, r.IsHidden, r.OwnerReply, r.OwnerRepliedAt,
                r.CustomerAccount.Name, r.CustomerAccount.FamilyName,
                ItemName = r.Appointment.Item.NameEn,
            })
            .ToListAsync();

        var dtos = rows.Select(r => new AdminReviewDto(
            r.Id, r.Rating, r.Comment, $"{r.Name} {r.FamilyName}".Trim(), r.ItemName,
            r.CreatedAt, r.UpdatedAt, r.IsHidden, r.OwnerReply, r.OwnerRepliedAt));

        return Ok(dtos);
    }

    [HttpPost("reviews/{id}/hide")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public Task<IActionResult> HideReview(string id) => SetReviewHidden(id, true);

    [HttpPost("reviews/{id}/unhide")]
    [Authorize(Policy = "PlatformAdminOnly")]
    public Task<IActionResult> UnhideReview(string id) => SetReviewHidden(id, false);

    private async Task<IActionResult> SetReviewHidden(string id, bool hidden)
    {
        var review = await db.Reviews.FindAsync(id);
        if (review is null) return NotFound();

        review.IsHidden = hidden;
        await db.SaveChangesAsync();
        await reviews.RecomputeAggregate(review.BusinessId);

        return Ok(new { review.Id, review.IsHidden });
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
