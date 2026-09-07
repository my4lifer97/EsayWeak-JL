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
public class BusinessOwnerRequestsController(AppDbContext db, IEmailSender emailSender, IConfiguration config, ILogger<BusinessOwnerRequestsController> logger) : ControllerBase
{
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
        if (string.IsNullOrWhiteSpace(req.BusinessTypeId))
            return BadRequest(new { error = "Business type is required" });

        var businessType = await db.BusinessTypeDefinitions
            .FirstOrDefaultAsync(t => t.Id == req.BusinessTypeId && t.IsActive);
        if (businessType is null)
            return BadRequest(new { error = "Invalid business type" });

        if (await db.Businesses.AnyAsync(b => b.Email == req.Email))
            return BadRequest(new { error = "This email already has an account. Try logging in instead." });

        if (await db.BusinessOwnerRequests.AnyAsync(r => r.Email == req.Email && r.Status == BusinessOwnerRequestStatus.Pending))
            return Conflict(new { error = "A request from this email is already pending review." });

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
