using BarberSaas.Api.Data;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, JwtService jwt) : ControllerBase
{
    private static readonly string[] ReservedSlugs =
        ["admin", "api", "login", "register", "cron", "whatsapp", "_next", "favicon"];

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

        await db.SaveChangesAsync();

        return StatusCode(201, new { barber.Id, barber.Name, barber.Email, barber.Slug });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var barber = await db.Barbers.FirstOrDefaultAsync(b => b.Email == req.Email);
        if (barber is null || !BCrypt.Net.BCrypt.Verify(req.Password, barber.PasswordHash))
            return Unauthorized(new { error = "Invalid email or password" });

        var token = jwt.Generate(barber.Id, barber.Email, barber.Name, barber.Slug);
        return Ok(new LoginResponse(token, barber.Id, barber.Name, barber.Email, barber.Slug));
    }
}
