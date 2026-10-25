using BarberSaas.Api.Models;

namespace BarberSaas.Api.Services;

// Active IWhatsAppSender implementation (see Program.cs) -- sends outbound WhatsApp messages
// (reminders, waitlist notifications, cancellation-approval requests) through the self-hosted
// whatsapp-bridge service instead of Twilio's WhatsApp Business API. TwilioWhatsAppSender is kept
// in the codebase, unregistered, as the fallback path if a real registered business + Trust Hub
// approval ever happens later -- see the Twilio Trust Hub rejection note in project history.
public class BridgeWhatsAppSender(IWhatsAppBridgeClient bridge, ChatLog chatLog) : IWhatsAppSender
{
    // E.164 always -- the bridge builds the WhatsApp id straight from the digits, and a local-format
    // number (e.g. an owner's phone typed as "0551234567" in Settings) names no real account, which
    // made the send hang until the HttpClient timeout instead of failing.
    public async Task SendAsync(Business business, string toPhone, string message)
    {
        var to = PhoneNormalizer.ToE164(toPhone);
        await bridge.SendAsync(business.Id, to, message);
        // Shown in the owner's Chats page as the bot's message -- except alerts to the owner's own
        // numbers (inquiries, cancellation approvals), which aren't a customer chat.
        if (!IsOwnerNumber(business, to))
            await chatLog.RecordAsync(business.Id, PhoneNormalizer.Normalize(to), ChatSender.Bot, message);
    }

    private static bool IsOwnerNumber(Business business, string to) =>
        new[] { business.Phone, business.InquiryWhatsAppNumber, business.CancelApprovalWhatsAppNumber }
            .Any(n => !string.IsNullOrWhiteSpace(n) && PhoneNormalizer.Normalize(n) == PhoneNormalizer.Normalize(to));
}
