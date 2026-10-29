using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

// The day-before reminder's "will you come? Reply 1 to confirm or 2 to cancel" (no-show
// prevention). CronController parks it on the customer's conversation-state row as
// ChatbotMode "ConfirmAttendance:{appointmentId}" (no extra column), and WhatsAppController
// answers the next message with it before anything else. Only asked when the customer has no live
// conversation (a service list, a cancel question, a chat with the owner): their "1"/"2" belongs
// to that, so they get the plain reminder instead.
public static class AttendanceQuestion
{
    public const string ModePrefix = "ConfirmAttendance:";
    public const string Asked = "ASKED";
    public const string Confirmed = "CONFIRMED";

    public static string? AppointmentId(string? chatbotMode) =>
        chatbotMode is not null && chatbotMode.StartsWith(ModePrefix) ? chatbotMode[ModePrefix.Length..] : null;

    // Whether the question can be parked for this phone now: no live conversation row. Returns the
    // expired row to reuse (the (BusinessId, Phone) index is unique), or null to add one.
    public static async Task<(bool CanAsk, WhatsAppConversationState? Existing)> CanAskAsync(AppDbContext db, string businessId, string phone)
    {
        var state = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == businessId && s.Phone == phone);
        return state is null ? (true, null) : (state.ExpiresAt <= DateTime.UtcNow, state);
    }

    // The question stays open until the appointment starts. Returns the state row.
    public static WhatsAppConversationState Park(AppDbContext db, WhatsAppConversationState? existing, Appointment appointment, string lang)
    {
        var state = existing ?? new WhatsAppConversationState { BusinessId = appointment.BusinessId, Phone = appointment.Customer.Phone };
        if (existing is null) db.WhatsAppConversationStates.Add(state);
        state.ChatbotMode = ModePrefix + appointment.Id;
        state.Language = lang;
        state.InvalidAttempts = 0;
        state.AwaitingBookingCompletion = false;
        state.InquiryNotified = false;
        state.ShownItemIds = null;
        state.HistoryJson = null;
        var localStart = DateTime.SpecifyKind(appointment.Date.Date.Add(TimeSpan.Parse(appointment.StartTime)), DateTimeKind.Unspecified);
        state.ExpiresAt = TimeZoneInfo.ConvertTimeToUtc(localStart, AppClock.Zone);
        appointment.AttendanceStatus = Asked;
        return state;
    }
}
