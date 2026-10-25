using BarberSaas.Api.DTOs;

namespace BarberSaas.Api.Services;

// Talks to the self-hosted whatsapp-bridge Node service (Baileys) that holds one linked WhatsApp
// session per business -- see whatsapp-bridge/README.md. Shared by BridgeWhatsAppSender (outbound)
// and PlatformAdminController's whatsapp/link endpoints (linking flow).
public interface IWhatsAppBridgeClient
{
    Task SendAsync(string businessId, string toPhone, string message);
    // A photo (image/*) or a document, with the message as its caption -- the owner's Chats page.
    Task SendMediaAsync(string businessId, string toPhone, string caption, string fileName, string contentType, byte[] data);
    Task<WhatsAppLinkStatusDto> StartLinkAsync(string businessId);
    Task<WhatsAppLinkStatusDto> GetStatusAsync(string businessId);
    Task UnlinkAsync(string businessId);
}
