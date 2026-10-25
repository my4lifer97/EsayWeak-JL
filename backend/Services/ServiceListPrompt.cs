using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

// The chatbot's numbered "which service?" list, shared by WhatsAppController and the owner's
// Chats page (finishing a chat can hand the customer back to the bot with this list).
public class ServiceListPrompt(AppDbContext db, BookingRules bookingRules)
{
    // Upserts the (BusinessId, Phone) conversation-state row (unique index guarantees at most one,
    // and records the resolved language on it for TryHandleServiceSelectionReply / future
    // signal-less replies to reuse) and replies with the numbered service list. Reused by the
    // conversation-start path and by the reschedule/no-appointment paths, which prefix their own
    // intro line first. A business's custom welcome message replaces the default greeting -- the
    // "which service + list + instructions" tail always stays in the detected language.
    public async Task<string> PromptAsync(string businessId, string businessName, string phone, string lang, string? welcomeMessage, bool inquiryEnabled)
    {
        // An expired subscription or ended trial: a link would only lead to "Booking unavailable"
        // and then 24 h of silence (QA run 4).
        var business = (await db.Businesses.FindAsync(businessId))!;
        if (!bookingRules.AcceptsBookings(business))
        {
            var stale = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == businessId && s.Phone == phone);
            if (stale is not null)
            {
                db.WhatsAppConversationStates.Remove(stale);
                await db.SaveChangesAsync();
            }
            return I18nService.T(lang, "whatsapp.bookingUnavailable", new() { ["businessName"] = businessName });
        }

        var services = await ActiveServicesAsync(businessId, lang);
        if (services.Count == 0)
            return I18nService.T(lang, "whatsapp.noServices", new() { ["businessName"] = businessName });

        var existing = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == businessId && s.Phone == phone);
        if (existing is null)
        {
            existing = new WhatsAppConversationState { BusinessId = businessId, Phone = phone };
            db.WhatsAppConversationStates.Add(existing);
        }
        existing.ExpiresAt = DateTime.UtcNow.Add(ConversationStateLifetime);
        existing.Language = lang;
        existing.InvalidAttempts = 0;
        existing.AwaitingBookingCompletion = false;
        // A fresh booking prompt always means "back in Booking mode" -- without this, a row left
        // over from an expired Inquiry session (see HandleInquiryModeAsync) would still read as
        // Inquiry mode once its ExpiresAt gets refreshed below, silently swallowing the customer's
        // next numeric service-selection reply as if it were inquiry content.
        existing.ChatbotMode = BookingMode;
        existing.InquiryNotified = false;
        existing.ShownItemIds = string.Join(",", services.Select(s => s.Id));
        await db.SaveChangesAsync();

        var list = string.Join("\n", services.Select((s, i) => $"{i + 1}. {s.Name}"));
        var escapeHatch = inquiryEnabled ? I18nService.T(lang, "whatsapp.inquiryEscapeHatch") : "";
        if (!string.IsNullOrWhiteSpace(welcomeMessage))
        {
            var tail = I18nService.T(lang, "whatsapp.selectServicePrompt", new() { ["list"] = list });
            return $"{welcomeMessage}\n\n{tail}{escapeHatch}";
        }
        return I18nService.T(lang, "whatsapp.selectService", new() { ["businessName"] = businessName, ["list"] = list }) + escapeHatch;
    }

    // Only bookable items make sense to offer through the booking chatbot -- a showcase-only
    // item has nothing for this flow to schedule.
    // Alphabetical in the customer's language -- ordering by Id looked random to owners.
    public async Task<List<(string Id, string Name)>> ActiveServicesAsync(string businessId, string lang)
    {
        var services = await db.Items
            .Where(s => s.BusinessId == businessId && s.IsActive && s.IsBookable)
            .ToListAsync();
        return services
            .Select(s => (s.Id, Name: lang switch
            {
                "AR" => s.NameAr,
                "HE" => s.NameHe,
                _ => s.NameEn,
            }))
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.Id)
            .ToList();
    }

    // Same values as WhatsAppController's own.
    public const string BookingMode = "Booking";
    public const string InquiryMode = "Inquiry";
    public static readonly TimeSpan ConversationStateLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan InquiryStateLifetime = TimeSpan.FromHours(24);
}
