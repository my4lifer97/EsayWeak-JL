using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

public class RecurringAppointmentService(AppDbContext db, AvailabilityService availability, IConfiguration config)
{
    private DateTime Horizon(DateTime today) =>
        today.AddDays((config.GetValue<int?>("RecurringGeneration:HorizonWeeks") ?? 8) * 7);

    public async Task<(int Total, int Created, int Skipped)> GenerateOccurrences()
    {
        // Local wall-clock "today" -- RecurringSeries/Appointment dates are barber-local,
        // never UTC-converted, same reasoning as AvailabilityService/CronController.
        var today = DateTime.Now.Date;
        var horizon = Horizon(today);

        var series = await db.RecurringSeries.Include(s => s.Service).Where(s => s.IsActive).ToListAsync();
        int total = 0, created = 0, skipped = 0;

        foreach (var s in series)
        {
            var (t, c, sk) = await GenerateForSeries(s, today, horizon);
            total += t; created += c; skipped += sk;
        }

        await db.SaveChangesAsync();
        return (total, created, skipped);
    }

    // Runs the same generation immediately for one just-created series, so its first
    // occurrence becomes a real (dashboard-visible, slot-blocking) Appointment right away
    // instead of waiting for the next daily cron run.
    public async Task GenerateForSeriesNow(string seriesId)
    {
        var today = DateTime.Now.Date;
        var horizon = Horizon(today);

        var s = await db.RecurringSeries.Include(x => x.Service).FirstOrDefaultAsync(x => x.Id == seriesId);
        if (s is null || !s.IsActive) return;

        await GenerateForSeries(s, today, horizon);
        await db.SaveChangesAsync();
    }

    private async Task<(int Total, int Created, int Skipped)> GenerateForSeries(RecurringSeries s, DateTime today, DateTime horizon)
    {
        int total = 0, created = 0, skipped = 0;

        if (!s.Service.IsActive)
        {
            s.IsActive = false;
            db.RecurringSkips.Add(new RecurringSkip { RecurringSeriesId = s.Id, Date = today, Reason = "service_inactive" });
            return (total, created, skipped);
        }
        if (s.EndDate.HasValue && s.EndDate.Value < today) { s.IsActive = false; return (total, created, skipped); }

        var cursor = s.LastGeneratedThrough?.AddDays(1) ?? s.StartDate;
        if (cursor < today) cursor = today; // never backfill past/missed dates, including weeks missed while paused

        for (var d = cursor; d <= horizon; d = d.AddDays(1))
        {
            if (s.EndDate.HasValue && d > s.EndDate.Value) break;
            if ((int)d.DayOfWeek != s.DayOfWeek) continue;

            total++;
            var exists = await db.Appointments.AnyAsync(a => a.RecurringSeriesId == s.Id && a.Date == d);
            if (!exists)
            {
                var dateStr = d.ToString("yyyy-MM-dd");
                var slots = await availability.GetAvailableSlots(s.BarberId, dateStr, s.Service.DurationMinutes);
                if (slots.Any(sl => sl.Start == s.StartTime))
                {
                    db.Appointments.Add(new Appointment
                    {
                        BarberId = s.BarberId,
                        CustomerId = s.CustomerId,
                        ServiceId = s.ServiceId,
                        Date = d,
                        StartTime = s.StartTime,
                        EndTime = AvailabilityService.AddMinutes(s.StartTime, s.Service.DurationMinutes),
                        Notes = s.Notes,
                        Status = AppointmentStatus.CONFIRMED,
                        RecurringSeriesId = s.Id,
                    });
                    created++;
                }
                else
                {
                    db.RecurringSkips.Add(new RecurringSkip { RecurringSeriesId = s.Id, Date = d, Reason = "slot_unavailable" });
                    skipped++;
                }
            }
            s.LastGeneratedThrough = d;
        }

        return (total, created, skipped);
    }
}
