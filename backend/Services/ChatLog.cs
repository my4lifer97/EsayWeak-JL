using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

// Writes ChatMessage rows for the owner's Chats page. Saves in its own DI scope, so recording a
// send never saves (or fails) the caller's half-done changes -- cron reminders send in a loop
// before their own SaveChanges. Best-effort: a failed write is logged, never thrown.
public class ChatLog(IServiceScopeFactory scopeFactory, ILogger<ChatLog> logger)
{
    // While the owner is talking to a customer (from the Chats page or the phone), the chatbot
    // doesn't answer that customer: its "didn't understand" replies landed in the middle of the
    // conversation and counted toward the lockout.
    public static readonly TimeSpan OwnerChatPause = TimeSpan.FromHours(2);

    public async Task RecordAsync(string businessId, string customerPhone, ChatSender sender, string text, string? customerName = null)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ChatMessages.Add(NewMessage(businessId, customerPhone, sender, text, customerName));
            if (sender == ChatSender.OwnerPhone)
                // Answering on the phone means the owner has read the chat.
                await db.ChatMessages
                    .Where(m => m.BusinessId == businessId && m.CustomerPhone == customerPhone && !m.IsRead)
                    .ExecuteUpdateAsync(u => u.SetProperty(m => m.IsRead, true));
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not record a chat message for business {BusinessId}", businessId);
        }
    }

    public static ChatMessage NewMessage(string businessId, string customerPhone, ChatSender sender, string text, string? customerName = null) => new()
    {
        BusinessId = businessId,
        CustomerPhone = customerPhone,
        Sender = sender,
        Text = text.Length > MaxTextChars ? text[..MaxTextChars] : text,
        CustomerName = customerName,
        IsRead = sender != ChatSender.Customer,
    };

    // WhatsApp's own limit is far higher; this keeps one pasted essay from bloating the table.
    public const int MaxTextChars = 4000;
}
