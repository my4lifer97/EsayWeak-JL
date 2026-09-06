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
