using BarberSaas.Api.Data;
using BarberSaas.Api.Filters;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BarberSaas.Api.Controllers;

// The owner's Chats page: the WhatsApp conversations of the business's linked number (ChatMessage,
// recorded by WhatsAppController and BridgeWhatsAppSender), and replying to a customer from the
// dashboard instead of the phone. A reply goes out from the linked number through the bridge,
// like every chatbot message, and pauses the chatbot for that customer (ChatLog.OwnerChatPause).
[ApiController]
[Route("api/admin/chats")]
[Authorize(Policy = "BusinessOnly")]
public class ChatsController(AppDbContext db, IWhatsAppBridgeClient bridge, ILogger<ChatsController> logger) : ControllerBase
{
    private string BusinessId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private const int MaxConversations = 100;
    private const int MaxMessages = 200;

    public record ChatSummaryDto(string Phone, string? Name, string LastText, string LastSender, DateTime LastAt, int Unread);
    public record ChatMessageDto(string Id, string Sender, string Text, DateTime CreatedAt);
    public record ChatDto(string Phone, string? Name, DateTime? BotPausedUntil, List<ChatMessageDto> Messages);
    public record ChatPhoneRequest(string Phone);
    public record SendChatMessageRequest(string Phone, string Text);

    // Newest conversation first.
    [HttpGet]
    public async Task<IActionResult> GetChats()
    {
        var chats = await db.ChatMessages
            .Where(m => m.BusinessId == BusinessId)
            .GroupBy(m => m.CustomerPhone)
            .Select(g => new
            {
                Phone = g.Key,
                LastAt = g.Max(m => m.CreatedAt),
                Unread = g.Count(m => !m.IsRead),
            })
            .OrderByDescending(g => g.LastAt)
            .Take(MaxConversations)
            .ToListAsync();

        var phones = chats.Select(c => c.Phone).ToList();
        var lastMessages = (await db.ChatMessages
                .Where(m => m.BusinessId == BusinessId && phones.Contains(m.CustomerPhone))
                .GroupBy(m => m.CustomerPhone)
                .Select(g => g.OrderByDescending(m => m.CreatedAt).First())
                .ToListAsync())
            .ToDictionary(m => m.CustomerPhone);
        var names = await NamesFor(phones);

        return Ok(chats.Select(c => new ChatSummaryDto(c.Phone, names.GetValueOrDefault(c.Phone),
            lastMessages[c.Phone].Text, lastMessages[c.Phone].Sender.ToString(), c.LastAt, c.Unread)));
    }

    // The sidebar badge.
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount() =>
        Ok(new { count = await db.ChatMessages.CountAsync(m => m.BusinessId == BusinessId && !m.IsRead) });

    // The last MaxMessages messages, oldest first. The phone is a query value, not a route part,
    // because of its "+".
    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages([FromQuery] string phone)
    {
        var customerPhone = PhoneNormalizer.Normalize(phone ?? "");
        var messages = await db.ChatMessages
            .Where(m => m.BusinessId == BusinessId && m.CustomerPhone == customerPhone)
            .OrderByDescending(m => m.CreatedAt).Take(MaxMessages)
            .ToListAsync();
        if (messages.Count == 0) return NotFound(new { error = "Not found" });

        var lastOwnerMessage = messages
            .Where(m => m.Sender is ChatSender.Owner or ChatSender.OwnerPhone)
            .Select(m => (DateTime?)m.CreatedAt).FirstOrDefault();
        DateTime? pausedUntil = lastOwnerMessage + ChatLog.OwnerChatPause;
        if (pausedUntil <= DateTime.UtcNow) pausedUntil = null;

        var names = await NamesFor([customerPhone]);
        return Ok(new ChatDto(customerPhone, names.GetValueOrDefault(customerPhone), pausedUntil,
            messages.OrderBy(m => m.CreatedAt).Select(ToDto).ToList()));
    }

    // Called by the open chat whenever it shows unread messages, so not an activity-log row each time.
    [HttpPost("read")]
    [SkipActivityLog]
    public async Task<IActionResult> MarkRead([FromBody] ChatPhoneRequest req)
    {
        var customerPhone = PhoneNormalizer.Normalize(req.Phone ?? "");
        await db.ChatMessages
            .Where(m => m.BusinessId == BusinessId && m.CustomerPhone == customerPhone && !m.IsRead)
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.IsRead, true));
        return Ok(new { ok = true });
    }

    // Only to a customer who already has a chat with the business: messaging numbers out of the
    // blue is what gets a WhatsApp number banned.
    [HttpPost("messages")]
    public async Task<IActionResult> SendMessage([FromBody] SendChatMessageRequest req)
    {
        var text = (req.Text ?? "").Replace("\0", "").Trim();
        if (text.Length == 0) return BadRequest(new { error = "Write a message first" });
        if (text.Length > ChatLog.MaxTextChars) return BadRequest(new { error = "This message is too long" });

        var customerPhone = PhoneNormalizer.Normalize(req.Phone ?? "");
        if (customerPhone.Length == 0
            || !await db.ChatMessages.AnyAsync(m => m.BusinessId == BusinessId && m.CustomerPhone == customerPhone))
            return NotFound(new { error = "Not found" });

        var business = await db.Businesses.FindAsync(BusinessId);
        if (string.IsNullOrWhiteSpace(business!.WhatsAppNumber))
            return BadRequest(new { error = "Your WhatsApp bot isn't linked, so messages can't be sent" });

        try
        {
            await bridge.SendAsync(BusinessId, PhoneNormalizer.ToE164(customerPhone), text);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Chat message to a customer of business {BusinessId} failed", BusinessId);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = "The message wasn't sent: the WhatsApp bot isn't connected right now" });
        }

        var message = ChatLog.NewMessage(BusinessId, customerPhone, ChatSender.Owner, text);
        db.ChatMessages.Add(message);
        // Replying means the owner has read the chat.
        await db.ChatMessages
            .Where(m => m.BusinessId == BusinessId && m.CustomerPhone == customerPhone && !m.IsRead)
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.IsRead, true));
        await db.SaveChangesAsync();
        this.SetActivityDetail($"Sent a chat message to {customerPhone}");
        return Ok(ToDto(message));
    }

    private static ChatMessageDto ToDto(ChatMessage m) => new(m.Id, m.Sender.ToString(), m.Text, m.CreatedAt);

    // The business's own name for the customer, else the latest WhatsApp profile name.
    private async Task<Dictionary<string, string>> NamesFor(List<string> phones)
    {
        var names = (await db.Customers
                .Where(c => c.BusinessId == BusinessId && phones.Contains(c.Phone))
                .Select(c => new { c.Phone, c.Name, c.FamilyName })
                .ToListAsync())
            .ToDictionary(c => c.Phone, c => ActivityDetailExtensions.FullName(c.Name, c.FamilyName));
        var missing = phones.Where(p => !names.ContainsKey(p)).ToList();
        if (missing.Count == 0) return names;

        var withKeys = await db.ChatMessages
            .Where(m => m.BusinessId == BusinessId && missing.Contains(m.CustomerPhone) && m.CustomerName != null)
            .GroupBy(m => m.CustomerPhone)
            .Select(g => new { Phone = g.Key, Name = g.OrderByDescending(m => m.CreatedAt).Select(m => m.CustomerName!).First() })
            .ToListAsync();
        foreach (var row in withKeys) names[row.Phone] = row.Name;
        return names;
    }
}
