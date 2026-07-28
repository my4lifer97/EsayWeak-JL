using BarberSaas.Api.Data;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

public class AvailabilityService(AppDbContext db)
{
    public async Task<List<TimeSlot>> GetAvailableSlots(string barberId, string dateStr, int serviceDuration)
    {
        var slots = await GetSlotsWithBookingInfo(barberId, dateStr, serviceDuration);
        return slots.Where(s => s.Available).Select(s => new TimeSlot(s.Start, s.End)).ToList();
    }

    // Same candidate-slot generation as GetAvailableSlots, but instead of dropping slots that
    // overlap a CONFIRMED appointment, flags them Available=false with that appointment's Id —
    // lets the customer-facing calendar show booked slots (for joining a waitlist) rather than
    // hiding them entirely. Breaks/blocked-slots are still fully excluded (not real bookings).
    public async Task<List<SlotWithBookingInfoDto>> GetSlotsWithBookingInfo(string barberId, string dateStr, int serviceDuration)
    {
        var date = DateTime.Parse(dateStr + "T00:00:00Z").ToUniversalTime();
        var dayOfWeek = (int)DateTime.Parse(dateStr).DayOfWeek;

        var workingHours = await db.WorkingHours
            .FirstOrDefaultAsync(w => w.BarberId == barberId && w.DayOfWeek == dayOfWeek && w.IsActive);

        if (workingHours is null) return [];

        var breaks = await db.Breaks
            .Where(b => b.BarberId == barberId && b.DayOfWeek == dayOfWeek)
            .ToListAsync();

        var blockedSlots = await db.BlockedSlots
            .Where(b => b.BarberId == barberId && b.Date == date)
            .ToListAsync();

        var existingAppointments = await db.Appointments
            .Where(a => a.BarberId == barberId && a.Date == date && a.Status == AppointmentStatus.CONFIRMED)
            .ToListAsync();

        if (blockedSlots.Any(b => b.StartTime is null)) return [];

        var blockedPeriods = breaks
            .Select(b => new TimeSlot(b.StartTime, b.EndTime))
            .Concat(blockedSlots
                .Where(b => b.StartTime is not null)
                .Select(b => new TimeSlot(b.StartTime!, b.EndTime!)))
            .ToList();

        var candidates = GenerateSlots(workingHours.StartTime, workingHours.EndTime, 30);

        // For today, don't offer slots that have already started — a customer booking at
        // 15:00 shouldn't see (or be able to grab) a 10:00 slot. WorkingHours/Appointment
        // times ("09:00", "17:30", ...) are the barber's local wall-clock hours, never
        // converted to/from UTC anywhere in this app — so "now" here must be local server
        // time too, not DateTime.UtcNow, or the comparison is off by the UTC offset (e.g. a
        // barber/customer 3 hours ahead of UTC would still see slots hours after they'd
        // actually passed).
        var now = DateTime.Now;
        var isToday = DateTime.Parse(dateStr).Date == now.Date;
        var nowTime = now.ToString("HH:mm");

        return candidates
            .Where(slot =>
            {
                var slotEnd = AddMinutes(slot.Start, serviceDuration);
                if (string.Compare(slotEnd, workingHours.EndTime, StringComparison.Ordinal) > 0) return false;
                if (isToday && string.Compare(slot.Start, nowTime, StringComparison.Ordinal) <= 0) return false;
                return !blockedPeriods.Any(b => Overlaps(slot.Start, slotEnd, b.Start, b.End));
            })
            .Select(slot =>
            {
                // slot.End here is the fixed 30-min generation interval, not serviceDuration's
                // end — matches GetAvailableSlots' pre-existing (unchanged) output shape.
                var serviceEnd = AddMinutes(slot.Start, serviceDuration);
                var booking = existingAppointments.FirstOrDefault(a => Overlaps(slot.Start, serviceEnd, a.StartTime, a.EndTime));
                return new SlotWithBookingInfoDto(slot.Start, slot.End, booking is null, booking?.Id);
            })
            .ToList();
    }

    public async Task<bool> HasConflictingAppointment(string barberId, string dateStr, string startTime, string endTime)
    {
        var date = DateTime.Parse(dateStr + "T00:00:00Z").ToUniversalTime();
        var existing = await db.Appointments
            .Where(a => a.BarberId == barberId && a.Date == date && a.Status == AppointmentStatus.CONFIRMED)
            .ToListAsync();
        return existing.Any(a => Overlaps(startTime, endTime, a.StartTime, a.EndTime));
    }

    // Wraps a booking/reschedule SaveChangesAsync that could collide with the partial unique
    // index on (BarberId, Date, StartTime) for CONFIRMED appointments (AppDbContext) -- the
    // DB-level guard against two requests claiming the same freed slot at once (e.g. two
    // waitlisted customers racing for a just-cancelled appointment). Returns false only if a
    // concurrent request genuinely won that exact slot (re-verified via HasConflictingAppointment
    // before swallowing the exception, so any other failure still surfaces as a real exception).
    public async Task<bool> TrySaveOrDetectConflict(string barberId, string dateStr, string startTime, string endTime)
    {
        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await HasConflictingAppointment(barberId, dateStr, startTime, endTime))
                return false;
            throw;
        }
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
