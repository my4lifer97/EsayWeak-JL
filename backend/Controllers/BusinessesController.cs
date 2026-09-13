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

    // Public business directory. Base filter: listed businesses whose subscription hasn't expired.
    // Filters (query / businessTypeKey / city) and sort combine; results are paged.
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? query,
        [FromQuery] string? businessTypeKey,
        [FromQuery] string? city,
        [FromQuery] string? sort,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var q = db.Businesses.Where(b => b.IsListed && b.SubscriptionStatus != SubStatus.EXPIRED);

        // Case-insensitive substring / exact match via lower() rather than EF.Functions.ILike --
        // ILike is Npgsql-only and can't run under the SQLite provider the tests use. lower()
        // translates on both, treats the user's input literally (no % / _ wildcard injection),
        // and stays case-insensitive on Postgres.
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLower();
            q = q.Where(b =>
                b.Name.ToLower().Contains(term) ||
                b.Slug.ToLower().Contains(term) ||
                (b.Description != null && b.Description.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(businessTypeKey))
            q = q.Where(b => b.BusinessType != null && b.BusinessType.Key == businessTypeKey);

        if (!string.IsNullOrWhiteSpace(city))
        {
            var c = city.Trim().ToLower();
            q = q.Where(b => b.City != null && b.City.ToLower() == c);
        }

        q = sort switch
        {
            "popular" => q.OrderByDescending(b => b.Follows.Count).ThenBy(b => b.Name),
            "newest" => q.OrderByDescending(b => b.CreatedAt),
            "name" => q.OrderBy(b => b.Name),
            // default: highest-rated first, then most-reviewed, then alphabetical.
            _ => q.OrderByDescending(b => b.RatingAverage).ThenByDescending(b => b.RatingCount).ThenBy(b => b.Name),
        };

        var total = await q.CountAsync();

        var accountId = CustomerAccountId;
        var rows = await q
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(b => new
            {
                b.Slug, b.Name, b.Description, b.Logo, b.Language,
                BusinessTypeKey = b.BusinessType != null ? b.BusinessType.Key : null,
                b.City, b.RatingAverage, b.RatingCount,
                FollowerCount = b.Follows.Count,
                IsFollowed = accountId != null && b.Follows.Any(f => f.CustomerAccountId == accountId),
            })
            .ToListAsync();

        var items = rows.Select(b => new BusinessSearchResultDto(
            b.Slug, b.Name, b.Description, b.Logo, b.Language.ToString(), b.IsFollowed,
            b.BusinessTypeKey, b.City, b.RatingAverage, b.RatingCount, b.FollowerCount)).ToList();

        return Ok(new PagedResult<BusinessSearchResultDto>(items, page, pageSize, total, page * pageSize < total));
    }

    // Distinct cities among listed, non-expired businesses -- backs the /browse city filter.
    [HttpGet("cities")]
    public async Task<IActionResult> GetCities()
    {
        var cities = await db.Businesses
            .Where(b => b.IsListed && b.SubscriptionStatus != SubStatus.EXPIRED
                && b.City != null && b.City != "")
            .Select(b => b.City!.Trim())
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        return Ok(cities);
    }

    [HttpGet("followed")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> GetFollowed()
    {
        var results = await db.Follows
            .Where(f => f.CustomerAccountId == CustomerAccountId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new BusinessSearchResultDto(
                f.Business.Slug, f.Business.Name, f.Business.Description, f.Business.Logo,
                f.Business.Language.ToString(), true,
                f.Business.BusinessType != null ? f.Business.BusinessType.Key : null,
                f.Business.City, f.Business.RatingAverage, f.Business.RatingCount,
                f.Business.Follows.Count))
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
