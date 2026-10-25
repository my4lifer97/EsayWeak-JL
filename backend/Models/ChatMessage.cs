using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

// Who wrote a ChatMessage: the customer, the chatbot (its replies and every automated send:
// confirmations, reminders, notices), the owner from the dashboard's Chats page, or the owner
// typing on the linked phone itself (reported by the bridge).
public enum ChatSender { Customer, Bot, Owner, OwnerPhone }

// One WhatsApp message between a business's linked number and a customer, for the owner's Chats
// page (ChatsController). Recorded from 2026-10-25 on; nothing earlier was stored.
public class ChatMessage
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BusinessId { get; set; } = "";
    // E.164, the same form as Customer.Phone.
    public string CustomerPhone { get; set; } = "";
    public ChatSender Sender { get; set; }
    public string Text { get; set; } = "";
    // WhatsApp's profile name on a customer's message, for a phone with no Customer row yet.
    public string? CustomerName { get; set; }
    // Only a customer's message starts unread (the owner hasn't seen it on the Chats page).
    public bool IsRead { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    // A photo or file the owner sent with it; Text is then its caption (may be empty).
    public string? AttachmentId { get; set; }

    public Business Business { get; set; } = null!;
    public ChatAttachment? Attachment { get; set; }
}
