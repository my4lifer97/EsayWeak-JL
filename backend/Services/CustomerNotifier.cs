using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

// WhatsApp messages to a customer about something the owner did to their appointment (QA run 2:
// an owner cancel, reschedule or decision on a cancellation request reached nobody). Best-effort
// and sent after the response (NotificationOutbox), so a failed save sends nothing. Silent no-op
// when the business has no linked WhatsApp number, same as reminders and booking confirmations.
public class CustomerNotifier(AppDbContext db, NotificationOutbox notifications, IConfiguration config)
{
    // The owner cancelled a booking the customer still had.
    public Task QueueCancelledByOwner(Appointment appointment) =>
        Queue(appointment, "whatsapp.ownerCancelled", StorefrontUrl, "owner-cancel notice");

    // The owner approved the customer's own cancellation request.
    public Task QueueCancellationApproved(Appointment appointment) =>
        Queue(appointment, "whatsapp.cancellationApproved", null, "cancellation-approved notice");

    // The owner declined the customer's cancellation request -- the appointment is still on.
    public Task QueueCancellationDeclined(Appointment appointment) =>
        Queue(appointment, "whatsapp.cancellationDeclined", AppointmentUrl, "cancellation-declined notice");

    // Call after the appointment has moved; oldDate is "yyyy-MM-dd".
    public Task QueueRescheduledByOwner(Appointment appointment, string oldDate, string oldStartTime) =>
        Queue(appointment, "whatsapp.ownerRescheduled", AppointmentUrl, "owner-reschedule notice",
            new() { ["oldDate"] = oldDate, ["oldTime"] = oldStartTime });

    // The owner booked for the customer (directly, or by giving them someone else's slot).
    public Task QueueBookedByOwner(Appointment appointment) =>
        Queue(appointment, "whatsapp.bookingConfirmed", null, "booking confirmation");

    private async Task Queue(Appointment appointment, string key, Func<Business, Appointment, string>? url,
        string description, Dictionary<string, string>? extra = null)
    {
        var business = await db.Businesses.FindAsync(appointment.BusinessId);
        if (business?.WhatsAppNumber is null) return;
        var customer = await db.Customers.FindAsync(appointment.CustomerId);
        if (customer is null || string.IsNullOrWhiteSpace(customer.Phone)) return;
        var item = await db.Items.FindAsync(appointment.ItemId);

        // The language the chatbot last spoke with them in, like the waitlist message; a customer
        // the owner typed in may not have their account linked to this row yet, so match by phone.
        var lastLanguage = await db.CustomerAccounts
            .Where(a => a.Id == customer.CustomerAccountId || a.Phone == customer.Phone)
            .Select(a => a.LastMessageLanguage)
            .FirstOrDefaultAsync(l => l != null);
        var lang = lastLanguage ?? (business.ChatbotDefaultLanguage ?? business.Language).ToString();
        var itemName = lang switch { "AR" => item?.NameAr, "HE" => item?.NameHe, _ => item?.NameEn };
        if (string.IsNullOrWhiteSpace(itemName)) itemName = item?.NameEn;

        var args = new Dictionary<string, string>
        {
            ["customerName"] = customer.Name,
            ["businessName"] = business.Name,
            ["service"] = itemName ?? "",
            ["date"] = appointment.Date.ToString("yyyy-MM-dd"),
            ["time"] = appointment.StartTime,
        };
        if (url is not null) args["url"] = url(business, appointment);
        foreach (var (k, v) in extra ?? []) args[k] = v;

        notifications.AddWhatsApp(business, customer.Phone, I18nService.T(lang, key, args),
            $"{description} for appointment {appointment.Id} (business {business.Id})");
    }

    private string AppUrl => config["AppUrl"] ?? "";
    private string StorefrontUrl(Business business, Appointment _) => $"{AppUrl}/{business.Slug}";
    // The same magic link as the reminders: view, reschedule or cancel without logging in.
    private string AppointmentUrl(Business business, Appointment appointment) =>
        $"{AppUrl}/{business.Slug}/appointments/{appointment.Id}?token={appointment.CancelToken}";
}
