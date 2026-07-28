using System.Security.Claims;
using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/{slug}/waitlist")]
[Authorize(Policy = "CustomerOnly")]
public class WaitlistController(AppDbContext db) : ControllerBase
{
    private string CustomerAccountId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpPost("{appointmentId}")]
    public async Task<IActionResult> Join(string slug, string appointmentId)
    {
        var barber = await db.Barbers.FirstOrDefaultAsync(b => b.Slug == slug);
        if (barber is null) return NotFound(new { error = "Not found" });
        if (!barber.WaitlistEnabled) return BadRequest(new { error = "Waitlist is not enabled for this business" });

        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == appointmentId && a.BarberId == barber.Id);
        if (appointment is null) return NotFound(new { error = "Appointment not found" });
        if (AppointmentStatusHelper.EffectiveStatus(appointment.Status, appointment.Date, appointment.EndTime) != "CONFIRMED")
            return Conflict(new { error = "This appointment is not currently booked" });

        var exists = await db.WaitlistEntries.AnyAsync(w => w.AppointmentId == appointmentId && w.CustomerAccountId == CustomerAccountId);
        if (exists) return Ok(new { ok = true });

        db.WaitlistEntries.Add(new WaitlistEntry { AppointmentId = appointmentId, BarberId = barber.Id, CustomerAccountId = CustomerAccountId });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Same idempotency reasoning as FollowService.EnsureFollowed -- a race between the
            // exists-check above and this insert (double-submitted click) still ends at the
            // same desired state (a WaitlistEntry exists), so swallow rather than 500.
            db.ChangeTracker.Clear();
            if (!await db.WaitlistEntries.AnyAsync(w => w.AppointmentId == appointmentId && w.CustomerAccountId == CustomerAccountId))
                throw;
        }

        return Ok(new { ok = true });
    }
}
