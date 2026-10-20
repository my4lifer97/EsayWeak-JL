using BarberSaas.Api.Data;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

public class AvailabilityService(AppDbContext db)
{
    // excludeAppointmentId: the appointment being rescheduled. Without it, a booking blocked its
    // own neighbouring slots, so a 60-min 10:00 appointment could never move to 10:30.
    public async Task<List<TimeSlot>> GetAvailableSlots(string businessId, string dateStr, int serviceDuration, string? excludeAppointmentId = null)
    {
        var slots = await GetSlotsWithBookingInfo(businessId, dateStr, serviceDuration, excludeAppointmentId);
        return slots.Where(s => s.Available).Select(s => new TimeSlot(s.Start, s.End)).ToList();
    }

    // Same candidate-slot generation as GetAvailableSlots, but instead of dropping slots that
    // overlap a CONFIRMED appointment, flags them Available=false with that appointment's Id —
    // lets the customer-facing calendar show booked slots (for joining a waitlist) rather than
    // hiding them entirely. Breaks/blocked-slots are still fully excluded (not real bookings).
    public const int MaxDaysAhead = 365;

    public async Task<List<SlotWithBookingInfoDto>> GetSlotsWithBookingInfo(string businessId, string dateStr, int serviceDuration, string? excludeAppointmentId = null)
    {
        // A date entirely before today (not just "already passed today's slots", see the isToday
        // check further down) must never offer any slots at all -- otherwise it's booking-able,
        // and the resulting appointment reads back as already COMPLETED, letting anyone fabricate
        // an instant "verified visit" and leave a review for a business they never went to.
        if (DateTime.Parse(dateStr).Date < AppClock.Now.Date) return [];
        // Nor more than a year ahead -- QA booked 14 months out, past any schedule the owner keeps.
        if (DateTime.Parse(dateStr).Date > AppClock.Now.Date.AddDays(MaxDaysAhead)) return [];

        var date = DateTime.Parse(dateStr + "T00:00:00Z").ToUniversalTime();
        var day = await GetEffectiveDay(businessId, DateTime.Parse(dateStr).Date);
        if (!day.IsActive) return [];
        var startTime = day.StartTime;
        var endTime = day.EndTime;
        var breaks = day.Breaks;

        var blockedSlots = await db.BlockedSlots
            .Where(b => b.BusinessId == businessId && b.Date == date)
            .ToListAsync();

        var existingAppointments = await db.Appointments
            .Where(a => a.BusinessId == businessId && a.Date == date && a.Status == AppointmentStatus.CONFIRMED
                && a.Id != excludeAppointmentId)
            .ToListAsync();

        if (blockedSlots.Any(b => b.StartTime is null)) return [];

        var blockedPeriods = breaks
            .Concat(blockedSlots
                .Where(b => b.StartTime is not null)
                .Select(b => new TimeSlot(b.StartTime!, b.EndTime!)))
            .ToList();

        var candidates = GenerateSlots(startTime, endTime, 30);

        // For today, don't offer slots that have already started — a customer booking at
        // 15:00 shouldn't see (or be able to grab) a 10:00 slot. WorkingHours/Appointment
        // times ("09:00", "17:30", ...) are the business's local wall-clock hours, never
        // converted to/from UTC anywhere in this app — so "now" here must be local server
        // time too, not DateTime.UtcNow, or the comparison is off by the UTC offset (e.g. a
        // business/customer 3 hours ahead of UTC would still see slots hours after they'd
        // actually passed).
        var now = AppClock.Now;
        var isToday = DateTime.Parse(dateStr).Date == now.Date;
        var nowTime = now.ToString("HH:mm");

        return candidates
            .Where(slot =>
            {
                var slotEnd = AddMinutes(slot.Start, serviceDuration);
                if (string.Compare(slotEnd, endTime, StringComparison.Ordinal) > 0) return false;
                if (isToday && string.Compare(slot.Start, nowTime, StringComparison.Ordinal) <= 0) return false;
                return !blockedPeriods.Any(b => Overlaps(slot.Start, slotEnd, b.Start, b.End));
            })
            .Select(slot =>
            {
                // Slots start every 30 minutes, but each one ends when the service would: the
                // booking summary showed 13:00-13:30 for a 60-minute service (QA run 3).
                var serviceEnd = AddMinutes(slot.Start, serviceDuration);
                var booking = existingAppointments.FirstOrDefault(a => Overlaps(slot.Start, serviceEnd, a.StartTime, a.EndTime));
                return new SlotWithBookingInfoDto(slot.Start, serviceEnd, booking is null, booking?.Id);
            })
            .ToList();
    }

