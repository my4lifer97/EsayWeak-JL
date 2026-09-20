using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

// One row per message a customer sends while in the chatbot's Inquiry mode (see
// WhatsAppController.HandleInquiryModeAsync) -- the always-on "in-app" side of owner notification,
// independent of whether Business.InquiryNotifyViaWhatsApp/Email are also on. Shown in the
// platform-admin-free business dashboard as an inbox the owner can mark read.
public class ChatbotInquiry
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BusinessId { get; set; } = "";
    public string CustomerPhone { get; set; } = "";
    // WhatsApp's inbound "ProfileName" -- null when it wasn't supplied.
    public string? CustomerName { get; set; }
    public string Message { get; set; } = "";
    public bool IsRead { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
}
