using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

public enum Language { EN, AR, HE }
public enum SubStatus { TRIAL, ACTIVE, EXPIRED }
public enum AppointmentStatus { CONFIRMED, CANCELLED, COMPLETED }
public enum ServicePhotoMode { None, OwnerGallery, CustomerUpload, Both }
public enum WaitlistEntryStatus { WAITING, NOTIFIED, RESOLVED }

public class Barber
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? Phone { get; set; }
    public string Slug { get; set; } = "";
    public string? Logo { get; set; }
    public string? Description { get; set; }
    public Language Language { get; set; } = Language.EN;
    // Which of the platform's own Twilio WhatsApp senders this barber's chatbot uses -- assigned
    // by the platform admin (see PlatformAdminController.SetTwilioNumber), not self-configured by
    // the barber. Credentials for sending/validating live in one platform-owned Twilio account
    // (config: Twilio:AccountSid/AuthToken), not per-barber -- see TwilioWhatsAppSender.
    public string? TwilioNumber { get; set; }
    public DateTime TrialEndsAt { get; set; }
    public SubStatus SubscriptionStatus { get; set; } = SubStatus.TRIAL;
    // Cardcom's reusable charge token (from LowProfile/Create with Operation=ChargeAndCreateToken).
    // Null until the barber's first successful payment.
    public string? CardcomToken { get; set; }
    // Latest Cardcom LowProfileId whose result has been processed -- webhook idempotency guard,
    // since Cardcom's webhook may redeliver the same notification.
    public string? CardcomLastLowProfileId { get; set; }
    // Drives the recurring-charge cron job (GET /api/cron/charge-subscriptions): null until the
    // first successful charge, then bumped by exactly 1 month on each successful recurring charge.
    public DateTime? CardcomNextChargeAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool EmailVerified { get; set; } = false;

    // Null means unlimited. Enforced per-customer (matched by phone) in BookingController.
    public int? MaxBookingsPerDay { get; set; }
    public int? MaxBookingsPerWeek { get; set; }

    public bool WaitlistEnabled { get; set; } = false;
    // When true, a customer cancelling doesn't finalize the cancellation immediately -- the slot
    // is frozen (Appointment.PendingCancellationApproval) and the owner gets a WhatsApp message
    // to decide (offer to waitlist / cancel silently / replace customer) via the dashboard.
    public bool RequireApprovalOnCustomerCancel { get; set; } = false;

    // WhatsApp chatbot customization ("Simple Mode" per the product spec). When ChatbotEnabled is
    // false, WhatsAppController sends no automated reply at all -- the barber wants to answer
    // messages themselves instead. The two message fields are free text the barber writes in
    // their own language; null means "use the built-in default text" (see I18nService).
    public bool ChatbotEnabled { get; set; } = true;
    public string? ChatbotWelcomeMessage { get; set; }
    public string? ChatbotConfirmationMessage { get; set; }

    public ICollection<Service> Services { get; set; } = [];
    public ICollection<WorkingHours> WorkingHours { get; set; } = [];
    public ICollection<Break> Breaks { get; set; } = [];
    public ICollection<BlockedSlot> BlockedSlots { get; set; } = [];
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<Customer> Customers { get; set; } = [];
    public ICollection<Follow> Follows { get; set; } = [];
    public ICollection<RecurringSeries> RecurringSeries { get; set; } = [];
}

public class Service
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BarberId { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameHe { get; set; } = "";
    public int DurationMinutes { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public ServicePhotoMode PhotoMode { get; set; } = ServicePhotoMode.None;

    public Barber Barber { get; set; } = null!;
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<ServiceGalleryPhoto> GalleryPhotos { get; set; } = [];
    public ICollection<RecurringSeries> RecurringSeries { get; set; } = [];
}

public class ServiceGalleryPhoto
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ServiceId { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Service Service { get; set; } = null!;
}

public class WorkingHours
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BarberId { get; set; } = "";
    public int DayOfWeek { get; set; }
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    public bool IsActive { get; set; } = true;

    public Barber Barber { get; set; } = null!;
}

public class Break
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BarberId { get; set; } = "";
    public int DayOfWeek { get; set; }
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";

    public Barber Barber { get; set; } = null!;
}

public class BlockedSlot
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BarberId { get; set; } = "";
    public DateTime Date { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? Reason { get; set; }

    public Barber Barber { get; set; } = null!;
}

public class Customer
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string BarberId { get; set; } = "";
    public string? CustomerAccountId { get; set; }

    public Barber Barber { get; set; } = null!;
    public CustomerAccount? CustomerAccount { get; set; }
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<RecurringSeries> RecurringSeries { get; set; } = [];
}

public class Appointment
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BarberId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string ServiceId { get; set; } = "";
    public DateTime Date { get; set; }
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    public string? Notes { get; set; }
    public string? PhotoUrl { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.CONFIRMED;
    public bool ReminderSent { get; set; } = false;
    public string CancelToken { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? RecurringSeriesId { get; set; }
    // Set when a customer cancels and the barber has RequireApprovalOnCustomerCancel on -- Status
    // deliberately stays CONFIRMED (so the slot keeps blocking availability/booking exactly as
    // before, no changes needed there) until the owner picks what happens to it.
    public bool PendingCancellationApproval { get; set; } = false;

    public Barber Barber { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public Service Service { get; set; } = null!;
    public RecurringSeries? RecurringSeries { get; set; }
    public ICollection<WaitlistEntry> WaitlistEntries { get; set; } = [];
}

public class RecurringSeries
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BarberId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string ServiceId { get; set; } = "";
    public int DayOfWeek { get; set; }
    public string StartTime { get; set; } = "";
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    // Rolling cursor: last calendar date the generator has evaluated (created OR skipped) for this rule.
    public DateTime? LastGeneratedThrough { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Barber Barber { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public Service Service { get; set; } = null!;
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<RecurringSkip> Skips { get; set; } = [];
}

public class RecurringSkip
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RecurringSeriesId { get; set; } = "";
    public DateTime Date { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public RecurringSeries RecurringSeries { get; set; } = null!;
}

public class WaitlistEntry
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AppointmentId { get; set; } = "";
    public string BarberId { get; set; } = "";
    public string CustomerAccountId { get; set; } = "";
    public WaitlistEntryStatus Status { get; set; } = WaitlistEntryStatus.WAITING;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? NotifiedAt { get; set; }

    public Appointment Appointment { get; set; } = null!;
    public Barber Barber { get; set; } = null!;
    public CustomerAccount CustomerAccount { get; set; } = null!;
}
