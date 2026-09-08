using System.Security.Claims;
using BarberSaas.Api.Data;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Filters;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/{slug}")]
public class BookingController(
    AppDbContext db, AvailabilityService availability, FollowService followService, IWebHostEnvironment env,
    WaitlistService waitlist, AppointmentCancellationService cancellationService) : ControllerBase
{
    private static readonly Dictionary<string, string> AllowedPhotoTypes = new()
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp",
    };
    private const long MaxPhotoBytes = 5 * 1024 * 1024;

    [HttpGet("info")]
    public async Task<IActionResult> GetBusinessInfo(string slug)
    {
        var business = await db.Businesses
            .Include(b => b.Items.Where(s => s.IsActive)).ThenInclude(s => s.GalleryPhotos)
            .Include(b => b.WorkingHours.Where(w => w.IsActive))
            .FirstOrDefaultAsync(b => b.Slug == slug);

        if (business is null) return NotFound(new { error = "Not found" });

        var isRTL = business.Language is Language.AR or Language.HE;
        var activeDays = business.WorkingHours.Select(w => w.DayOfWeek).ToArray();

        var items = business.Items.Select(s => new ItemDto(
            s.Id, s.BusinessId, s.NameEn, s.NameAr, s.NameHe, s.DurationMinutes, s.Price, s.IsActive,
            s.PhotoMode.ToString(), s.IsBookable, s.GalleryPhotos.Select(p => new ItemGalleryPhotoDto(p.Id, p.Url)).ToList())).ToList();

        var isFollowed = false;
        if (User.FindFirst("type")?.Value == "customer")
        {
            var accountId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            isFollowed = await db.Follows.AnyAsync(f => f.CustomerAccountId == accountId && f.BusinessId == business.Id);
        }

        // Resolved by slug regardless of IsListed -- unlisting hides a business from the directory
        // only; a direct or shared storefront link keeps working.
        return Ok(new PublicBusinessDto(
            business.Slug, business.Name, business.Description, business.Logo,
            business.Language.ToString(), isRTL, activeDays, items, isFollowed, business.WaitlistEnabled,
            business.City, business.AddressLine, business.MapUrl,
            business.RatingCount, business.RatingAverage));
    }

    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability(string slug, [FromQuery] string date, [FromQuery] string itemId)
    {
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Slug == slug);
        if (business is null) return NotFound(new { error = "Not found" });

        var item = await db.Items.FirstOrDefaultAsync(s => s.Id == itemId && s.BusinessId == business.Id && s.IsActive);
        if (item is null) return NotFound(new { error = "Item not found" });
        if (!item.IsBookable || item.DurationMinutes is null) return BadRequest(new { error = "This item is not bookable" });

        var slots = await availability.GetAvailableSlots(business.Id, date, item.DurationMinutes.Value);
        return Ok(new { slots });
    }

    // Same as availability above, but includes booked slots (flagged, not hidden) so the
    // waitlist feature can let a customer see and join the waitlist for an already-booked slot
    // instead of it just disappearing from the schedule.
    [HttpGet("availability/full")]
    public async Task<IActionResult> GetFullAvailability(string slug, [FromQuery] string date, [FromQuery] string itemId)
    {
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Slug == slug);
        if (business is null) return NotFound(new { error = "Not found" });

        var item = await db.Items.FirstOrDefaultAsync(s => s.Id == itemId && s.BusinessId == business.Id && s.IsActive);
        if (item is null) return NotFound(new { error = "Item not found" });
        if (!item.IsBookable || item.DurationMinutes is null) return BadRequest(new { error = "This item is not bookable" });

        var slots = await availability.GetSlotsWithBookingInfo(business.Id, date, item.DurationMinutes.Value);
        return Ok(new { slots });
    }

    [HttpPost("appointments")]
    public async Task<IActionResult> BookAppointment(string slug, [FromBody] BookAppointmentRequest req)
    {
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Slug == slug);
        if (business is null) return NotFound(new { error = "Not found" });

        if (business.SubscriptionStatus == SubStatus.EXPIRED ||
            (business.SubscriptionStatus == SubStatus.TRIAL && business.TrialEndsAt < DateTime.UtcNow))
            return StatusCode(403, new { error = "Booking unavailable" });

        var item = await db.Items.FirstOrDefaultAsync(s => s.Id == req.ItemId && s.BusinessId == business.Id && s.IsActive);
        if (item is null) return NotFound(new { error = "Item not found" });
        if (!item.IsBookable || item.DurationMinutes is null) return BadRequest(new { error = "This item is not bookable" });

        string? photoUrl = null;
        if (item.PhotoMode == ItemPhotoMode.OwnerGallery)
        {
            if (string.IsNullOrWhiteSpace(req.GalleryPhotoId))
                return BadRequest(new { error = "Please choose a photo for this service." });
            var photo = await db.ItemGalleryPhotos.FirstOrDefaultAsync(p => p.Id == req.GalleryPhotoId && p.ItemId == item.Id);
            if (photo is null) return BadRequest(new { error = "The selected photo is no longer available." });
            photoUrl = photo.Url;
        }
        else if (item.PhotoMode == ItemPhotoMode.CustomerUpload)
        {
            if (string.IsNullOrWhiteSpace(req.CustomerPhotoUrl) || !req.CustomerPhotoUrl.StartsWith("/api/uploads/appointment-photos/"))
                return BadRequest(new { error = "Please upload a photo for this service." });
            photoUrl = req.CustomerPhotoUrl;
        }
        else if (item.PhotoMode == ItemPhotoMode.Both)
        {
            if (!string.IsNullOrWhiteSpace(req.GalleryPhotoId))
            {
                var photo = await db.ItemGalleryPhotos.FirstOrDefaultAsync(p => p.Id == req.GalleryPhotoId && p.ItemId == item.Id);
                if (photo is null) return BadRequest(new { error = "The selected photo is no longer available." });
                photoUrl = photo.Url;
            }
            else if (!string.IsNullOrWhiteSpace(req.CustomerPhotoUrl))
            {
                if (!req.CustomerPhotoUrl.StartsWith("/api/uploads/appointment-photos/"))
                    return BadRequest(new { error = "Invalid photo reference." });
                photoUrl = req.CustomerPhotoUrl;
            }
            else
            {
                return BadRequest(new { error = "Please choose or upload a photo for this service." });
            }
        }

        var slots = await availability.GetAvailableSlots(business.Id, req.Date, item.DurationMinutes.Value);
        if (!slots.Any(s => s.Start == req.StartTime))
            return Conflict(new { error = "Slot no longer available" });

        var endTime = AvailabilityService.AddMinutes(req.StartTime, item.DurationMinutes.Value);
        var requestedDate = DateTime.Parse(req.Date + "T00:00:00Z").ToUniversalTime();

        // If a logged-in customer token is attached, link the booking to their account and trust
        // their verified phone over whatever was typed in the form (never let a client override
        // another account's Customer row via a spoofed phone number).
        string? customerAccountId = null;
        var phone = req.CustomerPhone;
        if (User.FindFirst("type")?.Value == "customer")
        {
            customerAccountId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            phone = User.FindFirst("phone")?.Value ?? phone;

            // Booking a business once is enough to follow them — no separate follow click required.
            // Done as its own save (before any Customer/Appointment tracking below) so FollowService
            // can safely clear the change tracker if it needs to recover from a concurrent duplicate.
            await followService.EnsureFollowed(customerAccountId!, business.Id);
        }

        // Per-customer limits (matched by phone, not account, so a guest booking can't dodge
        // them by simply not logging in). Counts CONFIRMED bookings with this business only.
        if (business.MaxBookingsPerDay is not null || business.MaxBookingsPerWeek is not null)
        {
            var existingDates = await db.Appointments
                .Where(a => a.BusinessId == business.Id && a.Customer.Phone == phone && a.Status == AppointmentStatus.CONFIRMED)
                .Select(a => a.Date)
                .ToListAsync();

            if (business.MaxBookingsPerDay is not null &&
                existingDates.Count(d => d == requestedDate) >= business.MaxBookingsPerDay)
                return StatusCode(409, new { error = "You've reached the maximum number of bookings allowed per day." });

            if (business.MaxBookingsPerWeek is not null)
            {
                var weekStart = requestedDate.AddDays(-(int)requestedDate.DayOfWeek);
                var weekEnd = weekStart.AddDays(6);
                if (existingDates.Count(d => d >= weekStart && d <= weekEnd) >= business.MaxBookingsPerWeek)
                    return StatusCode(409, new { error = "You've reached the maximum number of bookings allowed per week." });
            }
        }

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.BusinessId == business.Id && c.Phone == phone);
        if (customer is null)
        {
            customer = new Customer
            {
                Name = req.CustomerName, FamilyName = req.CustomerFamilyName ?? "", Phone = phone,
                BusinessId = business.Id, CustomerAccountId = customerAccountId,
            };
            db.Customers.Add(customer);
        }
        else
        {
            customer.Name = req.CustomerName;
            customer.FamilyName = req.CustomerFamilyName ?? "";
            if (customerAccountId is not null) customer.CustomerAccountId = customerAccountId;
        }

        var appointment = new Appointment
        {
            BusinessId = business.Id,
            CustomerId = customer.Id,
            ItemId = item.Id,
            Date = requestedDate,
            StartTime = req.StartTime,
            EndTime = endTime,
            Notes = req.Notes,
            PhotoUrl = photoUrl,
            Status = AppointmentStatus.CONFIRMED,
        };
        db.Appointments.Add(appointment);

        await waitlist.ResolveForRebooking(business.Id, requestedDate, req.StartTime);
        if (!await availability.TrySaveOrDetectConflict(business.Id, req.Date, req.StartTime, endTime))
            return Conflict(new { error = "Slot no longer available" });

        this.SetActivityDetail($"Booked appointment: {item.NameEn} with {business.Name} on {req.Date} at {req.StartTime}");

        return StatusCode(201, new BookAppointmentResponse(appointment.Id, appointment.CancelToken));
    }

    [HttpPost("appointments/photo")]
    [RequestSizeLimit(MaxPhotoBytes)]
    public async Task<IActionResult> UploadAppointmentPhoto(string slug, IFormFile file)
    {
        var business = await db.Businesses.FirstOrDefaultAsync(b => b.Slug == slug);
        if (business is null) return NotFound(new { error = "Not found" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (file.Length == 0 || file.Length > MaxPhotoBytes
            || !AllowedPhotoTypes.TryGetValue(ext, out var expectedContentType)
            || file.ContentType != expectedContentType)
            return BadRequest(new { error = "Please upload a JPG, PNG, or WEBP image up to 5MB." });

        var uploadsDir = Path.Combine(env.ContentRootPath, "wwwroot", "uploads", "appointment-photos");
        Directory.CreateDirectory(uploadsDir);

        var fileName = $"{Guid.NewGuid():N}{ext}";
        await using (var stream = new FileStream(Path.Combine(uploadsDir, fileName), FileMode.Create))
            await file.CopyToAsync(stream);

        return Ok(new { url = $"/api/uploads/appointment-photos/{fileName}" });
    }

    [HttpGet("appointments/{id}")]
    public async Task<IActionResult> GetAppointment(string slug, string id)
    {
        var appointment = await db.Appointments
            .Include(a => a.Customer)
            .Include(a => a.Item)
            .Include(a => a.Business)
            .FirstOrDefaultAsync(a => a.Id == id && a.Business.Slug == slug);

        if (appointment is null) return NotFound(new { error = "Not found" });

        return Ok(new AppointmentDetailDto(
            appointment.Id, appointment.BusinessId, appointment.CustomerId, appointment.ItemId,
            appointment.Date.ToString("yyyy-MM-dd"), appointment.StartTime, appointment.EndTime,
            appointment.Notes, AppointmentStatusHelper.CustomerFacingStatus(appointment.Status, appointment.PendingCancellationApproval, appointment.Date, appointment.EndTime), appointment.ReminderSent, appointment.CancelToken,
            appointment.CreatedAt,
            new CustomerSummary(appointment.Customer.Id, appointment.Customer.Name, appointment.Customer.FamilyName, appointment.Customer.Phone),
            new ItemSummary(appointment.Item.Id, appointment.Item.NameEn, appointment.Item.NameAr, appointment.Item.NameHe, appointment.Item.DurationMinutes, appointment.Item.Price),
            new BusinessSummary(appointment.Business.Name, appointment.Business.Slug, appointment.Business.Language.ToString()), appointment.PhotoUrl,
            appointment.RecurringSeriesId));
    }

    [HttpDelete("appointments/{id}")]
    public async Task<IActionResult> CancelAppointment(string slug, string id, [FromQuery] string token)
    {
        var appointment = await db.Appointments
            .Include(a => a.Business).Include(a => a.Item)
            .FirstOrDefaultAsync(a => a.Id == id && a.Business.Slug == slug);

        if (appointment is null) return NotFound(new { error = "Not found" });
        if (appointment.CancelToken != token) return StatusCode(403, new { error = "Invalid token" });
        if (appointment.PendingCancellationApproval || AppointmentStatusHelper.EffectiveStatus(appointment.Status, appointment.Date, appointment.EndTime) != "CONFIRMED")
            return Conflict(new { error = "This appointment can no longer be modified" });

        await cancellationService.CancelFromCustomerAsync(appointment);
        await db.SaveChangesAsync();

        // See the equivalent note in CustomerAppointmentsController.Cancel -- this doesn't always
        // finalize the cancellation; it may just freeze the slot pending owner approval instead.
        var verb = appointment.PendingCancellationApproval ? "Requested cancellation (awaiting owner approval)" : "Cancelled appointment";
        this.SetActivityDetail(
            $"{verb}: {appointment.Item.NameEn} with {appointment.Business.Name} on {appointment.Date:yyyy-MM-dd} at {appointment.StartTime}");

        return Ok(new { ok = true });
    }

    [HttpPatch("appointments/{id}")]
    public async Task<IActionResult> RescheduleAppointment(string slug, string id, [FromQuery] string token, [FromBody] RescheduleRequest req)
    {
        var appointment = await db.Appointments
            .Include(a => a.Item)
            .Include(a => a.Business)
            .FirstOrDefaultAsync(a => a.Id == id && a.Business.Slug == slug);

        if (appointment is null) return NotFound(new { error = "Not found" });
        if (appointment.CancelToken != token) return StatusCode(403, new { error = "Invalid token" });
        if (appointment.PendingCancellationApproval || AppointmentStatusHelper.EffectiveStatus(appointment.Status, appointment.Date, appointment.EndTime) != "CONFIRMED")
            return Conflict(new { error = "This appointment can no longer be modified" });
        if (!appointment.Item.IsBookable || appointment.Item.DurationMinutes is null)
            return Conflict(new { error = "This appointment can no longer be modified" });

        var slots = await availability.GetAvailableSlots(appointment.BusinessId, req.Date, appointment.Item.DurationMinutes.Value);
        if (!slots.Any(s => s.Start == req.StartTime))
            return Conflict(new { error = "Slot not available" });

        var oldDate = appointment.Date.ToString("yyyy-MM-dd");
        var oldStartTime = appointment.StartTime;

        appointment.Date = DateTime.Parse(req.Date + "T00:00:00Z").ToUniversalTime();
        appointment.StartTime = req.StartTime;
        appointment.EndTime = AvailabilityService.AddMinutes(req.StartTime, appointment.Item.DurationMinutes.Value);
        appointment.ReminderSent = false;

        await waitlist.ResolveForRebooking(appointment.BusinessId, appointment.Date, req.StartTime);
        if (!await availability.TrySaveOrDetectConflict(appointment.BusinessId, req.Date, req.StartTime, appointment.EndTime))
            return Conflict(new { error = "Slot not available" });

        this.SetActivityDetail(
            $"Rescheduled appointment: {appointment.Item.NameEn} with {appointment.Business.Name} from {oldDate} {oldStartTime} to {req.Date} at {req.StartTime}");

        return Ok(new { appointment.Id, Status = appointment.Status.ToString() });
    }
}

public record RescheduleRequest(string Date, string StartTime);
