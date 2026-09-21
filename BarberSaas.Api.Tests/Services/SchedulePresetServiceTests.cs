using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

// Covers ApplyDueScheduledPresets -- the daily cron sweep (CronController's
// /api/cron/apply-scheduled-presets) that applies a PresetSchedule once its StartDate arrives and
// reverts it once its EndDate has passed. The "starts today, applies immediately" path is already
// covered at the HTTP level in ScheduleTests.cs; this file covers the future-dated and
// past-due-for-revert cases the API-level tests can't easily simulate.
public class SchedulePresetServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public SchedulePresetServiceTests()
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

    private static DateTime AsDate(DateTime d) => DateTime.Parse(d.ToString("yyyy-MM-dd") + "T00:00:00Z").ToUniversalTime();

    private static (Business Business, SchedulePreset Target, SchedulePreset Backup) SeedBusinessWithPresets(AppDbContext db)
    {
        var business = new Business { Name = "Test Business", Email = $"{Guid.NewGuid():N}@example.com", Slug = $"business-{Guid.NewGuid():N}" };
        db.Businesses.Add(business);
        db.WorkingHours.Add(new WorkingHours { BusinessId = business.Id, DayOfWeek = 0, StartTime = "09:00", EndTime = "18:00", IsActive = true });

        var target = new SchedulePreset { BusinessId = business.Id, Name = "Christmas Hours" };
        target.Days = Enumerable.Range(0, 7).Select(d => new SchedulePresetDay
        {
            SchedulePresetId = target.Id, DayOfWeek = d, StartTime = "10:00", EndTime = "14:00", IsActive = d == 3,
        }).ToList();
        db.SchedulePresets.Add(target);

        var backup = new SchedulePreset { BusinessId = business.Id, Name = "Before Christmas Hours" };
        backup.Days = Enumerable.Range(0, 7).Select(d => new SchedulePresetDay
        {
            SchedulePresetId = backup.Id, DayOfWeek = d, StartTime = "09:00", EndTime = "18:00", IsActive = d == 0,
        }).ToList();
        db.SchedulePresets.Add(backup);

        db.SaveChanges();
        return (business, target, backup);
    }

    [Fact]
    public async Task AppliesAScheduleWhoseStartDateHasArrived()
    {
        using var db = NewDb();
        var (business, target, backup) = SeedBusinessWithPresets(db);
        db.PresetSchedules.Add(new PresetSchedule
        {
            BusinessId = business.Id, PresetId = target.Id, RevertToPresetId = backup.Id,
            StartDate = AsDate(DateTime.Now.Date), EndDate = AsDate(DateTime.Now.Date.AddDays(7)),
            Applied = false,
        });
        await db.SaveChangesAsync();

        var (applied, reverted) = await new SchedulePresetService(db).ApplyDueScheduledPresets();

        Assert.Equal(1, applied);
        Assert.Equal(0, reverted);
        var wednesday = await db.WorkingHours.FirstAsync(w => w.BusinessId == business.Id && w.DayOfWeek == 3);
        Assert.True(wednesday.IsActive);
        Assert.Equal("10:00", wednesday.StartTime);
        var schedule = await db.PresetSchedules.SingleAsync(s => s.BusinessId == business.Id);
        Assert.True(schedule.Applied);
    }

    [Fact]
    public async Task DoesNotApplyAScheduleThatStartsInTheFuture()
    {
        using var db = NewDb();
        var (business, target, backup) = SeedBusinessWithPresets(db);
        db.PresetSchedules.Add(new PresetSchedule
        {
            BusinessId = business.Id, PresetId = target.Id, RevertToPresetId = backup.Id,
            StartDate = AsDate(DateTime.Now.Date.AddDays(3)), EndDate = AsDate(DateTime.Now.Date.AddDays(10)),
            Applied = false,
        });
        await db.SaveChangesAsync();

        var (applied, reverted) = await new SchedulePresetService(db).ApplyDueScheduledPresets();

        Assert.Equal(0, applied);
        Assert.Equal(0, reverted);
        var wednesday = await db.WorkingHours.FirstOrDefaultAsync(w => w.BusinessId == business.Id && w.DayOfWeek == 3);
        Assert.Null(wednesday); // never touched
    }

    [Fact]
    public async Task RevertsAnAppliedScheduleWhoseEndDateHasPassed()
    {
        using var db = NewDb();
        var (business, target, backup) = SeedBusinessWithPresets(db);
        // Simulate it having already been applied (Wednesday active 10-14 from the target preset).
        db.WorkingHours.Add(new WorkingHours { BusinessId = business.Id, DayOfWeek = 3, StartTime = "10:00", EndTime = "14:00", IsActive = true });
        db.PresetSchedules.Add(new PresetSchedule
        {
            BusinessId = business.Id, PresetId = target.Id, RevertToPresetId = backup.Id,
            StartDate = AsDate(DateTime.Now.Date.AddDays(-10)), EndDate = AsDate(DateTime.Now.Date.AddDays(-1)),
            Applied = true,
        });
        await db.SaveChangesAsync();

        var (applied, reverted) = await new SchedulePresetService(db).ApplyDueScheduledPresets();

        Assert.Equal(0, applied);
        Assert.Equal(1, reverted);
        var sunday = await db.WorkingHours.FirstAsync(w => w.BusinessId == business.Id && w.DayOfWeek == 0);
        var wednesday = await db.WorkingHours.FirstAsync(w => w.BusinessId == business.Id && w.DayOfWeek == 3);
        Assert.True(sunday.IsActive); // restored from the backup preset
        Assert.False(wednesday.IsActive);
        Assert.False(await db.PresetSchedules.AnyAsync(s => s.BusinessId == business.Id)); // cleaned up
    }

    [Fact]
    public async Task DoesNotRevertAnAppliedScheduleStillWithinItsRange()
    {
        using var db = NewDb();
        var (business, target, backup) = SeedBusinessWithPresets(db);
        db.PresetSchedules.Add(new PresetSchedule
        {
            BusinessId = business.Id, PresetId = target.Id, RevertToPresetId = backup.Id,
            StartDate = AsDate(DateTime.Now.Date.AddDays(-3)), EndDate = AsDate(DateTime.Now.Date.AddDays(3)),
            Applied = true,
        });
        await db.SaveChangesAsync();

        var (applied, reverted) = await new SchedulePresetService(db).ApplyDueScheduledPresets();

        Assert.Equal(0, applied);
        Assert.Equal(0, reverted);
        Assert.True(await db.PresetSchedules.AnyAsync(s => s.BusinessId == business.Id)); // still there
    }
}
