using BarberSaas.Api.Models;

namespace BarberSaas.Api.Services;

// Active IWhatsAppSender implementation (see Program.cs) -- sends outbound WhatsApp messages
// (reminders, waitlist notifications, cancellation-approval requests) through the self-hosted
// whatsapp-bridge service instead of Twilio's WhatsApp Business API. TwilioWhatsAppSender is kept
// in the codebase, unregistered, as the fallback path if a real registered business + Trust Hub
// approval ever happens later -- see the Twilio Trust Hub rejection note in project history.
public class BridgeWhatsAppSender(IWhatsAppBridgeClient bridge) : IWhatsAppSender
{
    public Task SendAsync(Business business, string toPhone, string message) =>
        bridge.SendAsync(business.Id, toPhone, message);
}
