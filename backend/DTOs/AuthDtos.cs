namespace BarberSaas.Api.DTOs;

public record RegisterRequest(string Name, string Email, string Password, string Slug, string? BusinessTypeId = null);
public record LoginRequest(string Email, string Password);
public record LoginResponse(string Token, string Id, string Name, string Email, string Slug, bool MustChangePassword = false);
public record ChangePasswordRequest(string NewPassword);
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
    // WhatsAppNumber is read-only here -- assigned automatically once the platform admin links a
    // number by QR code (see UpdateSettingsRequest, which omits it).
    string? Description, string? Logo, string Language, string? WhatsAppNumber,
    DateTime TrialEndsAt, string SubscriptionStatus,
    int? MaxBookingsPerDay, int? MaxBookingsPerWeek, bool WaitlistEnabled, bool RequireApprovalOnCustomerCancel,
    bool ChatbotEnabled,
    string? ChatbotWelcomeMessageEn, string? ChatbotWelcomeMessageAr, string? ChatbotWelcomeMessageHe,
    string? ChatbotConfirmationMessageEn, string? ChatbotConfirmationMessageAr, string? ChatbotConfirmationMessageHe,
    string? ChatbotFinalMessageEn, string? ChatbotFinalMessageAr, string? ChatbotFinalMessageHe,
    string? ChatbotDefaultLanguage,
    bool ChatbotInquiryEnabled, bool InquiryNotifyViaWhatsApp, string? InquiryWhatsAppNumber,
    bool InquiryNotifyViaEmail, string? InquiryEmail,
    string? City, string? AddressLine, string? MapUrl, bool IsListed);

public record UpdateSettingsRequest(
    string? Name, string? Phone, string? Description, string? Language,
    int? MaxBookingsPerDay, int? MaxBookingsPerWeek, bool WaitlistEnabled = false,
    bool RequireApprovalOnCustomerCancel = false,
    bool ChatbotEnabled = true,
    string? ChatbotWelcomeMessageEn = null, string? ChatbotWelcomeMessageAr = null, string? ChatbotWelcomeMessageHe = null,
    string? ChatbotConfirmationMessageEn = null, string? ChatbotConfirmationMessageAr = null, string? ChatbotConfirmationMessageHe = null,
    string? ChatbotFinalMessageEn = null, string? ChatbotFinalMessageAr = null, string? ChatbotFinalMessageHe = null,
    string? ChatbotDefaultLanguage = null,
    bool ChatbotInquiryEnabled = false, bool InquiryNotifyViaWhatsApp = false, string? InquiryWhatsAppNumber = null,
    bool InquiryNotifyViaEmail = false, string? InquiryEmail = null,
    // The settings form always submits every field, so (like WaitlistEnabled) these are assigned
    // unconditionally rather than treated as "omitted when null". IsListed defaults true to match
    // Business.IsListed's default.
    string? City = null, string? AddressLine = null, string? MapUrl = null, bool IsListed = true);

public record ChatbotInquiryDto(string Id, string CustomerPhone, string? CustomerName, string Message, bool IsRead, DateTime CreatedAt);

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
    bool WaitlistEnabled,
    string? City, string? AddressLine, string? MapUrl, int RatingCount, double RatingAverage);

// Shared by BusinessesController.Search (the public directory) and .GetFollowed (the customer's
// followed list). BusinessTypeKey/City/rating/follower fields back the discovery result cards.
public record BusinessSearchResultDto(
    string Slug, string Name, string? Description, string? Logo, string Language, bool IsFollowed,
    string? BusinessTypeKey, string? City, double RatingAverage, int RatingCount, int FollowerCount);

public record CustomerAppointmentDto(
    string Id, string BusinessSlug, string BusinessName, string Date, string StartTime, string EndTime,
    string? Notes, string Status, string CancelToken, ItemSummary Item, string? PhotoUrl);

public record UpdateAppointmentPhotoRequest(string? GalleryPhotoId, string? CustomerPhotoUrl);

