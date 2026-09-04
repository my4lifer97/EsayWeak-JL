using BarberSaas.Api.Data;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Filters;
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
public class AdminController(
    AppDbContext db, IWebHostEnvironment env, AvailabilityService availability,
    WaitlistService waitlist, AppointmentCancellationService cancellationService) : ControllerBase
{
    private string BarberId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private static readonly Dictionary<string, string> AllowedLogoTypes = new()
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp",
    };
    private const long MaxLogoBytes = 5 * 1024 * 1024;

    private static ServiceDto ToServiceDto(Service s) => new(
        s.Id, s.BarberId, s.NameEn, s.NameAr, s.NameHe, s.DurationMinutes, s.Price, s.IsActive,
        s.PhotoMode.ToString(), s.GalleryPhotos.Select(p => new ServiceGalleryPhotoDto(p.Id, p.Url)).ToList());

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
            b.MaxBookingsPerDay, b.MaxBookingsPerWeek, b.WaitlistEnabled, b.RequireApprovalOnCustomerCancel,
            b.ChatbotEnabled, b.ChatbotWelcomeMessage, b.ChatbotConfirmationMessage));
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
        this.SetActivityDetail("Uploaded business logo");
        return Ok(new { logo = b.Logo });
    }

    [HttpPatch("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest req)
    {
        var b = await db.Barbers.FindAsync(BarberId);
        if (b is null) return NotFound();

        // Captured before assignment so we can report only the fields that actually changed --
        // the settings form always submits every field on every save, so "field is present in
        // the request" alone would make virtually every save claim to change everything.
        var oldName = b.Name;
        var oldPhone = b.Phone;
        var oldDescription = b.Description;
        var oldLanguage = b.Language;
        var oldTwilioNumber = b.TwilioNumber;
        var oldTwilioSid = b.TwilioSid;
        var oldMaxBookingsPerDay = b.MaxBookingsPerDay;
        var oldMaxBookingsPerWeek = b.MaxBookingsPerWeek;
        var oldWaitlistEnabled = b.WaitlistEnabled;
        var oldRequireApprovalOnCustomerCancel = b.RequireApprovalOnCustomerCancel;
        var oldChatbotEnabled = b.ChatbotEnabled;
        var oldChatbotWelcomeMessage = b.ChatbotWelcomeMessage;
        var oldChatbotConfirmationMessage = b.ChatbotConfirmationMessage;

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
        b.WaitlistEnabled = req.WaitlistEnabled;
        b.RequireApprovalOnCustomerCancel = req.RequireApprovalOnCustomerCancel;
        b.ChatbotEnabled = req.ChatbotEnabled;
        b.ChatbotWelcomeMessage = req.ChatbotWelcomeMessage;
        b.ChatbotConfirmationMessage = req.ChatbotConfirmationMessage;

        await db.SaveChangesAsync();

        var changes = new List<string>();
        if (b.Name != oldName) changes.Add($"name: \"{oldName}\" → \"{b.Name}\"");
        if (b.Phone != oldPhone) changes.Add($"phone: \"{oldPhone}\" → \"{b.Phone}\"");
        if (b.Description != oldDescription) changes.Add("description");
        if (b.Language != oldLanguage) changes.Add($"language: {oldLanguage} → {b.Language}");
        if (b.TwilioNumber != oldTwilioNumber) changes.Add($"WhatsApp number: \"{oldTwilioNumber}\" → \"{b.TwilioNumber}\"");
        if (b.TwilioSid != oldTwilioSid) changes.Add("Twilio SID");
        // Never log the token's actual value, before or after -- it's a live credential.
        if (req.TwilioToken is not null) changes.Add("Twilio auth token");
        if (b.MaxBookingsPerDay != oldMaxBookingsPerDay)
            changes.Add($"max bookings/day: {oldMaxBookingsPerDay?.ToString() ?? "unlimited"} → {b.MaxBookingsPerDay?.ToString() ?? "unlimited"}");
        if (b.MaxBookingsPerWeek != oldMaxBookingsPerWeek)
            changes.Add($"max bookings/week: {oldMaxBookingsPerWeek?.ToString() ?? "unlimited"} → {b.MaxBookingsPerWeek?.ToString() ?? "unlimited"}");
        if (b.WaitlistEnabled != oldWaitlistEnabled) changes.Add($"waitlist {(b.WaitlistEnabled ? "enabled" : "disabled")}");
        if (b.RequireApprovalOnCustomerCancel != oldRequireApprovalOnCustomerCancel)
            changes.Add($"cancellation approval {(b.RequireApprovalOnCustomerCancel ? "required" : "not required")}");
        if (b.ChatbotEnabled != oldChatbotEnabled) changes.Add($"chatbot {(b.ChatbotEnabled ? "enabled" : "disabled")}");
        if (b.ChatbotWelcomeMessage != oldChatbotWelcomeMessage) changes.Add("chatbot welcome message");
        if (b.ChatbotConfirmationMessage != oldChatbotConfirmationMessage) changes.Add("chatbot confirmation message");

        this.SetActivityDetail(changes.Count > 0 ? $"Updated settings: {string.Join(", ", changes)}" : "Updated settings (no fields changed)");

        return Ok(new { b.Id, b.Name, Language = b.Language.ToString() });
    }

    // ─── Services ───────────────────────────────────────────────────────────

    [HttpGet("services")]
    public async Task<IActionResult> GetServices()
    {
        var services = await db.Services
            .Include(s => s.GalleryPhotos)
            .Where(s => s.BarberId == BarberId && s.IsActive)
            .OrderBy(s => s.NameEn)
            .ToListAsync();
        return Ok(services.Select(ToServiceDto));
    }

    [HttpPost("services")]
    public async Task<IActionResult> CreateService([FromBody] CreateServiceRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NameEn) || string.IsNullOrWhiteSpace(req.NameAr) || string.IsNullOrWhiteSpace(req.NameHe))
            return BadRequest(new { error = "All name fields are required" });
        if (req.DurationMinutes < 15 || req.DurationMinutes % 15 != 0)
            return BadRequest(new { error = "Duration must be a multiple of 15 (min 15)" });
        if (!Enum.TryParse<ServicePhotoMode>(req.PhotoMode, out var photoMode))
            return BadRequest(new { error = "Invalid photo mode" });

        var service = new Service
        {
            BarberId = BarberId,
            NameEn = req.NameEn,
            NameAr = req.NameAr,
            NameHe = req.NameHe,
            DurationMinutes = req.DurationMinutes,
            Price = req.Price,
            PhotoMode = photoMode,
        };
        db.Services.Add(service);
        await db.SaveChangesAsync();
        this.SetActivityDetail($"Created service: {service.NameEn} ({service.DurationMinutes} min, ₪{service.Price:F2}, {DescribePhotoMode(service.PhotoMode)})");
        return StatusCode(201, ToServiceDto(service));
    }

    [HttpPatch("services/{id}")]
    public async Task<IActionResult> UpdateService(string id, [FromBody] CreateServiceRequest req)
    {
        if (!Enum.TryParse<ServicePhotoMode>(req.PhotoMode, out var photoMode))
            return BadRequest(new { error = "Invalid photo mode" });

        var service = await db.Services.Include(s => s.GalleryPhotos)
            .FirstOrDefaultAsync(s => s.Id == id && s.BarberId == BarberId);
        if (service is null) return NotFound();

        // Captured before assignment so we can report only the fields that actually changed,
        // same approach as UpdateSettings/SaveWorkingHours above.
        var oldNameEn = service.NameEn;
        var oldNameAr = service.NameAr;
        var oldNameHe = service.NameHe;
        var oldDuration = service.DurationMinutes;
        var oldPrice = service.Price;
        var oldPhotoMode = service.PhotoMode;

        service.NameEn = req.NameEn;
        service.NameAr = req.NameAr;
        service.NameHe = req.NameHe;
        service.DurationMinutes = req.DurationMinutes;
        service.Price = req.Price;
        service.PhotoMode = photoMode;

        await db.SaveChangesAsync();

        var changes = new List<string>();
        if (service.NameEn != oldNameEn) changes.Add($"name: \"{oldNameEn}\" → \"{service.NameEn}\"");
        if (service.NameAr != oldNameAr || service.NameHe != oldNameHe) changes.Add("translated names");
        if (service.DurationMinutes != oldDuration) changes.Add($"duration: {oldDuration} min → {service.DurationMinutes} min");
        if (service.Price != oldPrice) changes.Add($"price: ₪{oldPrice:F2} → ₪{service.Price:F2}");
        if (service.PhotoMode != oldPhotoMode) changes.Add($"photo mode: {DescribePhotoMode(oldPhotoMode)} → {DescribePhotoMode(service.PhotoMode)}");

        this.SetActivityDetail(changes.Count > 0
            ? $"Updated service: {string.Join(", ", changes)}"
            : $"Updated service: {service.NameEn} (no changes)");

        return Ok(ToServiceDto(service));
    }

    [HttpDelete("services/{id}")]
    public async Task<IActionResult> DeleteService(string id)
    {
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == id && s.BarberId == BarberId);
        if (service is null) return NotFound();
        service.IsActive = false;
        await db.SaveChangesAsync();
        this.SetActivityDetail($"Deleted service: {service.NameEn} ({service.DurationMinutes} min, ₪{service.Price:F2})");
        return Ok(new { ok = true });
    }

    private static string DescribePhotoMode(ServicePhotoMode mode) => mode switch
    {
        ServicePhotoMode.OwnerGallery => "owner gallery photos",
        ServicePhotoMode.CustomerUpload => "customer-uploaded photo",
        ServicePhotoMode.Both => "owner gallery or customer-uploaded photo",
        _ => "no reference photo",
    };

    // ─── Service gallery photos ─────────────────────────────────────────────

    [HttpPost("services/{id}/gallery")]
    [RequestSizeLimit(MaxLogoBytes)]
    public async Task<IActionResult> UploadGalleryPhoto(string id, IFormFile file)
    {
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == id && s.BarberId == BarberId);
        if (service is null) return NotFound();

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (file.Length == 0 || file.Length > MaxLogoBytes
            || !AllowedLogoTypes.TryGetValue(ext, out var expectedContentType)
            || file.ContentType != expectedContentType)
            return BadRequest(new { error = "Please upload a JPG, PNG, or WEBP image up to 5MB." });

        var uploadsDir = Path.Combine(env.ContentRootPath, "wwwroot", "uploads", "gallery", service.Id);
        Directory.CreateDirectory(uploadsDir);

        var fileName = $"{Guid.NewGuid():N}{ext}";
        await using (var stream = new FileStream(Path.Combine(uploadsDir, fileName), FileMode.Create))
            await file.CopyToAsync(stream);

        var photo = new ServiceGalleryPhoto
        {
            ServiceId = service.Id,
            Url = $"/api/uploads/gallery/{service.Id}/{fileName}",
        };
        db.ServiceGalleryPhotos.Add(photo);
        await db.SaveChangesAsync();

        this.SetActivityDetail($"Uploaded gallery photo for service: {service.NameEn}");

        return StatusCode(201, new ServiceGalleryPhotoDto(photo.Id, photo.Url));
    }

    [HttpDelete("services/{id}/gallery/{photoId}")]
    public async Task<IActionResult> DeleteGalleryPhoto(string id, string photoId)
    {
        var photo = await db.ServiceGalleryPhotos.Include(p => p.Service)
            .FirstOrDefaultAsync(p => p.Id == photoId && p.ServiceId == id && p.Service.BarberId == BarberId);
        if (photo is null) return NotFound();

        var path = Path.Combine(env.ContentRootPath, "wwwroot", photo.Url.Replace("/api/uploads/", "uploads/").Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);

        db.ServiceGalleryPhotos.Remove(photo);
        await db.SaveChangesAsync();

        this.SetActivityDetail($"Deleted gallery photo for service: {photo.Service.NameEn}");

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
        // Captured before assignment so we can report only the days that actually changed --
        // the schedule form always submits all 7 days on every save, mirroring UpdateSettings.
        var oldByDay = await db.WorkingHours.Where(w => w.BarberId == BarberId)
            .ToDictionaryAsync(w => w.DayOfWeek, w => (w.StartTime, w.EndTime, w.IsActive));

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

        var changes = new List<string>();
        foreach (var h in hours.OrderBy(h => h.DayOfWeek))
        {
            var dayName = ((DayOfWeek)h.DayOfWeek).ToString();
            if (!oldByDay.TryGetValue(h.DayOfWeek, out var old))
            {
                if (h.IsActive) changes.Add($"{dayName}: enabled ({h.StartTime}–{h.EndTime})");
                continue;
            }
            if (old.IsActive != h.IsActive)
                changes.Add(h.IsActive ? $"{dayName}: enabled ({h.StartTime}–{h.EndTime})" : $"{dayName}: disabled");
            else if (h.IsActive && (old.StartTime != h.StartTime || old.EndTime != h.EndTime))
                changes.Add($"{dayName}: {old.StartTime}–{old.EndTime} → {h.StartTime}–{h.EndTime}");
        }

        this.SetActivityDetail(changes.Count > 0 ? $"Updated working hours: {string.Join(", ", changes)}" : "Updated working hours (no changes)");

        return Ok(new { ok = true });
    }

    [HttpPost("schedule/breaks")]
    public async Task<IActionResult> AddBreak([FromBody] CreateBreakRequest req)
    {
        var br = new Break { BarberId = BarberId, DayOfWeek = req.DayOfWeek, StartTime = req.StartTime, EndTime = req.EndTime };
        db.Breaks.Add(br);
        await db.SaveChangesAsync();
        this.SetActivityDetail($"Added break: {(DayOfWeek)br.DayOfWeek}s {br.StartTime}–{br.EndTime}");
        return StatusCode(201, new BreakDto(br.Id, br.DayOfWeek, br.StartTime, br.EndTime));
    }

    [HttpDelete("schedule/breaks/{id}")]
    public async Task<IActionResult> DeleteBreak(string id)
    {
        var br = await db.Breaks.FirstOrDefaultAsync(b => b.Id == id && b.BarberId == BarberId);
        if (br is null) return NotFound();
        db.Breaks.Remove(br);
        await db.SaveChangesAsync();
        this.SetActivityDetail($"Deleted break: {(DayOfWeek)br.DayOfWeek}s {br.StartTime}–{br.EndTime}");
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

        var timeRange = slot.StartTime is not null && slot.EndTime is not null ? $" {slot.StartTime}–{slot.EndTime}" : " (full day)";
        var reasonSuffix = string.IsNullOrWhiteSpace(slot.Reason) ? "" : $" — {slot.Reason}";
        this.SetActivityDetail($"Blocked {req.Date}{timeRange}{reasonSuffix}");

        return StatusCode(201, new BlockedSlotDto(slot.Id, req.Date, slot.StartTime, slot.EndTime, slot.Reason));
    }

    [HttpDelete("schedule/blocked/{id}")]
    public async Task<IActionResult> DeleteBlockedSlot(string id)
    {
        var slot = await db.BlockedSlots.FirstOrDefaultAsync(b => b.Id == id && b.BarberId == BarberId);
        if (slot is null) return NotFound();
        db.BlockedSlots.Remove(slot);
        await db.SaveChangesAsync();

        var timeRange = slot.StartTime is not null && slot.EndTime is not null ? $" {slot.StartTime}–{slot.EndTime}" : " (full day)";
        this.SetActivityDetail($"Unblocked {slot.Date:yyyy-MM-dd}{timeRange}");

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
            a.Service.Price, a.PhotoUrl, a.RecurringSeriesId, a.PendingCancellationApproval)));
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
            a.Service.Price, a.PhotoUrl, a.RecurringSeriesId, a.PendingCancellationApproval)));
    }

    // ─── Manual appointment creation ───────────────────────────────────────

    [HttpGet("appointments/availability")]
    public async Task<IActionResult> GetAppointmentAvailability([FromQuery] string date, [FromQuery] string serviceId)
    {
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == serviceId && s.BarberId == BarberId && s.IsActive);
        if (service is null) return NotFound(new { error = "Service not found" });

        var slots = await availability.GetAvailableSlots(BarberId, date, service.DurationMinutes);
        return Ok(new { slots });
    }

    [HttpGet("customers/search")]
    public async Task<IActionResult> SearchCustomers([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2) return Ok(Array.Empty<CustomerSummary>());
        var q = query.Trim().ToLower();
        var customers = await db.Customers
            .Where(c => c.BarberId == BarberId &&
                // Match against the concatenated "First Last" rather than Name/FamilyName
                // separately -- a full-name search like "John Smith" spans both columns, and
                // this alone still matches a first-name-only or last-name-only query too.
                ((c.Name.ToLower() + " " + c.FamilyName.ToLower()).Contains(q) || c.Phone.ToLower().Contains(q)))
            .OrderBy(c => c.Name).Take(20)
            .Select(c => new CustomerSummary(c.Id, c.Name, c.FamilyName, c.Phone))
            .ToListAsync();
        return Ok(customers);
    }

    [HttpPost("appointments")]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAdminAppointmentRequest req)
    {
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == req.ServiceId && s.BarberId == BarberId && s.IsActive);
        if (service is null) return NotFound(new { error = "Service not found" });

        var (customer, customerError) = await ResolveCustomer(req.CustomerId, req.CustomerName, req.CustomerPhone, req.CustomerFamilyName);
        if (customerError is not null) return customerError;

        string? photoUrl = null;
        if ((service.PhotoMode == ServicePhotoMode.OwnerGallery || service.PhotoMode == ServicePhotoMode.Both) && !string.IsNullOrWhiteSpace(req.GalleryPhotoId))
        {
            var photo = await db.ServiceGalleryPhotos.FirstOrDefaultAsync(p => p.Id == req.GalleryPhotoId && p.ServiceId == service.Id);
            if (photo is null) return BadRequest(new { error = "The selected photo is no longer available." });
            photoUrl = photo.Url;
        }
        else if ((service.PhotoMode == ServicePhotoMode.CustomerUpload || service.PhotoMode == ServicePhotoMode.Both) && !string.IsNullOrWhiteSpace(req.CustomerPhotoUrl))
        {
            if (!req.CustomerPhotoUrl.StartsWith("/api/uploads/appointment-photos/"))
                return BadRequest(new { error = "Invalid photo reference." });
            photoUrl = req.CustomerPhotoUrl;
        }

        var endTime = AvailabilityService.AddMinutes(req.StartTime, service.DurationMinutes);

        if (!req.Force)
        {
            var slots = await availability.GetAvailableSlots(BarberId, req.Date, service.DurationMinutes);
            if (!slots.Any(s => s.Start == req.StartTime))
                return Conflict(new { error = "Slot not available" });
        }
        else if (await availability.HasConflictingAppointment(BarberId, req.Date, req.StartTime, endTime))
        {
            return Conflict(new { error = "This time overlaps an existing appointment" });
        }

        var requestedDate = DateTime.Parse(req.Date + "T00:00:00Z").ToUniversalTime();
        var appointment = new Appointment
        {
            BarberId = BarberId,
            CustomerId = customer!.Id,
            ServiceId = service.Id,
            Date = requestedDate,
            StartTime = req.StartTime,
            EndTime = endTime,
            Notes = req.Notes,
            PhotoUrl = photoUrl,
            Status = AppointmentStatus.CONFIRMED,
        };
        db.Appointments.Add(appointment);

        await waitlist.ResolveForRebooking(BarberId, requestedDate, req.StartTime);
        if (!await availability.TrySaveOrDetectConflict(BarberId, req.Date, req.StartTime, endTime))
            return Conflict(new { error = "Slot no longer available" });

        this.SetActivityDetail($"Booked appointment: {service.NameEn} for {ActivityDetailExtensions.FullName(customer.Name, customer.FamilyName)} on {req.Date} at {req.StartTime}");

        return StatusCode(201, new DashboardAppointmentDto(
            appointment.Id, req.Date, appointment.StartTime, appointment.EndTime,
            "CONFIRMED", appointment.Notes,
            new CustomerSummary(customer.Id, customer.Name, customer.FamilyName, customer.Phone),
            new ServiceSummary(service.Id, service.NameEn, service.NameAr, service.NameHe, service.DurationMinutes, service.Price),
            service.Price, appointment.PhotoUrl, appointment.RecurringSeriesId));
    }

    [HttpPatch("appointments/{id}")]
    public async Task<IActionResult> UpdateAppointmentStatus(string id, [FromBody] UpdateStatusRequest req)
    {
        // The barber can only cancel now — "Completed" is computed automatically once an
        // appointment's end time passes (AppointmentStatusHelper), not manually set.
        if (req.Status != nameof(AppointmentStatus.CANCELLED))
            return BadRequest(new { error = "Only cancelling is supported" });

        var appt = await db.Appointments
            .Include(a => a.Customer).Include(a => a.Service)
            .FirstOrDefaultAsync(a => a.Id == id && a.BarberId == BarberId);
        if (appt is null) return NotFound();

        // The owner can cancel regardless of effective status (e.g. correcting a past/completed
        // appointment after the fact) -- unlike the customer-facing cancel endpoints. Only ask
        // WaitlistService to notify if it's actually still effectively CONFIRMED, since offering
        // an already-passed slot to the waitlist would be a meaningless notification.
        var wasPendingApproval = appt.PendingCancellationApproval;
        var stillConfirmed = AppointmentStatusHelper.EffectiveStatus(appt.Status, appt.Date, appt.EndTime) == "CONFIRMED";
        var offeredToWaitlist = req.NotifyWaitlist && stillConfirmed;
        await cancellationService.CancelAsync(appt, notifyWaitlist: offeredToWaitlist);
        await db.SaveChangesAsync();

        var verb = wasPendingApproval ? "Resolved cancellation request" : "Cancelled appointment";
        var suffix = offeredToWaitlist ? " (offered to waitlist)" : "";
        this.SetActivityDetail(
            $"{verb}{suffix}: {appt.Service.NameEn} for {ActivityDetailExtensions.FullName(appt.Customer.Name, appt.Customer.FamilyName)} on {appt.Date:yyyy-MM-dd} at {appt.StartTime}");

        return Ok(new { appt.Id, Status = appt.Status.ToString() });
    }

    // ─── Waitlist ───────────────────────────────────────────────────────────

    [HttpGet("appointments/{id}/waitlist")]
    public async Task<IActionResult> GetWaitlist(string id)
    {
        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.BarberId == BarberId);
        if (appt is null) return NotFound();

        var entries = await db.WaitlistEntries
            .Include(w => w.CustomerAccount)
            .Where(w => w.AppointmentId == id && w.Status != WaitlistEntryStatus.RESOLVED)
            .OrderBy(w => w.CreatedAt)
            .Select(w => new WaitlistEntrySummaryDto(
                w.Id, w.CustomerAccountId, w.CustomerAccount.Name, w.CustomerAccount.FamilyName,
                w.CustomerAccount.Phone, w.Status.ToString(), w.CreatedAt))
            .ToListAsync();

        return Ok(entries);
    }

    // ─── Replace Customer (owner-cancel Option 3: swap who the slot belongs to, no cancel) ────

    [HttpPatch("appointments/{id}/customer")]
    public async Task<IActionResult> ReplaceCustomer(string id, [FromBody] ReplaceCustomerRequest req)
    {
        var appt = await db.Appointments.Include(a => a.Service)
            .FirstOrDefaultAsync(a => a.Id == id && a.BarberId == BarberId);
        if (appt is null) return NotFound();
        if (AppointmentStatusHelper.EffectiveStatus(appt.Status, appt.Date, appt.EndTime) != "CONFIRMED")
            return Conflict(new { error = "This appointment can no longer be modified" });

        Customer customer;
        if (!string.IsNullOrWhiteSpace(req.WaitlistEntryId))
        {
            var entry = await db.WaitlistEntries.Include(w => w.CustomerAccount)
                .FirstOrDefaultAsync(w => w.Id == req.WaitlistEntryId && w.AppointmentId == id && w.BarberId == BarberId);
            if (entry is null) return NotFound(new { error = "Waitlist entry not found" });

            customer = await ResolveCustomerFromAccount(entry.CustomerAccount);
            // They now hold the slot directly -- no longer "waiting" for it. Other entries for
            // this same appointment are left as-is (still relevant if it's cancelled later).
            entry.Status = WaitlistEntryStatus.RESOLVED;
        }
        else
        {
            var (resolved, error) = await ResolveCustomer(req.CustomerId, req.CustomerName, req.CustomerPhone, req.CustomerFamilyName);
            if (error is not null) return error;
            customer = resolved!;
        }

        var wasPendingApproval = appt.PendingCancellationApproval;
        appt.CustomerId = customer.Id;
        // Un-freezes it if the customer had already tried to cancel and the owner is choosing to
        // keep the slot filled (with someone else) instead of finalizing that cancellation.
        appt.PendingCancellationApproval = false;
        await db.SaveChangesAsync();

        var verb = wasPendingApproval ? "Resolved cancellation request by replacing customer on appointment"
            : "Replaced customer on appointment";
        this.SetActivityDetail(
            $"{verb}: {appt.Service.NameEn} on {appt.Date:yyyy-MM-dd} at {appt.StartTime} — now {ActivityDetailExtensions.FullName(customer.Name, customer.FamilyName)}");

        return Ok(new { appt.Id, appt.CustomerId });
    }

    // Shared by CreateAppointment and ReplaceCustomer: resolve an existing customer by id, or
    // upsert one by phone (same as public booking's upsert-by-phone logic).
    private async Task<(Customer? Customer, IActionResult? Error)> ResolveCustomer(
        string? customerId, string? customerName, string? customerPhone, string? customerFamilyName = null)
    {
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            var existing = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.BarberId == BarberId);
            return existing is null ? (null, NotFound(new { error = "Customer not found" })) : (existing, null);
        }

        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(customerPhone))
            return (null, BadRequest(new { error = "Customer name and phone are required" }));

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.BarberId == BarberId && c.Phone == customerPhone);
        if (customer is null)
        {
            customer = new Customer { Name = customerName, FamilyName = customerFamilyName ?? "", Phone = customerPhone, BarberId = BarberId };
            db.Customers.Add(customer);
        }
        else
        {
            customer.Name = customerName;
            customer.FamilyName = customerFamilyName ?? "";
        }
        return (customer, null);
    }

    // Replace-from-waitlist knows the customer's real account already (phone, name, family
    // name), unlike ResolveCustomer's typed-in path -- so upsert by phone the same way, but also
    // link CustomerAccountId and set FamilyName, which the typed-in path has no way to know.
    private async Task<Customer> ResolveCustomerFromAccount(CustomerAccount account)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.BarberId == BarberId && c.Phone == account.Phone);
        if (customer is null)
        {
            customer = new Customer
            {
                Name = account.Name, FamilyName = account.FamilyName, Phone = account.Phone,
                BarberId = BarberId, CustomerAccountId = account.Id,
            };
            db.Customers.Add(customer);
        }
        else
        {
            customer.Name = account.Name;
            customer.FamilyName = account.FamilyName;
            customer.CustomerAccountId = account.Id;
        }
        return customer;
    }
}

public record UpdateStatusRequest(string Status, bool NotifyWaitlist = false);
