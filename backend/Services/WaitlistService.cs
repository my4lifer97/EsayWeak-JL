using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

public class WaitlistService(AppDbContext db, IWhatsAppSender whatsAppSender, NotificationOutbox notifications, IConfiguration config, ILogger<WaitlistService> logger)
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
    // Called inside a request (an owner/customer cancel or reschedule), so the message itself goes
    // out after the response (NotificationQueue): the entry is marked NOTIFIED now, and a failed
    // send puts it back to WAITING for WaitlistQueueWorker to retry.
    public Task<(int Sent, int Failed)> NotifyForCancellation(Appointment cancelledAppointment) =>
        NotifyNextInQueue(cancelledAppointment, inBackground: true);

    // A slot whose start time has already passed isn't worth offering anymore. Local wall-clock
    // time, same convention as AvailabilityService.
    public static bool HasStarted(Appointment appointment) =>
        appointment.Date.Date.Add(TimeSpan.Parse(appointment.StartTime)) <= AppClock.Now;

    // Sends to the earliest-joined still-WAITING entry. If that send fails (bridge outage, bad
    // number) it moves on to the next one in the same pass rather than stalling the whole queue
    // on one customer -- the failed entry stays WAITING and gets picked up again on a later turn.
    // In the background (inBackground) the entry is only marked; see NotifyForCancellation.
    private async Task<(int Sent, int Failed)> NotifyNextInQueue(Appointment cancelledAppointment, bool inBackground = false)
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

        // Only AdvanceQueues checked this, so a cancel or reschedule after the start still sent
        // the first message, a link to a time the booking page no longer offers (QA run 4).
        if (HasStarted(cancelledAppointment))
        {
            foreach (var entry in entries) entry.Status = WaitlistEntryStatus.RESOLVED;
            return (0, 0);
        }

        // Someone may have booked over the freed slot without starting at exactly its time (or a
        // recurring series filled it), which ResolveForRebooking can miss. Offering it anyway
        // sends the customer to a link that says "not available", so end the queue instead.
        if (await IsSlotTaken(cancelledAppointment))
        {
            foreach (var entry in entries) entry.Status = WaitlistEntryStatus.RESOLVED;
            return (0, 0);
        }

        var item = await db.Items.FindAsync(cancelledAppointment.ItemId);

        var appUrl = config["AppUrl"] ?? "";
        var dateStr = cancelledAppointment.Date.ToString("yyyy-MM-dd");
        var deepLink = $"{appUrl}/{business.Slug}/book?itemId={cancelledAppointment.ItemId}&date={dateStr}&time={cancelledAppointment.StartTime}";

        int failed = 0;
        foreach (var entry in entries)
        {
            try
            {
                // In the language of the customer's last WhatsApp message, else the business's
                // chatbot/storefront language.
                var lang = entry.CustomerAccount.LastMessageLanguage
                    ?? (business.ChatbotDefaultLanguage ?? business.Language).ToString();
                var itemName = lang switch
                {
                    "AR" => item?.NameAr,
                    "HE" => item?.NameHe,
                    _ => item?.NameEn,
                };
                if (string.IsNullOrWhiteSpace(itemName)) itemName = item?.NameEn;
                var message = I18nService.T(lang, "whatsapp.waitlistSlotOpen", new()
                {
                    ["customerName"] = entry.CustomerAccount.Name,
                    ["businessName"] = business.Name,
                    ["service"] = itemName ?? "",
                    ["date"] = dateStr,
                    ["time"] = cancelledAppointment.StartTime,
                    ["url"] = deepLink,
                });

                if (inBackground)
                {
                    entry.Status = WaitlistEntryStatus.NOTIFIED;
                    entry.NotifiedAt = DateTime.UtcNow;
                    QueueSend(business, entry, message, cancelledAppointment.Id);
                    return (1, failed);
                }

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

    private void QueueSend(Business business, WaitlistEntry entry, string message, string appointmentId)
    {
        var entryId = entry.Id;
        var notifiedAt = entry.NotifiedAt;
        var phone = entry.CustomerAccount.Phone;
        notifications.Add($"waitlist notification for appointment {appointmentId} to waitlist entry {entryId}", async sp =>
        {
            try
            {
                await sp.GetRequiredService<IWhatsAppSender>().SendAsync(business, phone, message);
            }
            catch
            {
                // Not sent after all -- back in line, unless something else has moved it on since.
                var scopedDb = sp.GetRequiredService<AppDbContext>();
                var row = await scopedDb.WaitlistEntries.FindAsync(entryId);
                // (Postgres keeps microseconds, so compare with a tolerance.)
                if (row is not null && row.Status == WaitlistEntryStatus.NOTIFIED
                    && row.NotifiedAt is { } at && notifiedAt is { } sent && Math.Abs((at - sent).TotalMilliseconds) < 1)
                {
                    row.Status = WaitlistEntryStatus.WAITING;
                    row.NotifiedAt = null;
                    await scopedDb.SaveChangesAsync();
                }
                throw;
            }
        });
    }

    // Ends the queue for a slot the owner freed without offering it: every still-WAITING entry is
    // RESOLVED, so AdvanceQueues never picks it up. Does not call SaveChangesAsync.
    public async Task CloseQueue(Appointment appointment)
    {
        var entries = await db.WaitlistEntries
            .Where(w => w.AppointmentId == appointment.Id && w.Status == WaitlistEntryStatus.WAITING)
            .ToListAsync();
        foreach (var entry in entries) entry.Status = WaitlistEntryStatus.RESOLVED;
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

            if (HasStarted(appointment)) continue;

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

    // Rescheduling moves the appointment row itself (keeping its Id + CancelToken, so the links
    // the customer already has keep working), but for the waitlist it's a cancellation of the old
    // slot. Call this BEFORE changing the appointment's date/time: if anyone is waiting on it,
    // it leaves a CANCELLED copy behind at the old date/time and moves the waiting entries onto
    // it. Returns that copy (null when nobody was waiting) -- after the caller's save succeeds it
    // passes it to NotifyForCancellation and saves again, so no message goes out for a
    // reschedule that then failed on a slot conflict.
    public async Task<Appointment?> DetachForReschedule(Appointment appointment)
    {
        var entries = await db.WaitlistEntries
            .Where(w => w.AppointmentId == appointment.Id && w.Status != WaitlistEntryStatus.RESOLVED)
            .ToListAsync();
        if (entries.Count == 0) return null;

        var oldSlot = new Appointment
        {
            BusinessId = appointment.BusinessId,
            CustomerId = appointment.CustomerId,
            ItemId = appointment.ItemId,
            Date = appointment.Date,
            StartTime = appointment.StartTime,
            EndTime = appointment.EndTime,
            Notes = appointment.Notes,
            PhotoUrl = appointment.PhotoUrl,
            Status = AppointmentStatus.CANCELLED,
            ReminderSent = true,
            ReminderSentSoon = true,
            IsWaitlistCarrier = true,
        };
        db.Appointments.Add(oldSlot);
        foreach (var entry in entries)
            entry.AppointmentId = oldSlot.Id;
        return oldSlot;
    }

    // Flips any outstanding waitlist entry for a freed slot that a new booking overlaps to
    // RESOLVED, so stale entries don't linger or trigger a future notification for a slot that's
    // taken again. Overlap, not an identical start: a 09:30 booking takes a freed 09:00-10:00
    // slot just as much as a 09:00 one. Cheap no-op in the common case (a slot nobody was ever
    // waitlisted for) -- safe to call unconditionally from every booking/reschedule write path.
    // Does not call SaveChangesAsync.
    public async Task ResolveForRebooking(string businessId, DateTime date, string startTime, string endTime)
    {
        var entries = await db.WaitlistEntries
            .Include(w => w.Appointment)
            .Where(w => w.Status != WaitlistEntryStatus.RESOLVED
                && w.BusinessId == businessId
                && w.Appointment.Date == date
                && w.Appointment.Status == AppointmentStatus.CANCELLED)
            .ToListAsync();

        // "HH:MM" strings are zero-padded, so ordinal comparison is time order.
        foreach (var entry in entries.Where(w => Overlaps(w.Appointment.StartTime, w.Appointment.EndTime, startTime, endTime)))
            entry.Status = WaitlistEntryStatus.RESOLVED;
    }

    private async Task<bool> IsSlotTaken(Appointment freed)
    {
        var sameDay = await db.Appointments
            .Where(a => a.BusinessId == freed.BusinessId && a.Date == freed.Date
                && a.Status == AppointmentStatus.CONFIRMED && a.Id != freed.Id)
            .Select(a => new { a.StartTime, a.EndTime })
            .ToListAsync();
        return sameDay.Any(a => Overlaps(a.StartTime, a.EndTime, freed.StartTime, freed.EndTime));
    }

    private static bool Overlaps(string start1, string end1, string start2, string end2) =>
        string.CompareOrdinal(start1, end2) < 0 && string.CompareOrdinal(start2, end1) < 0;
}
