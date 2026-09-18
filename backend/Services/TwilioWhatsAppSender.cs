using BarberSaas.Api.Models;
using Twilio;
using Twilio.Rest.Api.V2010.Account;

namespace BarberSaas.Api.Services;

// Dormant fallback -- not registered in Program.cs (see BridgeWhatsAppSender, the active
// implementation). Kept in case a real registered business + Trust Hub approval ever happens
// later. One platform-owned Twilio account (Twilio:AccountSid/AuthToken, same secret-config
// pattern as Jwt:Secret/CronSecret) sends on behalf of every business -- only which number to
// send `from` is per-business (Business.WhatsAppNumber). "Not configured" is still a per-call
// condition -- callers are expected to check business.WhatsAppNumber is non-null before calling
// this (same permissive skip CronController.SendReminders always did).
public class TwilioWhatsAppSender(IConfiguration config) : IWhatsAppSender
{
    public async Task SendAsync(Business business, string toPhone, string message)
    {
        TwilioClient.Init(config["Twilio:AccountSid"], config["Twilio:AuthToken"]);
        await MessageResource.CreateAsync(
            from: new Twilio.Types.PhoneNumber($"whatsapp:{business.WhatsAppNumber}"),
            to: new Twilio.Types.PhoneNumber($"whatsapp:{toPhone}"),
            body: message);
    }
}
