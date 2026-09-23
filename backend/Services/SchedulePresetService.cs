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

    // Replaces the business's entire live Breaks list with the given set -- unlike WorkingHours
    // there's no fixed 7-row shape to upsert against (zero or many breaks per day), so this is a
    // straight delete-then-insert rather than an upsert.
    public async Task ReplaceBreaks(string businessId, IEnumerable<(int DayOfWeek, string StartTime, string EndTime)> breaks)
    {
        var existing = await db.Breaks.Where(b => b.BusinessId == businessId).ToListAsync();
        db.Breaks.RemoveRange(existing);
        foreach (var b in breaks)
            db.Breaks.Add(new Break { BusinessId = businessId, DayOfWeek = b.DayOfWeek, StartTime = b.StartTime, EndTime = b.EndTime });
        await db.SaveChangesAsync();
    }

    // Every business always has exactly one Default preset -- the baseline schedule everything
    // else reverts to. Existing businesses got theirs backfilled by the AddDefaultSchedulePreset
    // migration; this lazily creates one (snapshotting whatever WorkingHours/Breaks currently hold)
    // for any business that somehow doesn't have one yet, so callers never have to special-case it.
    public async Task<SchedulePreset> GetOrCreateDefaultPreset(string businessId)
    {
        var existing = await db.SchedulePresets.FirstOrDefaultAsync(p => p.BusinessId == businessId && p.IsDefault);
        if (existing is not null) return existing;

        var currentHours = await db.WorkingHours.Where(w => w.BusinessId == businessId).ToListAsync();
        var currentBreaks = await db.Breaks.Where(b => b.BusinessId == businessId).ToListAsync();
        var preset = new SchedulePreset { BusinessId = businessId, Name = "Default", IsDefault = true };
        preset.Days = Enumerable.Range(0, 7).Select(dow =>
        {
            var wh = currentHours.FirstOrDefault(w => w.DayOfWeek == dow);
            return new SchedulePresetDay
            {
                SchedulePresetId = preset.Id,
                DayOfWeek = dow,
                StartTime = wh?.StartTime ?? "09:00",
                EndTime = wh?.EndTime ?? "18:00",
                IsActive = wh?.IsActive ?? false,
            };
        }).ToList();
        preset.Breaks = currentBreaks.Select(b => new SchedulePresetBreak
        {
            SchedulePresetId = preset.Id,
            DayOfWeek = b.DayOfWeek,
            StartTime = b.StartTime,
            EndTime = b.EndTime,
        }).ToList();
        db.SchedulePresets.Add(preset);
        await db.SaveChangesAsync();
        return preset;
    }

    public async Task<bool> HasActiveSchedule(string businessId) =>
        await db.PresetSchedules.AnyAsync(s => s.BusinessId == businessId && s.Applied && !s.Reverted);

    // Applies both a preset's Days (-> WorkingHours) and its Breaks (-> Breaks) -- each preset
    // carries its own break schedule, so applying one replaces the live breaks too, not just hours.
    public async Task ApplyPresetToWorkingHours(string presetId)
    {
        var preset = await db.SchedulePresets.Include(p => p.Days).Include(p => p.Breaks).FirstAsync(p => p.Id == presetId);
        await UpsertWorkingHours(preset.BusinessId, preset.Days.Select(d => (d.DayOfWeek, d.StartTime, d.EndTime, d.IsActive)));
        await ReplaceBreaks(preset.BusinessId, preset.Breaks.Select(b => (b.DayOfWeek, b.StartTime, b.EndTime)));
    }

    // "Schedule for a date range": the weekly template switches to `preset`'s hours on StartDate
    // and automatically reverts to the business's Default preset after EndDate. Only one
    // un-reverted PresetSchedule per business exists at a time -- scheduling again *replaces* the
    // existing one (same preset with new dates, or a different preset) rather than being refused,
    // so the owner can move a range without cancelling it first. The Default preset itself can't
    // be scheduled this way -- it's already the baseline everything reverts to.
    public async Task<PresetSchedule> ScheduleForRange(string businessId, string presetId, DateTime startDate, DateTime endDate)
    {
        var preset = await db.SchedulePresets.FirstOrDefaultAsync(p => p.Id == presetId && p.BusinessId == businessId)
            ?? throw new InvalidOperationException("Preset not found");
        if (preset.IsDefault)
            throw new InvalidOperationException("The default schedule is always the baseline -- it can't be scheduled for a date range.");

        var defaultPreset = await GetOrCreateDefaultPreset(businessId);
        var startsNow = startDate <= DateTime.Now.Date;

        var existing = await db.PresetSchedules.Where(s => s.BusinessId == businessId && !s.Reverted).ToListAsync();
        // A replaced range that's already running must hand the live hours back to Default -- unless
        // the new range starts now, in which case it overwrites them immediately below anyway.
        if (existing.Any(s => s.Applied) && !startsNow)
            await ApplyPresetToWorkingHours(defaultPreset.Id);
        db.PresetSchedules.RemoveRange(existing);

        var schedule = new PresetSchedule
        {
            BusinessId = businessId,
            PresetId = presetId,
            RevertToPresetId = defaultPreset.Id,
            StartDate = startDate,
            EndDate = endDate,
        };
        db.PresetSchedules.Add(schedule);

        // A range starting today or in the past takes effect immediately rather than waiting for
        // tomorrow's cron sweep.
        if (startsNow)
        {
            await ApplyPresetToWorkingHours(presetId);
            schedule.Applied = true;
        }

        await db.SaveChangesAsync();
        return schedule;
    }

    // "Use it from now on": makes `presetId` the business's standing schedule. Its days and breaks
    // are copied into the Default preset (the baseline every date range reverts to) as well as
    // applied live -- otherwise the old Default would silently come back the next time anything
    // reverts to it (saving Default, cancelling or finishing a date range). A date range that's
    // currently running is ended outright, since the owner just replaced the schedule it was
    // overriding; a pending (not-yet-started) range is left alone and still reverts to the new Default.
    public async Task ApplyPresetPermanently(string businessId, string presetId)
    {
        var preset = await db.SchedulePresets.Include(p => p.Days).Include(p => p.Breaks)
            .FirstAsync(p => p.Id == presetId && p.BusinessId == businessId);

        if (!preset.IsDefault)
        {
            var defaultPresetId = (await GetOrCreateDefaultPreset(businessId)).Id;
            var defaultPreset = await db.SchedulePresets.Include(p => p.Days).Include(p => p.Breaks)
                .FirstAsync(p => p.Id == defaultPresetId);
            foreach (var d in preset.Days)
            {
                var target = defaultPreset.Days.FirstOrDefault(x => x.DayOfWeek == d.DayOfWeek);
                if (target is null)
                {
                    defaultPreset.Days.Add(new SchedulePresetDay
                    {
                        SchedulePresetId = defaultPreset.Id, DayOfWeek = d.DayOfWeek, StartTime = d.StartTime, EndTime = d.EndTime, IsActive = d.IsActive,
                    });
                    continue;
                }
                target.StartTime = d.StartTime;
                target.EndTime = d.EndTime;
                target.IsActive = d.IsActive;
            }
            db.SchedulePresetBreaks.RemoveRange(defaultPreset.Breaks);
            defaultPreset.Breaks = preset.Breaks.Select(b => new SchedulePresetBreak
            {
                SchedulePresetId = defaultPreset.Id, DayOfWeek = b.DayOfWeek, StartTime = b.StartTime, EndTime = b.EndTime,
            }).ToList();
        }

        var running = await db.PresetSchedules.Where(s => s.BusinessId == businessId && s.Applied && !s.Reverted).ToListAsync();
        db.PresetSchedules.RemoveRange(running);
        await db.SaveChangesAsync();

        await ApplyPresetToWorkingHours(presetId);
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
