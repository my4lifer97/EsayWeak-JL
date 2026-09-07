using BarberSaas.Api.Data;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, JwtService jwt, IEmailSender emailSender, IWebHostEnvironment env, ILogger<AuthController> logger) : ControllerBase
{
    private static readonly string[] ReservedSlugs =
        ["admin", "api", "login", "register", "cron", "whatsapp", "_next", "favicon", "browse", "account"];

    private const int EmailOtpCooldownSeconds = 45;
    private const int EmailOtpMaxPerHour = 5;
    private const int EmailOtpMaxAttempts = 5;
    private const int EmailOtpExpiryMinutes = 10;

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length < 2)
            return BadRequest(new { error = "Name must be at least 2 characters" });
        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
            return BadRequest(new { error = "Invalid email" });
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
            return BadRequest(new { error = "Password must be at least 6 characters" });
        if (!System.Text.RegularExpressions.Regex.IsMatch(req.Slug, @"^[a-z0-9-]+$") || req.Slug.Length < 3)
            return BadRequest(new { error = "Slug must be lowercase letters, numbers and hyphens (min 3 chars)" });
        if (ReservedSlugs.Contains(req.Slug))
            return BadRequest(new { error = "This URL is reserved" });

        if (await db.Businesses.AnyAsync(b => b.Email == req.Email))
            return BadRequest(new { error = "Email already registered" });
        if (await db.Businesses.AnyAsync(b => b.Slug == req.Slug))
            return BadRequest(new { error = "URL already taken" });

        var business = new Business
        {
            Name = req.Name,
            Email = req.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Slug = req.Slug,
            TrialEndsAt = DateTime.UtcNow.AddDays(30),
            EmailVerified = false,
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
            code = await IssueVerificationCode(req.Email);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send verification email to {Email}", req.Email);
            return StatusCode(502, new { error = "Could not send the verification email. Please check the address and try again." });
        }

        await db.SaveChangesAsync();

        string? devCode = env.IsDevelopment() ? code : null;
        return StatusCode(201, new { business.Id, business.Name, business.Email, business.Slug, devCode });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Email == req.Email);
        if (business is null || !BCrypt.Net.BCrypt.Verify(req.Password, business.PasswordHash))
            return Unauthorized(new { error = "Invalid email or password" });

        if (!business.EmailVerified)
            return StatusCode(403, new { error = "Please verify your email before signing in.", emailNotVerified = true });

        var token = jwt.Generate(business.Id, business.Email, business.Name, business.Slug);
        return Ok(new LoginResponse(token, business.Id, business.Name, business.Email, business.Slug));
    }

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest req)
    {
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Email == req.Email);
        if (business is null) return NotFound(new { error = "Not found" });
        if (business.EmailVerified) return BadRequest(new { error = "Email is already verified" });

        var since = DateTime.UtcNow.AddHours(-1);
        var recent = await db.BusinessEmailOtps
            .Where(o => o.Email == req.Email && o.CreatedAt > since)
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
            code = await IssueVerificationCode(req.Email);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send verification email to {Email}", req.Email);
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

        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Email == req.Email);
        if (business is null) return NotFound(new { error = "Not found" });

        var entry = await db.BusinessEmailOtps
            .Where(o => o.Email == req.Email && !o.Consumed && o.ExpiresAt > DateTime.UtcNow && o.Attempts < EmailOtpMaxAttempts)
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

        var token = jwt.Generate(business.Id, business.Email, business.Name, business.Slug);
        return Ok(new LoginResponse(token, business.Id, business.Name, business.Email, business.Slug));
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Email == req.Email);
        if (business is null) return NotFound(new { error = "Not found" });

        var since = DateTime.UtcNow.AddHours(-1);
        var recent = await db.BusinessPasswordResetOtps
            .Where(o => o.Email == req.Email && o.CreatedAt > since)
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
            code = await IssuePasswordResetCode(req.Email);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send password reset email to {Email}", req.Email);
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

        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Email == req.Email);
        if (business is null) return NotFound(new { error = "Not found" });

        var entry = await db.BusinessPasswordResetOtps
            .Where(o => o.Email == req.Email && !o.Consumed && o.ExpiresAt > DateTime.UtcNow && o.Attempts < EmailOtpMaxAttempts)
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
        await db.SaveChangesAsync();

        var token = jwt.Generate(business.Id, business.Email, business.Name, business.Slug);
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
