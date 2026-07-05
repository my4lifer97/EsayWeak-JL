using BarberSaas.Api.Data;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "BarberOnly")]
public class AdminController(AppDbContext db, IWebHostEnvironment env) : ControllerBase
{
    private string BarberId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private static readonly Dictionary<string, string> AllowedLogoTypes = new()
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp",
    };
    private const long MaxLogoBytes = 5 * 1024 * 1024;

    // ─── Settings ───────────────────────────────────────────────────────────

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var b = await db.Barbers.FindAsync(BarberId);
        if (b is null) return NotFound();
        return Ok(new SettingsDto(
            b.Id, b.Name, b.Email, b.Slug, b.Phone,
            b.Description, b.Logo, b.Language.ToString(), b.TwilioNumber, b.TwilioSid,
            b.TrialEndsAt, b.SubscriptionStatus.ToString(),
            b.MaxBookingsPerDay, b.MaxBookingsPerWeek));
    }

    [HttpPost("settings/logo")]
    [RequestSizeLimit(MaxLogoBytes)]
    public async Task<IActionResult> UploadLogo(IFormFile file)
    {
        var b = await db.Barbers.FindAsync(BarberId);
        if (b is null) return NotFound();

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (file.Length == 0 || file.Length > MaxLogoBytes
            || !AllowedLogoTypes.TryGetValue(ext, out var expectedContentType)
            || file.ContentType != expectedContentType)
            return BadRequest(new { error = "Please upload a JPG, PNG, or WEBP image up to 5MB." });

        var uploadsDir = Path.Combine(env.ContentRootPath, "wwwroot", "uploads", "logos");
        Directory.CreateDirectory(uploadsDir);

        if (!string.IsNullOrEmpty(b.Logo))
        {
            var oldPath = Path.Combine(env.ContentRootPath, "wwwroot", b.Logo.Replace("/api/uploads/", "uploads/").Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
        }

        var fileName = $"{b.Id}{ext}";
        await using (var stream = new FileStream(Path.Combine(uploadsDir, fileName), FileMode.Create))
            await file.CopyToAsync(stream);

        b.Logo = $"/api/uploads/logos/{fileName}";
        await db.SaveChangesAsync();
        return Ok(new { logo = b.Logo });
    }

    [HttpPatch("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest req)
    {
        var b = await db.Barbers.FindAsync(BarberId);
        if (b is null) return NotFound();

        if (req.Name is not null) b.Name = req.Name;
        if (req.Phone is not null) b.Phone = req.Phone;
        if (req.Description is not null) b.Description = req.Description;
        if (req.Language is not null && Enum.TryParse<Language>(req.Language, out var lang)) b.Language = lang;
        if (req.TwilioNumber is not null) b.TwilioNumber = req.TwilioNumber;
        if (req.TwilioSid is not null) b.TwilioSid = req.TwilioSid;
        if (req.TwilioToken is not null) b.TwilioToken = req.TwilioToken;
        // Unlike the fields above, null here is a real value (unlimited), not "omitted" —
        // the settings form always submits both, so assign unconditionally.
        b.MaxBookingsPerDay = req.MaxBookingsPerDay;
        b.MaxBookingsPerWeek = req.MaxBookingsPerWeek;

        await db.SaveChangesAsync();
        return Ok(new { b.Id, b.Name, Language = b.Language.ToString() });
    }

    // ─── Services ───────────────────────────────────────────────────────────

    [HttpGet("services")]
    public async Task<IActionResult> GetServices()
    {
        var services = await db.Services
            .Where(s => s.BarberId == BarberId && s.IsActive)
            .OrderBy(s => s.NameEn)
            .Select(s => new ServiceDto(s.Id, s.BarberId, s.NameEn, s.NameAr, s.NameHe, s.DurationMinutes, s.Price, s.IsActive))
            .ToListAsync();
        return Ok(services);
    }

    [HttpPost("services")]
    public async Task<IActionResult> CreateService([FromBody] CreateServiceRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NameEn) || string.IsNullOrWhiteSpace(req.NameAr) || string.IsNullOrWhiteSpace(req.NameHe))
            return BadRequest(new { error = "All name fields are required" });
        if (req.DurationMinutes < 15 || req.DurationMinutes % 15 != 0)
            return BadRequest(new { error = "Duration must be a multiple of 15 (min 15)" });

        var service = new Service
        {
            BarberId = BarberId,
            NameEn = req.NameEn,
            NameAr = req.NameAr,
            NameHe = req.NameHe,
            DurationMinutes = req.DurationMinutes,
            Price = req.Price,
        };
        db.Services.Add(service);
        await db.SaveChangesAsync();
        return StatusCode(201, new ServiceDto(service.Id, service.BarberId, service.NameEn, service.NameAr, service.NameHe, service.DurationMinutes, service.Price, service.IsActive));
    }

    [HttpPatch("services/{id}")]
    public async Task<IActionResult> UpdateService(string id, [FromBody] CreateServiceRequest req)
    {
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == id && s.BarberId == BarberId);
        if (service is null) return NotFound();

        service.NameEn = req.NameEn;
        service.NameAr = req.NameAr;
        service.NameHe = req.NameHe;
        service.DurationMinutes = req.DurationMinutes;
        service.Price = req.Price;

        await db.SaveChangesAsync();
        return Ok(new ServiceDto(service.Id, service.BarberId, service.NameEn, service.NameAr, service.NameHe, service.DurationMinutes, service.Price, service.IsActive));
    }

    [HttpDelete("services/{id}")]
    public async Task<IActionResult> DeleteService(string id)
    {
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == id && s.BarberId == BarberId);
        if (service is null) return NotFound();
        service.IsActive = false;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ─── Schedule ───────────────────────────────────────────────────────────

    [HttpGet("schedule")]
    public async Task<IActionResult> GetSchedule()
    {
        var wh = await db.WorkingHours.Where(w => w.BarberId == BarberId)
            .Select(w => new WorkingHoursDto(w.Id, w.DayOfWeek, w.StartTime, w.EndTime, w.IsActive))
            .ToListAsync();
        var brk = await db.Breaks.Where(b => b.BarberId == BarberId)
            .Select(b => new BreakDto(b.Id, b.DayOfWeek, b.StartTime, b.EndTime))
            .ToListAsync();
        var bsl = await db.BlockedSlots.Where(b => b.BarberId == BarberId)
            .OrderBy(b => b.Date)
            .Select(b => new BlockedSlotDto(b.Id, b.Date.ToString("yyyy-MM-dd"), b.StartTime, b.EndTime, b.Reason))
            .ToListAsync();
        return Ok(new ScheduleResponse(wh, brk, bsl));
    }

    [HttpPost("schedule")]
    public async Task<IActionResult> SaveWorkingHours([FromBody] List<WorkingHoursDto> hours)
    {
        foreach (var h in hours)
        {
            var existing = await db.WorkingHours
                .FirstOrDefaultAsync(w => w.BarberId == BarberId && w.DayOfWeek == h.DayOfWeek);
            if (existing is not null)
            {
                existing.StartTime = h.StartTime;
                existing.EndTime = h.EndTime;
                existing.IsActive = h.IsActive;
            }
            else
            {
                db.WorkingHours.Add(new WorkingHours
                {
                    BarberId = BarberId,
                    DayOfWeek = h.DayOfWeek,
                    StartTime = h.StartTime,
                    EndTime = h.EndTime,
                    IsActive = h.IsActive,
                });
            }
        }
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("schedule/breaks")]
    public async Task<IActionResult> AddBreak([FromBody] CreateBreakRequest req)
    {
        var br = new Break { BarberId = BarberId, DayOfWeek = req.DayOfWeek, StartTime = req.StartTime, EndTime = req.EndTime };
        db.Breaks.Add(br);
        await db.SaveChangesAsync();
        return StatusCode(201, new BreakDto(br.Id, br.DayOfWeek, br.StartTime, br.EndTime));
    }

    [HttpDelete("schedule/breaks/{id}")]
    public async Task<IActionResult> DeleteBreak(string id)
    {
        var br = await db.Breaks.FirstOrDefaultAsync(b => b.Id == id && b.BarberId == BarberId);
        if (br is null) return NotFound();
        db.Breaks.Remove(br);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("schedule/blocked")]
    public async Task<IActionResult> AddBlockedSlot([FromBody] CreateBlockedSlotRequest req)
    {
        var slot = new BlockedSlot
        {
            BarberId = BarberId,
            Date = DateTime.Parse(req.Date + "T00:00:00Z").ToUniversalTime(),
            StartTime = req.StartTime,
            EndTime = req.EndTime,
            Reason = req.Reason,
        };
        db.BlockedSlots.Add(slot);
        await db.SaveChangesAsync();
        return StatusCode(201, new BlockedSlotDto(slot.Id, req.Date, slot.StartTime, slot.EndTime, slot.Reason));
    }

    [HttpDelete("schedule/blocked/{id}")]
    public async Task<IActionResult> DeleteBlockedSlot(string id)
    {
        var slot = await db.BlockedSlots.FirstOrDefaultAsync(b => b.Id == id && b.BarberId == BarberId);
        if (slot is null) return NotFound();
        db.BlockedSlots.Remove(slot);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ─── Dashboard ──────────────────────────────────────────────────────────

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard([FromQuery] int week = 0)
    {
        // a.Date is the barber's local wall-clock calendar date, never converted to/from UTC
        // (see AvailabilityService) — bucket weeks by local "now" or this drifts a day near midnight.
        var now = DateTime.Now;
        var weekStart = now.AddDays(week * 7 - (int)now.DayOfWeek);
        var weekEnd = weekStart.AddDays(6);

        var appointments = await db.Appointments
            .Include(a => a.Customer)
            .Include(a => a.Service)
            .Where(a => a.BarberId == BarberId && a.Date >= weekStart && a.Date <= weekEnd && a.Status != AppointmentStatus.CANCELLED)
            .OrderBy(a => a.Date).ThenBy(a => a.StartTime)
            .ToListAsync();

        return Ok(appointments.Select(a => new DashboardAppointmentDto(
            a.Id, a.Date.ToString("yyyy-MM-dd"), a.StartTime, a.EndTime,
            AppointmentStatusHelper.EffectiveStatus(a.Status, a.Date, a.EndTime), a.Notes,
            new CustomerSummary(a.Customer.Id, a.Customer.Name, a.Customer.FamilyName, a.Customer.Phone),
            new ServiceSummary(a.Service.Id, a.Service.NameEn, a.Service.NameAr, a.Service.NameHe, a.Service.DurationMinutes, a.Service.Price),
            a.Service.Price)));
    }

    // ─── Appointments ────────────────────────────────────────────────────────

    [HttpGet("appointments")]
    public async Task<IActionResult> GetAppointments([FromQuery] string? filter = null)
    {
        // Same local-vs-UTC reasoning as GetDashboard above.
        var today = DateTime.Now.Date;
        var query = db.Appointments
            .Include(a => a.Customer)
            .Include(a => a.Service)
            .Where(a => a.BarberId == BarberId);

        query = filter switch
        {
            "today" => query.Where(a => a.Date == today),
            "upcoming" => query.Where(a => a.Date >= today),
            "past" => query.Where(a => a.Date < today),
            _ => query
        };

        var appointments = await query.OrderByDescending(a => a.Date).ThenBy(a => a.StartTime).ToListAsync();

        return Ok(appointments.Select(a => new DashboardAppointmentDto(
            a.Id, a.Date.ToString("yyyy-MM-dd"), a.StartTime, a.EndTime,
            AppointmentStatusHelper.EffectiveStatus(a.Status, a.Date, a.EndTime), a.Notes,
            new CustomerSummary(a.Customer.Id, a.Customer.Name, a.Customer.FamilyName, a.Customer.Phone),
            new ServiceSummary(a.Service.Id, a.Service.NameEn, a.Service.NameAr, a.Service.NameHe, a.Service.DurationMinutes, a.Service.Price),
            a.Service.Price)));
    }

    [HttpPatch("appointments/{id}")]
    public async Task<IActionResult> UpdateAppointmentStatus(string id, [FromBody] UpdateStatusRequest req)
    {
        // The barber can only cancel now — "Completed" is computed automatically once an
        // appointment's end time passes (AppointmentStatusHelper), not manually set.
        if (req.Status != nameof(AppointmentStatus.CANCELLED))
            return BadRequest(new { error = "Only cancelling is supported" });

        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.BarberId == BarberId);
        if (appt is null) return NotFound();
        appt.Status = AppointmentStatus.CANCELLED;
        await db.SaveChangesAsync();
        return Ok(new { appt.Id, Status = appt.Status.ToString() });
    }
}

public record UpdateStatusRequest(string Status);
