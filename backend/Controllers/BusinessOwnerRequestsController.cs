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

    [HttpPost("api/business-owner-requests")]
    public async Task<IActionResult> Create([FromBody] CreateBusinessOwnerRequestRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.BusinessName))
            return BadRequest(new { error = "Business name is required" });
        if (string.IsNullOrWhiteSpace(req.OwnerName))
            return BadRequest(new { error = "Owner name is required" });
        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
            return BadRequest(new { error = "Invalid email" });
        if (string.IsNullOrWhiteSpace(req.Phone))
            return BadRequest(new { error = "Phone is required" });

        var businessType = string.IsNullOrWhiteSpace(req.BusinessTypeId)
            ? null
            : await db.BusinessTypeDefinitions.FirstOrDefaultAsync(t => t.Id == req.BusinessTypeId && t.IsActive);
        if (!string.IsNullOrWhiteSpace(req.BusinessTypeId) && businessType is null)
            return BadRequest(new { error = "Invalid business type" });

        if (await db.Businesses.AnyAsync(b => b.Email == req.Email))
            return BadRequest(new { error = "This email already has an account. Try logging in instead." });

        if (await db.BusinessOwnerRequests.AnyAsync(r => r.Email == req.Email && r.Status == BusinessOwnerRequestStatus.Pending))
            return Conflict(new { error = "A request from this email is already pending review." });

        var request = new BusinessOwnerRequest
        {
            BusinessName = req.BusinessName,
            OwnerName = req.OwnerName,
            Email = req.Email,
            Phone = req.Phone,
            BusinessTypeId = businessType?.Id,
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
                $"Owner name: {request.OwnerName}\n" +
                $"Email: {request.Email}\n" +
                $"Phone: {request.Phone}\n" +
                $"Business type: {typeLabel}\n\n" +
                "Review it in the platform-admin panel under Pending Requests.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send business-owner-request admin notification for request {RequestId}", request.Id);
        }
    }
}
