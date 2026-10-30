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

    // After the answer the row stays a few minutes as "AttendanceAnswered:{id}", so a repeated
    // "1"/"2" (a double tap) is answered about the appointment instead of opening the menu.
    public const string AnsweredPrefix = "AttendanceAnswered:";
    public static readonly TimeSpan AnsweredWindow = TimeSpan.FromMinutes(10);

    public static string? AnsweredAppointmentId(string? chatbotMode) =>
        chatbotMode is not null && chatbotMode.StartsWith(AnsweredPrefix) ? chatbotMode[AnsweredPrefix.Length..] : null;

    public static void MarkAnswered(WhatsAppConversationState state, string appointmentId)
    {
        state.ChatbotMode = AnsweredPrefix + appointmentId;
        state.InvalidAttempts = 0;
        state.ExpiresAt = DateTime.UtcNow.Add(AnsweredWindow);
    }

    // Whether the question can be parked for this phone now: no live conversation row. Returns the
    // expired row to reuse (the (BusinessId, Phone) index is unique), or null to add one.
    public static async Task<(bool CanAsk, WhatsAppConversationState? Existing)> CanAskAsync(AppDbContext db, string businessId, string phone)
    {
        var state = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == businessId && s.Phone == phone);
        if (state is null) return (true, null);
        if (state.ExpiresAt <= DateTime.UtcNow) return (true, state);
        // An open question about an appointment that was cancelled since stays for a late "1"/"2"
        // (answered with "it was cancelled"), but it no longer needs the row: it kept the customer's
        // other appointment from being asked about (QA run 10).
        var openFor = AppointmentId(state.ChatbotMode);
        return (openFor is not null && await db.Appointments.AnyAsync(a => a.Id == openFor && a.Status == AppointmentStatus.CANCELLED), state);
    }

    // A moved appointment drops its open (or just answered) question: kept with its old expiry, a
    // later "1" confirmed the new time without asking about it (QA run 5). Saved by the caller.
    public static async Task ForgetAsync(AppDbContext db, Appointment appointment)
    {
        appointment.AttendanceStatus = null;
        var modes = new[] { ModePrefix + appointment.Id, AnsweredPrefix + appointment.Id };
        db.WhatsAppConversationStates.RemoveRange(await db.WhatsAppConversationStates
            .Where(s => s.BusinessId == appointment.BusinessId && modes.Contains(s.ChatbotMode))
            .ToListAsync());
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
