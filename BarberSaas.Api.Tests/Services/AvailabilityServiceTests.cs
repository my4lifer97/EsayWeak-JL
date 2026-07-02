using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

// SQLite in-memory (not the EF InMemory provider) for consistency with the integration
// tests' TestWebApplicationFactory, and because it's relational like the real Postgres DB.
public class AvailabilityServiceTests : IDisposable
{
    // 2026-07-06 is a Monday (DayOfWeek.Monday == 1).
    private const string TestDate = "2026-07-06";
    private const int MondayDayOfWeek = 1;

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public AvailabilityServiceTests()
    {
        _connection.Open();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private AppDbContext NewDb()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static Barber SeedBarber(AppDbContext db, string startTime = "09:00", string endTime = "12:00")
    {
        var barber = new Barber { Name = "Test Barber", Email = "t@example.com", Slug = "test-barber" };
        db.Barbers.Add(barber);
        db.WorkingHours.Add(new WorkingHours
        {
            BarberId = barber.Id,
            DayOfWeek = MondayDayOfWeek,
            StartTime = startTime,
            EndTime = endTime,
            IsActive = true,
        });
        db.SaveChanges();
        return barber;
    }

    [Fact]
    public async Task NoWorkingHoursForDay_ReturnsEmpty()
    {
        using var db = NewDb();
        var barber = new Barber { Name = "No Hours", Email = "n@example.com", Slug = "no-hours" };
        db.Barbers.Add(barber);
        await db.SaveChangesAsync();

        var slots = await new AvailabilityService(db).GetAvailableSlots(barber.Id, TestDate, 30);

        Assert.Empty(slots);
    }

    [Fact]
    public async Task GeneratesThirtyMinuteSlotsWithinWorkingHours()
    {
        using var db = NewDb();
        var barber = SeedBarber(db, "09:00", "10:00");

        var slots = await new AvailabilityService(db).GetAvailableSlots(barber.Id, TestDate, 30);

        Assert.Equal(2, slots.Count);
        Assert.Contains(slots, s => s.Start == "09:00" && s.End == "09:30");
        Assert.Contains(slots, s => s.Start == "09:30" && s.End == "10:00");
    }

    [Fact]
    public async Task ExcludesCandidateWhoseServiceDurationWouldExceedEndTime()
    {
        using var db = NewDb();
        var barber = SeedBarber(db, "09:00", "10:00");

        // 45-minute service: 09:00 fits (ends 09:45), 09:30 does not (would end 10:15).
        var slots = await new AvailabilityService(db).GetAvailableSlots(barber.Id, TestDate, 45);

        var slot = Assert.Single(slots);
        Assert.Equal("09:00", slot.Start);
    }

    [Fact]
    public async Task ExcludesSlotsOverlappingABreak()
    {
        using var db = NewDb();
        var barber = SeedBarber(db, "09:00", "12:00");
        db.Breaks.Add(new Break { BarberId = barber.Id, DayOfWeek = MondayDayOfWeek, StartTime = "10:00", EndTime = "10:30" });
        await db.SaveChangesAsync();

        var slots = await new AvailabilityService(db).GetAvailableSlots(barber.Id, TestDate, 30);

        Assert.DoesNotContain(slots, s => s.Start == "10:00");
        Assert.Equal(5, slots.Count); // 6 half-hour slots between 09:00-12:00 minus the one break slot
    }

    [Fact]
    public async Task BlockedSlotWithNoStartTime_BlocksTheWholeDay()
    {
        using var db = NewDb();
        var barber = SeedBarber(db);
        var date = DateTime.Parse(TestDate + "T00:00:00Z").ToUniversalTime();
        db.BlockedSlots.Add(new BlockedSlot { BarberId = barber.Id, Date = date, StartTime = null, EndTime = null, Reason = "Day off" });
        await db.SaveChangesAsync();

        var slots = await new AvailabilityService(db).GetAvailableSlots(barber.Id, TestDate, 30);

        Assert.Empty(slots);
    }

    [Fact]
    public async Task BlockedSlotWithRange_OnlyBlocksThatRange()
    {
        using var db = NewDb();
        var barber = SeedBarber(db, "09:00", "12:00");
        var date = DateTime.Parse(TestDate + "T00:00:00Z").ToUniversalTime();
        db.BlockedSlots.Add(new BlockedSlot { BarberId = barber.Id, Date = date, StartTime = "11:00", EndTime = "11:30" });
        await db.SaveChangesAsync();

        var slots = await new AvailabilityService(db).GetAvailableSlots(barber.Id, TestDate, 30);

        Assert.DoesNotContain(slots, s => s.Start == "11:00");
        Assert.Equal(5, slots.Count);
    }

    [Fact]
    public async Task ConfirmedAppointment_BlocksItsSlot()
    {
        using var db = NewDb();
        var barber = SeedBarber(db, "09:00", "10:00");
        var service = new Service { BarberId = barber.Id, NameEn = "Cut", NameAr = "Cut", NameHe = "Cut", DurationMinutes = 30, Price = 10 };
        var customer = new Customer { BarberId = barber.Id, Name = "C", Phone = "+1" };
        db.Services.Add(service);
        db.Customers.Add(customer);
        var date = DateTime.Parse(TestDate + "T00:00:00Z").ToUniversalTime();
        db.Appointments.Add(new Appointment
        {
            BarberId = barber.Id, CustomerId = customer.Id, ServiceId = service.Id,
            Date = date, StartTime = "09:00", EndTime = "09:30", Status = AppointmentStatus.CONFIRMED,
        });
        await db.SaveChangesAsync();

        var slots = await new AvailabilityService(db).GetAvailableSlots(barber.Id, TestDate, 30);

        var slot = Assert.Single(slots);
        Assert.Equal("09:30", slot.Start);
    }

    [Fact]
    public async Task CancelledAppointment_DoesNotBlockItsSlot()
    {
        using var db = NewDb();
        var barber = SeedBarber(db, "09:00", "10:00");
        var service = new Service { BarberId = barber.Id, NameEn = "Cut", NameAr = "Cut", NameHe = "Cut", DurationMinutes = 30, Price = 10 };
        var customer = new Customer { BarberId = barber.Id, Name = "C", Phone = "+1" };
        db.Services.Add(service);
        db.Customers.Add(customer);
        var date = DateTime.Parse(TestDate + "T00:00:00Z").ToUniversalTime();
        db.Appointments.Add(new Appointment
        {
            BarberId = barber.Id, CustomerId = customer.Id, ServiceId = service.Id,
            Date = date, StartTime = "09:00", EndTime = "09:30", Status = AppointmentStatus.CANCELLED,
        });
        await db.SaveChangesAsync();

        var slots = await new AvailabilityService(db).GetAvailableSlots(barber.Id, TestDate, 30);

        Assert.Equal(2, slots.Count);
        Assert.Contains(slots, s => s.Start == "09:00");
    }
}
