using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

public class WaitlistService(AppDbContext db, IWhatsAppSender whatsAppSender, IConfiguration config, ILogger<WaitlistService> logger)
{
    // Waitlist entries are offered the freed slot one at a time, in the order they joined
    // (CreatedAt): the first gets a message right away, and if the slot is still free
    // Waitlist:NotifyIntervalMinutes later (default 5) the next one gets it, and so on until
    // someone books it (ResolveForRebooking below flips every remaining entry to RESOLVED, which
    // stops the queue) or nobody is left. The follow-up sends are driven by
    // WaitlistQueueWorker calling AdvanceQueues every few seconds.
    public TimeSpan NotifyInterval => TimeSpan.FromMinutes(config.GetValue("Waitlist:NotifyIntervalMinutes", 5.0));

    // Sends the first message of the queue for the appointment that just got cancelled. Silent
    // no-op if the business hasn't turned the feature on, nobody's waiting, or no WhatsApp number
    // is linked for this business (same permissive skip CronController.SendReminders already
    // uses). Does not call SaveChangesAsync -- the caller's own save persists the appointment
    // status change and this entry's NOTIFIED flip together.
    public Task<(int Sent, int Failed)> NotifyForCancellation(Appointment cancelledAppointment) =>
        NotifyNextInQueue(cancelledAppointment);

    // Sends to the earliest-joined still-WAITING entry. If that send fails (bridge outage, bad
    // number) it moves on to the next one in the same pass rather than stalling the whole queue
    // on one customer -- the failed entry stays WAITING and gets picked up again on a later turn.
    private async Task<(int Sent, int Failed)> NotifyNextInQueue(Appointment cancelledAppointment)
    {
        var business = await db.Businesses.FindAsync(cancelledAppointment.BusinessId);
        if (business is null || !business.WaitlistEnabled) return (0, 0);
        if (business.WhatsAppNumber is null) return (0, 0);

        var entries = await db.WaitlistEntries
            .Include(w => w.CustomerAccount)
            .Where(w => w.AppointmentId == cancelledAppointment.Id && w.Status == WaitlistEntryStatus.WAITING)
            .OrderBy(w => w.CreatedAt)
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

        int failed = 0;
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
                return (1, failed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send waitlist notification for appointment {AppointmentId} to waitlist entry {WaitlistEntryId}",
                    cancelledAppointment.Id, entry.Id);
                failed++;
            }
        }
        return (0, failed);
    }

    // Moves every open queue forward one step where it's due: a cancelled appointment that still
    // has WAITING entries gets its next entry notified if nobody has been notified yet (the first
    // send failed, or the owner's cancel happened while the bridge was down) or if the last
    // notification is at least NotifyInterval old. Safe to call as often as needed -- a queue
    // that isn't due is skipped. Does not call SaveChangesAsync.
    public async Task<(int Total, int Sent, int Failed)> AdvanceQueues()
    {
        var pendingAppointmentIds = await db.WaitlistEntries
            .Where(w => w.Status == WaitlistEntryStatus.WAITING)
            .Select(w => w.AppointmentId)
            .Distinct()
            .ToListAsync();

        var now = DateTime.UtcNow;
        int totalSent = 0, totalFailed = 0;
        foreach (var appointmentId in pendingAppointmentIds)
        {
            var appointment = await db.Appointments.FindAsync(appointmentId);
            // Only a CANCELLED appointment is ever a real "slot opened up" notification -- a
            // WAITING entry whose appointment isn't cancelled (yet) has nothing to send.
            if (appointment is null || appointment.Status != AppointmentStatus.CANCELLED) continue;

            // A slot whose start time has already passed isn't worth offering anymore. Local
            // wall-clock time, same convention as AvailabilityService.
            if (appointment.Date.Date.Add(TimeSpan.Parse(appointment.StartTime)) <= DateTime.Now) continue;

            var lastNotifiedAt = await db.WaitlistEntries
                .Where(w => w.AppointmentId == appointmentId && w.Status == WaitlistEntryStatus.NOTIFIED)
                .MaxAsync(w => w.NotifiedAt);
            if (lastNotifiedAt is not null && now - lastNotifiedAt.Value < NotifyInterval) continue;

            var (sent, failed) = await NotifyNextInQueue(appointment);
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
