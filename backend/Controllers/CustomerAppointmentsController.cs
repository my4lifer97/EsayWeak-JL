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
[Route("api/customer/appointments")]
[Authorize(Policy = "CustomerOnly")]
public class CustomerAppointmentsController(
    AppDbContext db, AvailabilityService availability, WaitlistService waitlist, AppointmentCancellationService cancellationService) : ControllerBase
{
    private string AccountId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> GetMyAppointments([FromQuery] string? filter = null, [FromQuery] string? barberSlug = null)
    {
        // a.Date is the barber's local wall-clock calendar date, never converted to/from UTC
        // (see AvailabilityService) — filter by local "now" or this drifts a day near midnight.
        var today = DateTime.Now.Date;
        var query = db.Appointments
            .Include(a => a.Service).ThenInclude(s => s.GalleryPhotos)
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
            a.Notes, AppointmentStatusHelper.CustomerFacingStatus(a.Status, a.PendingCancellationApproval, a.Date, a.EndTime), a.CancelToken,
            new ServiceSummary(a.Service.Id, a.Service.NameEn, a.Service.NameAr, a.Service.NameHe, a.Service.DurationMinutes, a.Service.Price,
                a.Service.PhotoMode.ToString(), a.Service.GalleryPhotos.Select(p => new ServiceGalleryPhotoDto(p.Id, p.Url)).ToList()),
            a.PhotoUrl));

        return Ok(dtos.OrderBy(d => d.Status == "CONFIRMED" ? 0 : 1));
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(string id)
    {
        var appt = await db.Appointments
            .Include(a => a.Service).Include(a => a.Barber)
            .FirstOrDefaultAsync(a => a.Id == id && a.Customer.CustomerAccountId == AccountId);
        if (appt is null) return NotFound(new { error = "Not found" });
        if (appt.PendingCancellationApproval || AppointmentStatusHelper.EffectiveStatus(appt.Status, appt.Date, appt.EndTime) != "CONFIRMED")
            return Conflict(new { error = "This appointment can no longer be modified" });

        await cancellationService.CancelFromCustomerAsync(appt);
        await db.SaveChangesAsync();

        // CancelFromCustomerAsync doesn't always finalize the cancellation -- if the barber
        // requires approval and can be reached, it just freezes the slot (PendingCancellationApproval
        // = true, Status stays CONFIRMED) and texts them instead. Reflect whichever actually happened.
        var verb = appt.PendingCancellationApproval ? "Requested cancellation (awaiting owner approval)" : "Cancelled appointment";
        this.SetActivityDetail(
            $"{verb}: {appt.Service.NameEn} with {appt.Barber.Name} on {appt.Date:yyyy-MM-dd} at {appt.StartTime}");

        return Ok(new { ok = true });
    }

    [HttpPatch("{id}/reschedule")]
    public async Task<IActionResult> Reschedule(string id, [FromBody] RescheduleRequest req)
    {
        var appt = await db.Appointments
            .Include(a => a.Service).Include(a => a.Barber)
            .FirstOrDefaultAsync(a => a.Id == id && a.Customer.CustomerAccountId == AccountId);
        if (appt is null) return NotFound(new { error = "Not found" });
        if (appt.PendingCancellationApproval || AppointmentStatusHelper.EffectiveStatus(appt.Status, appt.Date, appt.EndTime) != "CONFIRMED")
            return Conflict(new { error = "This appointment can no longer be modified" });

        var slots = await availability.GetAvailableSlots(appt.BarberId, req.Date, appt.Service.DurationMinutes);
        if (!slots.Any(s => s.Start == req.StartTime))
            return Conflict(new { error = "Slot not available" });

        var oldDate = appt.Date.ToString("yyyy-MM-dd");
        var oldStartTime = appt.StartTime;

        appt.Date = DateTime.Parse(req.Date + "T00:00:00Z").ToUniversalTime();
        appt.StartTime = req.StartTime;
        appt.EndTime = AvailabilityService.AddMinutes(req.StartTime, appt.Service.DurationMinutes);
        appt.ReminderSent = false;

        await waitlist.ResolveForRebooking(appt.BarberId, appt.Date, req.StartTime);
        if (!await availability.TrySaveOrDetectConflict(appt.BarberId, req.Date, req.StartTime, appt.EndTime))
            return Conflict(new { error = "Slot not available" });

        this.SetActivityDetail(
            $"Rescheduled appointment: {appt.Service.NameEn} with {appt.Barber.Name} from {oldDate} {oldStartTime} to {req.Date} at {req.StartTime}");

        return Ok(new { appt.Id, Status = appt.Status.ToString() });
    }

    [HttpPatch("{id}/photo")]
    public async Task<IActionResult> UpdatePhoto(string id, [FromBody] UpdateAppointmentPhotoRequest req)
    {
        var appt = await db.Appointments
            .Include(a => a.Service).Include(a => a.Barber)
            .FirstOrDefaultAsync(a => a.Id == id && a.Customer.CustomerAccountId == AccountId);
        if (appt is null) return NotFound(new { error = "Not found" });
        if (appt.PendingCancellationApproval || AppointmentStatusHelper.EffectiveStatus(appt.Status, appt.Date, appt.EndTime) != "CONFIRMED")
            return Conflict(new { error = "This appointment can no longer be modified" });

        if (appt.Service.PhotoMode == ServicePhotoMode.OwnerGallery)
        {
            if (string.IsNullOrWhiteSpace(req.GalleryPhotoId))
                return BadRequest(new { error = "Please choose a photo for this service." });
            var photo = await db.ServiceGalleryPhotos.FirstOrDefaultAsync(p => p.Id == req.GalleryPhotoId && p.ServiceId == appt.ServiceId);
            if (photo is null) return BadRequest(new { error = "The selected photo is no longer available." });
            appt.PhotoUrl = photo.Url;
        }
        else if (appt.Service.PhotoMode == ServicePhotoMode.CustomerUpload)
        {
            if (string.IsNullOrWhiteSpace(req.CustomerPhotoUrl) || !req.CustomerPhotoUrl.StartsWith("/api/uploads/appointment-photos/"))
                return BadRequest(new { error = "Please upload a photo for this service." });
            appt.PhotoUrl = req.CustomerPhotoUrl;
        }
        else if (appt.Service.PhotoMode == ServicePhotoMode.Both)
        {
            if (!string.IsNullOrWhiteSpace(req.GalleryPhotoId))
            {
                var photo = await db.ServiceGalleryPhotos.FirstOrDefaultAsync(p => p.Id == req.GalleryPhotoId && p.ServiceId == appt.ServiceId);
                if (photo is null) return BadRequest(new { error = "The selected photo is no longer available." });
                appt.PhotoUrl = photo.Url;
            }
            else if (!string.IsNullOrWhiteSpace(req.CustomerPhotoUrl))
            {
                if (!req.CustomerPhotoUrl.StartsWith("/api/uploads/appointment-photos/"))
                    return BadRequest(new { error = "Invalid photo reference." });
                appt.PhotoUrl = req.CustomerPhotoUrl;
            }
            else
            {
                return BadRequest(new { error = "Please choose or upload a photo for this service." });
            }
        }
        else
        {
            return BadRequest(new { error = "This service doesn't use a reference photo." });
        }

        await db.SaveChangesAsync();

        this.SetActivityDetail($"Changed reference photo for appointment: {appt.Service.NameEn} with {appt.Barber.Name} on {appt.Date:yyyy-MM-dd}");

        return Ok(new { appt.Id, appt.PhotoUrl });
    }

    [HttpPatch("{id}/notes")]
    public async Task<IActionResult> UpdateNotes(string id, [FromBody] UpdateNotesRequest req)
    {
        var appt = await db.Appointments
            .Include(a => a.Service).Include(a => a.Barber)
            .FirstOrDefaultAsync(a => a.Id == id && a.Customer.CustomerAccountId == AccountId);
        if (appt is null) return NotFound(new { error = "Not found" });

        appt.Notes = req.Notes;
        await db.SaveChangesAsync();

        // Content deliberately never logged -- notes are free text a customer writes, same
        // "metadata only" principle as everywhere else in ActivityLogFilter.
        this.SetActivityDetail($"Updated appointment notes: {appt.Service.NameEn} with {appt.Barber.Name} on {appt.Date:yyyy-MM-dd}");

        return Ok(new { appt.Id, appt.Notes });
    }
}

public record UpdateNotesRequest(string? Notes);
