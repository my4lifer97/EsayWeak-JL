using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

// Shared by WhatsAppController and ChatsController for the owner's Chats page. A chat is "open"
// while the customer is in the chatbot's Inquiry mode (they sent "#2", or "2" at the opening
// menu): only then are messages recorded, and the owner can answer. The owner's "Finish chat"
// or the customer's "#1" ends it; so does InquiryStateLifetime without a message.
public static class ChatLog
{
    // WhatsApp's own limit is far higher; this keeps one pasted essay from bloating the table.
    public const int MaxTextChars = 4000;

    public static Task<bool> IsChatOpenAsync(AppDbContext db, string businessId, string customerPhone) =>
        db.WhatsAppConversationStates.AnyAsync(s => s.BusinessId == businessId && s.Phone == customerPhone
            && s.ChatbotMode == ServiceListPrompt.InquiryMode && s.ExpiresAt > DateTime.UtcNow);

    // The sign-in part of the links we send ("/w/<token>" booking links, "?token=" appointment
    // links) is left out of the stored copy: the customer has the working link on WhatsApp, and
    // chat history is kept long after (QA run 5).
    public static string WithoutLinkTokens(string text) =>
        // Inside links only: "/w/" or "token=" in the owner's own words stays (QA run 6).
        Regex.Replace(text, @"https?://\S+", m =>
            Regex.Replace(Regex.Replace(m.Value, @"/w/[A-Za-z0-9_-]+", "/w/..."), @"([?&]token=)[^&\s]+", "$1..."));

    public static ChatMessage NewMessage(string businessId, string customerPhone, ChatSender sender, string text, string? customerName = null) => new()
    {
        BusinessId = businessId,
        CustomerPhone = customerPhone,
        Sender = sender,
        Text = TextCut.Cut(sender == ChatSender.Customer ? text : WithoutLinkTokens(text), MaxTextChars),
        CustomerName = customerName,
        IsRead = sender != ChatSender.Customer,
    };

    // The owner wrote (dashboard or phone): the chat is read, and stays open as long as it would
    // after a customer message. The caller saves.
    public static async Task OwnerAnsweredAsync(AppDbContext db, string businessId, string customerPhone)
    {
        await db.ChatMessages
            .Where(m => m.BusinessId == businessId && m.CustomerPhone == customerPhone && !m.IsRead)
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.IsRead, true));
        var state = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == businessId && s.Phone == customerPhone);
        if (state is not null) state.ExpiresAt = DateTime.UtcNow.Add(ServiceListPrompt.InquiryStateLifetime);
    }
}
