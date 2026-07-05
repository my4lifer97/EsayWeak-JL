namespace BarberSaas.Api.DTOs;

public record RegisterRequest(string Name, string Email, string Password, string Slug);
public record LoginRequest(string Email, string Password);
public record LoginResponse(string Token, string Id, string Name, string Email, string Slug);

public record ServiceDto(string Id, string BarberId, string NameEn, string NameAr, string NameHe, int DurationMinutes, decimal Price, bool IsActive);
public record CreateServiceRequest(string NameEn, string NameAr, string NameHe, int DurationMinutes, decimal Price);

public record WorkingHoursDto(string? Id, int DayOfWeek, string StartTime, string EndTime, bool IsActive);
public record BreakDto(string Id, int DayOfWeek, string StartTime, string EndTime);
public record CreateBreakRequest(int DayOfWeek, string StartTime, string EndTime);
public record BlockedSlotDto(string Id, string Date, string? StartTime, string? EndTime, string? Reason);
public record CreateBlockedSlotRequest(string Date, string? StartTime, string? EndTime, string? Reason);

public record SettingsDto(
    string Id, string Name, string Email, string Slug, string? Phone,
    string? Description, string? Logo, string Language, string? TwilioNumber, string? TwilioSid,
    DateTime TrialEndsAt, string SubscriptionStatus,
    int? MaxBookingsPerDay, int? MaxBookingsPerWeek);

public record UpdateSettingsRequest(
    string? Name, string? Phone, string? Description, string? Language,
    string? TwilioNumber, string? TwilioSid, string? TwilioToken,
    int? MaxBookingsPerDay, int? MaxBookingsPerWeek);

public record BookAppointmentRequest(
    string ServiceId, string Date, string StartTime,
    string CustomerName, string CustomerPhone, string? Notes);

public record BookAppointmentResponse(string AppointmentId, string CancelToken);

public record TimeSlot(string Start, string End);

public record AvailabilityResponse(List<TimeSlot> Slots);

public record AppointmentDetailDto(
    string Id, string BarberId, string CustomerId, string ServiceId,
    string Date, string StartTime, string EndTime, string? Notes,
    string Status, bool ReminderSent, string CancelToken, DateTime CreatedAt,
    CustomerSummary Customer, ServiceSummary Service, BarberSummary Barber);

public record CustomerSummary(string Id, string Name, string FamilyName, string Phone);
public record ServiceSummary(string Id, string NameEn, string NameAr, string NameHe, int DurationMinutes, decimal Price);
public record BarberSummary(string Name, string Slug, string Language);

public record DashboardAppointmentDto(
    string Id, string Date, string StartTime, string EndTime,
    string Status, string? Notes, CustomerSummary Customer, ServiceSummary Service, decimal Price);

public record ScheduleResponse(
    List<WorkingHoursDto> WorkingHours,
    List<BreakDto> Breaks,
    List<BlockedSlotDto> BlockedSlots);

public record PublicBarberDto(
    string Slug, string Name, string? Description, string? Logo,
    string Language, bool IsRTL, int[] ActiveDays, List<ServiceDto> Services, bool IsFollowed);

public record BarberSearchResultDto(string Slug, string Name, string? Description, string? Logo, string Language, bool IsFollowed);

public record CustomerAppointmentDto(
    string Id, string BarberSlug, string BarberName, string Date, string StartTime, string EndTime,
    string? Notes, string Status, string CancelToken, ServiceSummary Service);
