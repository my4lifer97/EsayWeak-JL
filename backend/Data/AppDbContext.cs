using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Barber> Barbers => Set<Barber>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<ServiceGalleryPhoto> ServiceGalleryPhotos => Set<ServiceGalleryPhoto>();
    public DbSet<WorkingHours> WorkingHours => Set<WorkingHours>();
    public DbSet<Break> Breaks => Set<Break>();
    public DbSet<BlockedSlot> BlockedSlots => Set<BlockedSlot>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<WhatsAppBookingToken> WhatsAppBookingTokens => Set<WhatsAppBookingToken>();
    public DbSet<WhatsAppConversationState> WhatsAppConversationStates => Set<WhatsAppConversationState>();
    public DbSet<BarberEmailOtp> BarberEmailOtps => Set<BarberEmailOtp>();
    public DbSet<BarberPasswordResetOtp> BarberPasswordResetOtps => Set<BarberPasswordResetOtp>();
    public DbSet<RecurringSeries> RecurringSeries => Set<RecurringSeries>();
    public DbSet<RecurringSkip> RecurringSkips => Set<RecurringSkip>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Barber>()
            .HasIndex(x => x.Email).IsUnique();
        b.Entity<Barber>()
            .HasIndex(x => x.Slug).IsUnique();

        b.Entity<WorkingHours>()
            .HasIndex(x => new { x.BarberId, x.DayOfWeek }).IsUnique();

        b.Entity<Customer>()
            .HasIndex(x => new { x.BarberId, x.Phone }).IsUnique();

        b.Entity<Appointment>()
            .HasIndex(x => x.CancelToken).IsUnique();

        b.Entity<CustomerAccount>()
            .HasIndex(x => x.Phone).IsUnique();

        b.Entity<Follow>()
            .HasIndex(x => new { x.CustomerAccountId, x.BarberId }).IsUnique();

        b.Entity<WhatsAppBookingToken>()
            .HasIndex(x => x.ExpiresAt);

        b.Entity<WhatsAppConversationState>()
            .HasIndex(x => new { x.BarberId, x.Phone }).IsUnique();

        b.Entity<BarberEmailOtp>()
            .HasIndex(x => new { x.Email, x.CreatedAt });

        b.Entity<BarberPasswordResetOtp>()
            .HasIndex(x => new { x.Email, x.CreatedAt });

        b.Entity<RecurringSeries>()
            .HasIndex(x => new { x.BarberId, x.IsActive });

        b.Entity<Appointment>()
            .HasIndex(x => new { x.RecurringSeriesId, x.Date });

        b.Entity<WaitlistEntry>()
            .HasIndex(x => new { x.AppointmentId, x.CustomerAccountId }).IsUnique();

        b.Entity<PlatformAdmin>()
            .HasIndex(x => x.Email).IsUnique();

        b.Entity<ActivityLog>()
            .HasIndex(x => new { x.BarberId, x.CreatedAt });
        b.Entity<ActivityLog>()
            .HasIndex(x => new { x.CustomerAccountId, x.CreatedAt });

        // Closes a pre-existing TOCTOU gap (check-then-insert, no DB-level guard): only one
        // CONFIRMED appointment may occupy a given barber/date/start-time slot. Filtered so
        // cancelled/completed history never collides with a later booking of the same slot.
        b.Entity<Appointment>()
            .HasIndex(x => new { x.BarberId, x.Date, x.StartTime })
            .IsUnique()
            .HasFilter("\"Status\" = 'CONFIRMED'")
            .HasDatabaseName("IX_Appointments_BarberId_Date_StartTime_Confirmed");

        b.Entity<Service>()
            .Property(x => x.Price)
            .HasColumnType("decimal(10,2)");

        b.Entity<BlockedSlot>()
            .Property(x => x.Date)
            .HasColumnType("date");

        b.Entity<Appointment>()
            .Property(x => x.Date)
            .HasColumnType("date");

        b.Entity<RecurringSeries>()
            .Property(x => x.StartDate)
            .HasColumnType("date");
        b.Entity<RecurringSeries>()
            .Property(x => x.EndDate)
            .HasColumnType("date");
        b.Entity<RecurringSeries>()
            .Property(x => x.LastGeneratedThrough)
            .HasColumnType("date");
        b.Entity<RecurringSkip>()
            .Property(x => x.Date)
            .HasColumnType("date");

        b.Entity<Barber>()
            .Property(x => x.Language)
            .HasConversion<string>();
        b.Entity<Barber>()
            .Property(x => x.SubscriptionStatus)
            .HasConversion<string>();
        b.Entity<Appointment>()
            .Property(x => x.Status)
            .HasConversion<string>();
        b.Entity<Service>()
            .Property(x => x.PhotoMode)
            .HasConversion<string>();
        b.Entity<WaitlistEntry>()
            .Property(x => x.Status)
            .HasConversion<string>();
        // Explicit DB-level default (not just the C# property initializer, which only applies to
        // objects EF constructs itself) -- without this, EF's migration scaffolding backfills
        // existing rows with default(bool) = false when the column is added, which would silently
        // disable the chatbot for every barber that already existed before this feature shipped.
        b.Entity<Barber>()
            .Property(x => x.ChatbotEnabled)
            .HasDefaultValue(true);

        b.Entity<Service>()
            .HasOne(x => x.Barber).WithMany(x => x.Services)
            .HasForeignKey(x => x.BarberId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ServiceGalleryPhoto>()
            .HasOne(x => x.Service).WithMany(x => x.GalleryPhotos)
            .HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WorkingHours>()
            .HasOne(x => x.Barber).WithMany(x => x.WorkingHours)
            .HasForeignKey(x => x.BarberId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Break>()
            .HasOne(x => x.Barber).WithMany(x => x.Breaks)
            .HasForeignKey(x => x.BarberId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<BlockedSlot>()
            .HasOne(x => x.Barber).WithMany(x => x.BlockedSlots)
            .HasForeignKey(x => x.BarberId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Customer>()
            .HasOne(x => x.Barber).WithMany(x => x.Customers)
            .HasForeignKey(x => x.BarberId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Customer>()
            .HasOne(x => x.CustomerAccount).WithMany(x => x.Profiles)
            .HasForeignKey(x => x.CustomerAccountId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Follow>()
            .HasOne(x => x.CustomerAccount).WithMany(x => x.Follows)
            .HasForeignKey(x => x.CustomerAccountId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Follow>()
            .HasOne(x => x.Barber).WithMany(x => x.Follows)
            .HasForeignKey(x => x.BarberId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Appointment>()
            .HasOne(x => x.Barber).WithMany(x => x.Appointments)
            .HasForeignKey(x => x.BarberId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Appointment>()
            .HasOne(x => x.Customer).WithMany(x => x.Appointments)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Appointment>()
            .HasOne(x => x.Service).WithMany(x => x.Appointments)
            .HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<RecurringSeries>()
            .HasOne(x => x.Barber).WithMany(x => x.RecurringSeries)
            .HasForeignKey(x => x.BarberId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RecurringSeries>()
            .HasOne(x => x.Customer).WithMany(x => x.RecurringSeries)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RecurringSeries>()
            .HasOne(x => x.Service).WithMany(x => x.RecurringSeries)
            .HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RecurringSkip>()
            .HasOne(x => x.RecurringSeries).WithMany(x => x.Skips)
            .HasForeignKey(x => x.RecurringSeriesId).OnDelete(DeleteBehavior.Cascade);
        // SetNull (not Restrict/Cascade): deleting a series must never touch already-generated
        // Appointment rows -- per-occurrence independence -- it only unlinks them.
        b.Entity<Appointment>()
            .HasOne(x => x.RecurringSeries).WithMany(x => x.Appointments)
            .HasForeignKey(x => x.RecurringSeriesId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<WhatsAppBookingToken>()
            .HasOne(x => x.Barber).WithMany()
            .HasForeignKey(x => x.BarberId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WhatsAppBookingToken>()
            .HasOne(x => x.Service).WithMany()
            .HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WhatsAppConversationState>()
            .HasOne(x => x.Barber).WithMany()
            .HasForeignKey(x => x.BarberId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<WaitlistEntry>()
            .HasOne(x => x.Appointment).WithMany(x => x.WaitlistEntries)
            .HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WaitlistEntry>()
            .HasOne(x => x.Barber).WithMany()
            .HasForeignKey(x => x.BarberId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WaitlistEntry>()
            .HasOne(x => x.CustomerAccount).WithMany(x => x.WaitlistEntries)
            .HasForeignKey(x => x.CustomerAccountId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<ActivityLog>()
            .HasOne(x => x.Barber).WithMany()
            .HasForeignKey(x => x.BarberId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ActivityLog>()
            .HasOne(x => x.CustomerAccount).WithMany()
            .HasForeignKey(x => x.CustomerAccountId).OnDelete(DeleteBehavior.Cascade);
        // SetNull (not Cascade): deleting the admin account that performed an impersonation
        // must never destroy the target account's own history -- only unlink the attribution.
        b.Entity<ActivityLog>()
            .HasOne(x => x.ImpersonatedByPlatformAdmin).WithMany()
            .HasForeignKey(x => x.ImpersonatedByPlatformAdminId).OnDelete(DeleteBehavior.SetNull);
    }
}
