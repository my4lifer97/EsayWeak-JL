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

        if (await db.Barbers.AnyAsync(b => b.Email == req.Email))
            return BadRequest(new { error = "Email already registered" });
        if (await db.Barbers.AnyAsync(b => b.Slug == req.Slug))
            return BadRequest(new { error = "URL already taken" });

        var barber = new Barber
        {
            Name = req.Name,
            Email = req.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Slug = req.Slug,
            TrialEndsAt = DateTime.UtcNow.AddDays(30),
            EmailVerified = false,
        };

        db.Barbers.Add(barber);

        var defaultHours = new[] { 1, 2, 3, 4, 5 }.Select(day => new WorkingHours
        {
            BarberId = barber.Id,
            DayOfWeek = day,
            StartTime = "09:00",
            EndTime = "18:00",
            IsActive = true,
        });
        db.WorkingHours.AddRange(defaultHours);

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
        return StatusCode(201, new { barber.Id, barber.Name, barber.Email, barber.Slug, devCode });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var barber = await db.Barbers.FirstOrDefaultAsync(b => b.Email == req.Email);
        if (barber is null || !BCrypt.Net.BCrypt.Verify(req.Password, barber.PasswordHash))
            return Unauthorized(new { error = "Invalid email or password" });

        if (!barber.EmailVerified)
            return StatusCode(403, new { error = "Please verify your email before signing in.", emailNotVerified = true });

        var token = jwt.Generate(barber.Id, barber.Email, barber.Name, barber.Slug);
        return Ok(new LoginResponse(token, barber.Id, barber.Name, barber.Email, barber.Slug));
    }

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest req)
    {
        var barber = await db.Barbers.FirstOrDefaultAsync(b => b.Email == req.Email);
        if (barber is null) return NotFound(new { error = "Not found" });
        if (barber.EmailVerified) return BadRequest(new { error = "Email is already verified" });

        var since = DateTime.UtcNow.AddHours(-1);
        var recent = await db.BarberEmailOtps
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

        var barber = await db.Barbers.FirstOrDefaultAsync(b => b.Email == req.Email);
        if (barber is null) return NotFound(new { error = "Not found" });

        var entry = await db.BarberEmailOtps
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
        barber.EmailVerified = true;
        await db.SaveChangesAsync();

        var token = jwt.Generate(barber.Id, barber.Email, barber.Name, barber.Slug);
        return Ok(new LoginResponse(token, barber.Id, barber.Name, barber.Email, barber.Slug));
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req)
    {
        var barber = await db.Barbers.FirstOrDefaultAsync(b => b.Email == req.Email);
        if (barber is null) return NotFound(new { error = "Not found" });

        var since = DateTime.UtcNow.AddHours(-1);
        var recent = await db.BarberPasswordResetOtps
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

        var barber = await db.Barbers.FirstOrDefaultAsync(b => b.Email == req.Email);
        if (barber is null) return NotFound(new { error = "Not found" });

        var entry = await db.BarberPasswordResetOtps
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
        barber.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);
        await db.SaveChangesAsync();

        var token = jwt.Generate(barber.Id, barber.Email, barber.Name, barber.Slug);
        return Ok(new LoginResponse(token, barber.Id, barber.Name, barber.Email, barber.Slug));
    }

    private async Task<string> IssuePasswordResetCode(string email)
    {
        var code = Random.Shared.Next(100000, 999999).ToString();
        db.BarberPasswordResetOtps.Add(new BarberPasswordResetOtp
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
        db.BarberEmailOtps.Add(new BarberEmailOtp
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
