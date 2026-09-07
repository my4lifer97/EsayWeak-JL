using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

public enum Language { EN, AR, HE }
public enum SubStatus { TRIAL, ACTIVE, EXPIRED }
public enum AppointmentStatus { CONFIRMED, CANCELLED, COMPLETED }
public enum ItemPhotoMode { None, OwnerGallery, CustomerUpload, Both }
public enum WaitlistEntryStatus { WAITING, NOTIFIED, RESOLVED }
// Coarse business-level classification. Appointment: scheduling only. Showcase: a
// product/service catalog with no booking. Both: mixes bookable and non-bookable items.
public enum BusinessModel { Appointment, Showcase, Both }

public class Business
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
    // Which of the platform's own Twilio WhatsApp senders this business's chatbot uses -- assigned
    // by the platform admin (see PlatformAdminController.SetTwilioNumber), not self-configured by
    // the business. Credentials for sending/validating live in one platform-owned Twilio account
    // (config: Twilio:AccountSid/AuthToken), not per-business -- see TwilioWhatsAppSender.
    public string? TwilioNumber { get; set; }
    public DateTime TrialEndsAt { get; set; }
    public SubStatus SubscriptionStatus { get; set; } = SubStatus.TRIAL;
    // Cardcom's reusable charge token (from LowProfile/Create with Operation=ChargeAndCreateToken).
    // Null until the business's first successful payment.
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
    // false, WhatsAppController sends no automated reply at all -- the business wants to answer
    // messages themselves instead. The two message fields are free text the business writes in
    // their own language; null means "use the built-in default text" (see I18nService).
    public bool ChatbotEnabled { get; set; } = true;
    public string? ChatbotWelcomeMessage { get; set; }
    public string? ChatbotConfirmationMessage { get; set; }

    // Nullable so existing/legacy rows can be backfilled to a seeded lookup row rather than
    // requiring every tenant to have one from day one.
    public string? BusinessTypeId { get; set; }
    public BusinessModel BusinessModel { get; set; } = BusinessModel.Appointment;

    public BusinessTypeDefinition? BusinessType { get; set; }

    // True for an account created by PlatformAdminController.ApproveBusinessOwnerRequest with a
    // system-generated temp password -- RequirePasswordChangeFilter blocks every BusinessOnly
    // action except AuthController.ChangePassword until this is cleared.
    public bool MustChangePassword { get; set; } = false;

    public ICollection<Item> Items { get; set; } = [];
    public ICollection<WorkingHours> WorkingHours { get; set; } = [];
    public ICollection<Break> Breaks { get; set; } = [];
    public ICollection<BlockedSlot> BlockedSlots { get; set; } = [];
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<Customer> Customers { get; set; } = [];
    public ICollection<Follow> Follows { get; set; } = [];
    public ICollection<RecurringSeries> RecurringSeries { get; set; } = [];
}

// Extensible lookup of business verticals ("business", "dentist", "car_dealer", ...) -- adding a
// new vertical is a data insert here, not a deploy, unlike a hardcoded enum.
public class BusinessTypeDefinition
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Key { get; set; } = "";
    public string DisplayNameEn { get; set; } = "";
    public string DisplayNameAr { get; set; } = "";
    public string DisplayNameHe { get; set; } = "";
    public bool IsActive { get; set; } = true;

    public ICollection<Business> Businesses { get; set; } = [];
}

public class Item
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BusinessId { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameHe { get; set; } = "";
    // Null when IsBookable is false -- a showcase-only item has no appointment duration.
    public int? DurationMinutes { get; set; }
    // Null means no price shown (e.g. "contact for price") -- meaningful for showcase items.
    public decimal? Price { get; set; }
    public bool IsActive { get; set; } = true;
    public ItemPhotoMode PhotoMode { get; set; } = ItemPhotoMode.None;
    // Whether this item can be booked as an appointment. A Both-model business can mix bookable
    // and non-bookable items, so this lives per-item rather than only on Business.BusinessModel.
    public bool IsBookable { get; set; } = true;

    public Business Business { get; set; } = null!;
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<ItemGalleryPhoto> GalleryPhotos { get; set; } = [];
    public ICollection<RecurringSeries> RecurringSeries { get; set; } = [];
}

public class ItemGalleryPhoto
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ItemId { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Item Item { get; set; } = null!;
}

public class WorkingHours
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BusinessId { get; set; } = "";
    public int DayOfWeek { get; set; }
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";
    public bool IsActive { get; set; } = true;

    public Business Business { get; set; } = null!;
}

public class Break
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BusinessId { get; set; } = "";
    public int DayOfWeek { get; set; }
    public string StartTime { get; set; } = "";
    public string EndTime { get; set; } = "";

    public Business Business { get; set; } = null!;
}

public class BlockedSlot
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BusinessId { get; set; } = "";
    public DateTime Date { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? Reason { get; set; }

    public Business Business { get; set; } = null!;
}

public class Customer
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string BusinessId { get; set; } = "";
    public string? CustomerAccountId { get; set; }

    public Business Business { get; set; } = null!;
    public CustomerAccount? CustomerAccount { get; set; }
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<RecurringSeries> RecurringSeries { get; set; } = [];
}

public class Appointment
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BusinessId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string ItemId { get; set; } = "";
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
    // Set when a customer cancels and the business has RequireApprovalOnCustomerCancel on -- Status
    // deliberately stays CONFIRMED (so the slot keeps blocking availability/booking exactly as
    // before, no changes needed there) until the owner picks what happens to it.
    public bool PendingCancellationApproval { get; set; } = false;

    public Business Business { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public RecurringSeries? RecurringSeries { get; set; }
    public ICollection<WaitlistEntry> WaitlistEntries { get; set; } = [];
}

public class RecurringSeries
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BusinessId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public string ItemId { get; set; } = "";
    public int DayOfWeek { get; set; }
    public string StartTime { get; set; } = "";
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    // Rolling cursor: last calendar date the generator has evaluated (created OR skipped) for this rule.
    public DateTime? LastGeneratedThrough { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public Item Item { get; set; } = null!;
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
    public string BusinessId { get; set; } = "";
    public string CustomerAccountId { get; set; } = "";
    public WaitlistEntryStatus Status { get; set; } = WaitlistEntryStatus.WAITING;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? NotifiedAt { get; set; }

    public Appointment Appointment { get; set; } = null!;
    public Business Business { get; set; } = null!;
    public CustomerAccount CustomerAccount { get; set; } = null!;
}
