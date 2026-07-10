using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

public enum Language { EN, AR, HE }
public enum SubStatus { TRIAL, ACTIVE, EXPIRED }
public enum AppointmentStatus { CONFIRMED, CANCELLED, COMPLETED }

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
    public string? TwilioNumber { get; set; }
    public string? TwilioSid { get; set; }
    public string? TwilioToken { get; set; }
    public DateTime TrialEndsAt { get; set; }
    public SubStatus SubscriptionStatus { get; set; } = SubStatus.TRIAL;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool EmailVerified { get; set; } = false;

    // Null means unlimited. Enforced per-customer (matched by phone) in BookingController.
    public int? MaxBookingsPerDay { get; set; }
    public int? MaxBookingsPerWeek { get; set; }

    public ICollection<Service> Services { get; set; } = [];
    public ICollection<WorkingHours> WorkingHours { get; set; } = [];
    public ICollection<Break> Breaks { get; set; } = [];
    public ICollection<BlockedSlot> BlockedSlots { get; set; } = [];
    public ICollection<Appointment> Appointments { get; set; } = [];
    public ICollection<Customer> Customers { get; set; } = [];
    public ICollection<Follow> Follows { get; set; } = [];
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

    public Barber Barber { get; set; } = null!;
    public ICollection<Appointment> Appointments { get; set; } = [];
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
    public AppointmentStatus Status { get; set; } = AppointmentStatus.CONFIRMED;
    public bool ReminderSent { get; set; } = false;
    public string CancelToken { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Barber Barber { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public Service Service { get; set; } = null!;
}
