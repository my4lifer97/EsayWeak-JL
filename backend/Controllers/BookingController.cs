using System.Security.Claims;
using BarberSaas.Api.Data;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/{slug}")]
public class BookingController(AppDbContext db, AvailabilityService availability, FollowService followService, IWebHostEnvironment env) : ControllerBase
{
    private static readonly Dictionary<string, string> AllowedPhotoTypes = new()
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp",
    };
    private const long MaxPhotoBytes = 5 * 1024 * 1024;

    [HttpGet("info")]
    public async Task<IActionResult> GetBarberInfo(string slug)
    {
        var barber = await db.Barbers
            .Include(b => b.Services.Where(s => s.IsActive)).ThenInclude(s => s.GalleryPhotos)
            .Include(b => b.WorkingHours.Where(w => w.IsActive))
            .FirstOrDefaultAsync(b => b.Slug == slug);

        if (barber is null) return NotFound(new { error = "Not found" });

        var isRTL = barber.Language is Language.AR or Language.HE;
        var activeDays = barber.WorkingHours.Select(w => w.DayOfWeek).ToArray();

        var services = barber.Services.Select(s => new ServiceDto(
            s.Id, s.BarberId, s.NameEn, s.NameAr, s.NameHe, s.DurationMinutes, s.Price, s.IsActive,
            s.PhotoMode.ToString(), s.GalleryPhotos.Select(p => new ServiceGalleryPhotoDto(p.Id, p.Url)).ToList())).ToList();

        var isFollowed = false;
        if (User.FindFirst("type")?.Value == "customer")
        {
            var accountId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            isFollowed = await db.Follows.AnyAsync(f => f.CustomerAccountId == accountId && f.BarberId == barber.Id);
        }

        return Ok(new PublicBarberDto(
            barber.Slug, barber.Name, barber.Description, barber.Logo,
            barber.Language.ToString(), isRTL, activeDays, services, isFollowed));
    }

    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability(string slug, [FromQuery] string date, [FromQuery] string serviceId)
    {
        var barber = await db.Barbers.FirstOrDefaultAsync(b => b.Slug == slug);
        if (barber is null) return NotFound(new { error = "Not found" });

        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == serviceId && s.BarberId == barber.Id && s.IsActive);
        if (service is null) return NotFound(new { error = "Service not found" });

        var slots = await availability.GetAvailableSlots(barber.Id, date, service.DurationMinutes);
        return Ok(new { slots });
    }

    [HttpPost("appointments")]
    public async Task<IActionResult> BookAppointment(string slug, [FromBody] BookAppointmentRequest req)
    {
        var barber = await db.Barbers.FirstOrDefaultAsync(b => b.Slug == slug);
        if (barber is null) return NotFound(new { error = "Not found" });

        if (barber.SubscriptionStatus == SubStatus.EXPIRED ||
            (barber.SubscriptionStatus == SubStatus.TRIAL && barber.TrialEndsAt < DateTime.UtcNow))
            return StatusCode(403, new { error = "Booking unavailable" });

        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == req.ServiceId && s.BarberId == barber.Id && s.IsActive);
        if (service is null) return NotFound(new { error = "Service not found" });

        string? photoUrl = null;
        if (service.PhotoMode == ServicePhotoMode.OwnerGallery)
        {
            if (string.IsNullOrWhiteSpace(req.GalleryPhotoId))
                return BadRequest(new { error = "Please choose a photo for this service." });
            var photo = await db.ServiceGalleryPhotos.FirstOrDefaultAsync(p => p.Id == req.GalleryPhotoId && p.ServiceId == service.Id);
            if (photo is null) return BadRequest(new { error = "The selected photo is no longer available." });
            photoUrl = photo.Url;
        }
        else if (service.PhotoMode == ServicePhotoMode.CustomerUpload)
        {
            if (string.IsNullOrWhiteSpace(req.CustomerPhotoUrl) || !req.CustomerPhotoUrl.StartsWith("/api/uploads/appointment-photos/"))
                return BadRequest(new { error = "Please upload a photo for this service." });
            photoUrl = req.CustomerPhotoUrl;
        }

        var slots = await availability.GetAvailableSlots(barber.Id, req.Date, service.DurationMinutes);
        if (!slots.Any(s => s.Start == req.StartTime))
            return Conflict(new { error = "Slot no longer available" });

        var endTime = AvailabilityService.AddMinutes(req.StartTime, service.DurationMinutes);
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

            // Booking a barber once is enough to follow them — no separate follow click required.
            // Done as its own save (before any Customer/Appointment tracking below) so FollowService
            // can safely clear the change tracker if it needs to recover from a concurrent duplicate.
            await followService.EnsureFollowed(customerAccountId!, barber.Id);
        }

        // Per-customer limits (matched by phone, not account, so a guest booking can't dodge
        // them by simply not logging in). Counts CONFIRMED bookings with this barber only.
        if (barber.MaxBookingsPerDay is not null || barber.MaxBookingsPerWeek is not null)
        {
            var existingDates = await db.Appointments
                .Where(a => a.BarberId == barber.Id && a.Customer.Phone == phone && a.Status == AppointmentStatus.CONFIRMED)
                .Select(a => a.Date)
                .ToListAsync();

            if (barber.MaxBookingsPerDay is not null &&
                existingDates.Count(d => d == requestedDate) >= barber.MaxBookingsPerDay)
                return StatusCode(409, new { error = "You've reached the maximum number of bookings allowed per day." });

            if (barber.MaxBookingsPerWeek is not null)
            {
                var weekStart = requestedDate.AddDays(-(int)requestedDate.DayOfWeek);
                var weekEnd = weekStart.AddDays(6);
                if (existingDates.Count(d => d >= weekStart && d <= weekEnd) >= barber.MaxBookingsPerWeek)
                    return StatusCode(409, new { error = "You've reached the maximum number of bookings allowed per week." });
            }
        }

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.BarberId == barber.Id && c.Phone == phone);
        if (customer is null)
        {
            customer = new Customer { Name = req.CustomerName, Phone = phone, BarberId = barber.Id, CustomerAccountId = customerAccountId };
            db.Customers.Add(customer);
        }
        else
        {
            customer.Name = req.CustomerName;
            if (customerAccountId is not null) customer.CustomerAccountId = customerAccountId;
        }

        var appointment = new Appointment
        {
            BarberId = barber.Id,
            CustomerId = customer.Id,
            ServiceId = service.Id,
            Date = requestedDate,
            StartTime = req.StartTime,
            EndTime = endTime,
            Notes = req.Notes,
            PhotoUrl = photoUrl,
            Status = AppointmentStatus.CONFIRMED,
        };
        db.Appointments.Add(appointment);

        await db.SaveChangesAsync();

        return StatusCode(201, new BookAppointmentResponse(appointment.Id, appointment.CancelToken));
    }

    [HttpPost("appointments/photo")]
    [RequestSizeLimit(MaxPhotoBytes)]
    public async Task<IActionResult> UploadAppointmentPhoto(string slug, IFormFile file)
    {
        var barber = await db.Barbers.FirstOrDefaultAsync(b => b.Slug == slug);
        if (barber is null) return NotFound(new { error = "Not found" });

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
            .Include(a => a.Service)
            .Include(a => a.Barber)
            .FirstOrDefaultAsync(a => a.Id == id && a.Barber.Slug == slug);

        if (appointment is null) return NotFound(new { error = "Not found" });

        return Ok(new AppointmentDetailDto(
            appointment.Id, appointment.BarberId, appointment.CustomerId, appointment.ServiceId,
            appointment.Date.ToString("yyyy-MM-dd"), appointment.StartTime, appointment.EndTime,
            appointment.Notes, AppointmentStatusHelper.EffectiveStatus(appointment.Status, appointment.Date, appointment.EndTime), appointment.ReminderSent, appointment.CancelToken,
            appointment.CreatedAt,
            new CustomerSummary(appointment.Customer.Id, appointment.Customer.Name, appointment.Customer.FamilyName, appointment.Customer.Phone),
            new ServiceSummary(appointment.Service.Id, appointment.Service.NameEn, appointment.Service.NameAr, appointment.Service.NameHe, appointment.Service.DurationMinutes, appointment.Service.Price),
            new BarberSummary(appointment.Barber.Name, appointment.Barber.Slug, appointment.Barber.Language.ToString()), appointment.PhotoUrl));
    }

    [HttpDelete("appointments/{id}")]
    public async Task<IActionResult> CancelAppointment(string slug, string id, [FromQuery] string token)
    {
        var appointment = await db.Appointments
            .Include(a => a.Barber)
            .FirstOrDefaultAsync(a => a.Id == id && a.Barber.Slug == slug);

        if (appointment is null) return NotFound(new { error = "Not found" });
        if (appointment.CancelToken != token) return StatusCode(403, new { error = "Invalid token" });
        if (AppointmentStatusHelper.EffectiveStatus(appointment.Status, appointment.Date, appointment.EndTime) != "CONFIRMED")
            return Conflict(new { error = "This appointment can no longer be modified" });

        appointment.Status = AppointmentStatus.CANCELLED;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPatch("appointments/{id}")]
    public async Task<IActionResult> RescheduleAppointment(string slug, string id, [FromQuery] string token, [FromBody] RescheduleRequest req)
    {
        var appointment = await db.Appointments
            .Include(a => a.Service)
            .Include(a => a.Barber)
            .FirstOrDefaultAsync(a => a.Id == id && a.Barber.Slug == slug);

        if (appointment is null) return NotFound(new { error = "Not found" });
        if (appointment.CancelToken != token) return StatusCode(403, new { error = "Invalid token" });
        if (AppointmentStatusHelper.EffectiveStatus(appointment.Status, appointment.Date, appointment.EndTime) != "CONFIRMED")
            return Conflict(new { error = "This appointment can no longer be modified" });

        var slots = await availability.GetAvailableSlots(appointment.BarberId, req.Date, appointment.Service.DurationMinutes);
        if (!slots.Any(s => s.Start == req.StartTime))
            return Conflict(new { error = "Slot not available" });

        appointment.Date = DateTime.Parse(req.Date + "T00:00:00Z").ToUniversalTime();
        appointment.StartTime = req.StartTime;
        appointment.EndTime = AvailabilityService.AddMinutes(req.StartTime, appointment.Service.DurationMinutes);
        appointment.ReminderSent = false;
        await db.SaveChangesAsync();

        return Ok(new { appointment.Id, Status = appointment.Status.ToString() });
    }
}

public record RescheduleRequest(string Date, string StartTime);
