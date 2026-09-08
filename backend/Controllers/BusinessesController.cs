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
[Route("api/businesses")]
public class BusinessesController(AppDbContext db, FollowService followService) : ControllerBase
{
    private string? CustomerAccountId =>
        User.FindFirst("type")?.Value == "customer" ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? query)
    {
        var q = db.Businesses.AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query.Trim()}%";
            q = q.Where(b =>
                EF.Functions.ILike(b.Name, pattern) ||
                EF.Functions.ILike(b.Slug, pattern) ||
                (b.Description != null && EF.Functions.ILike(b.Description, pattern)));
        }

        var businesses = await q.OrderBy(b => b.Name).Take(30).ToListAsync();

        var followedSlugs = new HashSet<string>();
        var accountId = CustomerAccountId;
        if (accountId is not null)
        {
            followedSlugs = (await db.Follows
                .Where(f => f.CustomerAccountId == accountId)
                .Select(f => f.Business.Slug)
                .ToListAsync())
                .ToHashSet();
        }

        var results = businesses.Select(b => new BusinessSearchResultDto(
            b.Slug, b.Name, b.Description, b.Logo, b.Language.ToString(), followedSlugs.Contains(b.Slug)));

        return Ok(results);
    }

    [HttpGet("followed")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> GetFollowed()
    {
        var results = await db.Follows
            .Where(f => f.CustomerAccountId == CustomerAccountId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new BusinessSearchResultDto(
                f.Business.Slug, f.Business.Name, f.Business.Description, f.Business.Logo, f.Business.Language.ToString(), true))
            .ToListAsync();

        return Ok(results);
    }

    // Public: a business's non-hidden reviews, newest first, plus its rating aggregate.
    [HttpGet("{slug}/reviews")]
    public async Task<IActionResult> GetReviews(string slug, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Slug == slug);
        if (business is null) return NotFound(new { error = "Not found" });

        var q = db.Reviews.Where(r => r.BusinessId == business.Id && !r.IsHidden);
        var total = await q.CountAsync();

        var rows = await q
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new
            {
                r.Id, r.Rating, r.Comment, r.CreatedAt, r.OwnerReply, r.OwnerRepliedAt,
                r.CustomerAccount.Name, r.CustomerAccount.FamilyName,
            })
            .ToListAsync();

        var items = rows.Select(r => new PublicReviewDto(
            r.Id, r.Rating, r.Comment, ReviewerName(r.Name, r.FamilyName), r.CreatedAt, r.OwnerReply, r.OwnerRepliedAt)).ToList();

        var paged = new PagedResult<PublicReviewDto>(items, page, pageSize, total, page * pageSize < total);
        return Ok(new PublicReviewListDto(new BusinessRatingDto(business.RatingCount, business.RatingAverage), paged));
    }

    // "Sarah M." -- first name plus family initial, never the full family name.
    private static string ReviewerName(string name, string familyName)
    {
        var first = string.IsNullOrWhiteSpace(name) ? "Customer" : name.Trim();
        var initial = string.IsNullOrWhiteSpace(familyName) ? "" : $" {familyName.Trim()[0]}.";
        return first + initial;
    }

    [HttpPost("{slug}/follow")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> Follow(string slug)
    {
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Slug == slug);
        if (business is null) return NotFound(new { error = "Not found" });

        await followService.EnsureFollowed(CustomerAccountId!, business.Id);
        return Ok(new { ok = true });
    }

    [HttpDelete("{slug}/follow")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> Unfollow(string slug)
    {
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Slug == slug);
        if (business is null) return NotFound(new { error = "Not found" });

        var follow = await db.Follows.FirstOrDefaultAsync(f => f.CustomerAccountId == CustomerAccountId && f.BusinessId == business.Id);
        if (follow is not null)
        {
            db.Follows.Remove(follow);
            await db.SaveChangesAsync();
        }

        return Ok(new { ok = true });
    }
}
