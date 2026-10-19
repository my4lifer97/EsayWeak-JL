using BarberSaas.Api.Data;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Filters;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace BarberSaas.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "BusinessOnly")]
public class AdminController(
    AppDbContext db, IWebHostEnvironment env, AvailabilityService availability,
    WaitlistService waitlist, AppointmentCancellationService cancellationService,
    CustomerNotifier customerNotifier, SchedulePresetService schedulePresets) : ControllerBase
{
    private string BusinessId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;


    private static ItemDto ToItemDto(Item s) => new(
        s.Id, s.BusinessId, s.NameEn, s.NameAr, s.NameHe, s.DurationMinutes, s.Price, s.IsActive,
        s.PhotoMode.ToString(), s.IsBookable, s.GalleryPhotos.Select(p => new ItemGalleryPhotoDto(p.Id, p.Url)).ToList());

    // ─── Settings ───────────────────────────────────────────────────────────

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var b = await db.Businesses.FindAsync(BusinessId);
        if (b is null) return NotFound();
        return Ok(new SettingsDto(
            b.Id, b.Name, b.Email, b.Slug, b.Phone,
            b.Description, b.Logo, b.Language.ToString(), b.WhatsAppNumber,
            b.TrialEndsAt, b.SubscriptionStatus.ToString(),
            b.MaxBookingsPerDay, b.MaxBookingsPerWeek, b.WaitlistEnabled, b.RequireApprovalOnCustomerCancel,
            b.ChatbotEnabled,
            b.ChatbotWelcomeMessageEn, b.ChatbotWelcomeMessageAr, b.ChatbotWelcomeMessageHe,
            b.ChatbotConfirmationMessageEn, b.ChatbotConfirmationMessageAr, b.ChatbotConfirmationMessageHe,
            b.ChatbotFinalMessageEn, b.ChatbotFinalMessageAr, b.ChatbotFinalMessageHe,
            b.ChatbotDefaultLanguage?.ToString(),
            b.ChatbotInquiryEnabled, b.InquiryNotifyViaWhatsApp, b.InquiryWhatsAppNumber,
            b.InquiryNotifyViaEmail, b.InquiryEmail,
            b.City, b.AddressLine, b.MapUrl, b.IsListed,
            b.CancelApprovalNotifyViaWhatsApp, b.CancelApprovalWhatsAppNumber,
            b.CancelApprovalNotifyViaEmail, b.CancelApprovalEmail, b.Email));
    }

    [HttpPost("settings/logo")]
    [RequestSizeLimit(ImageUpload.MaxBytes)]
    public async Task<IActionResult> UploadLogo(IFormFile file)
    {
        var b = await db.Businesses.FindAsync(BusinessId);
        if (b is null) return NotFound();

        if (await ImageUpload.ValidateAsync(file) is not { } ext)
            return BadRequest(new { error = ImageUpload.Error });

        var uploadsDir = Path.Combine(env.ContentRootPath, "wwwroot", "uploads", "logos");
        Directory.CreateDirectory(uploadsDir);

        if (!string.IsNullOrEmpty(b.Logo))
        {
            var oldPath = Path.Combine(env.ContentRootPath, "wwwroot", b.Logo.Replace("/api/uploads/", "uploads/").Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
        }

        // A new name per upload: with a fixed {id}{ext} name, browsers and the WhatsApp preview kept
        // showing the cached old logo at the same URL.
        var fileName = $"{b.Id}-{Guid.NewGuid():N}{ext}";
        await using (var stream = new FileStream(Path.Combine(uploadsDir, fileName), FileMode.Create))
            await file.CopyToAsync(stream);

        b.Logo = $"/api/uploads/logos/{fileName}";
        await db.SaveChangesAsync();
        this.SetActivityDetail("Uploaded business logo");
        return Ok(new { logo = b.Logo });
    }

    public const int MaxChatbotMessageLength = 1000;

    // Everything here used to be accepted or silently dropped: negative booking limits, an unknown
    // language (left the old one), notify contacts that could never be reached.
    static string? SettingsInputError(UpdateSettingsRequest req, Func<string, bool> has)
    {
        if (has("maxBookingsPerDay") && req.MaxBookingsPerDay is < 1 or > 1000)
            return "Max bookings per day must be between 1 and 1000, or empty for unlimited";
        if (has("maxBookingsPerWeek") && req.MaxBookingsPerWeek is < 1 or > 1000)
            return "Max bookings per week must be between 1 and 1000, or empty for unlimited";
        if (req.Language is not null && !IsLanguage(req.Language))
            return "Language must be EN, AR or HE";
        if (has("chatbotDefaultLanguage") && !string.IsNullOrWhiteSpace(req.ChatbotDefaultLanguage) && !IsLanguage(req.ChatbotDefaultLanguage))
            return "Chatbot default language must be EN, AR or HE";
        string?[] messages = [
            req.ChatbotWelcomeMessageEn, req.ChatbotWelcomeMessageAr, req.ChatbotWelcomeMessageHe,
            req.ChatbotConfirmationMessageEn, req.ChatbotConfirmationMessageAr, req.ChatbotConfirmationMessageHe,
            req.ChatbotFinalMessageEn, req.ChatbotFinalMessageAr, req.ChatbotFinalMessageHe];
        if (messages.Any(m => m is not null && m.Trim().Length > MaxChatbotMessageLength))
            return $"Chatbot messages can be at most {MaxChatbotMessageLength} characters";
        if (has("inquiryWhatsAppNumber") && !string.IsNullOrWhiteSpace(req.InquiryWhatsAppNumber) && !PhoneNormalizer.IsValid(req.InquiryWhatsAppNumber))
            return "The inquiry WhatsApp number isn't a valid phone number";
        if (has("cancelApprovalWhatsAppNumber") && !string.IsNullOrWhiteSpace(req.CancelApprovalWhatsAppNumber) && !PhoneNormalizer.IsValid(req.CancelApprovalWhatsAppNumber))
            return "The cancellation-approval WhatsApp number isn't a valid phone number";
        if (has("inquiryEmail") && !string.IsNullOrWhiteSpace(req.InquiryEmail) && !EmailAddress.IsValid(req.InquiryEmail))
            return "The inquiry email address isn't valid";
        if (has("cancelApprovalEmail") && !string.IsNullOrWhiteSpace(req.CancelApprovalEmail) && !EmailAddress.IsValid(req.CancelApprovalEmail))
            return "The cancellation-approval email address isn't valid";
        return null;
    }

    // Enum.TryParse alone also takes "5" or "en".
    static bool IsLanguage(string value) => Enum.GetNames<Language>().Contains(value);
    // Same for photo mode: "99" parsed and was stored as an undefined value.
    static bool IsPhotoMode(string? value) => value is not null && Enum.GetNames<ItemPhotoMode>().Contains(value);

    [HttpPatch("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] JsonElement body)
    {
        var b = await db.Businesses.FindAsync(BusinessId);
        if (b is null) return NotFound();

        // A null/array body or a wrong value type ("abc" for a number) used to be a 500 that
        // echoed internal type names.
        if (body.ValueKind != JsonValueKind.Object) return BadRequest(new { error = "Invalid request" });
        UpdateSettingsRequest req;
        try
        {
            req = JsonSerializer.Deserialize<UpdateSettingsRequest>(body.GetRawText(),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        }
        catch (JsonException) { return BadRequest(new { error = "Invalid request" }); }
        // A key genuinely absent from the request body must leave that field untouched, not reset
        // it to the DTO's default -- the settings form always sends every key today, but a caller
        // sending a true partial update (e.g. just { "waitlistEnabled": true }) previously wiped
        // every other field to its default (city/address/chatbot messages to null, IsListed to
        // false, etc). Checking presence on the raw JSON, not just "is req.X null", is what
        // actually distinguishes "omitted" from "explicitly cleared" for the nullable string
        // fields below, which this form does send as explicit null when the owner clears them.
        bool Has(string prop) => body.TryGetProperty(prop, out _);

        var mapUrl = string.IsNullOrWhiteSpace(req.MapUrl) ? null : req.MapUrl.Trim();
        if (mapUrl is not null
            && !mapUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !mapUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Map link must start with http:// or https://" });
        if (SettingsInputError(req, Has) is { } settingsError) return BadRequest(new { error = settingsError });

        // Captured before assignment so we can report only the fields that actually changed --
        // the settings form always submits every field on every save, so "field is present in
        // the request" alone would make virtually every save claim to change everything.
        var oldName = b.Name;
        var oldPhone = b.Phone;
        var oldDescription = b.Description;
        var oldLanguage = b.Language;
        var oldMaxBookingsPerDay = b.MaxBookingsPerDay;
        var oldMaxBookingsPerWeek = b.MaxBookingsPerWeek;
        var oldWaitlistEnabled = b.WaitlistEnabled;
        var oldRequireApprovalOnCustomerCancel = b.RequireApprovalOnCustomerCancel;
        var oldChatbotEnabled = b.ChatbotEnabled;
        var oldChatbotWelcomeMessageEn = b.ChatbotWelcomeMessageEn;
        var oldChatbotWelcomeMessageAr = b.ChatbotWelcomeMessageAr;
        var oldChatbotWelcomeMessageHe = b.ChatbotWelcomeMessageHe;
        var oldChatbotConfirmationMessageEn = b.ChatbotConfirmationMessageEn;
        var oldChatbotConfirmationMessageAr = b.ChatbotConfirmationMessageAr;
        var oldChatbotConfirmationMessageHe = b.ChatbotConfirmationMessageHe;
        var oldChatbotFinalMessageEn = b.ChatbotFinalMessageEn;
        var oldChatbotFinalMessageAr = b.ChatbotFinalMessageAr;
        var oldChatbotFinalMessageHe = b.ChatbotFinalMessageHe;
        var oldChatbotDefaultLanguage = b.ChatbotDefaultLanguage;
        var oldChatbotInquiryEnabled = b.ChatbotInquiryEnabled;
        var oldInquiryNotifyViaWhatsApp = b.InquiryNotifyViaWhatsApp;
        var oldInquiryWhatsAppNumber = b.InquiryWhatsAppNumber;
        var oldInquiryNotifyViaEmail = b.InquiryNotifyViaEmail;
        var oldInquiryEmail = b.InquiryEmail;
        var oldCity = b.City;
        var oldAddressLine = b.AddressLine;
        var oldMapUrl = b.MapUrl;
        var oldIsListed = b.IsListed;
        var oldCancelApprovalNotifyViaWhatsApp = b.CancelApprovalNotifyViaWhatsApp;
        var oldCancelApprovalWhatsAppNumber = b.CancelApprovalWhatsAppNumber;
        var oldCancelApprovalNotifyViaEmail = b.CancelApprovalNotifyViaEmail;
        var oldCancelApprovalEmail = b.CancelApprovalEmail;

        if (req.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "Business name is required" });
            b.Name = req.Name.Trim();
        }
        if (req.Phone is not null) b.Phone = req.Phone;
        if (req.Description is not null) b.Description = req.Description;
        if (req.Language is not null) b.Language = Enum.Parse<Language>(req.Language);
        // null is a real value for these two (unlimited), not "omitted" -- gated on key presence,
        // not "is req.X null", so a caller can still explicitly send null to clear a limit.
        if (Has("maxBookingsPerDay")) b.MaxBookingsPerDay = req.MaxBookingsPerDay;
        if (Has("maxBookingsPerWeek")) b.MaxBookingsPerWeek = req.MaxBookingsPerWeek;
        if (Has("waitlistEnabled")) b.WaitlistEnabled = req.WaitlistEnabled;
        if (Has("requireApprovalOnCustomerCancel")) b.RequireApprovalOnCustomerCancel = req.RequireApprovalOnCustomerCancel;
        if (Has("chatbotEnabled")) b.ChatbotEnabled = req.ChatbotEnabled;
        // Whitespace-only counts as "no custom message", so the built-in default is used.
        static string? Message(string? m) => string.IsNullOrWhiteSpace(m) ? null : m.Trim();
        if (Has("chatbotWelcomeMessageEn")) b.ChatbotWelcomeMessageEn = Message(req.ChatbotWelcomeMessageEn);
        if (Has("chatbotWelcomeMessageAr")) b.ChatbotWelcomeMessageAr = Message(req.ChatbotWelcomeMessageAr);
        if (Has("chatbotWelcomeMessageHe")) b.ChatbotWelcomeMessageHe = Message(req.ChatbotWelcomeMessageHe);
        if (Has("chatbotConfirmationMessageEn")) b.ChatbotConfirmationMessageEn = Message(req.ChatbotConfirmationMessageEn);
        if (Has("chatbotConfirmationMessageAr")) b.ChatbotConfirmationMessageAr = Message(req.ChatbotConfirmationMessageAr);
        if (Has("chatbotConfirmationMessageHe")) b.ChatbotConfirmationMessageHe = Message(req.ChatbotConfirmationMessageHe);
        if (Has("chatbotFinalMessageEn")) b.ChatbotFinalMessageEn = Message(req.ChatbotFinalMessageEn);
        if (Has("chatbotFinalMessageAr")) b.ChatbotFinalMessageAr = Message(req.ChatbotFinalMessageAr);
        if (Has("chatbotFinalMessageHe")) b.ChatbotFinalMessageHe = Message(req.ChatbotFinalMessageHe);
        if (Has("chatbotDefaultLanguage"))
        {
            b.ChatbotDefaultLanguage = string.IsNullOrWhiteSpace(req.ChatbotDefaultLanguage)
                ? null : Enum.Parse<Language>(req.ChatbotDefaultLanguage);
        }
        if (Has("chatbotInquiryEnabled")) b.ChatbotInquiryEnabled = req.ChatbotInquiryEnabled;
        if (Has("inquiryNotifyViaWhatsApp")) b.InquiryNotifyViaWhatsApp = req.InquiryNotifyViaWhatsApp;
        if (Has("inquiryWhatsAppNumber")) b.InquiryWhatsAppNumber = string.IsNullOrWhiteSpace(req.InquiryWhatsAppNumber) ? null : PhoneNormalizer.Normalize(req.InquiryWhatsAppNumber);
        if (Has("inquiryNotifyViaEmail")) b.InquiryNotifyViaEmail = req.InquiryNotifyViaEmail;
        if (Has("inquiryEmail")) b.InquiryEmail = string.IsNullOrWhiteSpace(req.InquiryEmail) ? null : req.InquiryEmail.Trim();
        // Discovery fields -- empty strings are still normalized to null (clearing the field),
        // only an actually-absent key leaves the current value alone.
        if (Has("city")) b.City = string.IsNullOrWhiteSpace(req.City) ? null : req.City.Trim();
        if (Has("addressLine")) b.AddressLine = string.IsNullOrWhiteSpace(req.AddressLine) ? null : req.AddressLine.Trim();
        if (Has("mapUrl")) b.MapUrl = mapUrl;
        if (Has("isListed")) b.IsListed = req.IsListed;
        if (Has("cancelApprovalNotifyViaWhatsApp")) b.CancelApprovalNotifyViaWhatsApp = req.CancelApprovalNotifyViaWhatsApp;
        if (Has("cancelApprovalWhatsAppNumber")) b.CancelApprovalWhatsAppNumber = string.IsNullOrWhiteSpace(req.CancelApprovalWhatsAppNumber) ? null : PhoneNormalizer.Normalize(req.CancelApprovalWhatsAppNumber);
        if (Has("cancelApprovalNotifyViaEmail")) b.CancelApprovalNotifyViaEmail = req.CancelApprovalNotifyViaEmail;
        if (Has("cancelApprovalEmail")) b.CancelApprovalEmail = string.IsNullOrWhiteSpace(req.CancelApprovalEmail) ? null : req.CancelApprovalEmail.Trim();

        await db.SaveChangesAsync();

        var changes = new List<string>();
        if (b.Name != oldName) changes.Add($"name: \"{oldName}\" → \"{b.Name}\"");
        if (b.Phone != oldPhone) changes.Add($"phone: \"{oldPhone}\" → \"{b.Phone}\"");
        if (b.Description != oldDescription) changes.Add("description");
        if (b.Language != oldLanguage) changes.Add($"language: {oldLanguage} → {b.Language}");
        if (b.MaxBookingsPerDay != oldMaxBookingsPerDay)
            changes.Add($"max bookings/day: {oldMaxBookingsPerDay?.ToString() ?? "unlimited"} → {b.MaxBookingsPerDay?.ToString() ?? "unlimited"}");
        if (b.MaxBookingsPerWeek != oldMaxBookingsPerWeek)
            changes.Add($"max bookings/week: {oldMaxBookingsPerWeek?.ToString() ?? "unlimited"} → {b.MaxBookingsPerWeek?.ToString() ?? "unlimited"}");
        if (b.WaitlistEnabled != oldWaitlistEnabled) changes.Add($"waitlist {(b.WaitlistEnabled ? "enabled" : "disabled")}");
        if (b.RequireApprovalOnCustomerCancel != oldRequireApprovalOnCustomerCancel)
            changes.Add($"cancellation approval {(b.RequireApprovalOnCustomerCancel ? "required" : "not required")}");
        if (b.ChatbotEnabled != oldChatbotEnabled) changes.Add($"chatbot {(b.ChatbotEnabled ? "enabled" : "disabled")}");
        if (b.ChatbotWelcomeMessageEn != oldChatbotWelcomeMessageEn || b.ChatbotWelcomeMessageAr != oldChatbotWelcomeMessageAr || b.ChatbotWelcomeMessageHe != oldChatbotWelcomeMessageHe)
            changes.Add("chatbot welcome message");
        if (b.ChatbotConfirmationMessageEn != oldChatbotConfirmationMessageEn || b.ChatbotConfirmationMessageAr != oldChatbotConfirmationMessageAr || b.ChatbotConfirmationMessageHe != oldChatbotConfirmationMessageHe)
            changes.Add("chatbot confirmation message");
        if (b.ChatbotFinalMessageEn != oldChatbotFinalMessageEn || b.ChatbotFinalMessageAr != oldChatbotFinalMessageAr || b.ChatbotFinalMessageHe != oldChatbotFinalMessageHe)
            changes.Add("chatbot final message");
        if (b.ChatbotDefaultLanguage != oldChatbotDefaultLanguage)
            changes.Add($"chatbot default language: {(object?)oldChatbotDefaultLanguage ?? "business default"} → {(object?)b.ChatbotDefaultLanguage ?? "business default"}");
        if (b.ChatbotInquiryEnabled != oldChatbotInquiryEnabled) changes.Add($"chatbot inquiry {(b.ChatbotInquiryEnabled ? "enabled" : "disabled")}");
        if (b.InquiryNotifyViaWhatsApp != oldInquiryNotifyViaWhatsApp) changes.Add($"inquiry WhatsApp notify {(b.InquiryNotifyViaWhatsApp ? "on" : "off")}");
        if (b.InquiryWhatsAppNumber != oldInquiryWhatsAppNumber) changes.Add("inquiry WhatsApp number");
        if (b.InquiryNotifyViaEmail != oldInquiryNotifyViaEmail) changes.Add($"inquiry email notify {(b.InquiryNotifyViaEmail ? "on" : "off")}");
        if (b.InquiryEmail != oldInquiryEmail) changes.Add("inquiry email address");
        if (b.City != oldCity) changes.Add($"city: \"{oldCity}\" → \"{b.City}\"");
        if (b.AddressLine != oldAddressLine) changes.Add("address");
        if (b.MapUrl != oldMapUrl) changes.Add("map link");
        if (b.IsListed != oldIsListed) changes.Add($"directory listing {(b.IsListed ? "on" : "off")}");
        if (b.CancelApprovalNotifyViaWhatsApp != oldCancelApprovalNotifyViaWhatsApp) changes.Add($"cancel-approval WhatsApp notify {(b.CancelApprovalNotifyViaWhatsApp ? "on" : "off")}");
        if (b.CancelApprovalWhatsAppNumber != oldCancelApprovalWhatsAppNumber) changes.Add("cancel-approval WhatsApp number");
        if (b.CancelApprovalNotifyViaEmail != oldCancelApprovalNotifyViaEmail) changes.Add($"cancel-approval email notify {(b.CancelApprovalNotifyViaEmail ? "on" : "off")}");
        if (b.CancelApprovalEmail != oldCancelApprovalEmail) changes.Add("cancel-approval email address");

        this.SetActivityDetail(changes.Count > 0 ? $"Updated settings: {string.Join(", ", changes)}" : "Updated settings (no fields changed)");

        return Ok(new { b.Id, b.Name, Language = b.Language.ToString() });
    }

    // ─── Chatbot inquiries ──────────────────────────────────────────────────
    // Always-on "in-app" side of owner notification for the chatbot's Inquiry mode (see
    // WhatsAppController.HandleInquiryModeAsync) -- exists regardless of whether
    // InquiryNotifyViaWhatsApp/Email are also on.

    [HttpGet("chatbot-inquiries")]
    public async Task<IActionResult> GetChatbotInquiries([FromQuery] bool unreadOnly = false)
    {
        var query = db.ChatbotInquiries.Where(i => i.BusinessId == BusinessId);
        if (unreadOnly) query = query.Where(i => !i.IsRead);

        var inquiries = await query.OrderByDescending(i => i.CreatedAt).Take(200)
            .Select(i => new ChatbotInquiryDto(i.Id, i.CustomerPhone, i.CustomerName, i.Message, i.IsRead, i.CreatedAt))
            .ToListAsync();
        return Ok(inquiries);
    }

    [HttpPost("chatbot-inquiries/{id}/read")]
    public async Task<IActionResult> MarkChatbotInquiryRead(string id)
    {
        var inquiry = await db.ChatbotInquiries.FirstOrDefaultAsync(i => i.Id == id && i.BusinessId == BusinessId);
        if (inquiry is null) return NotFound();
        inquiry.IsRead = true;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ─── Items ──────────────────────────────────────────────────────────────

    [HttpGet("items")]
    public async Task<IActionResult> GetItems()
    {
        var items = await db.Items
            .Include(s => s.GalleryPhotos)
            .Where(s => s.BusinessId == BusinessId && s.IsActive)
            .OrderBy(s => s.NameEn)
            .ToListAsync();
        return Ok(items.Select(ToItemDto));
    }

    // Shared by create and edit -- edit used to skip the name check, and neither bounded price or
    // duration (a negative price saved; a huge one overflowed the DB column and returned 500).
    private static string? ItemInputError(CreateItemRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NameEn) || string.IsNullOrWhiteSpace(req.NameAr) || string.IsNullOrWhiteSpace(req.NameHe))
            return "All name fields are required";
        if (req.IsBookable && (req.DurationMinutes is null || req.DurationMinutes < 15 || req.DurationMinutes % 15 != 0))
            return "Duration must be a multiple of 15 (min 15)";
        if (req.IsBookable && req.DurationMinutes > 24 * 60)
            return "Duration can be at most 24 hours";
        if (req.Price is < 0 or > 1_000_000)
            return "Price must be between 0 and 1,000,000";
        return null;
    }

    [HttpPost("items")]
    public async Task<IActionResult> CreateItem([FromBody] CreateItemRequest req)
    {
        if (ItemInputError(req) is { } inputError)
            return BadRequest(new { error = inputError });
        if (!IsPhotoMode(req.PhotoMode) || !Enum.TryParse<ItemPhotoMode>(req.PhotoMode, out var photoMode))
            return BadRequest(new { error = "Invalid photo mode" });

        var item = new Item
        {
            BusinessId = BusinessId,
            NameEn = req.NameEn,
            NameAr = req.NameAr,
            NameHe = req.NameHe,
            DurationMinutes = req.IsBookable ? req.DurationMinutes : null,
            Price = req.Price,
            PhotoMode = photoMode,
            IsBookable = req.IsBookable,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        this.SetActivityDetail($"Created item: {item.NameEn} ({DescribeBookability(item)}, {DescribePhotoMode(item.PhotoMode)})");
        return StatusCode(201, ToItemDto(item));
    }

    [HttpPatch("items/{id}")]
    public async Task<IActionResult> UpdateItem(string id, [FromBody] CreateItemRequest req)
    {
        if (ItemInputError(req) is { } inputError)
            return BadRequest(new { error = inputError });
        if (!IsPhotoMode(req.PhotoMode) || !Enum.TryParse<ItemPhotoMode>(req.PhotoMode, out var photoMode))
            return BadRequest(new { error = "Invalid photo mode" });

        var item = await db.Items.Include(s => s.GalleryPhotos)
            .FirstOrDefaultAsync(s => s.Id == id && s.BusinessId == BusinessId);
        if (item is null) return NotFound();

        // Captured before assignment so we can report only the fields that actually changed,
        // same approach as UpdateSettings/SaveWorkingHours above.
        var oldNameEn = item.NameEn;
        var oldNameAr = item.NameAr;
        var oldNameHe = item.NameHe;
        var oldDuration = item.DurationMinutes;
        var oldPrice = item.Price;
        var oldPhotoMode = item.PhotoMode;
        var oldIsBookable = item.IsBookable;

        item.NameEn = req.NameEn;
        item.NameAr = req.NameAr;
        item.NameHe = req.NameHe;
        item.DurationMinutes = req.IsBookable ? req.DurationMinutes : null;
        item.Price = req.Price;
        item.PhotoMode = photoMode;
        item.IsBookable = req.IsBookable;

        await db.SaveChangesAsync();

        var changes = new List<string>();
        if (item.NameEn != oldNameEn) changes.Add($"name: \"{oldNameEn}\" → \"{item.NameEn}\"");
        if (item.NameAr != oldNameAr || item.NameHe != oldNameHe) changes.Add("translated names");
        if (item.IsBookable != oldIsBookable) changes.Add($"bookable: {oldIsBookable} → {item.IsBookable}");
        if (item.DurationMinutes != oldDuration) changes.Add($"duration: {DescribeDuration(oldDuration)} → {DescribeDuration(item.DurationMinutes)}");
        if (item.Price != oldPrice) changes.Add($"price: {DescribePrice(oldPrice)} → {DescribePrice(item.Price)}");
        if (item.PhotoMode != oldPhotoMode) changes.Add($"photo mode: {DescribePhotoMode(oldPhotoMode)} → {DescribePhotoMode(item.PhotoMode)}");

        this.SetActivityDetail(changes.Count > 0
            ? $"Updated item: {string.Join(", ", changes)}"
            : $"Updated item: {item.NameEn} (no changes)");

        return Ok(ToItemDto(item));
    }

    [HttpDelete("items/{id}")]
    public async Task<IActionResult> DeleteItem(string id)
    {
        var item = await db.Items.FirstOrDefaultAsync(s => s.Id == id && s.BusinessId == BusinessId);
        if (item is null) return NotFound();
        item.IsActive = false;
        await db.SaveChangesAsync();
        this.SetActivityDetail($"Deleted item: {item.NameEn} ({DescribeBookability(item)})");
        return Ok(new { ok = true });
    }

    private static string DescribeDuration(int? minutes) => minutes is null ? "none" : $"{minutes} min";
    private static string DescribePrice(decimal? price) => price is null ? "none" : $"₪{price:F2}";
    private static string DescribeBookability(Item item) =>
        item.IsBookable ? $"{DescribeDuration(item.DurationMinutes)}, {DescribePrice(item.Price)}" : "not bookable";

    private static string DescribePhotoMode(ItemPhotoMode mode) => mode switch
    {
        ItemPhotoMode.OwnerGallery => "owner gallery photos",
        ItemPhotoMode.CustomerUpload => "customer-uploaded photo",
        ItemPhotoMode.Both => "owner gallery or customer-uploaded photo",
        _ => "no reference photo",
    };

    // ─── Item gallery photos ────────────────────────────────────────────────

    [HttpPost("items/{id}/gallery")]
    [RequestSizeLimit(ImageUpload.MaxBytes)]
    public async Task<IActionResult> UploadGalleryPhoto(string id, IFormFile file)
    {
        var item = await db.Items.FirstOrDefaultAsync(s => s.Id == id && s.BusinessId == BusinessId);
        if (item is null) return NotFound();

        if (await ImageUpload.ValidateAsync(file) is not { } ext)
            return BadRequest(new { error = ImageUpload.Error });

        var uploadsDir = Path.Combine(env.ContentRootPath, "wwwroot", "uploads", "gallery", item.Id);
        Directory.CreateDirectory(uploadsDir);

        var fileName = $"{Guid.NewGuid():N}{ext}";
        await using (var stream = new FileStream(Path.Combine(uploadsDir, fileName), FileMode.Create))
            await file.CopyToAsync(stream);

        var photo = new ItemGalleryPhoto
        {
            ItemId = item.Id,
            Url = $"/api/uploads/gallery/{item.Id}/{fileName}",
        };
        db.ItemGalleryPhotos.Add(photo);
        await db.SaveChangesAsync();

        this.SetActivityDetail($"Uploaded gallery photo for item: {item.NameEn}");

        return StatusCode(201, new ItemGalleryPhotoDto(photo.Id, photo.Url));
    }

    [HttpDelete("items/{id}/gallery/{photoId}")]
    public async Task<IActionResult> DeleteGalleryPhoto(string id, string photoId)
    {
        var photo = await db.ItemGalleryPhotos.Include(p => p.Item)
            .FirstOrDefaultAsync(p => p.Id == photoId && p.ItemId == id && p.Item.BusinessId == BusinessId);
        if (photo is null) return NotFound();

        var path = Path.Combine(env.ContentRootPath, "wwwroot", photo.Url.Replace("/api/uploads/", "uploads/").Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);

        db.ItemGalleryPhotos.Remove(photo);
        await db.SaveChangesAsync();

        this.SetActivityDetail($"Deleted gallery photo for item: {photo.Item.NameEn}");

        return Ok(new { ok = true });
    }

    // ─── Schedule ───────────────────────────────────────────────────────────

    [HttpGet("schedule")]
    public async Task<IActionResult> GetSchedule()
    {
        var wh = await db.WorkingHours.Where(w => w.BusinessId == BusinessId)
            .Select(w => new WorkingHoursDto(w.Id, w.DayOfWeek, w.StartTime, w.EndTime, w.IsActive))
            .ToListAsync();
        var brk = await db.Breaks.Where(b => b.BusinessId == BusinessId)
            .Select(b => new BreakDto(b.Id, b.DayOfWeek, b.StartTime, b.EndTime))
            .ToListAsync();
        var bsl = await db.BlockedSlots.Where(b => b.BusinessId == BusinessId)
            .OrderBy(b => b.Date)
            .Select(b => new BlockedSlotDto(b.Id, b.Date.ToString("yyyy-MM-dd"), b.StartTime, b.EndTime, b.Reason))
            .ToListAsync();
        return Ok(new ScheduleResponse(wh, brk, bsl));
    }

    [HttpPost("schedule")]
    public async Task<IActionResult> SaveWorkingHours([FromBody] List<WorkingHoursDto> hours)
    {
        if (hours.Any(h => h.DayOfWeek is < 0 or > 6)) return BadRequest(new { error = "Invalid day of week" });
        if (hours.Where(h => h.IsActive).Select(h => TimeRangeError(h.StartTime, h.EndTime)).FirstOrDefault(e => e is not null) is { } hoursError)
            return BadRequest(new { error = hoursError });

        // Captured before assignment so we can report only the days that actually changed --
        // the schedule form always submits all 7 days on every save, mirroring UpdateSettings.
        var oldByDay = await db.WorkingHours.Where(w => w.BusinessId == BusinessId)
            .ToDictionaryAsync(w => w.DayOfWeek, w => (w.StartTime, w.EndTime, w.IsActive));

        await schedulePresets.UpsertWorkingHours(BusinessId, hours.Select(h => (h.DayOfWeek, h.StartTime, h.EndTime, h.IsActive)));

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
        if (req.DayOfWeek is < 0 or > 6) return BadRequest(new { error = "Invalid day of week" });
        if (TimeRangeError(req.StartTime, req.EndTime) is { } breakError) return BadRequest(new { error = breakError });
        var br = new Break { BusinessId = BusinessId, DayOfWeek = req.DayOfWeek, StartTime = req.StartTime, EndTime = req.EndTime };
        db.Breaks.Add(br);
        await db.SaveChangesAsync();
        this.SetActivityDetail($"Added break: {(DayOfWeek)br.DayOfWeek}s {br.StartTime}–{br.EndTime}");
        return StatusCode(201, new BreakDto(br.Id, br.DayOfWeek, br.StartTime, br.EndTime));
    }

    [HttpDelete("schedule/breaks/{id}")]
    public async Task<IActionResult> DeleteBreak(string id)
    {
        var br = await db.Breaks.FirstOrDefaultAsync(b => b.Id == id && b.BusinessId == BusinessId);
        if (br is null) return NotFound();
        db.Breaks.Remove(br);
        await db.SaveChangesAsync();
        this.SetActivityDetail($"Deleted break: {(DayOfWeek)br.DayOfWeek}s {br.StartTime}–{br.EndTime}");
        return Ok(new { ok = true });
    }

    [HttpPost("schedule/blocked")]
    public async Task<IActionResult> AddBlockedSlot([FromBody] CreateBlockedSlotRequest req)
    {
        if (BlockedTimesError(req.StartTime, req.EndTime) is { } timesError) return BadRequest(new { error = timesError });
        var slot = new BlockedSlot
        {
            BusinessId = BusinessId,
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

    // Blocks a consecutive streak of dates in one action (e.g. a week off) -- kept as a separate
    // endpoint alongside AddBlockedSlot rather than replacing it, since the owner still wants the
    // single-date picker for the common case. One transaction wraps the whole loop so a failure
    // partway rolls back the entire range instead of leaving it half-blocked; each row created is
    // otherwise an ordinary independent BlockedSlot, so the existing per-row delete endpoint needs
    // no changes to handle rows created this way.
    [HttpPost("schedule/blocked/range")]
    public async Task<IActionResult> AddBlockedRange([FromBody] CreateBlockedRangeRequest req)
    {
        var start = DateTime.Parse(req.StartDate + "T00:00:00Z").ToUniversalTime();
        var end = DateTime.Parse(req.EndDate + "T00:00:00Z").ToUniversalTime();
        if (end < start) return BadRequest(new { error = "End date cannot be before start date" });
        if (BlockedTimesError(req.StartTime, req.EndTime) is { } timesError) return BadRequest(new { error = timesError });
        if ((end - start).Days > 366) return BadRequest(new { error = "Range too large (max 366 days)" });

        await using var tx = await db.Database.BeginTransactionAsync();
        var created = new List<BlockedSlot>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var slot = new BlockedSlot { BusinessId = BusinessId, Date = d, StartTime = req.StartTime, EndTime = req.EndTime, Reason = req.Reason };
            db.BlockedSlots.Add(slot);
            created.Add(slot);
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        var timeRange = req.StartTime is not null && req.EndTime is not null ? $" {req.StartTime}–{req.EndTime}" : " (full day)";
        var reasonSuffix = string.IsNullOrWhiteSpace(req.Reason) ? "" : $" — {req.Reason}";
        this.SetActivityDetail($"Blocked date range {req.StartDate}–{req.EndDate}{timeRange}{reasonSuffix}");

        return StatusCode(201, created.OrderBy(s => s.Date)
            .Select(s => new BlockedSlotDto(s.Id, s.Date.ToString("yyyy-MM-dd"), s.StartTime, s.EndTime, s.Reason))
            .ToList());
    }

    // A partial block needs both times, in order. Null/null is a full-day block. One missing time
    // used to quietly become a full-day block, and 15:00-10:00 was accepted.
    private const string TimePattern = @"^([01]\d|2[0-3]):[0-5]\d$";

    private static string? BlockedTimesError(string? startTime, string? endTime)
    {
        if (startTime is null && endTime is null) return null;
        if (string.IsNullOrWhiteSpace(startTime) || string.IsNullOrWhiteSpace(endTime))
            return "Enter both a start and an end time, or block the full day";
        return TimeRangeError(startTime, endTime);
    }

    // Working hours, breaks and preset days. Availability parses these with TimeSpan.Parse, so one
    // bad value ("ab", "25:00") used to make every availability request for the business a 500.
    private static string? TimeRangeError(string? startTime, string? endTime)
    {
        if (startTime is null || endTime is null
            || !System.Text.RegularExpressions.Regex.IsMatch(startTime, TimePattern) || !System.Text.RegularExpressions.Regex.IsMatch(endTime, TimePattern))
            return "Invalid time";
        // "HH:MM" is zero-padded, so ordinal comparison is time order.
        return string.CompareOrdinal(endTime, startTime) <= 0 ? "End time must be after start time" : null;
    }

    [HttpDelete("schedule/blocked/{id}")]
    public async Task<IActionResult> DeleteBlockedSlot(string id)
    {
        var slot = await db.BlockedSlots.FirstOrDefaultAsync(b => b.Id == id && b.BusinessId == BusinessId);
        if (slot is null) return NotFound();
        db.BlockedSlots.Remove(slot);
        await db.SaveChangesAsync();

        var timeRange = slot.StartTime is not null && slot.EndTime is not null ? $" {slot.StartTime}–{slot.EndTime}" : " (full day)";
        this.SetActivityDetail($"Unblocked {slot.Date:yyyy-MM-dd}{timeRange}");

        return Ok(new { ok = true });
    }

    // ─── Schedule presets ───────────────────────────────────────────────────
    // A named snapshot of the 7-day WorkingHours template, saveable and re-applicable later (e.g.
    // "Christmas Hours"). Applying overwrites the standing weekly template via the same
    // UpsertWorkingHours helper SaveWorkingHours uses -- it does not auto-revert; confirmed with
    // the business owner as the desired behavior.

    [HttpGet("schedule/presets")]
    public async Task<IActionResult> GetSchedulePresets()
    {
        await schedulePresets.GetOrCreateDefaultPreset(BusinessId);

        var presets = await db.SchedulePresets.Where(p => p.BusinessId == BusinessId)
            .Include(p => p.Days)
            .Include(p => p.Breaks)
            .OrderByDescending(p => p.IsDefault).ThenByDescending(p => p.CreatedAt)
            .ToListAsync();
        return Ok(presets.Select(ToPresetDto).ToList());
    }

    private static SchedulePresetDto ToPresetDto(SchedulePreset p) => new(p.Id, p.Name, p.CreatedAt, p.IsDefault,
        p.Days.OrderBy(d => d.DayOfWeek).Select(d => new SchedulePresetDayDto(d.DayOfWeek, d.StartTime, d.EndTime, d.IsActive)).ToList(),
        p.Breaks.OrderBy(b => b.DayOfWeek).ThenBy(b => b.StartTime).Select(b => new SchedulePresetBreakDto(b.DayOfWeek, b.StartTime, b.EndTime)).ToList());

    private static void ValidateDays(List<SchedulePresetDayDto> days)
    {
        if (days.Count != 7 || days.Select(d => d.DayOfWeek).Distinct().Count() != 7 || days.Any(d => d.DayOfWeek is < 0 or > 6))
            throw new ArgumentException("Days must cover all 7 days of the week exactly once");
        // A closed day's times are never used, so only open days are checked.
        if (days.Where(d => d.IsActive).Select(d => TimeRangeError(d.StartTime, d.EndTime)).FirstOrDefault(e => e is not null) is { } error)
            throw new ArgumentException(error);
    }

    // Unlike Days there's no fixed count -- zero or many breaks per day, same as the live Break table.
    private static void ValidateBreaks(List<SchedulePresetBreakDto> breaks)
    {
        if (breaks.Any(b => b.DayOfWeek is < 0 or > 6))
            throw new ArgumentException("Break day must be between 0 and 6");
        if (breaks.Select(b => TimeRangeError(b.StartTime, b.EndTime)).FirstOrDefault(e => e is not null) is { } error)
            throw new ArgumentException(error);
    }

    [HttpPost("schedule/presets")]
    public async Task<IActionResult> SaveSchedulePreset([FromBody] SaveSchedulePresetRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "Name is required" });
        var breaks = req.Breaks ?? [];
        try
        {
            ValidateDays(req.Days);
            ValidateBreaks(breaks);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }

        var preset = new SchedulePreset { BusinessId = BusinessId, Name = req.Name.Trim() };
        preset.Days = req.Days.Select(d => new SchedulePresetDay
        {
            SchedulePresetId = preset.Id, DayOfWeek = d.DayOfWeek, StartTime = d.StartTime, EndTime = d.EndTime, IsActive = d.IsActive,
        }).ToList();
        preset.Breaks = breaks.Select(b => new SchedulePresetBreak
        {
            SchedulePresetId = preset.Id, DayOfWeek = b.DayOfWeek, StartTime = b.StartTime, EndTime = b.EndTime,
        }).ToList();

        db.SchedulePresets.Add(preset);
        await db.SaveChangesAsync();
        this.SetActivityDetail($"Saved schedule preset \"{preset.Name}\"");

        return StatusCode(201, ToPresetDto(preset));
    }

    [HttpPut("schedule/presets/{id}")]
    public async Task<IActionResult> UpdateSchedulePreset(string id, [FromBody] UpdateSchedulePresetRequest req)
    {
        var preset = await db.SchedulePresets.Include(p => p.Days).Include(p => p.Breaks).FirstOrDefaultAsync(p => p.Id == id && p.BusinessId == BusinessId);
        if (preset is null) return NotFound();
        var breaks = req.Breaks ?? [];
        try
        {
            ValidateDays(req.Days);
            ValidateBreaks(breaks);
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }

        // The Default preset's name is locked -- it's always "Default", regardless of what's sent.
        if (!preset.IsDefault)
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "Name is required" });
            preset.Name = req.Name.Trim();
        }
        foreach (var d in req.Days)
        {
            var existing = preset.Days.First(x => x.DayOfWeek == d.DayOfWeek);
            existing.StartTime = d.StartTime;
            existing.EndTime = d.EndTime;
            existing.IsActive = d.IsActive;
        }
        // Breaks don't have Days' fixed 7-row shape, so the count can change -- replace the whole
        // collection rather than upserting in place.
        db.SchedulePresetBreaks.RemoveRange(preset.Breaks);
        preset.Breaks = breaks.Select(b => new SchedulePresetBreak
        {
            SchedulePresetId = preset.Id, DayOfWeek = b.DayOfWeek, StartTime = b.StartTime, EndTime = b.EndTime,
        }).ToList();
        await db.SaveChangesAsync();

        // The Default preset IS the live baseline -- editing it applies immediately unless a
        // scheduled range is currently overriding the weekly template (in which case it'll take
        // effect once that range reverts back to Default).
        if (preset.IsDefault && !await schedulePresets.HasActiveSchedule(BusinessId))
            await schedulePresets.ApplyPresetToWorkingHours(preset.Id);
        // Likewise, a preset whose date range is running right now IS the live schedule -- push the
        // edit through so it doesn't sit unused until the next time the range is (re)applied.
        else if (!preset.IsDefault && await db.PresetSchedules.AnyAsync(s => s.BusinessId == BusinessId && s.PresetId == preset.Id && s.Applied && !s.Reverted))
            await schedulePresets.ApplyPresetToWorkingHours(preset.Id);

        this.SetActivityDetail($"Updated schedule preset \"{preset.Name}\"");

        return Ok(ToPresetDto(preset));
    }

    [HttpPost("schedule/presets/{id}/apply")]
    public async Task<IActionResult> ApplySchedulePreset(string id)
    {
        var preset = await db.SchedulePresets.FirstOrDefaultAsync(p => p.Id == id && p.BusinessId == BusinessId);
        if (preset is null) return NotFound();

        await schedulePresets.ApplyPresetPermanently(BusinessId, id);
        this.SetActivityDetail($"Applied schedule preset \"{preset.Name}\" (overwrote weekly working hours and the Default preset)");

        return Ok(new { ok = true });
    }

    [HttpDelete("schedule/presets/{id}")]
    public async Task<IActionResult> DeleteSchedulePreset(string id)
    {
        var preset = await db.SchedulePresets.FirstOrDefaultAsync(p => p.Id == id && p.BusinessId == BusinessId);
        if (preset is null) return NotFound();
        if (preset.IsDefault) return BadRequest(new { error = "The default schedule can't be deleted -- the system always needs a baseline." });
        try
        {
            db.SchedulePresets.Remove(preset);
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return BadRequest(new { error = "This preset is used by a pending or active scheduled change -- cancel that first." });
        }
        this.SetActivityDetail($"Deleted schedule preset \"{preset.Name}\"");
        return Ok(new { ok = true });
    }

    // ─── Preset schedules (date-range application) ─────────────────────────
    // "Apply now" (above) overwrites the weekly template immediately and permanently. This instead
    // schedules a preset to take over for a date range and automatically revert afterward -- see
    // SchedulePresetService for the actual apply/revert mechanics, which the daily
    // /api/cron/apply-scheduled-presets sweep also drives.

    [HttpGet("schedule/preset-schedule")]
    public async Task<IActionResult> GetPresetSchedule()
    {
        var schedule = await db.PresetSchedules.Include(s => s.Preset)
            .Where(s => s.BusinessId == BusinessId && !s.Reverted)
            .FirstOrDefaultAsync();
        // Wrapped in an object (never a bare null body) so the client can always deserialize the
        // response the same way, whether or not a schedule exists.
        if (schedule is null) return Ok(new PresetScheduleResponse(null));

        return Ok(new PresetScheduleResponse(new PresetScheduleDto(schedule.Id, schedule.PresetId, schedule.Preset.Name,
            schedule.StartDate.ToString("yyyy-MM-dd"), schedule.EndDate.ToString("yyyy-MM-dd"), schedule.Applied)));
    }

    [HttpPost("schedule/presets/{id}/schedule")]
    public async Task<IActionResult> SchedulePresetForRange(string id, [FromBody] SchedulePresetRangeRequest req)
    {
        var start = DateTime.Parse(req.StartDate + "T00:00:00Z").ToUniversalTime();
        var end = DateTime.Parse(req.EndDate + "T00:00:00Z").ToUniversalTime();
        if (end < start) return BadRequest(new { error = "End date cannot be before start date" });
        // A range that's already over used to count as "starting now" and replace the live hours.
        if (DateTime.Parse(req.EndDate) < DateTime.Now.Date) return BadRequest(new { error = "This date range has already ended" });

        try
        {
            var schedule = await schedulePresets.ScheduleForRange(BusinessId, id, start, end);
            var preset = await db.SchedulePresets.FirstAsync(p => p.Id == id);
            this.SetActivityDetail($"Scheduled preset \"{preset.Name}\" for {req.StartDate}–{req.EndDate}");
            return StatusCode(201, new PresetScheduleDto(schedule.Id, schedule.PresetId, preset.Name, req.StartDate, req.EndDate, schedule.Applied));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("schedule/preset-schedule/{id}")]
    public async Task<IActionResult> CancelPresetSchedule(string id)
    {
        try
        {
            await schedulePresets.CancelPresetSchedule(BusinessId, id);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        this.SetActivityDetail("Cancelled scheduled preset change");
        return Ok(new { ok = true });
    }

    // ─── Dashboard ──────────────────────────────────────────────────────────

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard([FromQuery] int week = 0)
    {
        // a.Date is the business's local wall-clock calendar date, never converted to/from UTC
        // (see AvailabilityService) — bucket weeks by local "now" or this drifts a day near midnight.
        var now = DateTime.Now.Date;
        var weekStart = now.AddDays(week * 7 - (int)now.DayOfWeek);
        var weekEnd = weekStart.AddDays(6);

        var appointments = await db.Appointments
            .Include(a => a.Customer)
            .Include(a => a.Item)
            .Where(a => a.BusinessId == BusinessId && a.Date >= weekStart && a.Date <= weekEnd && a.Status != AppointmentStatus.CANCELLED)
            .OrderBy(a => a.Date).ThenBy(a => a.StartTime)
            .ToListAsync();

        return Ok(appointments.Select(a => new DashboardAppointmentDto(
            a.Id, a.Date.ToString("yyyy-MM-dd"), a.StartTime, a.EndTime,
            AppointmentStatusHelper.EffectiveStatus(a.Status, a.Date, a.EndTime), a.Notes,
            new CustomerSummary(a.Customer.Id, a.Customer.Name, a.Customer.FamilyName, a.Customer.Phone),
            new ItemSummary(a.Item.Id, a.Item.NameEn, a.Item.NameAr, a.Item.NameHe, a.Item.DurationMinutes, a.Item.Price),
            a.Item.Price, a.PhotoUrl, a.RecurringSeriesId, a.PendingCancellationApproval)));
    }

    // ─── Appointments ────────────────────────────────────────────────────────

    [HttpGet("appointments")]
    public async Task<IActionResult> GetAppointments([FromQuery] string? filter = null)
    {
        // Same local-vs-UTC reasoning as GetDashboard above.
        var today = DateTime.Now.Date;
        var query = db.Appointments
            .Include(a => a.Customer)
            .Include(a => a.Item)
            .Where(a => a.BusinessId == BusinessId && !a.IsWaitlistCarrier);

        query = filter switch
        {
            "today" => query.Where(a => a.Date == today),
            "upcoming" => query.Where(a => a.Date >= today),
            "past" => query.Where(a => a.Date < today),
            _ => query
        };

        // Upcoming and today read soonest first; past and all, newest first.
        var ordered = filter is "upcoming" or "today"
            ? query.OrderBy(a => a.Date).ThenBy(a => a.StartTime)
            : query.OrderByDescending(a => a.Date).ThenBy(a => a.StartTime);
        var appointments = await ordered.ToListAsync();

        return Ok(appointments.Select(a => new DashboardAppointmentDto(
            a.Id, a.Date.ToString("yyyy-MM-dd"), a.StartTime, a.EndTime,
            AppointmentStatusHelper.EffectiveStatus(a.Status, a.Date, a.EndTime), a.Notes,
            new CustomerSummary(a.Customer.Id, a.Customer.Name, a.Customer.FamilyName, a.Customer.Phone),
            new ItemSummary(a.Item.Id, a.Item.NameEn, a.Item.NameAr, a.Item.NameHe, a.Item.DurationMinutes, a.Item.Price),
            a.Item.Price, a.PhotoUrl, a.RecurringSeriesId, a.PendingCancellationApproval)));
    }

    // ─── Manual appointment creation ───────────────────────────────────────

    [HttpGet("appointments/availability")]
    public async Task<IActionResult> GetAppointmentAvailability([FromQuery] string date, [FromQuery] string itemId, [FromQuery] string? excludeAppointmentId = null)
    {
        var item = await db.Items.FirstOrDefaultAsync(s => s.Id == itemId && s.BusinessId == BusinessId && s.IsActive);
        if (item is null) return NotFound(new { error = "Item not found" });
        if (!item.IsBookable || item.DurationMinutes is null) return BadRequest(new { error = "This item is not bookable" });

        // The reschedule modal passes the appointment it's moving, so it doesn't block itself.
        var slots = await availability.GetAvailableSlots(BusinessId, date, item.DurationMinutes.Value, excludeAppointmentId);
        return Ok(new { slots });
    }

    [HttpGet("customers/search")]
    public async Task<IActionResult> SearchCustomers([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2) return Ok(Array.Empty<CustomerSummary>());
        var q = query.Trim().ToLower();
        var customers = await db.Customers
            .Where(c => c.BusinessId == BusinessId &&
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
        var business = await db.Businesses.FindAsync(BusinessId);
        if (business is null) return NotFound();

        var item = await db.Items.FirstOrDefaultAsync(s => s.Id == req.ItemId && s.BusinessId == BusinessId && s.IsActive);
        if (item is null) return NotFound(new { error = "Item not found" });
        if (!item.IsBookable || item.DurationMinutes is null) return BadRequest(new { error = "This item is not bookable" });

        var (customer, customerError) = await ResolveCustomer(req.CustomerId, req.CustomerName, req.CustomerPhone, req.CustomerFamilyName);
        if (customerError is not null) return customerError;

        string? photoUrl = null;
        if ((item.PhotoMode == ItemPhotoMode.OwnerGallery || item.PhotoMode == ItemPhotoMode.Both) && !string.IsNullOrWhiteSpace(req.GalleryPhotoId))
        {
            var photo = await db.ItemGalleryPhotos.FirstOrDefaultAsync(p => p.Id == req.GalleryPhotoId && p.ItemId == item.Id);
            if (photo is null) return BadRequest(new { error = "The selected photo is no longer available." });
            photoUrl = photo.Url;
        }
        else if ((item.PhotoMode == ItemPhotoMode.CustomerUpload || item.PhotoMode == ItemPhotoMode.Both) && !string.IsNullOrWhiteSpace(req.CustomerPhotoUrl))
        {
            if (!req.CustomerPhotoUrl.StartsWith("/api/uploads/appointment-photos/"))
                return BadRequest(new { error = "Invalid photo reference." });
            photoUrl = req.CustomerPhotoUrl;
        }

        if (req.Force && AvailabilityService.ForcedSlotError(req.Date, req.StartTime, item.DurationMinutes.Value) is { } forcedError)
            return BadRequest(new { error = forcedError });

        var endTime = AvailabilityService.AddMinutes(req.StartTime, item.DurationMinutes.Value);

        if (!req.Force)
        {
            var slots = await availability.GetAvailableSlots(BusinessId, req.Date, item.DurationMinutes.Value);
            if (!slots.Any(s => s.Start == req.StartTime))
                return Conflict(new { error = "Slot not available" });
        }
        else if (await availability.HasConflictingAppointment(BusinessId, req.Date, req.StartTime, endTime))
        {
            return Conflict(new { error = "This time overlaps an existing appointment" });
        }

        var requestedDate = DateTime.Parse(req.Date + "T00:00:00Z").ToUniversalTime();
        var appointment = new Appointment
        {
            BusinessId = BusinessId,
            CustomerId = customer!.Id,
            ItemId = item.Id,
            Date = requestedDate,
            StartTime = req.StartTime,
            EndTime = endTime,
            Notes = req.Notes,
            PhotoUrl = photoUrl,
            Status = AppointmentStatus.CONFIRMED,
        };
        db.Appointments.Add(appointment);

        await waitlist.ResolveForRebooking(BusinessId, requestedDate, req.StartTime, endTime);
        if (!await availability.TrySaveOrDetectConflict(BusinessId, req.Date, req.StartTime, endTime))
            return Conflict(new { error = "Slot no longer available" });

        this.SetActivityDetail($"Booked appointment: {item.NameEn} for {ActivityDetailExtensions.FullName(customer.Name, customer.FamilyName)} on {req.Date} at {req.StartTime}");

        // Best-effort, same as the public booking flow -- an appointment the owner books directly
        // should still confirm on WhatsApp for a business with a linked number, not just ones the
        // customer books themselves. Sent after the response (NotificationQueue).
        await customerNotifier.QueueBookedByOwner(appointment);

        return StatusCode(201, new DashboardAppointmentDto(
            appointment.Id, req.Date, appointment.StartTime, appointment.EndTime,
            "CONFIRMED", appointment.Notes,
            new CustomerSummary(customer.Id, customer.Name, customer.FamilyName, customer.Phone),
            new ItemSummary(item.Id, item.NameEn, item.NameAr, item.NameHe, item.DurationMinutes, item.Price),
            item.Price, appointment.PhotoUrl, appointment.RecurringSeriesId));
    }

    [HttpPatch("appointments/{id}/reschedule")]
    public async Task<IActionResult> RescheduleAppointment(string id, [FromBody] RescheduleRequest req)
    {
        var appt = await db.Appointments
            .Include(a => a.Item).Include(a => a.Customer)
            .FirstOrDefaultAsync(a => a.Id == id && a.BusinessId == BusinessId);
        if (appt is null) return NotFound(new { error = "Not found" });
        if (appt.PendingCancellationApproval || AppointmentStatusHelper.EffectiveStatus(appt.Status, appt.Date, appt.EndTime) != "CONFIRMED")
            return Conflict(new { error = "This appointment can no longer be modified" });
        if (!appt.Item.IsBookable || appt.Item.DurationMinutes is null)
            return Conflict(new { error = "This appointment can no longer be modified" });

        var slots = await availability.GetAvailableSlots(BusinessId, req.Date, appt.Item.DurationMinutes.Value, appt.Id);
        if (!slots.Any(s => s.Start == req.StartTime))
            return Conflict(new { error = "Slot not available" });

        var oldDate = appt.Date.ToString("yyyy-MM-dd");
        var oldStartTime = appt.StartTime;

        var freedSlot = await waitlist.DetachForReschedule(appt);

        appt.Date = DateTime.Parse(req.Date + "T00:00:00Z").ToUniversalTime();
        appt.StartTime = req.StartTime;
        appt.EndTime = AvailabilityService.AddMinutes(req.StartTime, appt.Item.DurationMinutes.Value);
        // Both reminders are for the new time, including the 3-hours-before one.
        appt.ReminderSent = false;
        appt.ReminderSentSoon = false;

        await waitlist.ResolveForRebooking(BusinessId, appt.Date, appt.StartTime, appt.EndTime);
        if (!await availability.TrySaveOrDetectConflict(BusinessId, req.Date, req.StartTime, appt.EndTime))
            return Conflict(new { error = "Slot not available" });
        if (freedSlot is not null)
        {
            await waitlist.NotifyForCancellation(freedSlot);
            await db.SaveChangesAsync();
        }
        if (req.NotifyCustomer)
            await customerNotifier.QueueRescheduledByOwner(appt, oldDate, oldStartTime);

        this.SetActivityDetail(
            $"Rescheduled appointment: {appt.Item.NameEn} for {ActivityDetailExtensions.FullName(appt.Customer.Name, appt.Customer.FamilyName)} from {oldDate} {oldStartTime} to {req.Date} at {req.StartTime}");

        return Ok(new { appt.Id, Status = appt.Status.ToString() });
    }

    [HttpPatch("appointments/{id}")]
    public async Task<IActionResult> UpdateAppointmentStatus(string id, [FromBody] UpdateStatusRequest req)
    {
        // The business can only cancel now — "Completed" is computed automatically once an
        // appointment's end time passes (AppointmentStatusHelper), not manually set.
        if (req.Status != nameof(AppointmentStatus.CANCELLED))
            return BadRequest(new { error = "Only cancelling is supported" });

        var appt = await db.Appointments
            .Include(a => a.Customer).Include(a => a.Item)
            .FirstOrDefaultAsync(a => a.Id == id && a.BusinessId == BusinessId);
        if (appt is null) return NotFound();

        // The owner can cancel regardless of effective status (e.g. correcting a past/completed
        // appointment after the fact) -- unlike the customer-facing cancel endpoints. Only ask
        // WaitlistService to notify if it's actually still effectively CONFIRMED, since offering
        // an already-passed slot to the waitlist would be a meaningless notification.
        var wasPendingApproval = appt.PendingCancellationApproval;
        var stillConfirmed = AppointmentStatusHelper.EffectiveStatus(appt.Status, appt.Date, appt.EndTime) == "CONFIRMED";
        var offeredToWaitlist = req.NotifyWaitlist && stillConfirmed;
        if (!await cancellationService.CancelAsync(appt, notifyWaitlist: offeredToWaitlist))
            return Conflict(new { error = "This appointment can no longer be modified" });
        await db.SaveChangesAsync();
        // Not for a booking that's already over -- that's the owner correcting history.
        if (req.NotifyCustomer && stillConfirmed)
        {
            if (wasPendingApproval) await customerNotifier.QueueCancellationApproved(appt);
            else await customerNotifier.QueueCancelledByOwner(appt);
        }

        var verb = wasPendingApproval ? "Resolved cancellation request" : "Cancelled appointment";
        var suffix = offeredToWaitlist ? " (offered to waitlist)" : "";
        this.SetActivityDetail(
            $"{verb}{suffix}: {appt.Item.NameEn} for {ActivityDetailExtensions.FullName(appt.Customer.Name, appt.Customer.FamilyName)} on {appt.Date:yyyy-MM-dd} at {appt.StartTime}");

        return Ok(new { appt.Id, Status = appt.Status.ToString() });
    }

    // ─── Waitlist ───────────────────────────────────────────────────────────

    [HttpGet("appointments/{id}/waitlist")]
    public async Task<IActionResult> GetWaitlist(string id)
    {
        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id && a.BusinessId == BusinessId);
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

    // ─── Keep (decline a customer's cancellation request) ────────────────────────────────────

    [HttpPost("appointments/{id}/keep")]
    public async Task<IActionResult> KeepAppointment(string id, [FromQuery] bool notifyCustomer = true)
    {
        var appt = await db.Appointments.Include(a => a.Item)
            .FirstOrDefaultAsync(a => a.Id == id && a.BusinessId == BusinessId);
        if (appt is null) return NotFound();
        if (!appt.PendingCancellationApproval)
            return Conflict(new { error = "This appointment has no cancellation request" });

        // Claimed like a cancel: "keep" racing a "replace" both succeeded, and the customer was
        // told both "still on" and "cancelled" (QA run 3).
        if (!await cancellationService.TryClaimAsync(appt, AppointmentStatus.CONFIRMED, pendingApproval: false))
            return Conflict(new { error = "This appointment can no longer be modified" });
        appt.PendingCancellationApproval = false;
        await db.SaveChangesAsync();
        if (notifyCustomer) await customerNotifier.QueueCancellationDeclined(appt);

        this.SetActivityDetail(
            $"Declined cancellation request: {appt.Item.NameEn} on {appt.Date:yyyy-MM-dd} at {appt.StartTime}");
        return Ok(new { appt.Id });
    }

    // ─── Replace Customer (owner-cancel Option 3: give the slot to someone else) ──────────────

    [HttpPatch("appointments/{id}/customer")]
    public async Task<IActionResult> ReplaceCustomer(string id, [FromBody] ReplaceCustomerRequest req)
    {
        var appt = await db.Appointments.Include(a => a.Item)
            .FirstOrDefaultAsync(a => a.Id == id && a.BusinessId == BusinessId);
        if (appt is null) return NotFound();
        if (AppointmentStatusHelper.EffectiveStatus(appt.Status, appt.Date, appt.EndTime) != "CONFIRMED")
            return Conflict(new { error = "This appointment can no longer be modified" });

        Customer customer;
        if (!string.IsNullOrWhiteSpace(req.WaitlistEntryId))
        {
            var entry = await db.WaitlistEntries.Include(w => w.CustomerAccount)
                .FirstOrDefaultAsync(w => w.Id == req.WaitlistEntryId && w.AppointmentId == id && w.BusinessId == BusinessId);
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
        // The original customer keeps their own row, cancelled, and the new customer gets a fresh
        // one. Reassigning the row instead made it vanish from the original customer's list, and
        // their magic link (same row, same token) then showed the new customer's name and phone.
        // Notes, photo and recurring series belong to the original booking, so they aren't copied.
        var replacement = new Appointment
        {
            BusinessId = appt.BusinessId,
            CustomerId = customer.Id,
            ItemId = appt.ItemId,
            Date = appt.Date,
            StartTime = appt.StartTime,
            EndTime = appt.EndTime,
            Status = AppointmentStatus.CONFIRMED,
        };
        appt.Status = AppointmentStatus.CANCELLED;
        appt.PendingCancellationApproval = false;

        // Two saves in one transaction: the partial unique index allows one CONFIRMED row per
        // start time, so the original must be cancelled before the replacement is inserted.
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            // Claimed with one conditional UPDATE: a double-click or two staff members replacing
            // at once both passed the CONFIRMED check above, and the second insert hit the unique
            // index as a 500 (QA run 3). The row lock makes the second UPDATE wait, then match nothing.
            var claimed = await db.Appointments
                .Where(a => a.Id == appt.Id && a.Status == AppointmentStatus.CONFIRMED && a.PendingCancellationApproval == wasPendingApproval)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, AppointmentStatus.CANCELLED)
                    .SetProperty(a => a.PendingCancellationApproval, false));
            if (claimed == 0)
            {
                await tx.RollbackAsync();
                return Conflict(new { error = "This appointment can no longer be modified" });
            }
            await db.SaveChangesAsync();
            db.Appointments.Add(replacement);
            // Whoever is still waiting for this slot keeps waiting -- on the new appointment.
            var waiting = await db.WaitlistEntries
                .Where(w => w.AppointmentId == appt.Id && w.Status != WaitlistEntryStatus.RESOLVED)
                .ToListAsync();
            foreach (var w in waiting) w.AppointmentId = replacement.Id;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // Giving the slot back to the same customer isn't news to them.
        if (req.NotifyCustomer && customer.Id != appt.CustomerId)
        {
            if (wasPendingApproval) await customerNotifier.QueueCancellationApproved(appt);
            else await customerNotifier.QueueCancelledByOwner(appt);
            await customerNotifier.QueueBookedByOwner(replacement);
        }

        var verb = wasPendingApproval ? "Resolved cancellation request by replacing customer on appointment"
            : "Replaced customer on appointment";
        this.SetActivityDetail(
            $"{verb}: {appt.Item.NameEn} on {appt.Date:yyyy-MM-dd} at {appt.StartTime} — now {ActivityDetailExtensions.FullName(customer.Name, customer.FamilyName)}");

        return Ok(new { replacement.Id, replacement.CustomerId });
    }

    // Shared by CreateAppointment and ReplaceCustomer: resolve an existing customer by id, or
    // find or create one by phone -- an existing phone with a different name is a 409, see OwnerTypedCustomer.
    private async Task<(Customer? Customer, IActionResult? Error)> ResolveCustomer(
        string? customerId, string? customerName, string? customerPhone, string? customerFamilyName = null)
    {
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            var existing = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.BusinessId == BusinessId);
            return existing is null ? (null, NotFound(new { error = "Customer not found" })) : (existing, null);
        }

        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(customerPhone))
            return (null, BadRequest(new { error = "Customer name and phone are required" }));
        if (!PhoneNormalizer.IsValid(customerPhone))
            return (null, BadRequest(new { error = "Please enter a valid phone number" }));
        // Same stored format as every other path, so "050..." typed here finds the customer who
        // booked on the website or WhatsApp as "+97250..." instead of creating a duplicate.
        customerPhone = PhoneNormalizer.Normalize(customerPhone);

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.BusinessId == BusinessId && c.Phone == customerPhone);
        // Link to an existing CustomerAccount by phone, same as the public booking flow does from
        // its JWT -- otherwise an appointment the owner types in by hand for an already-registered
        // customer never shows up in that customer's own "My Bookings" until they book/reschedule
        // with this business themselves.
        var customerAccountId = (await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Phone == customerPhone))?.Id;
        if (customer is null)
        {
            customer = new Customer { Name = customerName, FamilyName = customerFamilyName ?? "", Phone = customerPhone, BusinessId = BusinessId, CustomerAccountId = customerAccountId };
            db.Customers.Add(customer);
        }
        else
        {
            if (OwnerTypedCustomer.MatchExisting(customer, customerName, customerFamilyName) is { } conflict)
                return (null, Conflict(new { error = conflict }));
            if (customerAccountId is not null) customer.CustomerAccountId = customerAccountId;
        }
        return (customer, null);
    }

    // Replace-from-waitlist knows the customer's real account already (phone, name, family
    // name), unlike ResolveCustomer's typed-in path -- so upsert by phone the same way, but also
    // link CustomerAccountId and set FamilyName, which the typed-in path has no way to know.
    private async Task<Customer> ResolveCustomerFromAccount(CustomerAccount account)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.BusinessId == BusinessId && c.Phone == account.Phone);
        if (customer is null)
        {
            customer = new Customer
            {
                Name = account.Name, FamilyName = account.FamilyName, Phone = account.Phone,
                BusinessId = BusinessId, CustomerAccountId = account.Id,
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

    // ─── Reviews ────────────────────────────────────────────────────────────

    [HttpGet("reviews")]
    public async Task<IActionResult> GetReviews()
    {
        var rows = await db.Reviews
            .Where(r => r.BusinessId == BusinessId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                r.Id, r.Rating, r.Comment, r.CreatedAt, r.UpdatedAt, r.IsHidden, r.OwnerReply, r.OwnerRepliedAt,
                r.CustomerAccount.Name, r.CustomerAccount.FamilyName,
                ItemName = r.Appointment.Item.NameEn,
            })
            .ToListAsync();

        var dtos = rows.Select(r => new AdminReviewDto(
            r.Id, r.Rating, r.Comment, $"{r.Name} {r.FamilyName}".Trim(), r.ItemName,
            r.CreatedAt, r.UpdatedAt, r.IsHidden, r.OwnerReply, r.OwnerRepliedAt));

        return Ok(dtos);
    }

    [HttpPost("reviews/{id}/reply")]
    public async Task<IActionResult> ReplyToReview(string id, [FromBody] OwnerReplyRequest req)
    {
        var reply = req.Reply?.Trim();
        if (string.IsNullOrEmpty(reply)) return BadRequest(new { error = "Reply cannot be empty" });
        if (reply.Length > 1000) return BadRequest(new { error = "Reply must be 1000 characters or fewer" });

        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == id && r.BusinessId == BusinessId);
        if (review is null) return NotFound(new { error = "Not found" });

        review.OwnerReply = reply;
        review.OwnerRepliedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        this.SetActivityDetail("Replied to a customer review");
        return Ok(new { review.Id, review.OwnerReply, review.OwnerRepliedAt });
    }

    [HttpDelete("reviews/{id}/reply")]
    public async Task<IActionResult> DeleteReviewReply(string id)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == id && r.BusinessId == BusinessId);
        if (review is null) return NotFound(new { error = "Not found" });

        review.OwnerReply = null;
        review.OwnerRepliedAt = null;
        await db.SaveChangesAsync();

        this.SetActivityDetail("Removed a reply to a customer review");
        return Ok(new { ok = true });
    }
}

// NotifyCustomer: tell the customer on WhatsApp (the business needs a linked number).
public record UpdateStatusRequest(string Status, bool NotifyWaitlist = false, bool NotifyCustomer = true);
