using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

// Shared funnel for the places in this app that cancel an appointment, so the waitlist-notify
// hook is written once instead of duplicated at every call site. Caller still owns
// SaveChangesAsync -- keeps this composable with bulk-cancel loops (e.g. deleting a recurring
// series cancels every future occurrence in one save after the loop).
public class AppointmentCancellationService(AppDbContext db, WaitlistService waitlist, IWhatsAppSender whatsAppSender, IConfiguration config)
{
    public async Task CancelAsync(Appointment appointment, bool notifyWaitlist)
    {
        appointment.Status = AppointmentStatus.CANCELLED;
        appointment.PendingCancellationApproval = false;
        if (notifyWaitlist) await waitlist.NotifyForCancellation(appointment);
    }

    // Entry point for the 3 customer-initiated cancel paths (magic-link, logged-in "My
    // Bookings", WhatsApp "cancel" keyword). Routes through the barber's own choice: finalize
    // immediately like before (RequireApprovalOnCustomerCancel off, or we simply can't reach the
    // owner), or freeze the slot and text the owner to decide instead of guessing on their
    // behalf. Status deliberately stays CONFIRMED while frozen -- the slot keeps blocking
    // availability/booking exactly as it already did, no changes needed to that logic at all.
    public async Task CancelFromCustomerAsync(Appointment appointment)
    {
        var barber = await db.Barbers.FindAsync(appointment.BarberId);
        var canNotifyOwner = barber is not null && barber.RequireApprovalOnCustomerCancel
            && barber.TwilioNumber is not null
            && !string.IsNullOrWhiteSpace(barber.Phone);

        if (!canNotifyOwner)
        {
            await CancelAsync(appointment, notifyWaitlist: true);
            return;
        }

        appointment.PendingCancellationApproval = true;

        var customer = await db.Customers.FindAsync(appointment.CustomerId);
        var service = await db.Services.FindAsync(appointment.ServiceId);
        var lang = barber!.Language.ToString();
        var serviceName = lang switch
        {
            "AR" => service?.NameAr,
            "HE" => service?.NameHe,
            _ => service?.NameEn,
        };

        var appUrl = config["AppUrl"] ?? "";
        var message = I18nService.T(lang, "whatsapp.ownerCancellationApprovalNeeded", new()
        {
            ["customerName"] = customer?.Name ?? "",
            ["date"] = appointment.Date.ToString("yyyy-MM-dd"),
            ["time"] = appointment.StartTime,
            ["service"] = serviceName ?? "",
            ["url"] = $"{appUrl}/admin/appointments",
        });

        await whatsAppSender.SendAsync(barber, barber.Phone!, message);
    }
}
