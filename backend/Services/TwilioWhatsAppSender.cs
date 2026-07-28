using BarberSaas.Api.Models;
using Twilio;
using Twilio.Rest.Api.V2010.Account;

namespace BarberSaas.Api.Services;

// Twilio creds are per-barber (stored on the Barber row), not in appsettings, so "not configured"
// is a per-call condition -- callers are expected to check barber.TwilioSid/Token/Number are all
// non-null before calling this (same permissive skip CronController.SendReminders always did).
public class TwilioWhatsAppSender : IWhatsAppSender
{
    public async Task SendAsync(Barber barber, string toPhone, string message)
    {
        TwilioClient.Init(barber.TwilioSid, barber.TwilioToken);
        await MessageResource.CreateAsync(
            from: new Twilio.Types.PhoneNumber($"whatsapp:{barber.TwilioNumber}"),
            to: new Twilio.Types.PhoneNumber($"whatsapp:{toPhone}"),
            body: message);
    }
}