    // The hours and breaks that actually apply on `date`. Live WorkingHours/Breaks are a single
    // weekly template that a PresetSchedule only overwrites once its StartDate arrives (and only
    // puts back after EndDate), so on their own they're right for "this week" but wrong for any
    // date on the other side of a range boundary -- e.g. a customer booking a date inside a
    // not-yet-started holiday range, or a date after a running range ends. Resolve those from the
    // presets directly: inside the range -> the scheduled preset; outside a running range -> the
    // preset it reverts to (Default); otherwise the live template.
    public async Task<EffectiveDay> GetEffectiveDay(string businessId, DateTime date) =>
        (await LoadScheduleContext(businessId)).Resolve(date);

    // Which of the next `days` dates (starting today) the business is open on at all, per
    // GetEffectiveDay -- backs the public booking calendar, which used to filter by weekday alone.
    public async Task<List<string>> GetOpenDates(string businessId, int days)
    {
        var ctx = await LoadScheduleContext(businessId);
        var today = AppClock.Now.Date;

        // A date the owner closed entirely (full-day BlockedSlot, StartTime null) isn't offered in
        // the date picker at all -- the customer would only find "closed" after tapping it. A
        // partial block (e.g. 14:00-16:00) leaves the day listed, since other times are still open.
        // BlockedSlot.Date is stored as UTC midnight of the calendar date (see GetSlotsWithBookingInfo).
        var from = DateTime.Parse(today.ToString("yyyy-MM-dd") + "T00:00:00Z").ToUniversalTime();
        var to = from.AddDays(days);
        var fullyBlocked = (await db.BlockedSlots.AsNoTracking()
                .Where(b => b.BusinessId == businessId && b.StartTime == null && b.Date >= from && b.Date < to)
                .Select(b => b.Date)
                .ToListAsync())
            .Select(d => d.ToString("yyyy-MM-dd"))
            .ToHashSet();

        return Enumerable.Range(0, days)
            .Select(i => today.AddDays(i))
            .Where(d => ctx.Resolve(d).IsActive)
            .Select(d => d.ToString("yyyy-MM-dd"))
            .Where(d => !fullyBlocked.Contains(d))
            .ToList();
    }

    // The opening hours of the next `days` dates for the storefront (QA run 2: it showed none), per
    // GetEffectiveDay so a scheduled preset range shows its own hours. A closed or fully blocked
    // date has null times.
    public async Task<List<OpeningDayDto>> GetUpcomingHours(string businessId, int days)
    {
        var ctx = await LoadScheduleContext(businessId);
        var open = (await GetOpenDates(businessId, days)).ToHashSet();
        var today = AppClock.Now.Date;
        return Enumerable.Range(0, days).Select(i =>
        {
            var date = today.AddDays(i);
            var key = date.ToString("yyyy-MM-dd");
            var day = ctx.Resolve(date);
            return open.Contains(key)
                ? new OpeningDayDto(key, (int)date.DayOfWeek, day.StartTime, day.EndTime)
                : new OpeningDayDto(key, (int)date.DayOfWeek, null, null);
        }).ToList();
    }

