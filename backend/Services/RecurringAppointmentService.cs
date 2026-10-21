using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

public class RecurringAppointmentService(AppDbContext db, AvailabilityService availability, IConfiguration config, ILogger<RecurringAppointmentService> logger)
{
    private DateTime Horizon(DateTime today) =>
        today.AddDays((config.GetValue<int?>("RecurringGeneration:HorizonWeeks") ?? 8) * 7);

    public async Task<(int Total, int Created, int Skipped)> GenerateOccurrences()
    {
        // Local wall-clock "today" -- RecurringSeries/Appointment dates are business-local,
        // never UTC-converted, same reasoning as AvailabilityService/CronController.
        var today = AppClock.Now.Date;
        var horizon = Horizon(today);

        // One series at a time, each saved on its own: a failure in one used to roll back the
        // whole run, for every business (QA run 4).
        var seriesIds = await db.RecurringSeries.Where(s => s.IsActive).Select(s => s.Id).ToListAsync();
        int total = 0, created = 0, skipped = 0;

        foreach (var id in seriesIds)
        {
            db.ChangeTracker.Clear();
            try
            {
                var s = await db.RecurringSeries.Include(x => x.Item).FirstAsync(x => x.Id == id);
                var (t, c, sk) = await GenerateForSeries(s, today, horizon);
                await db.SaveChangesAsync();
                total += t; created += c; skipped += sk;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Generating recurring series {SeriesId} failed; continuing with the others", id);
            }
        }
        db.ChangeTracker.Clear();
        return (total, created, skipped);
    }

    // Runs the same generation immediately for one just-created series, so its first
    // occurrence becomes a real (dashboard-visible, slot-blocking) Appointment right away
    // instead of waiting for the next daily cron run.
    // Returns how many dates were in range and how many occurrences were created.
    public async Task<(int Total, int Created)> GenerateForSeriesNow(string seriesId)
    {
        var today = AppClock.Now.Date;
        var horizon = Horizon(today);

        var s = await db.RecurringSeries.Include(x => x.Item).FirstOrDefaultAsync(x => x.Id == seriesId);
        if (s is null || !s.IsActive) return (0, 0);

        var (total, created, _) = await GenerateForSeries(s, today, horizon);
        await db.SaveChangesAsync();
        return (total, created);
    }

    private async Task<(int Total, int Created, int Skipped)> GenerateForSeries(RecurringSeries s, DateTime today, DateTime horizon)
    {
        int total = 0, created = 0, skipped = 0;

        if (!s.Item.IsActive || !s.Item.IsBookable || s.Item.DurationMinutes is null)
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
                var endTime = AvailabilityService.AddMinutes(s.StartTime, s.Item.DurationMinutes.Value);
                // Force mirrors CreateAdminAppointmentRequest.Force's one-off semantics: skip the
                // working-hours/breaks/blocked-slot check entirely and only still hard-block on an
                // exact conflicting-appointment overlap. Checked on every occurrence, not just the
                // first, since this loop re-evaluates availability for every future week too.
                // ForcedSlotError also stops a forced series from creating today's occurrence after
                // its start time has passed (an instantly "completed" visit).
                var slotOk = s.Force
                    ? AvailabilityService.ForcedSlotError(dateStr, s.StartTime, s.Item.DurationMinutes.Value) is null
                      && !await availability.HasConflictingAppointment(s.BusinessId, dateStr, s.StartTime, endTime)
                    : (await availability.GetAvailableSlots(s.BusinessId, dateStr, s.Item.DurationMinutes.Value)).Any(sl => sl.Start == s.StartTime);
                // Saved under the same per-date lock as every other booking write, so a slot taken
                // since the check above (or earlier in this run) is a skip, not a duplicate.
                if (slotOk && await availability.TryAddOccurrence(new Appointment
                    {
                        BusinessId = s.BusinessId,
                        CustomerId = s.CustomerId,
                        ItemId = s.ItemId,
                        Date = d,
                        StartTime = s.StartTime,
                        EndTime = endTime,
                        Notes = s.Notes,
                        Status = AppointmentStatus.CONFIRMED,
                        RecurringSeriesId = s.Id,
                    }))
                {
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
