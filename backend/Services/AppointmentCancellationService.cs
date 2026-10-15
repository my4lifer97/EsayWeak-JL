using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

// Shared funnel for the places in this app that cancel an appointment, so the waitlist-notify
// hook is written once instead of duplicated at every call site. Caller still owns
// SaveChangesAsync -- keeps this composable with bulk-cancel loops (e.g. deleting a recurring
// series cancels every future occurrence in one save after the loop).
public class AppointmentCancellationService(AppDbContext db, WaitlistService waitlist, NotificationOutbox notifications, IConfiguration config)
{
    public async Task CancelAsync(Appointment appointment, bool notifyWaitlist)
    {
        appointment.Status = AppointmentStatus.CANCELLED;
        appointment.PendingCancellationApproval = false;
        if (notifyWaitlist) await waitlist.NotifyForCancellation(appointment);
    }

    // Entry point for the 3 customer-initiated cancel paths (magic-link, logged-in "My
    // Bookings", WhatsApp "cancel" keyword). Routes through the business's own choice: finalize
    // immediately like before (RequireApprovalOnCustomerCancel off, or we simply can't reach the
    // owner), or freeze the slot and text the owner to decide instead of guessing on their
    // behalf. Status deliberately stays CONFIRMED while frozen -- the slot keeps blocking
    // availability/booking exactly as it already did, no changes needed to that logic at all.
    public async Task CancelFromCustomerAsync(Appointment appointment)
    {
        var business = await db.Businesses.FindAsync(appointment.BusinessId);
        if (business is null || !business.RequireApprovalOnCustomerCancel)
        {
            await CancelAsync(appointment, notifyWaitlist: true);
            return;
        }

        // Each channel the owner turned on, if it can actually reach them. WhatsApp also needs the
        // business's own bot number to send from; a blank contact falls back to the business's
        // own Phone/Email.
        var ownerWhatsApp = business.CancelApprovalWhatsAppNumber ?? business.Phone;
        var viaWhatsApp = business.CancelApprovalNotifyViaWhatsApp
            && business.WhatsAppNumber is not null
            && !string.IsNullOrWhiteSpace(ownerWhatsApp);
        var ownerEmail = business.CancelApprovalEmail ?? business.Email;
        var viaEmail = business.CancelApprovalNotifyViaEmail && !string.IsNullOrWhiteSpace(ownerEmail);

        // Can't ask anyone -- don't freeze a slot the owner will never hear about.
        if (!viaWhatsApp && !viaEmail)
        {
            await CancelAsync(appointment, notifyWaitlist: true);
            return;
        }

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
    }
}
