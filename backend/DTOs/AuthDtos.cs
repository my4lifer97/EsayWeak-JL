namespace BarberSaas.Api.DTOs;

public record RegisterRequest(string Name, string Email, string Password, string Slug);
public record LoginRequest(string Email, string Password);
public record LoginResponse(string Token, string Id, string Name, string Email, string Slug);
public record VerifyEmailRequest(string Email, string Code);
public record ResendVerificationRequest(string Email);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Code, string NewPassword);

public record ItemGalleryPhotoDto(string Id, string Url);
public record ItemDto(string Id, string BusinessId, string NameEn, string NameAr, string NameHe, int? DurationMinutes, decimal? Price, bool IsActive, string PhotoMode, bool IsBookable, List<ItemGalleryPhotoDto> GalleryPhotos);
public record CreateItemRequest(string NameEn, string NameAr, string NameHe, int? DurationMinutes, decimal? Price, string PhotoMode = "None", bool IsBookable = true);

public record WorkingHoursDto(string? Id, int DayOfWeek, string StartTime, string EndTime, bool IsActive);
public record BreakDto(string Id, int DayOfWeek, string StartTime, string EndTime);
public record CreateBreakRequest(int DayOfWeek, string StartTime, string EndTime);
public record BlockedSlotDto(string Id, string Date, string? StartTime, string? EndTime, string? Reason);
public record CreateBlockedSlotRequest(string Date, string? StartTime, string? EndTime, string? Reason);

public record SettingsDto(
    string Id, string Name, string Email, string Slug, string? Phone,
    // TwilioNumber is read-only here -- assigned by the platform admin, not settable by the
    // business (see UpdateSettingsRequest, which omits it).
    string? Description, string? Logo, string Language, string? TwilioNumber,
    DateTime TrialEndsAt, string SubscriptionStatus,
    int? MaxBookingsPerDay, int? MaxBookingsPerWeek, bool WaitlistEnabled, bool RequireApprovalOnCustomerCancel,
    bool ChatbotEnabled, string? ChatbotWelcomeMessage, string? ChatbotConfirmationMessage);

public record UpdateSettingsRequest(
    string? Name, string? Phone, string? Description, string? Language,
    int? MaxBookingsPerDay, int? MaxBookingsPerWeek, bool WaitlistEnabled = false,
    bool RequireApprovalOnCustomerCancel = false,
    bool ChatbotEnabled = true, string? ChatbotWelcomeMessage = null, string? ChatbotConfirmationMessage = null);

public record BookAppointmentRequest(
    string ItemId, string Date, string StartTime,
    string CustomerName, string CustomerPhone, string? Notes,
    string? GalleryPhotoId = null, string? CustomerPhotoUrl = null, string? CustomerFamilyName = null);

public record BookAppointmentResponse(string AppointmentId, string CancelToken);

public record CreateAdminAppointmentRequest(
    string? CustomerId, string? CustomerName, string? CustomerPhone,
    string ItemId, string Date, string StartTime, string? Notes,
    string? GalleryPhotoId = null, string? CustomerPhotoUrl = null, bool Force = false,
    string? CustomerFamilyName = null);

public record RecurringSkipDto(string Date, string Reason);
public record RecurringSeriesDto(
    string Id, CustomerSummary Customer, ItemSummary Item,
    int DayOfWeek, string StartTime, string? Notes, bool IsActive,
    string StartDate, string? EndDate, string? NextOccurrenceDate,
    List<RecurringSkipDto> RecentSkips);
public record CreateRecurringSeriesRequest(
    string? CustomerId, string? CustomerName, string? CustomerPhone,
    string ItemId, int DayOfWeek, string StartTime, string? Notes,
    string? StartDate = null, string? EndDate = null, string? CustomerFamilyName = null);

public record TimeSlot(string Start, string End);

public record AvailabilityResponse(List<TimeSlot> Slots);

public record SlotWithBookingInfoDto(string Start, string End, bool Available, string? AppointmentId);

public record FullAvailabilityResponse(List<SlotWithBookingInfoDto> Slots);

public record ReplaceCustomerRequest(string? CustomerId, string? CustomerName, string? CustomerPhone, string? WaitlistEntryId = null, string? CustomerFamilyName = null);

public record WaitlistEntrySummaryDto(string Id, string CustomerAccountId, string Name, string FamilyName, string Phone, string Status, DateTime CreatedAt);

public record AppointmentDetailDto(
    string Id, string BusinessId, string CustomerId, string ItemId,
    string Date, string StartTime, string EndTime, string? Notes,
    string Status, bool ReminderSent, string CancelToken, DateTime CreatedAt,
    CustomerSummary Customer, ItemSummary Item, BusinessSummary Business, string? PhotoUrl,
    string? RecurringSeriesId = null);

public record CustomerSummary(string Id, string Name, string FamilyName, string Phone);
public record ItemSummary(
    string Id, string NameEn, string NameAr, string NameHe, int? DurationMinutes, decimal? Price,
    string PhotoMode = "None", List<ItemGalleryPhotoDto>? GalleryPhotos = null, bool IsBookable = true);
public record BusinessSummary(string Name, string Slug, string Language);

public record DashboardAppointmentDto(
    string Id, string Date, string StartTime, string EndTime,
    string Status, string? Notes, CustomerSummary Customer, ItemSummary Item, decimal? Price, string? PhotoUrl,
    string? RecurringSeriesId = null, bool PendingCancellationApproval = false);

public record ScheduleResponse(
    List<WorkingHoursDto> WorkingHours,
    List<BreakDto> Breaks,
    List<BlockedSlotDto> BlockedSlots);

public record PublicBusinessDto(
    string Slug, string Name, string? Description, string? Logo,
    string Language, bool IsRTL, int[] ActiveDays, List<ItemDto> Items, bool IsFollowed,
    bool WaitlistEnabled);

public record BusinessSearchResultDto(string Slug, string Name, string? Description, string? Logo, string Language, bool IsFollowed);

public record CustomerAppointmentDto(
    string Id, string BusinessSlug, string BusinessName, string Date, string StartTime, string EndTime,
    string? Notes, string Status, string CancelToken, ItemSummary Item, string? PhotoUrl);

public record UpdateAppointmentPhotoRequest(string? GalleryPhotoId, string? CustomerPhotoUrl);

public record PlatformAdminBootstrapRequest(string Email, string Password, string Name);
public record PlatformAdminLoginRequest(string Email, string Password);
public record PlatformAdminLoginResponse(string Token, string Id, string Name, string Email);

public record PlatformAdminBusinessSummaryDto(string Id, string Name, string Email, string Slug, string SubscriptionStatus);
public record PlatformAdminBusinessDetailDto(
    string Id, string Name, string Email, string Slug, string? Phone,
    DateTime TrialEndsAt, string SubscriptionStatus, DateTime CreatedAt, string? TwilioNumber);

public record SetTwilioNumberRequest(string? TwilioNumber);

public record PlatformAdminCustomerSummaryDto(string Id, string Name, string FamilyName, string Phone);
public record PlatformAdminCustomerDetailDto(string Id, string Name, string FamilyName, string Phone, DateTime CreatedAt);

public record PlatformAdminImpersonateResponse(string Token);

public record PlatformAdminActivityLogDto(
    string Id, string Action, string Description, string Method, string Path,
    int StatusCode, string? IpAddress, DateTime CreatedAt, bool Impersonated);
