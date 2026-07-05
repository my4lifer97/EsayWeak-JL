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
[Route("api/customer/appointments")]
[Authorize(Policy = "CustomerOnly")]
public class CustomerAppointmentsController(AppDbContext db, AvailabilityService availability) : ControllerBase
{
    private string AccountId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> GetMyAppointments([FromQuery] string? filter = null, [FromQuery] string? barberSlug = null)
    {
        var today = DateTime.UtcNow.Date;
        var query = db.Appointments
            .Include(a => a.Service)
            .Include(a => a.Barber)
            .Where(a => a.Customer.CustomerAccountId == AccountId);

        if (!string.IsNullOrEmpty(barberSlug))
            query = query.Where(a => a.Barber.Slug == barberSlug);

        query = filter switch
        {
            "today" => query.Where(a => a.Date == today),
            "upcoming" => query.Where(a => a.Date >= today),
            "past" => query.Where(a => a.Date < today),
            _ => query
        };

        var appointments = await query.OrderByDescending(a => a.Date).ThenBy(a => a.StartTime).ToListAsync();

        var dtos = appointments.Select(a => new CustomerAppointmentDto(
            a.Id, a.Barber.Slug, a.Barber.Name, a.Date.ToString("yyyy-MM-dd"), a.StartTime, a.EndTime,
            a.Notes, a.Status.ToString(), a.CancelToken,
            new ServiceSummary(a.Service.Id, a.Service.NameEn, a.Service.NameAr, a.Service.NameHe, a.Service.DurationMinutes, a.Service.Price)));

        return Ok(dtos.OrderBy(d => d.Status == "CONFIRMED" ? 0 : 1));
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(string id)
    {
        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.Customer.CustomerAccountId == AccountId);
        if (appt is null) return NotFound(new { error = "Not found" });

        appt.Status = AppointmentStatus.CANCELLED;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPatch("{id}/reschedule")]
    public async Task<IActionResult> Reschedule(string id, [FromBody] RescheduleRequest req)
    {
        var appt = await db.Appointments
            .Include(a => a.Service)
            .FirstOrDefaultAsync(a => a.Id == id && a.Customer.CustomerAccountId == AccountId);
        if (appt is null) return NotFound(new { error = "Not found" });

        var slots = await availability.GetAvailableSlots(appt.BarberId, req.Date, appt.Service.DurationMinutes);
        if (!slots.Any(s => s.Start == req.StartTime))
            return Conflict(new { error = "Slot not available" });

        appt.Date = DateTime.Parse(req.Date + "T00:00:00Z").ToUniversalTime();
        appt.StartTime = req.StartTime;
        appt.EndTime = AvailabilityService.AddMinutes(req.StartTime, appt.Service.DurationMinutes);
        appt.ReminderSent = false;
        await db.SaveChangesAsync();

        return Ok(new { appt.Id, Status = appt.Status.ToString() });
    }

    [HttpPatch("{id}/notes")]
    public async Task<IActionResult> UpdateNotes(string id, [FromBody] UpdateNotesRequest req)
    {
        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.Customer.CustomerAccountId == AccountId);
        if (appt is null) return NotFound(new { error = "Not found" });

        appt.Notes = req.Notes;
        await db.SaveChangesAsync();
        return Ok(new { appt.Id, appt.Notes });
    }
}

public record UpdateNotesRequest(string? Notes);
