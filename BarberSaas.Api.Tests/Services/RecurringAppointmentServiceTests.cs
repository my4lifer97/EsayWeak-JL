using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

    private static (Barber Barber, Service Service, Customer Customer) SeedBarberServiceCustomer(AppDbContext db, DayOfWeek dayOfWeek)
    {
        var barber = new Barber { Name = "Test Barber", Email = $"{Guid.NewGuid():N}@example.com", Slug = $"barber-{Guid.NewGuid():N}" };
        var service = new Service { BarberId = barber.Id, NameEn = "Cut", NameAr = "Cut", NameHe = "Cut", DurationMinutes = 30, Price = 20, IsActive = true };
        var customer = new Customer { BarberId = barber.Id, Name = "Mohamed", Phone = "+15551110000" };
        db.Barbers.Add(barber);
        db.Services.Add(service);
        db.Customers.Add(customer);
        db.WorkingHours.Add(new WorkingHours { BarberId = barber.Id, DayOfWeek = (int)dayOfWeek, StartTime = "09:00", EndTime = "18:00", IsActive = true });
        db.SaveChanges();
        return (barber, service, customer);
    }

    [Fact]
    public async Task CreatesOccurrence_WhenSlotIsAvailable()
    {
        using var db = NewDb();
        var target = AsStoredDate(DateStr(DateTime.Now.Date.AddDays(1)));
        var (barber, service, customer) = SeedBarberServiceCustomer(db, target.DayOfWeek);
        var series = new RecurringSeries
        {
            BarberId = barber.Id, CustomerId = customer.Id, ServiceId = service.Id,
            DayOfWeek = (int)target.DayOfWeek, StartTime = "13:00", StartDate = target, EndDate = target, IsActive = true,
        };
        db.RecurringSeries.Add(series);
        await db.SaveChangesAsync();

        var result = await new RecurringAppointmentService(db, new AvailabilityService(db), EmptyConfig()).GenerateOccurrences();

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
        var target = AsStoredDate(DateStr(DateTime.Now.Date.AddDays(1)));
        var (barber, service, customer) = SeedBarberServiceCustomer(db, target.DayOfWeek);
        db.BlockedSlots.Add(new BlockedSlot { BarberId = barber.Id, Date = target, StartTime = null, EndTime = null, Reason = "Day off" });
        var series = new RecurringSeries
        {
            BarberId = barber.Id, CustomerId = customer.Id, ServiceId = service.Id,
            DayOfWeek = (int)target.DayOfWeek, StartTime = "13:00", StartDate = target, EndDate = target, IsActive = true,
        };
        db.RecurringSeries.Add(series);
        await db.SaveChangesAsync();

        var result = await new RecurringAppointmentService(db, new AvailabilityService(db), EmptyConfig()).GenerateOccurrences();

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(db.Appointments.Where(a => a.RecurringSeriesId == series.Id));
        var skip = Assert.Single(db.RecurringSkips.Where(s => s.RecurringSeriesId == series.Id));
        Assert.Equal("slot_unavailable", skip.Reason);
    }

    [Fact]
    public async Task SecondRun_DoesNotDuplicateAlreadyGeneratedOccurrence()
    {
        using var db = NewDb();
        var target = AsStoredDate(DateStr(DateTime.Now.Date.AddDays(1)));
        var (barber, service, customer) = SeedBarberServiceCustomer(db, target.DayOfWeek);
        var series = new RecurringSeries
        {
            BarberId = barber.Id, CustomerId = customer.Id, ServiceId = service.Id,
            DayOfWeek = (int)target.DayOfWeek, StartTime = "13:00", StartDate = target, EndDate = target, IsActive = true,
        };
        db.RecurringSeries.Add(series);
        await db.SaveChangesAsync();
        var generator = new RecurringAppointmentService(db, new AvailabilityService(db), EmptyConfig());

        await generator.GenerateOccurrences();
        var secondResult = await generator.GenerateOccurrences();

        Assert.Equal(0, secondResult.Created);
        Assert.Single(db.Appointments.Where(a => a.RecurringSeriesId == series.Id));
    }

    [Fact]
    public async Task AutoPausesSeries_WhenLinkedServiceIsDeactivated()
    {
        using var db = NewDb();
        var target = AsStoredDate(DateStr(DateTime.Now.Date.AddDays(1)));
        var (barber, service, customer) = SeedBarberServiceCustomer(db, target.DayOfWeek);
        service.IsActive = false;
        var series = new RecurringSeries
        {
            BarberId = barber.Id, CustomerId = customer.Id, ServiceId = service.Id,
            DayOfWeek = (int)target.DayOfWeek, StartTime = "13:00", StartDate = target, EndDate = target, IsActive = true,
        };
        db.RecurringSeries.Add(series);
        await db.SaveChangesAsync();

        var result = await new RecurringAppointmentService(db, new AvailabilityService(db), EmptyConfig()).GenerateOccurrences();

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
        var tomorrow = AsStoredDate(DateStr(DateTime.Now.Date.AddDays(1)));
        var pastStart = AsStoredDate(DateStr(DateTime.Now.Date.AddDays(1 - 21)));
        var endDate = AsStoredDate(DateStr(DateTime.Now.Date.AddDays(1 + 7)));
        var (barber, service, customer) = SeedBarberServiceCustomer(db, tomorrow.DayOfWeek);
        var series = new RecurringSeries
        {
            BarberId = barber.Id, CustomerId = customer.Id, ServiceId = service.Id,
            DayOfWeek = (int)tomorrow.DayOfWeek, StartTime = "13:00", StartDate = pastStart, EndDate = endDate, IsActive = true,
        };
        db.RecurringSeries.Add(series);
        await db.SaveChangesAsync();

        await new RecurringAppointmentService(db, new AvailabilityService(db), EmptyConfig()).GenerateOccurrences();

        var appointments = db.Appointments.Where(a => a.RecurringSeriesId == series.Id).ToList();
        var today = DateTime.Now.Date;
        Assert.All(appointments, a => Assert.True(a.Date >= today, $"appointment on {a.Date:yyyy-MM-dd} should not be in the past"));
        Assert.Contains(appointments, a => a.Date == tomorrow);
    }

    [Fact]
    public async Task RespectsEndDate_StopsGeneratingAndDeactivatesOncePassed()
    {
        using var db = NewDb();
        var target = AsStoredDate(DateStr(DateTime.Now.Date.AddDays(1)));
        var pastEnd = AsStoredDate(DateStr(DateTime.Now.Date.AddDays(-1)));
        var (barber, service, customer) = SeedBarberServiceCustomer(db, target.DayOfWeek);
        var series = new RecurringSeries
        {
            BarberId = barber.Id, CustomerId = customer.Id, ServiceId = service.Id,
            DayOfWeek = (int)target.DayOfWeek, StartTime = "13:00", StartDate = target.AddDays(-30), EndDate = pastEnd, IsActive = true,
        };
        db.RecurringSeries.Add(series);
        await db.SaveChangesAsync();

        var result = await new RecurringAppointmentService(db, new AvailabilityService(db), EmptyConfig()).GenerateOccurrences();

        Assert.Equal(0, result.Created);
        Assert.Empty(db.Appointments.Where(a => a.RecurringSeriesId == series.Id));
        var reloaded = await db.RecurringSeries.FindAsync(series.Id);
        Assert.False(reloaded!.IsActive);
    }
}
