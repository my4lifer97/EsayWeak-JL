using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Barber> Barbers => Set<Barber>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<WorkingHours> WorkingHours => Set<WorkingHours>();
    public DbSet<Break> Breaks => Set<Break>();
    public DbSet<BlockedSlot> BlockedSlots => Set<BlockedSlot>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<CustomerOtp> CustomerOtps => Set<CustomerOtp>();
    public DbSet<BarberEmailOtp> BarberEmailOtps => Set<BarberEmailOtp>();

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

        b.Entity<CustomerOtp>()
            .HasIndex(x => new { x.Phone, x.CreatedAt });

        b.Entity<BarberEmailOtp>()
            .HasIndex(x => new { x.Email, x.CreatedAt });

        b.Entity<Service>()
            .Property(x => x.Price)
            .HasColumnType("decimal(10,2)");

        b.Entity<BlockedSlot>()
            .Property(x => x.Date)
            .HasColumnType("date");

        b.Entity<Appointment>()
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
            .HasOne(x => x.Barber).WithMany(x => x.Services)
            .HasForeignKey(x => x.BarberId).OnDelete(DeleteBehavior.Cascade);
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
    }
}
