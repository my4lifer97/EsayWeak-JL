using BarberSaas.Api.Data;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

public class AvailabilityService(AppDbContext db)
{
    public async Task<List<TimeSlot>> GetAvailableSlots(string barberId, string dateStr, int serviceDuration)
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
            .Concat(existingAppointments
                .Select(a => new TimeSlot(a.StartTime, a.EndTime)))
            .ToList();

        var candidates = GenerateSlots(workingHours.StartTime, workingHours.EndTime, 30);

        return candidates.Where(slot =>
        {
            var slotEnd = AddMinutes(slot.Start, serviceDuration);
            if (string.Compare(slotEnd, workingHours.EndTime, StringComparison.Ordinal) > 0) return false;
            return !blockedPeriods.Any(b => Overlaps(slot.Start, slotEnd, b.Start, b.End));
        }).ToList();
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
