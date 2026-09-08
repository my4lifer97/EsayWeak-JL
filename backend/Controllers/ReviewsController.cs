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
[Route("api/reviews")]
[Authorize(Policy = "CustomerOnly")]
public class ReviewsController(AppDbContext db, ReviewService reviews) : ControllerBase
{
    private string AccountId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private const int MaxCommentLength = 1000;

    [HttpGet("eligibility")]
    public async Task<IActionResult> Eligibility([FromQuery] string businessSlug)
    {
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Slug == businessSlug);
        if (business is null) return NotFound(new { error = "Not found" });

        var existing = await db.Reviews
            .FirstOrDefaultAsync(r => r.BusinessId == business.Id && r.CustomerAccountId == AccountId);

        var canReview = await reviews.HasCompletedAppointment(AccountId, business.Id);

        return Ok(new ReviewEligibilityDto(canReview, existing is not null, existing is null ? null : ToDto(existing, business.Slug)));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateReviewRequest req)
    {
        if (req.Rating is < 1 or > 5) return BadRequest(new { error = "Rating must be between 1 and 5" });
        var comment = Normalize(req.Comment);
        if (comment is { Length: > MaxCommentLength }) return BadRequest(new { error = $"Comment must be {MaxCommentLength} characters or fewer" });

        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Slug == req.BusinessSlug);
        if (business is null) return NotFound(new { error = "Not found" });

        if (await db.Reviews.AnyAsync(r => r.BusinessId == business.Id && r.CustomerAccountId == AccountId))
            return Conflict(new { error = "You've already reviewed this business" });

        var completed = await reviews.MostRecentCompletedAppointment(AccountId, business.Id);
        if (completed is null)
            return StatusCode(403, new { error = "You can review a business only after a completed appointment there." });

        var review = new Review
        {
            BusinessId = business.Id,
            CustomerAccountId = AccountId,
            AppointmentId = completed.Id,
            Rating = req.Rating,
            Comment = comment,
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync();
        await reviews.RecomputeAggregate(business.Id);

        this.SetActivityDetail($"Reviewed {business.Name}: {req.Rating}★");
        return StatusCode(201, ToDto(review, business.Slug));
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateReviewRequest req)
    {
        if (req.Rating is < 1 or > 5) return BadRequest(new { error = "Rating must be between 1 and 5" });
        var comment = Normalize(req.Comment);
        if (comment is { Length: > MaxCommentLength }) return BadRequest(new { error = $"Comment must be {MaxCommentLength} characters or fewer" });

        var review = await db.Reviews
            .Include(r => r.Business)
            .FirstOrDefaultAsync(r => r.Id == id && r.CustomerAccountId == AccountId);
        if (review is null) return NotFound(new { error = "Not found" });

        review.Rating = req.Rating;
        review.Comment = comment;
        review.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await reviews.RecomputeAggregate(review.BusinessId);

        this.SetActivityDetail($"Updated review for {review.Business.Name}: {req.Rating}★");
        return Ok(ToDto(review, review.Business.Slug));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var review = await db.Reviews
            .Include(r => r.Business)
            .FirstOrDefaultAsync(r => r.Id == id && r.CustomerAccountId == AccountId);
        if (review is null) return NotFound(new { error = "Not found" });

        var businessId = review.BusinessId;
        var businessName = review.Business.Name;
        db.Reviews.Remove(review);
        await db.SaveChangesAsync();
        await reviews.RecomputeAggregate(businessId);

        this.SetActivityDetail($"Deleted review for {businessName}");
        return Ok(new { ok = true });
    }

    private static string? Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static ReviewDto ToDto(Review r, string slug) => new(
        r.Id, slug, r.Rating, r.Comment, r.CreatedAt, r.UpdatedAt, r.OwnerReply, r.OwnerRepliedAt);
}
