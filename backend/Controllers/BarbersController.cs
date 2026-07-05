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
[Route("api/barbers")]
public class BarbersController(AppDbContext db, FollowService followService) : ControllerBase
{
    private string? CustomerAccountId =>
        User.FindFirst("type")?.Value == "customer" ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? query)
    {
        var q = db.Barbers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = $"%{query.Trim()}%";
            q = q.Where(b =>
                EF.Functions.ILike(b.Name, pattern) ||
                EF.Functions.ILike(b.Slug, pattern) ||
                (b.Description != null && EF.Functions.ILike(b.Description, pattern)));
        }

        var barbers = await q.OrderBy(b => b.Name).Take(30).ToListAsync();

        var followedSlugs = new HashSet<string>();
        var accountId = CustomerAccountId;
        if (accountId is not null)
        {
            followedSlugs = (await db.Follows
                .Where(f => f.CustomerAccountId == accountId)
                .Select(f => f.Barber.Slug)
                .ToListAsync())
                .ToHashSet();
        }

        var results = barbers.Select(b => new BarberSearchResultDto(
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
            .Select(f => new BarberSearchResultDto(
                f.Barber.Slug, f.Barber.Name, f.Barber.Description, f.Barber.Logo, f.Barber.Language.ToString(), true))
            .ToListAsync();

        return Ok(results);
    }

    [HttpPost("{slug}/follow")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> Follow(string slug)
    {
        var barber = await db.Barbers.FirstOrDefaultAsync(b => b.Slug == slug);
        if (barber is null) return NotFound(new { error = "Not found" });

        await followService.EnsureFollowed(CustomerAccountId!, barber.Id);
        return Ok(new { ok = true });
    }

    [HttpDelete("{slug}/follow")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> Unfollow(string slug)
    {
        var barber = await db.Barbers.FirstOrDefaultAsync(b => b.Slug == slug);
        if (barber is null) return NotFound(new { error = "Not found" });

        var follow = await db.Follows.FirstOrDefaultAsync(f => f.CustomerAccountId == CustomerAccountId && f.BarberId == barber.Id);
        if (follow is not null)
        {
            db.Follows.Remove(follow);
            await db.SaveChangesAsync();
        }

        return Ok(new { ok = true });
    }
}
