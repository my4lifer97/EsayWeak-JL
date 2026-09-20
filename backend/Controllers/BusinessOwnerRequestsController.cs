using System.Text.RegularExpressions;
using BarberSaas.Api.Data;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Controllers;

// Public submission side of the admin-approved onboarding flow -- see PlatformAdminController's
// approve/reject actions for the review side.
[ApiController]
public class BusinessOwnerRequestsController(
    AppDbContext db, IEmailSender emailSender, IOwnerEmailSender ownerEmailSender,
    IConfiguration config, IWebHostEnvironment env, ILogger<BusinessOwnerRequestsController> logger) : ControllerBase
{
    // Same limits as AuthController's self-service email verification.
    private const int EmailOtpCooldownSeconds = 45;
    private const int EmailOtpMaxPerHour = 5;
    private const int EmailOtpMaxAttempts = 5;
    private const int EmailOtpExpiryMinutes = 10;

    [HttpGet("api/business-types")]
    public async Task<IActionResult> ListBusinessTypes()
    {
        var types = await db.BusinessTypeDefinitions
            .Where(t => t.IsActive)
            .OrderBy(t => t.DisplayNameEn)
            .Select(t => new BusinessTypeDto(t.Id, t.Key, t.DisplayNameEn, t.DisplayNameAr, t.DisplayNameHe))
            .ToListAsync();
        return Ok(types);
    }

    // First name / family name are restricted to English letters (plus space, hyphen, apostrophe)
    // because the backend derives the login username from them -- see UsernameGenerator.
    private static readonly Regex EnglishNameRegex = new(@"^[A-Za-z][A-Za-z '-]*$", RegexOptions.Compiled);

    // Proves the requester actually controls the email address before their request ever reaches
    // the admin's review queue -- sent via the admin's own Gmail (IOwnerEmailSender), same as the
    // platform-admin panel's owner-email composer, so it visibly comes from a real account rather
    // than a generic no-reply address.
    [HttpPost("api/business-owner-requests/send-email-code")]
    public async Task<IActionResult> SendEmailCode([FromBody] SendBusinessOwnerRequestEmailCodeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
            return BadRequest(new { error = "Invalid email" });
        var email = req.Email.Trim();

        var since = DateTime.UtcNow.AddHours(-1);
        var recent = await db.BusinessOwnerRequestEmailOtps
            .Where(o => o.Email == email && o.CreatedAt > since)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        var last = recent.FirstOrDefault();
        if (last is not null && (DateTime.UtcNow - last.CreatedAt).TotalSeconds < EmailOtpCooldownSeconds)
            return StatusCode(429, new { error = "Please wait before requesting another code" });
        if (recent.Count >= EmailOtpMaxPerHour)
            return StatusCode(429, new { error = "Too many requests. Try again later" });

        var code = Random.Shared.Next(100000, 999999).ToString();
        db.BusinessOwnerRequestEmailOtps.Add(new BusinessOwnerRequestEmailOtp
        {
            Email = email,
            CodeHash = BCrypt.Net.BCrypt.HashPassword(code),
            ExpiresAt = DateTime.UtcNow.AddMinutes(EmailOtpExpiryMinutes),
        });
        await db.SaveChangesAsync();

        try
        {
            await ownerEmailSender.SendAsync(email, "Verify your email for EsayWeek",
                $"Your verification code is {code}. It expires in {EmailOtpExpiryMinutes} minutes.\n\nEnter it on the account request form to continue.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send business-owner-request email verification code to {Email}", email);
            return StatusCode(502, new { error = "Could not send the verification email. Please try again shortly." });
        }

        string? devCode = env.IsDevelopment() ? code : null;
        return Ok(new { devCode });
    }

    [HttpPost("api/business-owner-requests")]
    public async Task<IActionResult> Create([FromBody] CreateBusinessOwnerRequestRequest req)
    {
        var firstName = (req.OwnerFirstName ?? "").Trim();
        var familyName = (req.OwnerFamilyName ?? "").Trim();

        if (string.IsNullOrWhiteSpace(req.BusinessName))
            return BadRequest(new { error = "Business name is required" });
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(familyName))
            return BadRequest(new { error = "First name and family name are required" });
        if (!EnglishNameRegex.IsMatch(firstName) || !EnglishNameRegex.IsMatch(familyName))
            return BadRequest(new { error = "First name and family name must be in English letters" });
        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
            return BadRequest(new { error = "Invalid email" });
        if (string.IsNullOrWhiteSpace(req.Phone))
            return BadRequest(new { error = "Phone is required" });
        // Digit count only (not a full E.164 check) -- catches an obviously-truncated or junk
        // entry ("12", "0") without rejecting real numbers in whatever local format someone types.
        var phoneDigits = Regex.Replace(req.Phone, @"[^\d]", "");
        if (phoneDigits.Length < 7 || phoneDigits.Length > 15)
            return BadRequest(new { error = "Please enter a valid phone number" });
        if (string.IsNullOrWhiteSpace(req.BusinessTypeId))
            return BadRequest(new { error = "Business type is required" });
        // Non-nullable string params would otherwise trigger [ApiController]'s automatic
        // ModelState validation (a generic ValidationProblemDetails body, no "error" field the
        // frontend knows to show) if this field is ever missing -- e.g. a stale client-side bundle
        // from before this verification step existed, still submitting the old request shape.
        // An explicit check here gives a message the frontend can actually surface.
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { error = "Please verify your email address first." });

        var businessType = await db.BusinessTypeDefinitions
            .FirstOrDefaultAsync(t => t.Id == req.BusinessTypeId && t.IsActive);
        if (businessType is null)
            return BadRequest(new { error = "Invalid business type" });

        if (await db.Businesses.AnyAsync(b => b.Email == req.Email))
            return BadRequest(new { error = "This email already has an account. Try logging in instead." });

        if (await db.BusinessOwnerRequests.AnyAsync(r => r.Email == req.Email && r.Status == BusinessOwnerRequestStatus.Pending))
            return Conflict(new { error = "A request from this email is already pending review." });

        // The request only ever reaches the admin once the requester has proven they control this
        // email -- see SendEmailCode above.
        var otp = await db.BusinessOwnerRequestEmailOtps
            .Where(o => o.Email == req.Email && !o.Consumed && o.ExpiresAt > DateTime.UtcNow && o.Attempts < EmailOtpMaxAttempts)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();
        if (otp is null || !BCrypt.Net.BCrypt.Verify(req.Code ?? "", otp.CodeHash))
        {
            if (otp is not null)
            {
                otp.Attempts++;
                await db.SaveChangesAsync();
            }
            return BadRequest(new { error = "Invalid or expired verification code" });
        }
        otp.Consumed = true;

        var request = new BusinessOwnerRequest
        {
            BusinessName = req.BusinessName.Trim(),
            OwnerFirstName = firstName,
            OwnerFamilyName = familyName,
            Email = req.Email.Trim(),
            Phone = req.Phone.Trim(),
            BusinessTypeId = businessType.Id,
            BusinessDescription = string.IsNullOrWhiteSpace(req.BusinessDescription) ? null : req.BusinessDescription.Trim(),
            SystemNeeds = string.IsNullOrWhiteSpace(req.SystemNeeds) ? null : req.SystemNeeds.Trim(),
        };
        db.BusinessOwnerRequests.Add(request);
        await db.SaveChangesAsync();

        await NotifyAdminAsync(request, businessType);

        return StatusCode(201, new { request.Id });
    }

    // Best-effort only -- this already works today without a verified Resend domain, since
    // Resend's unverified tier can still deliver to the account owner's own address. A failure
    // here must never fail the request submission itself; the admin can still see pending
    // requests in the platform-admin panel regardless.
    private async Task NotifyAdminAsync(BusinessOwnerRequest request, BusinessTypeDefinition? businessType)
    {
        var adminEmail = config["Platform:AdminNotificationEmail"];
        if (string.IsNullOrWhiteSpace(adminEmail)) return;

        try
        {
            var typeLabel = businessType?.DisplayNameEn ?? "(not specified)";
            await emailSender.SendAsync(adminEmail, "New business account request",
                $"Business name: {request.BusinessName}\n" +
                $"Owner: {request.OwnerFirstName} {request.OwnerFamilyName}\n" +
                $"Email: {request.Email}\n" +
                $"Phone: {request.Phone}\n" +
                $"Business type: {typeLabel}\n\n" +
                "Review it in the platform-admin panel under Business requests.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send business-owner-request admin notification for request {RequestId}", request.Id);
        }
    }
}