    // Everything GetEffectiveDay needs, loaded once so resolving many dates costs no extra queries.
    private async Task<ScheduleContext> LoadScheduleContext(string businessId)
    {
        var schedule = await db.PresetSchedules.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BusinessId == businessId && !s.Reverted);
        var presetIds = schedule is null ? [] : new[] { schedule.PresetId, schedule.RevertToPresetId };
        var presets = await db.SchedulePresets.AsNoTracking().Include(p => p.Days).Include(p => p.Breaks)
            .Where(p => presetIds.Contains(p.Id)).ToListAsync();
        var hours = await db.WorkingHours.AsNoTracking().Where(w => w.BusinessId == businessId).ToListAsync();
        var breaks = await db.Breaks.AsNoTracking().Where(b => b.BusinessId == businessId).ToListAsync();
        return new ScheduleContext(schedule, presets, hours, breaks);
    }

    private record ScheduleContext(PresetSchedule? Schedule, List<SchedulePreset> Presets, List<WorkingHours> Hours, List<Break> Breaks)
    {
        public EffectiveDay Resolve(DateTime date)
        {
            date = date.Date;
            var dayOfWeek = (int)date.DayOfWeek;

            string? presetId = null;
            if (Schedule is not null)
            {
                if (date >= Schedule.StartDate.Date && date <= Schedule.EndDate.Date) presetId = Schedule.PresetId;
                else if (Schedule.Applied) presetId = Schedule.RevertToPresetId;
            }

            var preset = presetId is null ? null : Presets.FirstOrDefault(p => p.Id == presetId);
            if (preset is not null)
            {
                var pd = preset.Days.FirstOrDefault(d => d.DayOfWeek == dayOfWeek);
                return new EffectiveDay(pd?.IsActive ?? false, pd?.StartTime ?? "", pd?.EndTime ?? "",
                    preset.Breaks.Where(b => b.DayOfWeek == dayOfWeek).Select(b => new TimeSlot(b.StartTime, b.EndTime)).ToList());
            }

            var wh = Hours.FirstOrDefault(w => w.DayOfWeek == dayOfWeek);
            return new EffectiveDay(wh?.IsActive ?? false, wh?.StartTime ?? "", wh?.EndTime ?? "",
                Breaks.Where(b => b.DayOfWeek == dayOfWeek).Select(b => new TimeSlot(b.StartTime, b.EndTime)).ToList());
        }
    }

    // Lets the customer-facing booking flow explain WHY a date has no slots, instead of a bare
    // "no available times". Returns null when the date isn't blocked at all (e.g. just no working
    // hours that weekday).
    public async Task<BlockedDayInfo?> GetFullDayBlockInfo(string businessId, string dateStr)
    {
        var date = DateTime.Parse(dateStr + "T00:00:00Z").ToUniversalTime();
        var blocked = await db.BlockedSlots.FirstOrDefaultAsync(b => b.BusinessId == businessId && b.Date == date && b.StartTime == null);
        return blocked is null ? null : new BlockedDayInfo(blocked.Reason);
    }

    // Reads what's actually stored (AsNoTracking), so an appointment being moved in this request
    // is seen at its old time -- callers saving it pass its id in `excludeIds`.
    public async Task<bool> HasConflictingAppointment(string businessId, string dateStr, string startTime, string endTime,
        IReadOnlyCollection<string>? excludeIds = null)
    {
        var date = DateTime.Parse(dateStr + "T00:00:00Z").ToUniversalTime();
        var existing = await db.Appointments.AsNoTracking()
            .Where(a => a.BusinessId == businessId && a.Date == date && a.Status == AppointmentStatus.CONFIRMED)
            .ToListAsync();
        return existing.Any(a => (excludeIds is null || !excludeIds.Contains(a.Id)) && Overlaps(startTime, endTime, a.StartTime, a.EndTime));
    }

    // Saves a booking/reschedule only if nothing CONFIRMED overlaps it. The callers' earlier
    // GetAvailableSlots check isn't enough on its own: two requests for overlapping-but-different
    // start times (09:00 for 60 min, 09:30 for 30 min) can both pass it before either saves, and the
    // partial unique index on (BusinessId, Date, StartTime) only catches identical starts. So on
    // Postgres the overlap re-check and the save run inside one transaction holding an advisory lock
    // per business+day, which serializes every booking write for that day. SQLite (tests) has no
    // advisory locks but only ever has one writer at a time anyway. The unique index stays as a
    // backstop; a DbUpdateException is still re-verified before being reported as a conflict.
    public async Task<bool> TrySaveOrDetectConflict(string businessId, string dateStr, string startTime, string endTime)
    {
        // The appointment(s) this request is adding or moving -- not a conflict with themselves.
        var savingIds = db.ChangeTracker.Entries<Appointment>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(e => e.Entity.Id)
            .ToList();

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync() : null;
        try
        {
            if (db.Database.IsNpgsql())
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtext({"booking:" + businessId + ":" + dateStr}))");

            if (await HasConflictingAppointment(businessId, dateStr, startTime, endTime, savingIds))
            {
                db.ChangeTracker.Clear();
                return false;
            }

            await db.SaveChangesAsync();
            if (tx is not null) await tx.CommitAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            if (tx is not null) await tx.RollbackAsync();
            db.ChangeTracker.Clear();
            if (await HasConflictingAppointment(businessId, dateStr, startTime, endTime, savingIds))
                return false;
            throw;
        }
    }

    // Per-customer limits (Business.MaxBookingsPerDay/Week), matched by phone: CONFIRMED bookings
    // with this business on the requested day and its Sun-Sat week. A reschedule passes the row it
    // moves, which doesn't count against itself. Null when the booking is within the limits.
    public async Task<string?> BookingLimitError(Business business, string phone, DateTime requestedDate, string? excludeAppointmentId = null)
    {
        if (business.MaxBookingsPerDay is null && business.MaxBookingsPerWeek is null) return null;
        // The day is inside its Sun-Sat week, so that week's rows answer both limits.
        var weekStart = requestedDate.AddDays(-(int)requestedDate.DayOfWeek);
        var weekEnd = weekStart.AddDays(6);
        var existingDates = await db.Appointments
            .Where(a => a.BusinessId == business.Id && a.Customer.Phone == phone && a.Status == AppointmentStatus.CONFIRMED
                && a.Id != excludeAppointmentId && a.Date >= weekStart && a.Date <= weekEnd)
            .Select(a => a.Date)
            .ToListAsync();

        if (business.MaxBookingsPerDay is not null && existingDates.Count(d => d == requestedDate) >= business.MaxBookingsPerDay)
            return "You've reached the maximum number of bookings allowed per day.";
        if (business.MaxBookingsPerWeek is not null && existingDates.Count >= business.MaxBookingsPerWeek)
            return "You've reached the maximum number of bookings allowed per week.";
        return null;
    }

    // BookingLimitError under the per-customer lock, for a caller already inside a transaction:
    // the lock is held until it commits, so that customer's parallel bookings and reschedules count
    // each other (QA run 3: 4 requests at once against "1 per day" gave 3 bookings). Take it before
    // TrySaveOrDetectConflict's per-date lock, never after, so no deadlock.
    public async Task<string?> LockedBookingLimitError(Business business, string phone, DateTime requestedDate, string? excludeAppointmentId = null)
    {
        if (business.MaxBookingsPerDay is null && business.MaxBookingsPerWeek is null) return null;
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({"booking-limit:" + business.Id + ":" + phone}))");
        return await BookingLimitError(business, phone, requestedDate, excludeAppointmentId);
    }

    // A forced (walk-in) booking skips GetAvailableSlots, and with it the checks that come for free
    // there: a real "HH:MM" time, ending by midnight, and not already in the past. Without them an
    // owner could save "23:30" + 60 min as ending "24:30", or a past-dated booking that counts as
    // completed at once and unlocks a verified review. Returns null when the slot is fine.
    public static string? ForcedSlotError(string date, string startTime, int durationMinutes)
    {
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var day))
            return "Invalid date";
        if (SlotTimeError(startTime, durationMinutes) is { } timeError) return timeError;
        if (day.AddMinutes(int.Parse(startTime[..2]) * 60 + int.Parse(startTime[3..])) <= AppClock.Now)
            return "This time has already passed";
        return null;
    }

    // The date-independent half of ForcedSlotError -- also used for a forced recurring series,
    // whose individual dates are checked as each occurrence is generated.
    public static string? SlotTimeError(string startTime, int durationMinutes)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(startTime ?? "", @"^([01]\d|2[0-3]):[0-5]\d$"))
            return "Invalid time";
        var startMinutes = int.Parse(startTime![..2]) * 60 + int.Parse(startTime[3..]);
        return startMinutes + durationMinutes > 24 * 60 ? "The appointment must end by midnight" : null;
    }

    public static string AddMinutes(string time, int minutes)
    {
        var parts = time.Split(':');
        var total = int.Parse(parts[0]) * 60 + int.Parse(parts[1]) + minutes;
        return $"{total / 60:D2}:{total % 60:D2}";
    }

    private static bool Overlaps(string s1, string e1, string s2, string e2) =>
        string.Compare(s1, e2, StringComparison.Ordinal) < 0 &&
        string.Compare(e1, s2, StringComparison.Ordinal) > 0;

    private static List<TimeSlot> GenerateSlots(string start, string end, int intervalMinutes)
    {
        var slots = new List<TimeSlot>();
        var current = start;
        while (string.Compare(AddMinutes(current, intervalMinutes), end, StringComparison.Ordinal) <= 0)
        {
            slots.Add(new TimeSlot(current, AddMinutes(current, intervalMinutes)));
            current = AddMinutes(current, intervalMinutes);
        }
        return slots;
    }
}
