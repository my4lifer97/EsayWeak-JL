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
    // The next upcoming Monday, computed at runtime rather than a fixed date -- a hardcoded date
    // eventually lands in the past relative to whenever the suite actually runs, which (after this
    // file's own fix landed in AvailabilityService) would make every test here fail against a
    // date that's no longer "today or later".
    private static readonly string TestDate = NextMonday();
    private const int MondayDayOfWeek = 1;

    private static string NextMonday()
    {
        var d = DateTime.Now.Date.AddDays(1);
        while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1);
        return d.ToString("yyyy-MM-dd");
    }

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
    public async Task GetFullDayBlockInfo_ReturnsReasonForAFullDayBlock()
    {
        using var db = NewDb();
        var business = SeedBusiness(db, "09:00", "18:00");
        var date = DateTime.Parse(TestDate + "T00:00:00Z").ToUniversalTime();
        db.BlockedSlots.Add(new BlockedSlot { BusinessId = business.Id, Date = date, StartTime = null, EndTime = null, Reason = "I'm on a trip, not in the country" });
        await db.SaveChangesAsync();

        var blockInfo = await new AvailabilityService(db).GetFullDayBlockInfo(business.Id, TestDate);

        Assert.NotNull(blockInfo);
        Assert.Equal("I'm on a trip, not in the country", blockInfo!.Reason);
    }

    [Fact]
    public async Task GetFullDayBlockInfo_ReturnsNullWhenNotBlocked()
    {
        using var db = NewDb();
        var business = SeedBusiness(db, "09:00", "18:00");

        var blockInfo = await new AvailabilityService(db).GetFullDayBlockInfo(business.Id, TestDate);

        Assert.Null(blockInfo);
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

    [Fact]
    public async Task DateEntirelyInThePast_ReturnsEmpty()
    {
        using var db = NewDb();
        var business = SeedBusiness(db, "09:00", "12:00");
        var yesterday = DateTime.Now.AddDays(-1);
        // Give "yesterday"'s day-of-week real working hours too, so an empty result here is
        // actually the past-date guard and not just "no hours configured for that weekday".
        // Skipped when yesterday happens to land on MondayDayOfWeek -- SeedBusiness already
        // seeded that exact (BusinessId, DayOfWeek) row above, and the DB's real unique
        // constraint on that pair rejects a second insert for it.
        if ((int)yesterday.DayOfWeek != MondayDayOfWeek)
        {
            db.WorkingHours.Add(new WorkingHours
            {
                BusinessId = business.Id, DayOfWeek = (int)yesterday.DayOfWeek,
                StartTime = "09:00", EndTime = "12:00", IsActive = true,
            });
        }
        await db.SaveChangesAsync();

        var slots = await new AvailabilityService(db).GetAvailableSlots(business.Id, yesterday.ToString("yyyy-MM-dd"), 30);

        Assert.Empty(slots);
    }

    // A scheduled range that hasn't started yet (Applied = false) hasn't touched live WorkingHours,
    // but a customer booking a date inside it must already see that preset's hours -- here it opens
    // a Tuesday the live template has closed.
    [Fact]
    public async Task PendingScheduledPreset_OpensDateInsideRange()
    {
        using var db = NewDb();
        var business = SeedBusiness(db);
        var monday = DateTime.Parse(TestDate);
        var tuesday = monday.AddDays(1);
        var (holiday, defaultPreset) = SeedPresets(db, business.Id, holidayDayOfWeek: 2);
        db.PresetSchedules.Add(new PresetSchedule
        {
            BusinessId = business.Id, PresetId = holiday.Id, RevertToPresetId = defaultPreset.Id,
            StartDate = tuesday, EndDate = tuesday, Applied = false,
        });
        await db.SaveChangesAsync();

        var service = new AvailabilityService(db);
        Assert.NotEmpty(await service.GetAvailableSlots(business.Id, tuesday.ToString("yyyy-MM-dd"), 30));
        // Outside a not-yet-running range, the live template still applies.
        Assert.NotEmpty(await service.GetAvailableSlots(business.Id, TestDate, 30));
        Assert.Contains(tuesday.ToString("yyyy-MM-dd"), await service.GetOpenDates(business.Id, 14));
    }

    // Once a range is running, live WorkingHours hold the scheduled preset -- dates after it ends
    // must resolve to the Default preset instead.
    [Fact]
    public async Task RunningScheduledPreset_DatesAfterRangeUseDefault()
    {
        using var db = NewDb();
        var business = SeedBusiness(db);
        var monday = DateTime.Parse(TestDate);
        var (holiday, defaultPreset) = SeedPresets(db, business.Id, holidayDayOfWeek: 2);
        // Live template currently mirrors the holiday preset: Monday closed.
        db.WorkingHours.Single(w => w.BusinessId == business.Id && w.DayOfWeek == MondayDayOfWeek).IsActive = false;
        db.PresetSchedules.Add(new PresetSchedule
        {
            BusinessId = business.Id, PresetId = holiday.Id, RevertToPresetId = defaultPreset.Id,
            StartDate = DateTime.Now.Date, EndDate = monday.AddDays(-1), Applied = true,
        });
        await db.SaveChangesAsync();

        Assert.NotEmpty(await new AvailabilityService(db).GetAvailableSlots(business.Id, TestDate, 30));
    }

    // Default: Monday 09:00-12:00 only. Holiday: only `holidayDayOfWeek` open.
    private static (SchedulePreset Holiday, SchedulePreset Default) SeedPresets(AppDbContext db, string businessId, int holidayDayOfWeek)
    {
        SchedulePreset Make(string name, bool isDefault, int openDay) => new()
        {
            BusinessId = businessId, Name = name, IsDefault = isDefault,
            Days = Enumerable.Range(0, 7).Select(d => new SchedulePresetDay
            {
                DayOfWeek = d, StartTime = "09:00", EndTime = "12:00", IsActive = d == openDay,
            }).ToList(),
        };
        var holiday = Make("eid", false, holidayDayOfWeek);
        var def = Make("Default", true, MondayDayOfWeek);
        db.SchedulePresets.AddRange(holiday, def);
        db.SaveChanges();
        return (holiday, def);
    }
}