public record PlatformAdminBootstrapRequest(string Email, string Password, string Name);
public record PlatformAdminLoginRequest(string Email, string Password);
public record PlatformAdminLoginResponse(string Token, string Id, string Name, string Email);

public record PlatformAdminBusinessSummaryDto(string Id, string Name, string Email, string Slug, string SubscriptionStatus);
public record PlatformAdminBusinessDetailDto(
    string Id, string Name, string Email, string Slug, string? Phone, string? Username,
    DateTime TrialEndsAt, string SubscriptionStatus, DateTime CreatedAt, string? WhatsAppNumber, bool IsDisabled);

public record WhatsAppLinkStatusDto(string State, string? Qr, string? PhoneNumber);

public record EmailOwnerRequest(string Subject, string Body);

public record PlatformAdminCustomerSummaryDto(string Id, string Name, string FamilyName, string Phone);
public record PlatformAdminCustomerDetailDto(string Id, string Name, string FamilyName, string Phone, DateTime CreatedAt);

public record PlatformAdminImpersonateResponse(string Token);

public record PlatformAdminActivityLogDto(
    string Id, string Action, string Description, string Method, string Path,
    int StatusCode, string? IpAddress, string? UserAgent, DateTime CreatedAt, bool Impersonated);

public record BusinessTypeDto(string Id, string Key, string DisplayNameEn, string DisplayNameAr, string DisplayNameHe);

public record CreateBusinessOwnerRequestRequest(
    string BusinessName, string OwnerFirstName, string OwnerFamilyName, string Email, string Phone,
    string BusinessTypeId, string? BusinessDescription, string? SystemNeeds, string? Code);

public record SendBusinessOwnerRequestEmailCodeRequest(string Email);

public record BusinessOwnerRequestDto(
    string Id, string BusinessName, string OwnerFirstName, string OwnerFamilyName, string Email, string Phone,
    string? BusinessTypeId, string? BusinessTypeName, string? BusinessDescription, string? SystemNeeds,
    string Status, string? RejectionNote,
    DateTime CreatedAt, DateTime? ReviewedAt, string? CreatedBusinessSlug, string? CreatedUsername);

public record ApproveBusinessOwnerRequestRequest(string Slug, bool Silent = false);
public record ApproveBusinessOwnerRequestResponse(
    string BusinessId, string Slug, string Username, string TempPassword, bool EmailSent);
public record RejectBusinessOwnerRequestRequest(string? Note);

public record PlatformAdminResetPasswordRequest(bool Temporary, string? NewPassword, bool Silent = false);
public record PlatformAdminResetPasswordResponse(string? TempPassword, bool EmailSent);

// ---- Reviews & discovery ----

// Generic paged envelope. First paginated endpoint in the codebase -- kept minimal.
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total, bool HasMore);

public record BusinessRatingDto(int Count, double Average);

// What the public sees on a business page. ReviewerName is a first name + family initial.
public record PublicReviewDto(
    string Id, int Rating, string? Comment, string ReviewerName, DateTime CreatedAt,
    string? OwnerReply, DateTime? OwnerRepliedAt);

public record PublicReviewListDto(BusinessRatingDto Rating, PagedResult<PublicReviewDto> Reviews);

// The customer's own review (author view -- editable).
public record ReviewDto(
    string Id, string BusinessSlug, int Rating, string? Comment, DateTime CreatedAt, DateTime UpdatedAt,
    string? OwnerReply, DateTime? OwnerRepliedAt);

public record ReviewEligibilityDto(bool CanReview, bool AlreadyReviewed, ReviewDto? Review);

public record CreateReviewRequest(string BusinessSlug, int Rating, string? Comment);
public record UpdateReviewRequest(int Rating, string? Comment);
public record OwnerReplyRequest(string Reply);

// Owner-facing (admin) and platform-admin moderation view.
public record AdminReviewDto(
    string Id, int Rating, string? Comment, string ReviewerName, string? ItemName,
    DateTime CreatedAt, DateTime UpdatedAt, bool IsHidden, string? OwnerReply, DateTime? OwnerRepliedAt);
