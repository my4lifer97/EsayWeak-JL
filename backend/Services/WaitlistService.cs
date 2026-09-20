using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

public class WaitlistService(AppDbContext db, IWhatsAppSender whatsAppSender, IConfiguration config, ILogger<WaitlistService> logger)
{
    // Notifies every WAITING entry for the appointment that just got cancelled. Silent no-op if
    // the business hasn't turned the feature on, nobody's waiting, or no WhatsApp number is linked
    // for this business (same permissive skip CronController.SendReminders already uses). Does not
    // call SaveChangesAsync -- the caller's own save persists the appointment status change and
    // these entries' NOTIFIED flips together. Safe to call again for the same appointment later
    // (see RetryFailedNotifications below) -- an entry that already succeeded is NOTIFIED, not
    // WAITING, so it's excluded from the query and never double-sent.
    public async Task<(int Sent, int Failed)> NotifyForCancellation(Appointment cancelledAppointment)
    {
        var business = await db.Businesses.FindAsync(cancelledAppointment.BusinessId);
        if (business is null || !business.WaitlistEnabled) return (0, 0);
        if (business.WhatsAppNumber is null) return (0, 0);

        var entries = await db.WaitlistEntries
            .Include(w => w.CustomerAccount)
            .Where(w => w.AppointmentId == cancelledAppointment.Id && w.Status == WaitlistEntryStatus.WAITING)
            .ToListAsync();
        if (entries.Count == 0) return (0, 0);

        var item = await db.Items.FindAsync(cancelledAppointment.ItemId);
        var lang = business.Language.ToString();
        var itemName = lang switch
        {
            "AR" => item?.NameAr,
            "HE" => item?.NameHe,
            _ => item?.NameEn,
        };

        var appUrl = config["AppUrl"] ?? "";
        var dateStr = cancelledAppointment.Date.ToString("yyyy-MM-dd");
        var deepLink = $"{appUrl}/{business.Slug}/book?itemId={cancelledAppointment.ItemId}&date={dateStr}&time={cancelledAppointment.StartTime}";

        int sent = 0, failed = 0;
        foreach (var entry in entries)
        {
            try
            {
                var message = I18nService.T(lang, "whatsapp.waitlistSlotOpen", new()
                {
                    ["customerName"] = entry.CustomerAccount.Name,
                    ["businessName"] = business.Name,
                    ["service"] = itemName ?? "",
                    ["date"] = dateStr,
                    ["time"] = cancelledAppointment.StartTime,
                    ["url"] = deepLink,
                });

                await whatsAppSender.SendAsync(business, entry.CustomerAccount.Phone, message);

                entry.Status = WaitlistEntryStatus.NOTIFIED;
                entry.NotifiedAt = DateTime.UtcNow;
                sent++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send waitlist notification for appointment {AppointmentId} to waitlist entry {WaitlistEntryId}",
                    cancelledAppointment.Id, entry.Id);
                failed++;
            }
        }
        return (sent, failed);
    }

    // Picks up any WAITING entry whose notification never went out (or failed) the first time --
    // NotifyForCancellation leaves an entry WAITING on send failure with no retry of its own, so
    // without this a transient bridge outage stranded that customer with no way to find out the
    // slot opened up. Meant to run periodically (see CronController.RetryWaitlistNotifications);
    // safe to call as often as needed since each attempt only ever touches still-WAITING entries.
    public async Task<(int Total, int Sent, int Failed)> RetryFailedNotifications()
    {
        var pendingAppointmentIds = await db.WaitlistEntries
            .Where(w => w.Status == WaitlistEntryStatus.WAITING)
            .Select(w => w.AppointmentId)
            .Distinct()
            .ToListAsync();

        int totalSent = 0, totalFailed = 0;
        foreach (var appointmentId in pendingAppointmentIds)
        {
            var appointment = await db.Appointments.FindAsync(appointmentId);
            // Only a CANCELLED appointment is ever a real "slot opened up" notification -- a
            // WAITING entry whose appointment isn't cancelled (yet) has nothing to retry.
            if (appointment is null || appointment.Status != AppointmentStatus.CANCELLED) continue;

            var (sent, failed) = await NotifyForCancellation(appointment);
            totalSent += sent;
            totalFailed += failed;
        }

        return (totalSent + totalFailed, totalSent, totalFailed);
    }

    // Flips any outstanding waitlist entry for a slot that just got (re)booked to RESOLVED, so
    // stale entries don't linger or trigger a future notification for a slot that's taken again.
    // Cheap no-op in the common case (a slot nobody was ever waitlisted for) -- safe to call
    // unconditionally from every booking/reschedule write path. Does not call SaveChangesAsync.
    public async Task ResolveForRebooking(string businessId, DateTime date, string startTime)
    {
        var entries = await db.WaitlistEntries
            .Include(w => w.Appointment)
            .Where(w => w.Status != WaitlistEntryStatus.RESOLVED
                && w.BusinessId == businessId
                && w.Appointment.Date == date
                && w.Appointment.StartTime == startTime
                && w.Appointment.Status == AppointmentStatus.CANCELLED)
            .ToListAsync();

        foreach (var entry in entries)
            entry.Status = WaitlistEntryStatus.RESOLVED;
    }
}
