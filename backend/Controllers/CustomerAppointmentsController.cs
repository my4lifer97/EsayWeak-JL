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
    public async Task<IActionResult> GetMyAppointments([FromQuery] string? filter = null, [FromQuery] string? businessSlug = null)
    {
        // a.Date is the business's local wall-clock calendar date, never converted to/from UTC
        // (see AvailabilityService) — filter by local "now" or this drifts a day near midnight.
        var today = AppClock.Now.Date;
        var query = db.Appointments
            .Include(a => a.Item).ThenInclude(s => s.GalleryPhotos)
            .Include(a => a.Business)
            .Where(a => a.Customer.CustomerAccountId == AccountId && !a.IsWaitlistCarrier);

        if (!string.IsNullOrEmpty(businessSlug))
            query = query.Where(a => a.Business.Slug == businessSlug);

        query = filter switch
        {
            "today" => query.Where(a => a.Date == today),
            "upcoming" => query.Where(a => a.Date >= today),
            "past" => query.Where(a => a.Date < today),
            _ => query
        };

        // Upcoming and today soonest first, like the owner's lists; past and all newest first.
        // Upcoming was farthest-first (QA run 3).
        var ordered = filter is "upcoming" or "today"
            ? query.OrderBy(a => a.Date).ThenBy(a => a.StartTime)
            : query.OrderByDescending(a => a.Date).ThenBy(a => a.StartTime);
        var appointments = await ordered.ToListAsync();

        var dtos = appointments.Select(a => new CustomerAppointmentDto(
            a.Id, a.Business.Slug, a.Business.Name, a.Date.ToString("yyyy-MM-dd"), a.StartTime, a.EndTime,
            a.Notes, AppointmentStatusHelper.CustomerFacingStatus(a.Status, a.PendingCancellationApproval, a.Date, a.EndTime), a.CancelToken,
            new ItemSummary(a.Item.Id, a.Item.NameEn, a.Item.NameAr, a.Item.NameHe, a.Item.DurationMinutes, a.Item.Price,
                a.Item.PhotoMode.ToString(), a.Item.GalleryPhotos.Select(p => new ItemGalleryPhotoDto(p.Id, p.Url)).ToList()),
            a.PhotoUrl));

        return Ok(dtos.OrderBy(d => d.Status == "CONFIRMED" ? 0 : 1));
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(string id)
    {
        var appt = await db.Appointments
            .Include(a => a.Item).Include(a => a.Business)
            .FirstOrDefaultAsync(a => a.Id == id && a.Customer.CustomerAccountId == AccountId);
        if (appt is null) return NotFound(new { error = "Not found" });
        if (appt.PendingCancellationApproval || AppointmentStatusHelper.EffectiveStatus(appt.Status, appt.Date, appt.EndTime) != "CONFIRMED")
            return Conflict(new { error = "This appointment can no longer be modified" });

        if (!await cancellationService.CancelFromCustomerAsync(appt))
            return Conflict(new { error = "This appointment can no longer be modified" });
        await db.SaveChangesAsync();

        // CancelFromCustomerAsync doesn't always finalize the cancellation -- if the business
        // requires approval and can be reached, it just freezes the slot (PendingCancellationApproval
        // = true, Status stays CONFIRMED) and texts them instead. Reflect whichever actually happened.
        var verb = appt.PendingCancellationApproval ? "Requested cancellation (awaiting owner approval)" : "Cancelled appointment";
        this.SetActivityDetail(
            $"{verb}: {appt.Item.NameEn} with {appt.Business.Name} on {appt.Date:yyyy-MM-dd} at {appt.StartTime}");

        return Ok(new { ok = true });
    }

    [HttpPatch("{id}/reschedule")]
    public async Task<IActionResult> Reschedule(string id, [FromBody] RescheduleRequest req)
    {
        var appt = await db.Appointments
            .Include(a => a.Item).Include(a => a.Business)
            .FirstOrDefaultAsync(a => a.Id == id && a.Customer.CustomerAccountId == AccountId);
        if (appt is null) return NotFound(new { error = "Not found" });
        if (appt.PendingCancellationApproval || AppointmentStatusHelper.EffectiveStatus(appt.Status, appt.Date, appt.EndTime) != "CONFIRMED")
            return Conflict(new { error = "This appointment can no longer be modified" });
        if (!appt.Item.IsBookable || appt.Item.DurationMinutes is null)
            return Conflict(new { error = "This appointment can no longer be modified" });

        var slots = await availability.GetAvailableSlots(appt.BusinessId, req.Date, appt.Item.DurationMinutes.Value, appt.Id);
        if (!slots.Any(s => s.Start == req.StartTime))
            return Conflict(new { error = "Slot not available" });

        // Same limits as a new booking (QA run 3: a reschedule got around "1 per day").
        var limitDate = DateTime.Parse(req.Date + "T00:00:00Z").ToUniversalTime();
        var customerPhone = await db.Customers.Where(c => c.Id == appt.CustomerId).Select(c => c.Phone).FirstAsync();

        var oldDate = appt.Date.ToString("yyyy-MM-dd");
        var oldStartTime = appt.StartTime;

        // Held from here until the move commits: parallel reschedules of one appointment (or one
        // racing a cancel) all returned 200 and the last writer won, with notices for times that
        // never stuck (QA run 3).
        await using var moveTx = await db.Database.BeginTransactionAsync();
        if (await availability.LockedBookingLimitError(appt.Business, customerPhone, limitDate, appt.Id) is { } limitError)
            return StatusCode(409, new { error = limitError });
        if (!await cancellationService.TryClaimAsync(appt, appt.Status, appt.PendingCancellationApproval))
            return Conflict(new { error = "This appointment can no longer be modified" });
        var freedSlot = await waitlist.DetachForReschedule(appt);

        appt.Date = DateTime.Parse(req.Date + "T00:00:00Z").ToUniversalTime();
        appt.StartTime = req.StartTime;
        appt.EndTime = AvailabilityService.AddMinutes(req.StartTime, appt.Item.DurationMinutes.Value);
        // Both reminders are for the new time, including the 3-hours-before one.
        appt.ReminderSent = false;
        appt.ReminderSentSoon = false;

        await waitlist.ResolveForRebooking(appt.BusinessId, appt.Date, appt.StartTime, appt.EndTime);
        if (!await availability.TrySaveOrDetectConflict(appt.BusinessId, req.Date, req.StartTime, appt.EndTime))
            return Conflict(new { error = "Slot not available" });
        await moveTx.CommitAsync();
        if (freedSlot is not null)
        {
            await waitlist.NotifyForCancellation(freedSlot);
            await db.SaveChangesAsync();
        }

        this.SetActivityDetail(
            $"Rescheduled appointment: {appt.Item.NameEn} with {appt.Business.Name} from {oldDate} {oldStartTime} to {req.Date} at {req.StartTime}");

        return Ok(new { appt.Id, Status = appt.Status.ToString() });
    }

    [HttpPatch("{id}/photo")]
    public async Task<IActionResult> UpdatePhoto(string id, [FromBody] UpdateAppointmentPhotoRequest req)
    {
        var appt = await db.Appointments
            .Include(a => a.Item).Include(a => a.Business)
            .FirstOrDefaultAsync(a => a.Id == id && a.Customer.CustomerAccountId == AccountId);
        if (appt is null) return NotFound(new { error = "Not found" });
        if (appt.PendingCancellationApproval || AppointmentStatusHelper.EffectiveStatus(appt.Status, appt.Date, appt.EndTime) != "CONFIRMED")
            return Conflict(new { error = "This appointment can no longer be modified" });

        if (appt.Item.PhotoMode == ItemPhotoMode.OwnerGallery)
        {
            if (string.IsNullOrWhiteSpace(req.GalleryPhotoId))
                return BadRequest(new { error = "Please choose a photo for this service." });
            var photo = await db.ItemGalleryPhotos.FirstOrDefaultAsync(p => p.Id == req.GalleryPhotoId && p.ItemId == appt.ItemId);
            if (photo is null) return BadRequest(new { error = "The selected photo is no longer available." });
            appt.PhotoUrl = photo.Url;
        }
        else if (appt.Item.PhotoMode == ItemPhotoMode.CustomerUpload)
        {
            if (string.IsNullOrWhiteSpace(req.CustomerPhotoUrl) || !ImageUpload.IsCustomerPhotoUrl(req.CustomerPhotoUrl))
                return BadRequest(new { error = "Please upload a photo for this service." });
            appt.PhotoUrl = req.CustomerPhotoUrl;
        }
        else if (appt.Item.PhotoMode == ItemPhotoMode.Both)
        {
            if (!string.IsNullOrWhiteSpace(req.GalleryPhotoId))
            {
                var photo = await db.ItemGalleryPhotos.FirstOrDefaultAsync(p => p.Id == req.GalleryPhotoId && p.ItemId == appt.ItemId);
                if (photo is null) return BadRequest(new { error = "The selected photo is no longer available." });
                appt.PhotoUrl = photo.Url;
            }
            else if (!string.IsNullOrWhiteSpace(req.CustomerPhotoUrl))
            {
                if (!ImageUpload.IsCustomerPhotoUrl(req.CustomerPhotoUrl))
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

        this.SetActivityDetail($"Changed reference photo for appointment: {appt.Item.NameEn} with {appt.Business.Name} on {appt.Date:yyyy-MM-dd}");

        return Ok(new { appt.Id, appt.PhotoUrl });
    }

    [HttpPatch("{id}/notes")]
    public async Task<IActionResult> UpdateNotes(string id, [FromBody] UpdateNotesRequest req)
    {
        var appt = await db.Appointments
            .Include(a => a.Item).Include(a => a.Business)
            .FirstOrDefaultAsync(a => a.Id == id && a.Customer.CustomerAccountId == AccountId);
        if (appt is null) return NotFound(new { error = "Not found" });
        // Same rule as reschedule: the card only offers notes on a live booking (QA run 2: the API
        // still took them on a cancelled or finished one).
        if (appt.PendingCancellationApproval || AppointmentStatusHelper.EffectiveStatus(appt.Status, appt.Date, appt.EndTime) != "CONFIRMED")
            return Conflict(new { error = "This appointment can no longer be modified" });

        appt.Notes = req.Notes;
        await db.SaveChangesAsync();

        // Content deliberately never logged -- notes are free text a customer writes, same
        // "metadata only" principle as everywhere else in ActivityLogFilter.
        this.SetActivityDetail($"Updated appointment notes: {appt.Item.NameEn} with {appt.Business.Name} on {appt.Date:yyyy-MM-dd}");

        return Ok(new { appt.Id, appt.Notes });
    }
}

public record UpdateNotesRequest(string? Notes);
