using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

// SQLite in-memory (not the EF InMemory provider), same rationale as AvailabilityServiceTests.
public class RecurringAppointmentServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public RecurringAppointmentServiceTests()
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

    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    private static string DateStr(DateTime d) => d.ToString("yyyy-MM-dd");

    // Matches the exact parse used by AvailabilityService/RecurringAppointmentService, so a
    // seeded date's Ticks line up with whatever the production code compares it against.
    private static DateTime AsStoredDate(string dateStr) => DateTime.Parse(dateStr + "T00:00:00Z").ToUniversalTime();

    private static (Business Business, Item Item, Customer Customer) SeedBusinessServiceCustomer(AppDbContext db, DayOfWeek dayOfWeek)
    {
        var business = new Business { Name = "Test Business", Email = $"{Guid.NewGuid():N}@example.com", Slug = $"business-{Guid.NewGuid():N}" };
        var item = new Item { BusinessId = business.Id, NameEn = "Cut", NameAr = "Cut", NameHe = "Cut", DurationMinutes = 30, Price = 20, IsActive = true };
        var customer = new Customer { BusinessId = business.Id, Name = "Mohamed", Phone = "+15551110000" };
        db.Businesses.Add(business);
        db.Items.Add(item);
        db.Customers.Add(customer);
        db.WorkingHours.Add(new WorkingHours { BusinessId = business.Id, DayOfWeek = (int)dayOfWeek, StartTime = "09:00", EndTime = "18:00", IsActive = true });
        db.SaveChanges();
        return (business, item, customer);
    }

    // QA run 4 ST-9: two series on one slot made the whole run fail (duplicate key), so nothing
    // was generated for any business. Now the second is a skip, and other series still run.
    [Fact]
    public async Task TwoSeriesOnOneSlot_TheSecondIsSkipped_AndOtherSeriesStillRun()
    {
        using var db = NewDb();
        var target = AsStoredDate(DateStr(AppClock.Now.Date.AddDays(1)));
        var (business, item, customer) = SeedBusinessServiceCustomer(db, target.DayOfWeek);
        var (other, otherItem, otherCustomer) = SeedBusinessServiceCustomer(db, target.DayOfWeek);
        RecurringSeries Series(Business b, Item i, Customer c) => new()
        {
            BusinessId = b.Id, CustomerId = c.Id, ItemId = i.Id,
            DayOfWeek = (int)target.DayOfWeek, StartTime = "13:00", StartDate = target, EndDate = target, IsActive = true,
            Force = true, // skips the availability check, so only the save-time check can catch it
        };
        db.RecurringSeries.AddRange(Series(business, item, customer), Series(business, item, customer), Series(other, otherItem, otherCustomer));
        await db.SaveChangesAsync();

        var result = await new RecurringAppointmentService(db, new AvailabilityService(db), EmptyConfig(), NullLogger<RecurringAppointmentService>.Instance).GenerateOccurrences();

        Assert.Equal(2, result.Created);
        Assert.Equal(1, result.Skipped);
        Assert.Single(db.Appointments.Where(a => a.BusinessId == business.Id));
        Assert.Single(db.Appointments.Where(a => a.BusinessId == other.Id));
    }

    [Fact]
    public async Task CreatesOccurrence_WhenSlotIsAvailable()
    {
        using var db = NewDb();
        var target = AsStoredDate(DateStr(AppClock.Now.Date.AddDays(1)));
        var (business, item, customer) = SeedBusinessServiceCustomer(db, target.DayOfWeek);
        var series = new RecurringSeries
        {
            BusinessId = business.Id, CustomerId = customer.Id, ItemId = item.Id,
            DayOfWeek = (int)target.DayOfWeek, StartTime = "13:00", StartDate = target, EndDate = target, IsActive = true,
        };
        db.RecurringSeries.Add(series);
        await db.SaveChangesAsync();

        var result = await new RecurringAppointmentService(db, new AvailabilityService(db), EmptyConfig(), NullLogger<RecurringAppointmentService>.Instance).GenerateOccurrences();

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Skipped);
        var appt = Assert.Single(db.Appointments.Where(a => a.RecurringSeriesId == series.Id));
        Assert.Equal(target, appt.Date);
        Assert.Equal("13:00", appt.StartTime);
    }

    [Fact]
    public async Task SkipsAndLogsRecurringSkip_WhenBlockedSlotCoversIt()
    {
        using var db = NewDb();
        var target = AsStoredDate(DateStr(AppClock.Now.Date.AddDays(1)));
        var (business, item, customer) = SeedBusinessServiceCustomer(db, target.DayOfWeek);
        db.BlockedSlots.Add(new BlockedSlot { BusinessId = business.Id, Date = target, StartTime = null, EndTime = null, Reason = "Day off" });
        var series = new RecurringSeries
        {
            BusinessId = business.Id, CustomerId = customer.Id, ItemId = item.Id,
            DayOfWeek = (int)target.DayOfWeek, StartTime = "13:00", StartDate = target, EndDate = target, IsActive = true,
        };
        db.RecurringSeries.Add(series);
        await db.SaveChangesAsync();

        var result = await new RecurringAppointmentService(db, new AvailabilityService(db), EmptyConfig(), NullLogger<RecurringAppointmentService>.Instance).GenerateOccurrences();

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(db.Appointments.Where(a => a.RecurringSeriesId == series.Id));
        var skip = Assert.Single(db.RecurringSkips.Where(s => s.RecurringSeriesId == series.Id));
        Assert.Equal("slot_unavailable", skip.Reason);
    }

    [Fact]
    public async Task Force_CreatesOccurrence_EvenWithNoWorkingHoursConfiguredForThatDay()
    {
        using var db = NewDb();
        var target = AsStoredDate(DateStr(AppClock.Now.Date.AddDays(1)));
        // Deliberately no WorkingHours row for this day (not calling SeedBusinessServiceCustomer,
        // which always adds one) -- Force must bypass that gate entirely, the same problem the
        // "New Recurring Series" form's day-of-week grid otherwise gets permanently stuck on.
        var business = new Business { Name = "Test Business", Email = $"{Guid.NewGuid():N}@example.com", Slug = $"business-{Guid.NewGuid():N}" };
        var item = new Item { BusinessId = business.Id, NameEn = "Cut", NameAr = "Cut", NameHe = "Cut", DurationMinutes = 30, Price = 20, IsActive = true };
        var customer = new Customer { BusinessId = business.Id, Name = "Mohamed", Phone = "+15551110000" };
        db.Businesses.Add(business);
        db.Items.Add(item);
        db.Customers.Add(customer);
        var series = new RecurringSeries
        {
            BusinessId = business.Id, CustomerId = customer.Id, ItemId = item.Id,
            DayOfWeek = (int)target.DayOfWeek, StartTime = "13:00", StartDate = target, EndDate = target, IsActive = true,
            Force = true,
        };
        db.RecurringSeries.Add(series);
        await db.SaveChangesAsync();

        var result = await new RecurringAppointmentService(db, new AvailabilityService(db), EmptyConfig(), NullLogger<RecurringAppointmentService>.Instance).GenerateOccurrences();

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Skipped);
        var appt = Assert.Single(db.Appointments.Where(a => a.RecurringSeriesId == series.Id));
        Assert.Equal("13:00", appt.StartTime);
    }

    [Fact]
    public async Task Force_StillBlocksOnExactConflictingAppointment()
    {
        using var db = NewDb();
        var target = AsStoredDate(DateStr(AppClock.Now.Date.AddDays(1)));
        var (business, item, customer) = SeedBusinessServiceCustomer(db, target.DayOfWeek);
        var otherCustomer = new Customer { BusinessId = business.Id, Name = "Other", Phone = "+15551110001" };
        db.Customers.Add(otherCustomer);
        db.Appointments.Add(new Appointment
        {
            BusinessId = business.Id, CustomerId = otherCustomer.Id, ItemId = item.Id,
            Date = target, StartTime = "13:00", EndTime = "13:30", Status = AppointmentStatus.CONFIRMED,
        });
        var series = new RecurringSeries
        {
            BusinessId = business.Id, CustomerId = customer.Id, ItemId = item.Id,
            DayOfWeek = (int)target.DayOfWeek, StartTime = "13:00", StartDate = target, EndDate = target, IsActive = true,
            Force = true,
        };
        db.RecurringSeries.Add(series);
        await db.SaveChangesAsync();

        var result = await new RecurringAppointmentService(db, new AvailabilityService(db), EmptyConfig(), NullLogger<RecurringAppointmentService>.Instance).GenerateOccurrences();

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(db.Appointments.Where(a => a.RecurringSeriesId == series.Id));
    }

    [Fact]
    public async Task SecondRun_DoesNotDuplicateAlreadyGeneratedOccurrence()
    {
        using var db = NewDb();
        var target = AsStoredDate(DateStr(AppClock.Now.Date.AddDays(1)));
        var (business, item, customer) = SeedBusinessServiceCustomer(db, target.DayOfWeek);
        var series = new RecurringSeries
        {
            BusinessId = business.Id, CustomerId = customer.Id, ItemId = item.Id,
            DayOfWeek = (int)target.DayOfWeek, StartTime = "13:00", StartDate = target, EndDate = target, IsActive = true,
        };
        db.RecurringSeries.Add(series);
        await db.SaveChangesAsync();
        var generator = new RecurringAppointmentService(db, new AvailabilityService(db), EmptyConfig(), NullLogger<RecurringAppointmentService>.Instance);

        await generator.GenerateOccurrences();
        var secondResult = await generator.GenerateOccurrences();

        Assert.Equal(0, secondResult.Created);
        Assert.Single(db.Appointments.Where(a => a.RecurringSeriesId == series.Id));
    }

    [Fact]
    public async Task AutoPausesSeries_WhenLinkedServiceIsDeactivated()
    {
        using var db = NewDb();
        var target = AsStoredDate(DateStr(AppClock.Now.Date.AddDays(1)));
        var (business, item, customer) = SeedBusinessServiceCustomer(db, target.DayOfWeek);
        item.IsActive = false;
        var series = new RecurringSeries
        {
            BusinessId = business.Id, CustomerId = customer.Id, ItemId = item.Id,
            DayOfWeek = (int)target.DayOfWeek, StartTime = "13:00", StartDate = target, EndDate = target, IsActive = true,
        };
        db.RecurringSeries.Add(series);
        await db.SaveChangesAsync();

        var result = await new RecurringAppointmentService(db, new AvailabilityService(db), EmptyConfig(), NullLogger<RecurringAppointmentService>.Instance).GenerateOccurrences();

        Assert.Equal(0, result.Created);
        Assert.Empty(db.Appointments.Where(a => a.RecurringSeriesId == series.Id));
        var reloaded = await db.RecurringSeries.FindAsync(series.Id);
        Assert.False(reloaded!.IsActive);
        var skip = Assert.Single(db.RecurringSkips.Where(s => s.RecurringSeriesId == series.Id));
        Assert.Equal("service_inactive", skip.Reason);
    }

    [Fact]
    public async Task DoesNotBackfillPastDates_WhenSeriesStartedWeeksAgo()
    {
        using var db = NewDb();
        var tomorrow = AsStoredDate(DateStr(AppClock.Now.Date.AddDays(1)));
        var pastStart = AsStoredDate(DateStr(AppClock.Now.Date.AddDays(1 - 21)));
        var endDate = AsStoredDate(DateStr(AppClock.Now.Date.AddDays(1 + 7)));
        var (business, item, customer) = SeedBusinessServiceCustomer(db, tomorrow.DayOfWeek);
        var series = new RecurringSeries
        {
            BusinessId = business.Id, CustomerId = customer.Id, ItemId = item.Id,
            DayOfWeek = (int)tomorrow.DayOfWeek, StartTime = "13:00", StartDate = pastStart, EndDate = endDate, IsActive = true,
        };
        db.RecurringSeries.Add(series);
        await db.SaveChangesAsync();

        await new RecurringAppointmentService(db, new AvailabilityService(db), EmptyConfig(), NullLogger<RecurringAppointmentService>.Instance).GenerateOccurrences();

        var appointments = db.Appointments.Where(a => a.RecurringSeriesId == series.Id).ToList();
        var today = AppClock.Now.Date;
        Assert.All(appointments, a => Assert.True(a.Date >= today, $"appointment on {a.Date:yyyy-MM-dd} should not be in the past"));
        Assert.Contains(appointments, a => a.Date == tomorrow);
    }

    [Fact]
    public async Task RespectsEndDate_StopsGeneratingAndDeactivatesOncePassed()
    {
        using var db = NewDb();
        var target = AsStoredDate(DateStr(AppClock.Now.Date.AddDays(1)));
        var pastEnd = AsStoredDate(DateStr(AppClock.Now.Date.AddDays(-1)));
        var (business, item, customer) = SeedBusinessServiceCustomer(db, target.DayOfWeek);
        var series = new RecurringSeries
        {
            BusinessId = business.Id, CustomerId = customer.Id, ItemId = item.Id,
            DayOfWeek = (int)target.DayOfWeek, StartTime = "13:00", StartDate = target.AddDays(-30), EndDate = pastEnd, IsActive = true,
        };
        db.RecurringSeries.Add(series);
        await db.SaveChangesAsync();

        var result = await new RecurringAppointmentService(db, new AvailabilityService(db), EmptyConfig(), NullLogger<RecurringAppointmentService>.Instance).GenerateOccurrences();

        Assert.Equal(0, result.Created);
        Assert.Empty(db.Appointments.Where(a => a.RecurringSeriesId == series.Id));
        var reloaded = await db.RecurringSeries.FindAsync(series.Id);
        Assert.False(reloaded!.IsActive);
    }
}
