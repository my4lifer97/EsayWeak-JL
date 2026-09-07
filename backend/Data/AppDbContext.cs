using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<BusinessTypeDefinition> BusinessTypeDefinitions => Set<BusinessTypeDefinition>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemGalleryPhoto> ItemGalleryPhotos => Set<ItemGalleryPhoto>();
    public DbSet<WorkingHours> WorkingHours => Set<WorkingHours>();
    public DbSet<Break> Breaks => Set<Break>();
    public DbSet<BlockedSlot> BlockedSlots => Set<BlockedSlot>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<WhatsAppBookingToken> WhatsAppBookingTokens => Set<WhatsAppBookingToken>();
    public DbSet<WhatsAppConversationState> WhatsAppConversationStates => Set<WhatsAppConversationState>();
    public DbSet<BusinessEmailOtp> BusinessEmailOtps => Set<BusinessEmailOtp>();
    public DbSet<BusinessPasswordResetOtp> BusinessPasswordResetOtps => Set<BusinessPasswordResetOtp>();
    public DbSet<RecurringSeries> RecurringSeries => Set<RecurringSeries>();
    public DbSet<RecurringSkip> RecurringSkips => Set<RecurringSkip>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<BusinessOwnerRequest> BusinessOwnerRequests => Set<BusinessOwnerRequest>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Business>()
            .HasIndex(x => x.Email).IsUnique();
        b.Entity<Business>()
            .HasIndex(x => x.Slug).IsUnique();
        b.Entity<Business>()
            .HasIndex(x => x.BusinessTypeId);
        b.Entity<Business>()
            .HasIndex(x => x.BusinessModel);

        b.Entity<BusinessTypeDefinition>()
            .HasIndex(x => x.Key).IsUnique();

        b.Entity<WorkingHours>()
            .HasIndex(x => new { x.BusinessId, x.DayOfWeek }).IsUnique();

        b.Entity<Customer>()
            .HasIndex(x => new { x.BusinessId, x.Phone }).IsUnique();

        b.Entity<Appointment>()
            .HasIndex(x => x.CancelToken).IsUnique();

        b.Entity<CustomerAccount>()
            .HasIndex(x => x.Phone).IsUnique();

        b.Entity<Follow>()
            .HasIndex(x => new { x.CustomerAccountId, x.BusinessId }).IsUnique();

        b.Entity<WhatsAppBookingToken>()
            .HasIndex(x => x.ExpiresAt);

        b.Entity<WhatsAppConversationState>()
            .HasIndex(x => new { x.BusinessId, x.Phone }).IsUnique();

        b.Entity<BusinessEmailOtp>()
            .HasIndex(x => new { x.Email, x.CreatedAt });

        b.Entity<BusinessPasswordResetOtp>()
            .HasIndex(x => new { x.Email, x.CreatedAt });

        b.Entity<RecurringSeries>()
            .HasIndex(x => new { x.BusinessId, x.IsActive });

        b.Entity<Appointment>()
            .HasIndex(x => new { x.RecurringSeriesId, x.Date });

        b.Entity<WaitlistEntry>()
            .HasIndex(x => new { x.AppointmentId, x.CustomerAccountId }).IsUnique();

        b.Entity<PlatformAdmin>()
            .HasIndex(x => x.Email).IsUnique();

        b.Entity<ActivityLog>()
            .HasIndex(x => new { x.BusinessId, x.CreatedAt });
        b.Entity<ActivityLog>()
            .HasIndex(x => new { x.CustomerAccountId, x.CreatedAt });

        b.Entity<BusinessOwnerRequest>()
            .HasIndex(x => x.Email);
        b.Entity<BusinessOwnerRequest>()
            .HasIndex(x => x.Status);

        // Closes a pre-existing TOCTOU gap (check-then-insert, no DB-level guard): only one
        // CONFIRMED appointment may occupy a given business/date/start-time slot. Filtered so
        // cancelled/completed history never collides with a later booking of the same slot.
        b.Entity<Appointment>()
            .HasIndex(x => new { x.BusinessId, x.Date, x.StartTime })
            .IsUnique()
            .HasFilter("\"Status\" = 'CONFIRMED'")
            .HasDatabaseName("IX_Appointments_BusinessId_Date_StartTime_Confirmed");

        b.Entity<Item>()
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

        b.Entity<Business>()
            .Property(x => x.Language)
            .HasConversion<string>();
        b.Entity<Business>()
            .Property(x => x.SubscriptionStatus)
            .HasConversion<string>();
        b.Entity<Appointment>()
            .Property(x => x.Status)
            .HasConversion<string>();
        b.Entity<Item>()
            .Property(x => x.PhotoMode)
            .HasConversion<string>();
        // Explicit DB-level default -- see the ChatbotEnabled/BusinessModel comment above; every
        // pre-existing service is a real bookable offering, so it must backfill as bookable.
        b.Entity<Item>()
            .Property(x => x.IsBookable)
            .HasDefaultValue(true);
        b.Entity<WaitlistEntry>()
            .Property(x => x.Status)
            .HasConversion<string>();
        // Explicit DB-level default (not just the C# property initializer, which only applies to
        // objects EF constructs itself) -- without this, EF's migration scaffolding backfills
        // existing rows with default(bool) = false when the column is added, which would silently
        // disable the chatbot for every business that already existed before this feature shipped.
        b.Entity<Business>()
            .Property(x => x.ChatbotEnabled)
            .HasDefaultValue(true);
        b.Entity<Business>()
            .Property(x => x.BusinessModel)
            .HasConversion<string>()
            .HasDefaultValue(BusinessModel.Appointment);
        // Explicit DB-level default -- see the ChatbotEnabled comment above; every pre-existing
        // business predates this flag and was never issued a system-generated temp password.
        b.Entity<Business>()
            .Property(x => x.MustChangePassword)
            .HasDefaultValue(false);
        b.Entity<BusinessOwnerRequest>()
            .Property(x => x.Status)
            .HasConversion<string>();

        b.Entity<Business>()
            .HasOne(x => x.BusinessType).WithMany(x => x.Businesses)
            .HasForeignKey(x => x.BusinessTypeId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<Item>()
            .HasOne(x => x.Business).WithMany(x => x.Items)
            .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ItemGalleryPhoto>()
            .HasOne(x => x.Item).WithMany(x => x.GalleryPhotos)
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WorkingHours>()
            .HasOne(x => x.Business).WithMany(x => x.WorkingHours)
            .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Break>()
            .HasOne(x => x.Business).WithMany(x => x.Breaks)
            .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<BlockedSlot>()
            .HasOne(x => x.Business).WithMany(x => x.BlockedSlots)
            .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Customer>()
            .HasOne(x => x.Business).WithMany(x => x.Customers)
            .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Customer>()
            .HasOne(x => x.CustomerAccount).WithMany(x => x.Profiles)
            .HasForeignKey(x => x.CustomerAccountId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Follow>()
            .HasOne(x => x.CustomerAccount).WithMany(x => x.Follows)
            .HasForeignKey(x => x.CustomerAccountId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Follow>()
            .HasOne(x => x.Business).WithMany(x => x.Follows)
            .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Appointment>()
            .HasOne(x => x.Business).WithMany(x => x.Appointments)
            .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Appointment>()
            .HasOne(x => x.Customer).WithMany(x => x.Appointments)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Appointment>()
            .HasOne(x => x.Item).WithMany(x => x.Appointments)
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<RecurringSeries>()
            .HasOne(x => x.Business).WithMany(x => x.RecurringSeries)
            .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RecurringSeries>()
            .HasOne(x => x.Customer).WithMany(x => x.RecurringSeries)
            .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RecurringSeries>()
            .HasOne(x => x.Item).WithMany(x => x.RecurringSeries)
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RecurringSkip>()
            .HasOne(x => x.RecurringSeries).WithMany(x => x.Skips)
            .HasForeignKey(x => x.RecurringSeriesId).OnDelete(DeleteBehavior.Cascade);
        // SetNull (not Restrict/Cascade): deleting a series must never touch already-generated
        // Appointment rows -- per-occurrence independence -- it only unlinks them.
        b.Entity<Appointment>()
            .HasOne(x => x.RecurringSeries).WithMany(x => x.Appointments)
            .HasForeignKey(x => x.RecurringSeriesId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<WhatsAppBookingToken>()
            .HasOne(x => x.Business).WithMany()
            .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WhatsAppBookingToken>()
            .HasOne(x => x.Item).WithMany()
            .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WhatsAppConversationState>()
            .HasOne(x => x.Business).WithMany()
            .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<WaitlistEntry>()
            .HasOne(x => x.Appointment).WithMany(x => x.WaitlistEntries)
            .HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WaitlistEntry>()
            .HasOne(x => x.Business).WithMany()
            .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WaitlistEntry>()
            .HasOne(x => x.CustomerAccount).WithMany(x => x.WaitlistEntries)
            .HasForeignKey(x => x.CustomerAccountId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<ActivityLog>()
            .HasOne(x => x.Business).WithMany()
            .HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ActivityLog>()
            .HasOne(x => x.CustomerAccount).WithMany()
            .HasForeignKey(x => x.CustomerAccountId).OnDelete(DeleteBehavior.Cascade);
        // SetNull (not Cascade): deleting the admin account that performed an impersonation
        // must never destroy the target account's own history -- only unlink the attribution.
        b.Entity<ActivityLog>()
            .HasOne(x => x.ImpersonatedByPlatformAdmin).WithMany()
            .HasForeignKey(x => x.ImpersonatedByPlatformAdminId).OnDelete(DeleteBehavior.SetNull);

        // All three relations below are SetNull: a request is a historical record of how a
        // business/review came to be, and must never block deleting the type/admin/business it
        // references.
        b.Entity<BusinessOwnerRequest>()
            .HasOne(x => x.BusinessType).WithMany()
            .HasForeignKey(x => x.BusinessTypeId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<BusinessOwnerRequest>()
            .HasOne(x => x.ReviewedByPlatformAdmin).WithMany()
            .HasForeignKey(x => x.ReviewedByPlatformAdminId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<BusinessOwnerRequest>()
            .HasOne(x => x.CreatedBusiness).WithMany()
            .HasForeignKey(x => x.CreatedBusinessId).OnDelete(DeleteBehavior.SetNull);
    }
}
