using BarberSaas.Api.Models;
using Twilio;
using Twilio.Rest.Api.V2010.Account;

namespace BarberSaas.Api.Services;

// One platform-owned Twilio account (Twilio:AccountSid/AuthToken, same secret-config pattern as
// Jwt:Secret/CronSecret) sends on behalf of every barber -- only which number to send `from` is
// per-barber (Barber.TwilioNumber, assigned by the platform admin). "Not configured" is still a
// per-call condition -- callers are expected to check barber.TwilioNumber is non-null before
// calling this (same permissive skip CronController.SendReminders always did).
public class TwilioWhatsAppSender(IConfiguration config) : IWhatsAppSender
{
    public async Task SendAsync(Barber barber, string toPhone, string message)
    {
        TwilioClient.Init(config["Twilio:AccountSid"], config["Twilio:AuthToken"]);
        await MessageResource.CreateAsync(
            from: new Twilio.Types.PhoneNumber($"whatsapp:{barber.TwilioNumber}"),
            to: new Twilio.Types.PhoneNumber($"whatsapp:{toPhone}"),
            body: message);
    }
}
