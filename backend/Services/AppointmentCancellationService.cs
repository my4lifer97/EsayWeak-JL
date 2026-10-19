using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

// Shared funnel for the places in this app that cancel an appointment, so the waitlist-notify
// hook is written once instead of duplicated at every call site. Caller still owns
// SaveChangesAsync -- keeps this composable with bulk-cancel loops (e.g. deleting a recurring
// series cancels every future occurrence in one save after the loop).
public class AppointmentCancellationService(AppDbContext db, WaitlistService waitlist, NotificationOutbox notifications, IConfiguration config, OwnerEmailHealth ownerEmailHealth)
{
    // False when the appointment changed since the caller loaded it (another cancel, keep or
    // replace got there first) -- the caller should answer 409 and send nothing.
    public async Task<bool> CancelAsync(Appointment appointment, bool notifyWaitlist)
    {
        if (appointment.Status == AppointmentStatus.CANCELLED
            || !await TryClaimAsync(appointment, AppointmentStatus.CANCELLED, pendingApproval: false)) return false;
        appointment.Status = AppointmentStatus.CANCELLED;
        appointment.PendingCancellationApproval = false;
        if (notifyWaitlist) await waitlist.NotifyForCancellation(appointment);
        // The owner chose not to offer the slot ("cancel silently", a plain owner cancel, a
        // deleted series). Left WAITING, WaitlistQueueWorker messaged them anyway (QA run 3).
        else await waitlist.CloseQueue(appointment);
        return true;
    }

    // Compare-and-set on the row as the caller loaded it. Simultaneous cancels (magic link, My
    // Bookings, owner) all passed their status checks and each queued a "slot open" message to the
    // same waitlist entry (QA run 3). The row lock makes the later UPDATE wait, then match nothing.
    // The date and time are compared too, so a request working from a row that was moved meanwhile
    // also loses. Commits on its own unless the caller opened a transaction (a reschedule claims
    // with the same status, just to hold the row until its move commits); the caller's
    // SaveChangesAsync then writes the same values again.
    public async Task<bool> TryClaimAsync(Appointment appointment, AppointmentStatus status, bool pendingApproval)
    {
        var (loadedStatus, loadedPending) = (appointment.Status, appointment.PendingCancellationApproval);
        var (loadedDate, loadedStart) = (appointment.Date, appointment.StartTime);
        var claimed = await db.Appointments
            .Where(a => a.Id == appointment.Id && a.Status == loadedStatus && a.PendingCancellationApproval == loadedPending
                && a.Date == loadedDate && a.StartTime == loadedStart)
            .ExecuteUpdateAsync(u => u
                .SetProperty(a => a.Status, status)
                .SetProperty(a => a.PendingCancellationApproval, pendingApproval));
        return claimed == 1;
    }

    // Entry point for the 3 customer-initiated cancel paths (magic-link, logged-in "My
    // Bookings", WhatsApp "cancel" keyword). Routes through the business's own choice: finalize
    // immediately like before (RequireApprovalOnCustomerCancel off, or we simply can't reach the
    // owner), or freeze the slot and text the owner to decide instead of guessing on their
    // behalf. Status deliberately stays CONFIRMED while frozen -- the slot keeps blocking
    // availability/booking exactly as it already did, no changes needed to that logic at all.
    // False when the appointment changed since the caller loaded it (see CancelAsync).
    public async Task<bool> CancelFromCustomerAsync(Appointment appointment)
    {
        var business = await db.Businesses.FindAsync(appointment.BusinessId);
        if (business is null || !business.RequireApprovalOnCustomerCancel)
            return await CancelAsync(appointment, notifyWaitlist: true);

        // Each channel the owner turned on, if it can actually reach them. WhatsApp also needs the
        // business's own bot number to send from; a blank contact falls back to the business's
        // own Phone/Email.
        var ownerWhatsApp = business.CancelApprovalWhatsAppNumber ?? business.Phone;
        var viaWhatsApp = business.CancelApprovalNotifyViaWhatsApp
            && business.WhatsAppNumber is not null
            && !string.IsNullOrWhiteSpace(ownerWhatsApp);
        var ownerEmail = business.CancelApprovalEmail ?? business.Email;
        // A Gmail channel known to be broken can't reach the owner either (QA run 3: the slot froze
        // while every approval email failed).
        var viaEmail = business.CancelApprovalNotifyViaEmail && !string.IsNullOrWhiteSpace(ownerEmail)
            && ownerEmailHealth.GetSnapshot().Status != "failing";

        // Can't ask anyone -- don't freeze a slot the owner will never hear about.
        if (!viaWhatsApp && !viaEmail)
            return await CancelAsync(appointment, notifyWaitlist: true);

        if (!await TryClaimAsync(appointment, AppointmentStatus.CONFIRMED, pendingApproval: true)) return false;
        appointment.PendingCancellationApproval = true;

        var customer = await db.Customers.FindAsync(appointment.CustomerId);
        var item = await db.Items.FindAsync(appointment.ItemId);
        // The language the owner picked in Settings (also their dashboard's language).
        var lang = business.Language.ToString();
        var itemName = lang switch
        {
            "AR" => item?.NameAr,
            "HE" => item?.NameHe,
            _ => item?.NameEn,
        };

        var appUrl = config["AppUrl"] ?? "";
        var message = I18nService.T(lang, "whatsapp.ownerCancellationApprovalNeeded", new()
        {
            ["customerName"] = customer?.Name ?? "",
            ["date"] = appointment.Date.ToString("yyyy-MM-dd"),
            ["time"] = appointment.StartTime,
            ["service"] = itemName ?? "",
            ["url"] = $"{appUrl}/admin/appointments",
        });

        // Best-effort on each channel, sent after the response (NotificationQueue) -- the
        // appointment stays frozen (PendingCancellationApproval already set above) whether or not
        // the owner could actually be reached. Email goes through the platform's own Gmail
        // (IOwnerEmailSender), the same sender as the owner credentials email from the
        // platform-admin panel, not the Brevo system-mail chain.
        if (viaWhatsApp)
            notifications.AddWhatsApp(business, ownerWhatsApp!, message,
                $"cancellation-approval WhatsApp to the owner of business {business.Id} for appointment {appointment.Id}");
        if (viaEmail)
            notifications.AddOwnerEmail(ownerEmail!,
                I18nService.T(lang, "email.ownerCancellationApprovalSubject", new() { ["businessName"] = business.Name }), message,
                $"cancellation-approval email to the owner of business {business.Id} for appointment {appointment.Id}");
        return true;
    }
}
