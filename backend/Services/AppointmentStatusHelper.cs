using BarberSaas.Api.Models;

namespace BarberSaas.Api.Services;

// The barber no longer manually marks an appointment "Completed" — once its end time has
// passed, the system treats a still-CONFIRMED appointment as completed automatically.
// CANCELLED (and any legacy manually-set COMPLETED) rows are returned as-is.
public static class AppointmentStatusHelper
{
    public static string EffectiveStatus(AppointmentStatus status, DateTime date, string endTime)
    {
        if (status != AppointmentStatus.CONFIRMED) return status.ToString();

        // EndTime ("17:30") is the barber's local wall-clock hour, never converted to/from
        // UTC anywhere in this app (see AvailabilityService) — compare against local server
        // time, not DateTime.UtcNow, or this is off by the UTC offset.
        var parts = endTime.Split(':');
        var endsAt = date.Date.AddHours(int.Parse(parts[0])).AddMinutes(int.Parse(parts[1]));
        return endsAt < DateTime.Now ? "COMPLETED" : status.ToString();
    }
}
