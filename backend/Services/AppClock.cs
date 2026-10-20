namespace BarberSaas.Api.Services;

// "Now" on the businesses' wall clock. Appointment dates and "HH:MM" times are Israeli local time
// and never converted (see AvailabilityService), so they must be compared with Israeli "now".
// DateTime.Now is the server's zone, and Railway's containers run in UTC: today's past slots stayed
// bookable for 2-3 more hours, appointments turned COMPLETED late, and the "3 hours before"
// reminder went out once the appointment had already started.
public static class AppClock
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);
}
