using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

public class WaitlistService(AppDbContext db, IWhatsAppSender whatsAppSender, IConfiguration config, ILogger<WaitlistService> logger)
{
    // Notifies every WAITING entry for the appointment that just got cancelled. Silent no-op if
    // the barber hasn't turned the feature on, nobody's waiting, or Twilio isn't configured for
    // this barber (same permissive skip CronController.SendReminders already uses). Does not
    // call SaveChangesAsync -- the caller's own save persists the appointment status change and
    // these entries' NOTIFIED flips together.
    public async Task NotifyForCancellation(Appointment cancelledAppointment)
    {
        var barber = await db.Barbers.FindAsync(cancelledAppointment.BarberId);
        if (barber is null || !barber.WaitlistEnabled) return;
        if (barber.TwilioNumber is null) return;

        var entries = await db.WaitlistEntries
            .Include(w => w.CustomerAccount)
            .Where(w => w.AppointmentId == cancelledAppointment.Id && w.Status == WaitlistEntryStatus.WAITING)
            .ToListAsync();
        if (entries.Count == 0) return;

        var service = await db.Services.FindAsync(cancelledAppointment.ServiceId);
        var lang = barber.Language.ToString();
        var serviceName = lang switch
        {
            "AR" => service?.NameAr,
            "HE" => service?.NameHe,
            _ => service?.NameEn,
        };

        var appUrl = config["AppUrl"] ?? "";
        var dateStr = cancelledAppointment.Date.ToString("yyyy-MM-dd");
        var deepLink = $"{appUrl}/{barber.Slug}/book?serviceId={cancelledAppointment.ServiceId}&date={dateStr}&time={cancelledAppointment.StartTime}";

        foreach (var entry in entries)
        {
            try
            {
                var message = I18nService.T(lang, "whatsapp.waitlistSlotOpen", new()
                {
                    ["customerName"] = entry.CustomerAccount.Name,
                    ["barberName"] = barber.Name,
                    ["service"] = serviceName ?? "",
                    ["date"] = dateStr,
                    ["time"] = cancelledAppointment.StartTime,
                    ["url"] = deepLink,
                });

                await whatsAppSender.SendAsync(barber, entry.CustomerAccount.Phone, message);

                entry.Status = WaitlistEntryStatus.NOTIFIED;
                entry.NotifiedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send waitlist notification for appointment {AppointmentId} to waitlist entry {WaitlistEntryId}",
                    cancelledAppointment.Id, entry.Id);
            }
        }
    }

    // Flips any outstanding waitlist entry for a slot that just got (re)booked to RESOLVED, so
    // stale entries don't linger or trigger a future notification for a slot that's taken again.
    // Cheap no-op in the common case (a slot nobody was ever waitlisted for) -- safe to call
    // unconditionally from every booking/reschedule write path. Does not call SaveChangesAsync.
    public async Task ResolveForRebooking(string barberId, DateTime date, string startTime)
    {
        var entries = await db.WaitlistEntries
            .Include(w => w.Appointment)
            .Where(w => w.Status != WaitlistEntryStatus.RESOLVED
                && w.BarberId == barberId
                && w.Appointment.Date == date
                && w.Appointment.StartTime == startTime
                && w.Appointment.Status == AppointmentStatus.CANCELLED)
            .ToListAsync();

        foreach (var entry in entries)
            entry.Status = WaitlistEntryStatus.RESOLVED;
    }
}
