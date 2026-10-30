using BarberSaas.Api.Data;
using BarberSaas.Api.Filters;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BarberSaas.Api.Controllers;

// The owner's Chats page: customers who chose to talk to the owner in the WhatsApp chatbot
// (Inquiry mode, "#2" -- see ChatLog), answered from the dashboard instead of the phone. Messages
// go out from the linked number through the bridge, each starting with a "Message from the
// business owner" line in the customer's language. The owner can send a booking link for a
// service, and finishes the chat, optionally handing the customer back to the bot's service list.
[ApiController]
[Route("api/admin/chats")]
[Authorize(Policy = "BusinessOnly")]
public class ChatsController(
    AppDbContext db, IWhatsAppBridgeClient bridge, WhatsAppBookingTokenService bookingTokens,
    ServiceListPrompt serviceList, BookingRules bookingRules, IConfiguration config,
    ChatTemplates templates, ILogger<ChatsController> logger) : ControllerBase
{
    private string BusinessId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private const int MaxConversations = 100;
    private const int MaxMessages = 200;

    public record ChatSummaryDto(string Phone, string? Name, string LastText, string LastSender, DateTime LastAt, int Unread, bool IsOpen);
    public record ChatAttachmentDto(string Id, string FileName, string ContentType);
    public record ChatMessageDto(string Id, string Sender, string Text, DateTime CreatedAt, ChatAttachmentDto? Attachment);
    // CustomerId: the business's Customer row for this phone, if any (booking from the chat uses it).
    public record ChatDto(string Phone, string? Name, bool IsOpen, string? CustomerId, List<ChatMessageDto> Messages);
    public record ChatPhoneRequest(string Phone);
    public record SendChatMessageRequest(string Phone, string Text);
    public record SendBookingLinkRequest(string Phone, string ItemId);
    public record FinishChatRequest(string Phone, bool SendServiceList);
    public record QuickReplyDto(string Id, string Title, string Text);
    public record SaveQuickReplyRequest(string Title, string Text);

    private const int MaxQuickReplies = 100;
    private const int MaxQuickReplyTitleChars = 100;

    // Open chats first, then the newest.
    [HttpGet]
    public async Task<IActionResult> GetChats()
    {
        // The MaxConversations most recent, plus every open chat and every chat with unread
        // messages, however old: taking the newest 100 before sorting open ones first hid an older
        // open chat while its unread messages still counted in the sidebar badge (QA run 5).
        var openPhones = await db.WhatsAppConversationStates
            .Where(s => s.BusinessId == BusinessId && s.ChatbotMode == ServiceListPrompt.InquiryMode && s.ExpiresAt > DateTime.UtcNow)
            .Select(s => s.Phone)
            .ToListAsync();
        var grouped = db.ChatMessages
            .Where(m => m.BusinessId == BusinessId)
            .GroupBy(m => m.CustomerPhone)
            .Select(g => new
            {
                Phone = g.Key,
                LastAt = g.Max(m => m.CreatedAt),
                Unread = g.Count(m => !m.IsRead),
            });
        var recent = await grouped.OrderByDescending(g => g.LastAt).Take(MaxConversations).ToListAsync();
        var pinned = await grouped.Where(g => g.Unread > 0 || openPhones.Contains(g.Phone)).ToListAsync();
        var chats = recent.Concat(pinned).DistinctBy(c => c.Phone).ToList();

        var phones = chats.Select(c => c.Phone).ToList();
        var lastMessages = (await db.ChatMessages
                .Where(m => m.BusinessId == BusinessId && phones.Contains(m.CustomerPhone))
                .GroupBy(m => m.CustomerPhone)
                .Select(g => g.OrderByDescending(m => m.CreatedAt)
                    .Select(m => new { m.CustomerPhone, m.Sender, m.Text, FileName = m.Attachment == null ? null : m.Attachment.FileName })
                    .First())
                .ToListAsync())
            .ToDictionary(m => m.CustomerPhone);
        var open = await OpenChats(phones);
        var names = await NamesFor(phones);

        return Ok(chats
            .Select(c => new ChatSummaryDto(c.Phone, names.GetValueOrDefault(c.Phone),
                // A preview: the whole message (up to 4000 characters) went out for every chat every 5 s (QA run 6).
                TextCut.Cut(lastMessages[c.Phone].Text.Length == 0 && lastMessages[c.Phone].FileName is { } file ? $"📎 {file}" : lastMessages[c.Phone].Text, 200),
                lastMessages[c.Phone].Sender.ToString(), c.LastAt, c.Unread, open.Contains(c.Phone)))
            .OrderByDescending(c => c.IsOpen).ThenByDescending(c => c.LastAt));
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
        // Projected: an attachment's file bytes stay in the database (GetAttachment serves them).
        var messages = await db.ChatMessages
            .Where(m => m.BusinessId == BusinessId && m.CustomerPhone == customerPhone)
            .OrderByDescending(m => m.CreatedAt).Take(MaxMessages)
            .Select(m => new ChatMessageDto(m.Id, m.Sender.ToString(), m.Text, m.CreatedAt,
                m.Attachment == null ? null : new ChatAttachmentDto(m.Attachment.Id, m.Attachment.FileName, m.Attachment.ContentType)))
            .ToListAsync();
        if (messages.Count == 0) return NotFound(new { error = "Not found" });

        var names = await NamesFor([customerPhone]);
        var customerId = await db.Customers.Where(c => c.BusinessId == BusinessId && c.Phone == customerPhone)
            .Select(c => c.Id).FirstOrDefaultAsync();
        return Ok(new ChatDto(customerPhone, names.GetValueOrDefault(customerPhone),
            await ChatLog.IsChatOpenAsync(db, BusinessId, customerPhone), customerId,
            messages.OrderBy(m => m.CreatedAt).ToList()));
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

    [HttpPost("messages")]
    public async Task<IActionResult> SendMessage([FromBody] SendChatMessageRequest req)
    {
        var text = (req.Text ?? "").Replace("\0", "").Trim();
        if (text.Length == 0) return BadRequest(new { error = "Write a message first" });
        if (text.Length > ChatLog.MaxTextChars) return BadRequest(new { error = "This message is too long" });

        var (chat, error) = await OpenChatFor(req.Phone);
        if (error is not null) return error;

        var sendError = await SendAsOwner(chat!, text);
        if (sendError is not null) return sendError;
        this.SetActivityDetail($"Sent a chat message to {chat!.Phone}");
        return Ok(new { ok = true });
    }

    // A photo or file from the owner's device, with the message box's text as its caption. Photos go
    // as WhatsApp images, the rest as documents.
    // Every field optional for binding, and the body limit above the file limit: a missing file part
    // or a file over 10MB got the framework's generic "Invalid request" instead of the file rule
    // (QA run 5).
    [HttpPost("attachment")]
    [RequestSizeLimit(ChatFiles.MaxRequestBodyBytes)]
    public async Task<IActionResult> SendAttachment([FromForm] string? phone, [FromForm] string? caption, IFormFile? file)
    {
        var text = (caption ?? "").Replace("\0", "").Trim();
        if (text.Length > ChatLog.MaxTextChars) return BadRequest(new { error = "This message is too long" });
        if (file is null) return BadRequest(new { error = ChatFiles.Error });
        var contentType = await ChatFiles.ValidateAsync(file);
        if (contentType is null) return BadRequest(new { error = ChatFiles.Error });

        var (chat, error) = await OpenChatFor(phone ?? "");
        if (error is not null) return error;

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        var fileName = ChatFiles.SafeFileName(file.FileName);
        var ownerLine = I18nService.T(chat!.Language, "whatsapp.fromOwner");
        var data = buffer.ToArray();
        IActionResult? sendFailure = null;
        try
        {
            await bridge.SendMediaAsync(BusinessId, PhoneNormalizer.ToE164(chat.Phone),
                text.Length == 0 ? ownerLine : $"{ownerLine}\n{text}", fileName, contentType, data);
        }
        catch (Exception ex) when (SendFailure(ex) is { } failure)
        {
            logger.LogWarning(ex, "Chat attachment to a customer of business {BusinessId} failed", BusinessId);
            // A timeout may well have gone out: kept, like a text (QA run 7).
            if (ex is not TaskCanceledException) return failure;
            sendFailure = failure;
        }

        var attachment = new ChatAttachment { BusinessId = BusinessId, FileName = fileName, ContentType = contentType, Data = data };
        db.ChatAttachments.Add(attachment);
        var message = ChatLog.NewMessage(BusinessId, chat.Phone, ChatSender.Owner, text);
        message.AttachmentId = attachment.Id;
        db.ChatMessages.Add(message);
        await ChatLog.OwnerAnsweredAsync(db, BusinessId, chat.Phone);
        await db.SaveChangesAsync();
        if (sendFailure is not null) return sendFailure;
        this.SetActivityDetail($"Sent a file to {chat.Phone}");
        return Ok(new { ok = true });
    }

    // The file of a photo or file in this business's chats (the owner's or the customer's); fetched
    // with the owner's token (not a public URL). nosniff: a customer's file is only ever the type
    // stored for it (ChatFiles.InboundContentType), never guessed into a page.
    [HttpGet("attachments/{id}")]
    public async Task<IActionResult> GetAttachment(string id)
    {
        var attachment = await db.ChatAttachments.FirstOrDefaultAsync(a => a.Id == id && a.BusinessId == BusinessId);
        if (attachment is null) return NotFound(new { error = "Not found" });
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(attachment.Data, attachment.ContentType, attachment.FileName);
    }

    // A booking link for one of the business's services, the same kind the chatbot sends: it signs
    // the customer in and opens booking on that service.
    [HttpPost("booking-link")]
    public async Task<IActionResult> SendBookingLink([FromBody] SendBookingLinkRequest req)
    {
        var (chat, error) = await OpenChatFor(req.Phone);
        if (error is not null) return error;

        var business = (await db.Businesses.FindAsync(BusinessId))!;
        if (!bookingRules.AcceptsBookings(business))
            return BadRequest(new { error = "This business isn't taking online bookings right now" });
        var service = (await serviceList.ActiveServicesAsync(BusinessId, chat!.Language)).FirstOrDefault(s => s.Id == req.ItemId);
        if (service.Id is null) return BadRequest(new { error = "This service can't be booked" });

        var customerName = await db.Customers.Where(c => c.BusinessId == BusinessId && c.Phone == chat.Phone)
            .Select(c => c.Name).FirstOrDefaultAsync();
        var token = await bookingTokens.CreateAsync(BusinessId, service.Id, chat.Phone, customerName, chat.Language);
        var url = $"{config["AppUrl"]}/{business.Slug}/w/{token.Id}";
        var text = I18nService.T(chat.Language, "whatsapp.ownerBookingLink", new() { ["service"] = service.Name, ["url"] = url });

        var sendError = await SendAsOwner(chat, text);
        if (sendError is not null) return sendError;
        this.SetActivityDetail($"Sent a booking link for {service.Name} to {chat.Phone}");
        return Ok(new { ok = true });
    }

    // Ends the chat: the customer gets a closing message (and, if the owner chose it, the bot's
    // service list, ready for a numbered reply); the chatbot answers them again from then on.
    [HttpPost("finish")]
    public async Task<IActionResult> Finish([FromBody] FinishChatRequest req)
    {
        // Also while the bot is unlinked: the chat couldn't be ended at all (QA run 6).
        var (chat, error) = await OpenChatFor(req.Phone, requireLinkedBot: false);
        if (error is not null) return error;

        var business = (await db.Businesses.FindAsync(BusinessId))!;
        var ended = I18nService.T(chat!.Language, "whatsapp.ownerChatEnded", new() { ["businessName"] = business.Name });
        // Inquiry mode ends here either way: the list leaves a Booking-mode row waiting for a number,
        // otherwise the row goes and the customer's next message starts a fresh conversation.
        var text = req.SendServiceList
            ? $"{ended}\n\n{await serviceList.PromptAsync(BusinessId, business.Name, chat.Phone, chat.Language, null, business.ChatbotInquiryEnabled)}"
            : ended;
        // No list was shown ("no services" / "not taking bookings" leave the row as it was, or gone).
        if (db.Entry(chat.State).State != EntityState.Detached && chat.State.ChatbotMode == ServiceListPrompt.InquiryMode)
            db.WhatsAppConversationStates.Remove(chat.State);

        // Recorded in the chat only when it went out: a failed send showed as delivered (QA run 6).
        var sent = !string.IsNullOrWhiteSpace(business.WhatsAppNumber);
        if (sent)
        {
            try
            {
                await bridge.SendAsync(BusinessId, PhoneNormalizer.ToE164(chat.Phone), text);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Finished anyway: the owner is done, the customer just doesn't get told.
                logger.LogWarning(ex, "Chat-finished message to a customer of business {BusinessId} failed", BusinessId);
                sent = ex is TaskCanceledException; // a timeout may well have gone out
            }
        }
        if (sent) db.ChatMessages.Add(ChatLog.NewMessage(BusinessId, chat.Phone, ChatSender.Bot, text));
        // The customer never saw the service list, so no row waits for a number from it: their next
        // "1" got a booking link out of nowhere (QA run 8).
        else if (req.SendServiceList && db.Entry(chat.State).State is EntityState.Unchanged or EntityState.Modified or EntityState.Added)
            db.WhatsAppConversationStates.Remove(chat.State);
        await db.ChatMessages
            .Where(m => m.BusinessId == BusinessId && m.CustomerPhone == chat.Phone && !m.IsRead)
            .ExecuteUpdateAsync(u => u.SetProperty(m => m.IsRead, true));
        await db.SaveChangesAsync();
        this.SetActivityDetail($"Finished the chat with {chat.Phone}");
        // notified false: the customer wasn't told, which the page says (QA run 7).
        return Ok(new { ok = true, notified = sent });
    }

    // ─── Ready messages ─────────────────────────────────────────────────────

    // A "From the system" ready message (ChatTemplates.Kinds) for this chat, in the customer's
    // language: the conversation's, else the chatbot's default. Text only -- the page puts it in
    // the message box, and the owner sends it like any other message.
    [HttpGet("system-message")]
    public async Task<IActionResult> GetSystemMessage([FromQuery] string phone, [FromQuery] string kind,
        [FromQuery] string? date, [FromQuery] string? itemId)
    {
        var customerPhone = PhoneNormalizer.Normalize(phone ?? "");
        if (!await db.ChatMessages.AnyAsync(m => m.BusinessId == BusinessId && m.CustomerPhone == customerPhone))
            return NotFound(new { error = "Not found" });
        var business = (await db.Businesses.FindAsync(BusinessId))!;
        var lang = await db.WhatsAppConversationStates
            .Where(s => s.BusinessId == BusinessId && s.Phone == customerPhone).Select(s => s.Language).FirstOrDefaultAsync()
            ?? (business.ChatbotDefaultLanguage ?? business.Language).ToString();

        var result = await templates.BuildAsync(business, customerPhone, lang, kind ?? "", date, itemId);
        return result.Error is null ? Ok(new { text = result.Text }) : BadRequest(new { error = result.Error });
    }

    [HttpGet("quick-replies")]
    public async Task<IActionResult> GetQuickReplies() =>
        Ok(await db.QuickReplies.Where(q => q.BusinessId == BusinessId).OrderBy(q => q.CreatedAt)
            .Select(q => new QuickReplyDto(q.Id, q.Title, q.Text)).ToListAsync());

    [HttpPost("quick-replies")]
    public async Task<IActionResult> CreateQuickReply([FromBody] SaveQuickReplyRequest req)
    {
        if (QuickReplyError(req) is { } error) return BadRequest(new { error });
        if (await db.QuickReplies.CountAsync(q => q.BusinessId == BusinessId) >= MaxQuickReplies)
            return BadRequest(new { error = "You can save up to 100 ready messages" });
        var reply = new QuickReply { BusinessId = BusinessId, Title = req.Title.Trim(), Text = req.Text.Trim() };
        db.QuickReplies.Add(reply);
        await db.SaveChangesAsync();
        // Counted again after the insert: two requests at once both passed the check above and
        // went past the limit (QA run 5). Only the ones past the first 100 (by when they were made)
        // take themselves back out; each removing itself on any excess also emptied a free slot
        // (QA run 6).
        var keptIds = await db.QuickReplies.Where(q => q.BusinessId == BusinessId)
            .OrderBy(q => q.CreatedAt).ThenBy(q => q.Id).Take(MaxQuickReplies).Select(q => q.Id).ToListAsync();
        if (!keptIds.Contains(reply.Id))
        {
            db.QuickReplies.Remove(reply);
            await db.SaveChangesAsync();
            return BadRequest(new { error = "You can save up to 100 ready messages" });
        }
        return StatusCode(201, new QuickReplyDto(reply.Id, reply.Title, reply.Text));
    }

    [HttpPut("quick-replies/{id}")]
    public async Task<IActionResult> UpdateQuickReply(string id, [FromBody] SaveQuickReplyRequest req)
    {
        if (QuickReplyError(req) is { } error) return BadRequest(new { error });
        var reply = await db.QuickReplies.FirstOrDefaultAsync(q => q.Id == id && q.BusinessId == BusinessId);
        if (reply is null) return NotFound(new { error = "Not found" });
        reply.Title = req.Title.Trim();
        reply.Text = req.Text.Trim();
        await db.SaveChangesAsync();
        return Ok(new QuickReplyDto(reply.Id, reply.Title, reply.Text));
    }

    [HttpDelete("quick-replies/{id}")]
    public async Task<IActionResult> DeleteQuickReply(string id)
    {
        var reply = await db.QuickReplies.FirstOrDefaultAsync(q => q.Id == id && q.BusinessId == BusinessId);
        if (reply is null) return NotFound(new { error = "Not found" });
        db.QuickReplies.Remove(reply);
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private static string? QuickReplyError(SaveQuickReplyRequest req)
    {
        var title = (req.Title ?? "").Trim();
        var text = (req.Text ?? "").Trim();
        if (title.Length == 0 || text.Length == 0) return "Write a name and a message";
        if (title.Length > MaxQuickReplyTitleChars) return "The name is too long";
        if (text.Length > ChatLog.MaxTextChars) return "This message is too long";
        return null;
    }

    private sealed record OpenChat(string Phone, string Language, WhatsAppConversationState State);

    // The customer's open chat, or the error to answer: 404 for no chat at all, 409 for a finished
    // one (the owner can't start conversations -- messaging numbers out of the blue is what gets a
    // WhatsApp number banned), 400 when no WhatsApp number is linked.
    // requireLinkedBot false: for finishing, which works without the bot (the customer just isn't told).
    private async Task<(OpenChat? Chat, IActionResult? Error)> OpenChatFor(string? phone, bool requireLinkedBot = true)
    {
        var customerPhone = PhoneNormalizer.Normalize(phone ?? "");
        if (customerPhone.Length == 0
            || !await db.ChatMessages.AnyAsync(m => m.BusinessId == BusinessId && m.CustomerPhone == customerPhone))
            return (null, NotFound(new { error = "Not found" }));

        var state = await db.WhatsAppConversationStates.FirstOrDefaultAsync(s => s.BusinessId == BusinessId && s.Phone == customerPhone
            && s.ChatbotMode == ServiceListPrompt.InquiryMode && s.ExpiresAt > DateTime.UtcNow);
        if (state is null) return (null, Conflict(new { error = "This chat has ended" }));

        var business = await db.Businesses.FindAsync(BusinessId);
        if (requireLinkedBot && string.IsNullOrWhiteSpace(business!.WhatsAppNumber))
            return (null, BadRequest(new { error = "Your WhatsApp bot isn't linked, so messages can't be sent" }));
        return (new OpenChat(customerPhone, state.Language, state), null);
    }

    // The answer to a failed send through the bridge: 502 when it refused (not connected), 504 when
    // it didn't answer in time -- then the message may well have gone out, so the owner checks
    // before sending again. A timeout used to be a 500 (QA run 5). Null: not a send failure.
    private ObjectResult? SendFailure(Exception ex) => ex switch
    {
        HttpRequestException => StatusCode(StatusCodes.Status502BadGateway,
            new { error = "The message wasn't sent: the WhatsApp bot isn't connected right now" }),
        TaskCanceledException when !HttpContext.RequestAborted.IsCancellationRequested => StatusCode(StatusCodes.Status504GatewayTimeout,
            new { error = "WhatsApp didn't answer in time, so the message may or may not have been sent. Check the phone before sending it again." }),
        _ => null,
    };

    // Sends with the "Message from the business owner" first line and records it (without that
    // line, which is only for WhatsApp). Null on success.
    private async Task<IActionResult?> SendAsOwner(OpenChat chat, string text)
    {
        try
        {
            await bridge.SendAsync(BusinessId, PhoneNormalizer.ToE164(chat.Phone),
                $"{I18nService.T(chat.Language, "whatsapp.fromOwner")}\n{text}");
        }
        catch (Exception ex) when (SendFailure(ex) is { } failure)
        {
            logger.LogWarning(ex, "Chat message to a customer of business {BusinessId} failed", BusinessId);
            // A timeout may well have been delivered: kept in the chat so the owner sees what they
            // sent while checking (QA run 6: no trace at all).
            if (ex is TaskCanceledException)
            {
                db.ChatMessages.Add(ChatLog.NewMessage(BusinessId, chat.Phone, ChatSender.Owner, text));
                await ChatLog.OwnerAnsweredAsync(db, BusinessId, chat.Phone);
                await db.SaveChangesAsync();
            }
            return failure;
        }
        db.ChatMessages.Add(ChatLog.NewMessage(BusinessId, chat.Phone, ChatSender.Owner, text));
        await ChatLog.OwnerAnsweredAsync(db, BusinessId, chat.Phone);
        await db.SaveChangesAsync();
        return null;
    }


    private async Task<HashSet<string>> OpenChats(List<string> phones) =>
        (await db.WhatsAppConversationStates
            .Where(s => s.BusinessId == BusinessId && phones.Contains(s.Phone)
                && s.ChatbotMode == ServiceListPrompt.InquiryMode && s.ExpiresAt > DateTime.UtcNow)
            .Select(s => s.Phone).ToListAsync()).ToHashSet();

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

        var profileNames = await db.ChatMessages
            .Where(m => m.BusinessId == BusinessId && missing.Contains(m.CustomerPhone) && m.CustomerName != null)
            .GroupBy(m => m.CustomerPhone)
            .Select(g => new { Phone = g.Key, Name = g.OrderByDescending(m => m.CreatedAt).Select(m => m.CustomerName!).First() })
            .ToListAsync();
        foreach (var row in profileNames) names[row.Phone] = row.Name;
        return names;
    }
}
