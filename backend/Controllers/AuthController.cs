using System.Security.Claims;
using BarberSaas.Api.Data;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Filters;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, JwtService jwt, IEmailSender emailSender, IWebHostEnvironment env, ILogger<AuthController> logger) : ControllerBase
{
    private const int EmailOtpCooldownSeconds = 45;
    private const int EmailOtpMaxPerHour = 5;
    private const int EmailOtpMaxAttempts = 5;
    private const int EmailOtpExpiryMinutes = 10;

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length < 2)
            return BadRequest(new { error = "Name must be at least 2 characters" });
        var email = EmailAddress.Normalize(req.Email);
        if (!email.Contains('@'))
            return BadRequest(new { error = "Invalid email" });
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
            return BadRequest(new { error = "Password must be at least 6 characters" });
        if (!SlugValidator.IsValidFormat(req.Slug))
            return BadRequest(new { error = "Slug must be lowercase letters, numbers and hyphens (min 3 chars)" });
        if (SlugValidator.IsReserved(req.Slug))
            return BadRequest(new { error = "This URL is reserved" });

        if (await db.Businesses.AnyAsync(b => b.Email.ToLower() == email))
            return BadRequest(new { error = "Email already registered" });
        if (await db.Businesses.AnyAsync(b => b.Slug == req.Slug))
            return BadRequest(new { error = "URL already taken" });

        // Checked before anything is sent: an unknown id used to fail the save with a 500 after
        // the verification email had already gone out.
        if (req.BusinessTypeId is not null && !await db.BusinessTypeDefinitions.AnyAsync(t => t.Id == req.BusinessTypeId && t.IsActive))
            return BadRequest(new { error = "Invalid business type" });

        var business = new Business
        {
            Name = req.Name,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Slug = req.Slug,
            TrialEndsAt = DateTime.UtcNow.AddDays(30),
            EmailVerified = false,
            // Optional -- the self-service register form asks for this so a self-registered
            // business shows up under category filters in /browse the same as an admin-approved
            // one, but existing callers that don't send it (older clients, tests) still work.
            BusinessTypeId = req.BusinessTypeId,
        };

        db.Businesses.Add(business);

        // RegisterRequest doesn't yet expose a way to register directly as Showcase (every new
        // business defaults to BusinessModel.Appointment), but guard the seeding anyway so a
        // future onboarding flow that does can register a showcase-only business without an
        // unwanted default schedule.
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

        string code;
        try
        {
            code = await IssueVerificationCode(email);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send verification email to {Email}", email);
            return StatusCode(502, new { error = "Could not send the verification email. Please check the address and try again." });
        }

        await db.SaveChangesAsync();

        string? devCode = env.IsDevelopment() ? code : null;
        return StatusCode(201, new { business.Id, business.Name, business.Email, business.Slug, devCode });
    }

    // Same shape as BlockDisabledBusinessFilter's response, which the frontend signs out on.
    private ObjectResult AccountDisabled() =>
        StatusCode(403, new { error = "This account has been disabled. Contact support.", accountDisabled = true });

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, [FromServices] LoginThrottle throttle)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (throttle.RetryAfter("owner", req.Email ?? "", ip, DateTime.UtcNow) is { } wait)
        {
            Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
            return StatusCode(429, new { error = LoginThrottle.TooManyAttemptsMessage(wait) });
        }

        // The `Email` field carries either an email or a system-generated username (approved
        // accounts get one — see PlatformAdminController.ApproveBusinessOwnerRequest). Match on
        // whichever; the username lookup is skipped for blank/null so legacy rows with a null
        // username can't be hit by an empty identifier.
        var identifier = EmailAddress.Normalize(req.Email);
        var business = await db.Businesses.FirstOrDefaultAsync(b =>
            b.Email.ToLower() == identifier || (b.Username != null && b.Username.ToLower() == identifier));
        if (business is null || !BCrypt.Net.BCrypt.Verify(req.Password, business.PasswordHash))
        {
            throttle.RecordFailure("owner", req.Email ?? "", ip, DateTime.UtcNow);
            return Unauthorized(new { error = "Invalid email or password" });
        }
        throttle.RecordSuccess("owner", req.Email ?? "", ip);

        if (business.IsDisabled) return AccountDisabled();

        if (!business.EmailVerified)
            return StatusCode(403, new { error = "Please verify your email before signing in.", emailNotVerified = true });

        var token = jwt.Generate(business.Id, business.Email, business.Name, business.Slug, business.PasswordHash, business.MustChangePassword);
        await LogLoginAsync(business.Id, nameof(Login));
        return Ok(new LoginResponse(token, business.Id, business.Name, business.Email, business.Slug, business.MustChangePassword));
    }

    // Login itself is anonymous (no JWT on the request yet), so ActivityLogFilter's generic
    // per-request sweep -- which reads identity off the *incoming* request -- never sees it, even
    // though the response carries a freshly minted token. Explicit logging here (and at every
    // other action that logs someone in) is the only way "signed in" ends up in the activity feed.
    private async Task LogLoginAsync(string businessId, string action)
    {
        db.ActivityLogs.Add(new ActivityLog
        {
            BusinessId = businessId,
            Action = $"{nameof(AuthController)}.{action}",
            Description = "Signed in",
            Method = Request.Method,
            Path = Request.Path.ToString(),
            StatusCode = 200,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null,
        });
        await db.SaveChangesAsync();
    }

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest req)
    {
        var email = EmailAddress.Normalize(req.Email);
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Email.ToLower() == email);
        if (business is null) return NotFound(new { error = "Not found" });
        if (business.EmailVerified) return BadRequest(new { error = "Email is already verified" });

        var since = DateTime.UtcNow.AddHours(-1);
        var recent = await db.BusinessEmailOtps
            .Where(o => o.Email == email && o.CreatedAt > since)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var last = recent.FirstOrDefault();
        if (last is not null && (DateTime.UtcNow - last.CreatedAt).TotalSeconds < EmailOtpCooldownSeconds)
            return StatusCode(429, new { error = "Please wait before requesting another code" });

        if (recent.Count >= EmailOtpMaxPerHour)
            return StatusCode(429, new { error = "Too many requests. Try again later" });

        string code;
        try
        {
            code = await IssueVerificationCode(email);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send verification email to {Email}", email);
            return StatusCode(502, new { error = "Could not send the verification email. Please try again shortly." });
        }
        await db.SaveChangesAsync();

        string? devCode = env.IsDevelopment() ? code : null;
        return Ok(new { devCode });
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { error = "Email and code are required" });

        var email = EmailAddress.Normalize(req.Email);
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Email.ToLower() == email);
        if (business is null) return NotFound(new { error = "Not found" });
        if (business.IsDisabled) return AccountDisabled();

        var entry = await db.BusinessEmailOtps
            .Where(o => o.Email == email && !o.Consumed && o.ExpiresAt > DateTime.UtcNow && o.Attempts < EmailOtpMaxAttempts)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (entry is null || !BCrypt.Net.BCrypt.Verify(req.Code, entry.CodeHash))
        {
            if (entry is not null)
            {
                entry.Attempts++;
                await db.SaveChangesAsync();
            }
            return BadRequest(new { error = "Invalid or expired code" });
        }

        entry.Consumed = true;
        business.EmailVerified = true;
        await db.SaveChangesAsync();

        var token = jwt.Generate(business.Id, business.Email, business.Name, business.Slug, business.PasswordHash, business.MustChangePassword);
        await LogLoginAsync(business.Id, nameof(VerifyEmail));
        return Ok(new LoginResponse(token, business.Id, business.Name, business.Email, business.Slug, business.MustChangePassword));
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        // Same 200 whether or not the email has an account, so this can't be used to find out
        // which emails are registered. A disabled account gets the same silent answer -- resetting
        // its password would otherwise hand a disabled owner a fresh login.
        var email = EmailAddress.Normalize(req.Email);
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Email.ToLower() == email);
        if (business is null || business.IsDisabled) return Ok(new { devCode = (string?)null });

        var since = DateTime.UtcNow.AddHours(-1);
        var recent = await db.BusinessPasswordResetOtps
            .Where(o => o.Email == email && o.CreatedAt > since)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var last = recent.FirstOrDefault();
        if (last is not null && (DateTime.UtcNow - last.CreatedAt).TotalSeconds < EmailOtpCooldownSeconds)
            return StatusCode(429, new { error = "Please wait before requesting another code" });

        if (recent.Count >= EmailOtpMaxPerHour)
            return StatusCode(429, new { error = "Too many requests. Try again later" });

        string code;
        try
        {
            code = await IssuePasswordResetCode(email);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send password reset email to {Email}", email);
            return StatusCode(502, new { error = "Could not send the reset email. Please try again shortly." });
        }
        await db.SaveChangesAsync();

        string? devCode = env.IsDevelopment() ? code : null;
        return Ok(new { devCode });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 6)
            return BadRequest(new { error = "Password must be at least 6 characters" });

        // An unknown email gets the same answer as a wrong code (see ForgotPassword).
        var email = EmailAddress.Normalize(req.Email);
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Email.ToLower() == email);
        if (business is null) return BadRequest(new { error = "Invalid or expired code" });
        // Also covers a reset code issued before the account was disabled.
        if (business.IsDisabled) return AccountDisabled();

        var entry = await db.BusinessPasswordResetOtps
            .Where(o => o.Email == email && !o.Consumed && o.ExpiresAt > DateTime.UtcNow && o.Attempts < EmailOtpMaxAttempts)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (entry is null || !BCrypt.Net.BCrypt.Verify(req.Code, entry.CodeHash))
        {
            if (entry is not null)
            {
                entry.Attempts++;
                await db.SaveChangesAsync();
            }
            return BadRequest(new { error = "Invalid or expired code" });
        }

        entry.Consumed = true;
        business.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        // Proving email ownership via OTP and setting a real password accomplishes the same thing
        // as ChangePassword -- an admin-issued temp password no longer needs to be replaced.
        business.MustChangePassword = false;
        await db.SaveChangesAsync();

        // The new hash changes the password stamp, so every other login of this account ends.
        var token = jwt.Generate(business.Id, business.Email, business.Name, business.Slug, business.PasswordHash);
        await LogLoginAsync(business.Id, nameof(ResetPassword));
        return Ok(new LoginResponse(token, business.Id, business.Name, business.Email, business.Slug));
    }

    [HttpPatch("change-password")]
    [Authorize(Policy = "BusinessOnly")]
    [AllowWithPendingPasswordChange]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, [FromServices] LoginThrottle throttle)
    {
        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 6)
            return BadRequest(new { error = "Password must be at least 6 characters" });

        var businessId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var business = await db.Businesses.FindAsync(businessId);
        if (business is null) return NotFound();

        // The current password is required so a stolen or left-open session can't take over the
        // account. Throttled like login, since this would otherwise be a way to guess it. 400, not
        // 401, so the frontend doesn't treat a typo as an expired session.
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (throttle.RetryAfter("change-password", businessId, ip, DateTime.UtcNow) is { } wait)
        {
            Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
            return StatusCode(429, new { error = LoginThrottle.TooManyAttemptsMessage(wait) });
        }
        if (string.IsNullOrEmpty(req.CurrentPassword) || !BCrypt.Net.BCrypt.Verify(req.CurrentPassword, business.PasswordHash))
        {
            throttle.RecordFailure("change-password", businessId, ip, DateTime.UtcNow);
            return BadRequest(new { error = "Current password is incorrect", currentPasswordIncorrect = true });
        }
        throttle.RecordSuccess("change-password", businessId, ip);
        // Otherwise an admin-issued temporary password could just be kept.
        if (req.NewPassword == req.CurrentPassword)
            return BadRequest(new { error = "Choose a password different from your current one", samePassword = true });

        business.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        business.MustChangePassword = false;
        await db.SaveChangesAsync();

        // Fresh token without the mustChangePassword claim, so the client can keep using it
        // immediately without a second login round-trip. Its new password stamp ends every other
        // login of this account.
        var token = jwt.Generate(business.Id, business.Email, business.Name, business.Slug, business.PasswordHash);
        return Ok(new LoginResponse(token, business.Id, business.Name, business.Email, business.Slug));
    }

    private async Task<string> IssuePasswordResetCode(string email)
    {
        var code = Random.Shared.Next(100000, 999999).ToString();
        db.BusinessPasswordResetOtps.Add(new BusinessPasswordResetOtp
        {
            Email = email,
            CodeHash = BCrypt.Net.BCrypt.HashPassword(code),
            ExpiresAt = DateTime.UtcNow.AddMinutes(EmailOtpExpiryMinutes),
        });

        await emailSender.SendAsync(email, "Reset your EsayWeek password",
            $"Your password reset code is {code}. It expires in {EmailOtpExpiryMinutes} minutes. If you didn't request this, you can ignore this email.");

        return code;
    }

    private async Task<string> IssueVerificationCode(string email)
    {
        var code = Random.Shared.Next(100000, 999999).ToString();
        db.BusinessEmailOtps.Add(new BusinessEmailOtp
        {
            Email = email,
            CodeHash = BCrypt.Net.BCrypt.HashPassword(code),
            ExpiresAt = DateTime.UtcNow.AddMinutes(EmailOtpExpiryMinutes),
        });

        await emailSender.SendAsync(email, "Verify your EsayWeek email",
            $"Your verification code is {code}. It expires in {EmailOtpExpiryMinutes} minutes.");

        return code;
    }
}
