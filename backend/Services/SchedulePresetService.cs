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
    // un-reverted PresetSchedule per business is allowed at a time. The Default preset itself can't
    // be scheduled this way -- it's already the baseline everything reverts to.
    public async Task<PresetSchedule> ScheduleForRange(string businessId, string presetId, DateTime startDate, DateTime endDate)
    {
        var preset = await db.SchedulePresets.FirstOrDefaultAsync(p => p.Id == presetId && p.BusinessId == businessId)
            ?? throw new InvalidOperationException("Preset not found");
        if (preset.IsDefault)
            throw new InvalidOperationException("The default schedule is always the baseline -- it can't be scheduled for a date range.");

        var alreadyScheduled = await db.PresetSchedules.AnyAsync(s => s.BusinessId == businessId && !s.Reverted);
        if (alreadyScheduled)
            throw new InvalidOperationException("A scheduled preset change is already pending or active for this business");

        var defaultPreset = await GetOrCreateDefaultPreset(businessId);

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
