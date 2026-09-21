using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

// Shared between AdminController (owner-triggered actions) and CronController (the daily
// apply/revert sweep) so both sides use the exact same upsert/apply/revert logic.
public class SchedulePresetService(AppDbContext db)
{
    // Same upsert-by-DayOfWeek semantics the "Save Working Hours" form itself uses -- one row per
    // (BusinessId, DayOfWeek), update in place if it exists.
    public async Task UpsertWorkingHours(string businessId, IEnumerable<(int DayOfWeek, string StartTime, string EndTime, bool IsActive)> days)
    {
        foreach (var h in days)
        {
            var existing = await db.WorkingHours
                .FirstOrDefaultAsync(w => w.BusinessId == businessId && w.DayOfWeek == h.DayOfWeek);
            if (existing is not null)
            {
                existing.StartTime = h.StartTime;
                existing.EndTime = h.EndTime;
                existing.IsActive = h.IsActive;
            }
            else
            {
                db.WorkingHours.Add(new WorkingHours
                {
                    BusinessId = businessId,
                    DayOfWeek = h.DayOfWeek,
                    StartTime = h.StartTime,
                    EndTime = h.EndTime,
                    IsActive = h.IsActive,
                });
            }
        }
        await db.SaveChangesAsync();
    }

    // Used only for the automatic backup a scheduled range creates (see ScheduleForRange) -- the
    // preset editor modal itself always saves caller-supplied Days, not a snapshot of the DB.
    public async Task<SchedulePreset> SnapshotCurrentHoursAsPreset(string businessId, string name)
    {
        var currentHours = await db.WorkingHours.Where(w => w.BusinessId == businessId).ToListAsync();
        var preset = new SchedulePreset { BusinessId = businessId, Name = name };
        preset.Days = Enumerable.Range(0, 7).Select(dow =>
        {
            var existing = currentHours.FirstOrDefault(w => w.DayOfWeek == dow);
            return new SchedulePresetDay
            {
                SchedulePresetId = preset.Id,
                DayOfWeek = dow,
                StartTime = existing?.StartTime ?? "09:00",
                EndTime = existing?.EndTime ?? "18:00",
                IsActive = existing?.IsActive ?? false,
            };
        }).ToList();
        db.SchedulePresets.Add(preset);
        await db.SaveChangesAsync();
        return preset;
    }

    public async Task ApplyPresetToWorkingHours(string presetId)
    {
        var preset = await db.SchedulePresets.Include(p => p.Days).FirstAsync(p => p.Id == presetId);
        await UpsertWorkingHours(preset.BusinessId, preset.Days.Select(d => (d.DayOfWeek, d.StartTime, d.EndTime, d.IsActive)));
    }

    // "Schedule for a date range": the weekly template switches to `preset`'s hours on StartDate
    // and automatically reverts after EndDate. Always creates a backup preset from whatever the
    // template is right now, so reverting restores exactly that -- not just "whatever the template
    // happens to be by EndDate" if it gets edited in the meantime. Only one un-reverted
    // PresetSchedule per business is allowed at a time.
    public async Task<PresetSchedule> ScheduleForRange(string businessId, string presetId, DateTime startDate, DateTime endDate)
    {
        var preset = await db.SchedulePresets.FirstOrDefaultAsync(p => p.Id == presetId && p.BusinessId == businessId)
            ?? throw new InvalidOperationException("Preset not found");

        var alreadyScheduled = await db.PresetSchedules.AnyAsync(s => s.BusinessId == businessId && !s.Reverted);
        if (alreadyScheduled)
            throw new InvalidOperationException("A scheduled preset change is already pending or active for this business");

        var backup = await SnapshotCurrentHoursAsPreset(businessId, $"Before {preset.Name} ({DateTime.Now:yyyy-MM-dd})");

        var schedule = new PresetSchedule
        {
            BusinessId = businessId,
            PresetId = presetId,
            RevertToPresetId = backup.Id,
            StartDate = startDate,
            EndDate = endDate,
        };
        db.PresetSchedules.Add(schedule);

        // A range starting today or in the past takes effect immediately rather than waiting for
        // tomorrow's cron sweep.
        if (startDate <= DateTime.Now.Date)
        {
            await ApplyPresetToWorkingHours(presetId);
            schedule.Applied = true;
        }

        await db.SaveChangesAsync();
        return schedule;
    }

    // "Close" a pending or active scheduled change. Pending (not yet Applied) just removes the
    // row; active (Applied, not yet Reverted) reverts the weekly template back to the backup first.
    public async Task CancelPresetSchedule(string businessId, string presetScheduleId)
    {
        var schedule = await db.PresetSchedules.FirstOrDefaultAsync(s => s.Id == presetScheduleId && s.BusinessId == businessId)
            ?? throw new InvalidOperationException("Scheduled change not found");

        if (schedule.Applied && !schedule.Reverted)
            await ApplyPresetToWorkingHours(schedule.RevertToPresetId);

        db.PresetSchedules.Remove(schedule);
        await db.SaveChangesAsync();
    }

    // The daily cron sweep (CronController): applies any schedule whose start date has arrived,
    // and reverts any applied schedule whose end date has passed.
    public async Task<(int Applied, int Reverted)> ApplyDueScheduledPresets()
    {
        var today = DateTime.Now.Date;
        int applied = 0, reverted = 0;

        var toApply = await db.PresetSchedules.Where(s => !s.Applied && s.StartDate <= today).ToListAsync();
        foreach (var s in toApply)
        {
            await ApplyPresetToWorkingHours(s.PresetId);
            s.Applied = true;
            applied++;
        }

        var toRevert = await db.PresetSchedules.Where(s => s.Applied && !s.Reverted && s.EndDate < today).ToListAsync();
        foreach (var s in toRevert)
        {
            await ApplyPresetToWorkingHours(s.RevertToPresetId);
            db.PresetSchedules.Remove(s);
            reverted++;
        }

        await db.SaveChangesAsync();
        return (applied, reverted);
    }
}
