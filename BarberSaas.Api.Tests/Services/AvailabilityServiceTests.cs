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

    private static Business SeedBusiness(AppDbContext db, string startTime = "09:00", string endTime = "12:00")
    {
        var business = new Business { Name = "Test Business", Email = "t@example.com", Slug = "test-business" };
        db.Businesses.Add(business);
        db.WorkingHours.Add(new WorkingHours
        {
            BusinessId = business.Id,
            DayOfWeek = MondayDayOfWeek,
            StartTime = startTime,
            EndTime = endTime,
            IsActive = true,
        });
        db.SaveChanges();
        return business;
    }

    [Fact]
    public async Task NoWorkingHoursForDay_ReturnsEmpty()
    {
        using var db = NewDb();
        var business = new Business { Name = "No Hours", Email = "n@example.com", Slug = "no-hours" };
        db.Businesses.Add(business);
        await db.SaveChangesAsync();

        var slots = await new AvailabilityService(db).GetAvailableSlots(business.Id, TestDate, 30);

        Assert.Empty(slots);
    }

    [Fact]
    public async Task GeneratesThirtyMinuteSlotsWithinWorkingHours()
    {
        using var db = NewDb();
        var business = SeedBusiness(db, "09:00", "10:00");

        var slots = await new AvailabilityService(db).GetAvailableSlots(business.Id, TestDate, 30);

        Assert.Equal(2, slots.Count);
        Assert.Contains(slots, s => s.Start == "09:00" && s.End == "09:30");
        Assert.Contains(slots, s => s.Start == "09:30" && s.End == "10:00");
    }

    [Fact]
    public async Task ExcludesCandidateWhoseServiceDurationWouldExceedEndTime()
    {
        using var db = NewDb();
        var business = SeedBusiness(db, "09:00", "10:00");

        // 45-minute service: 09:00 fits (ends 09:45), 09:30 does not (would end 10:15).
        var slots = await new AvailabilityService(db).GetAvailableSlots(business.Id, TestDate, 45);

        var slot = Assert.Single(slots);
        Assert.Equal("09:00", slot.Start);
    }

    [Fact]
    public async Task ExcludesSlotsOverlappingABreak()
    {
        using var db = NewDb();
        var business = SeedBusiness(db, "09:00", "12:00");
        db.Breaks.Add(new Break { BusinessId = business.Id, DayOfWeek = MondayDayOfWeek, StartTime = "10:00", EndTime = "10:30" });
        await db.SaveChangesAsync();

        var slots = await new AvailabilityService(db).GetAvailableSlots(business.Id, TestDate, 30);

        Assert.DoesNotContain(slots, s => s.Start == "10:00");
        Assert.Equal(5, slots.Count); // 6 half-hour slots between 09:00-12:00 minus the one break slot
    }

    [Fact]
    public async Task BlockedSlotWithNoStartTime_BlocksTheWholeDay()
    {
        using var db = NewDb();
        var business = SeedBusiness(db);
        var date = DateTime.Parse(TestDate + "T00:00:00Z").ToUniversalTime();
        db.BlockedSlots.Add(new BlockedSlot { BusinessId = business.Id, Date = date, StartTime = null, EndTime = null, Reason = "Day off" });
        await db.SaveChangesAsync();

        var slots = await new AvailabilityService(db).GetAvailableSlots(business.Id, TestDate, 30);

        Assert.Empty(slots);
    }

    [Fact]
    public async Task BlockedSlotWithRange_OnlyBlocksThatRange()
    {
        using var db = NewDb();
        var business = SeedBusiness(db, "09:00", "12:00");
        var date = DateTime.Parse(TestDate + "T00:00:00Z").ToUniversalTime();
        db.BlockedSlots.Add(new BlockedSlot { BusinessId = business.Id, Date = date, StartTime = "11:00", EndTime = "11:30" });
        await db.SaveChangesAsync();

        var slots = await new AvailabilityService(db).GetAvailableSlots(business.Id, TestDate, 30);

        Assert.DoesNotContain(slots, s => s.Start == "11:00");
        Assert.Equal(5, slots.Count);
    }

    [Fact]
    public async Task ConfirmedAppointment_BlocksItsSlot()
    {
        using var db = NewDb();
        var business = SeedBusiness(db, "09:00", "10:00");
        var item = new Item { BusinessId = business.Id, NameEn = "Cut", NameAr = "Cut", NameHe = "Cut", DurationMinutes = 30, Price = 10 };
        var customer = new Customer { BusinessId = business.Id, Name = "C", Phone = "+1" };
        db.Items.Add(item);
        db.Customers.Add(customer);
        var date = DateTime.Parse(TestDate + "T00:00:00Z").ToUniversalTime();
        db.Appointments.Add(new Appointment
        {
            BusinessId = business.Id, CustomerId = customer.Id, ItemId = item.Id,
            Date = date, StartTime = "09:00", EndTime = "09:30", Status = AppointmentStatus.CONFIRMED,
        });
        await db.SaveChangesAsync();

        var slots = await new AvailabilityService(db).GetAvailableSlots(business.Id, TestDate, 30);

        var slot = Assert.Single(slots);
        Assert.Equal("09:30", slot.Start);
    }

    [Fact]
    public async Task CancelledAppointment_DoesNotBlockItsSlot()
    {
        using var db = NewDb();
        var business = SeedBusiness(db, "09:00", "10:00");
        var item = new Item { BusinessId = business.Id, NameEn = "Cut", NameAr = "Cut", NameHe = "Cut", DurationMinutes = 30, Price = 10 };
        var customer = new Customer { BusinessId = business.Id, Name = "C", Phone = "+1" };
        db.Items.Add(item);
        db.Customers.Add(customer);
        var date = DateTime.Parse(TestDate + "T00:00:00Z").ToUniversalTime();
        db.Appointments.Add(new Appointment
        {
            BusinessId = business.Id, CustomerId = customer.Id, ItemId = item.Id,
            Date = date, StartTime = "09:00", EndTime = "09:30", Status = AppointmentStatus.CANCELLED,
        });
        await db.SaveChangesAsync();

        var slots = await new AvailabilityService(db).GetAvailableSlots(business.Id, TestDate, 30);

        Assert.Equal(2, slots.Count);
        Assert.Contains(slots, s => s.Start == "09:00");
    }

    [Fact]
    public async Task ForToday_ExcludesSlotsThatHaveAlreadyStarted()
    {
        using var db = NewDb();
        // WorkingHours/slot times are the business's local wall-clock hours (never UTC —
        // see AvailabilityService), so "today"/"now" here must be local server time too,
        // matching what the service itself compares against.
        var todayStr = DateTime.Now.ToString("yyyy-MM-dd");
        var business = new Business { Name = "Today Business", Email = "today@example.com", Slug = "today-business" };
        db.Businesses.Add(business);
        db.WorkingHours.Add(new WorkingHours
        {
            BusinessId = business.Id, DayOfWeek = (int)DateTime.Now.DayOfWeek,
            StartTime = "00:00", EndTime = "23:30", IsActive = true,
        });
        await db.SaveChangesAsync();

        var slots = await new AvailabilityService(db).GetAvailableSlots(business.Id, todayStr, 30);

        var nowTime = DateTime.Now.ToString("HH:mm");
        Assert.All(slots, s => Assert.True(string.Compare(s.Start, nowTime, StringComparison.Ordinal) > 0,
            $"slot {s.Start} should be after current time {nowTime}"));
    }
}
