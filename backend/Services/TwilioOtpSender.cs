using Twilio;
using Twilio.Rest.Api.V2010.Account;

namespace BarberSaas.Api.Services;

// Sends the customer login OTP as a real SMS via Twilio's Programmable SMS API, using the same
// platform-owned Twilio:AccountSid/AuthToken as TwilioWhatsAppSender, plus one dedicated
// Twilio:FromNumber -- a platform-level SMS-capable number, separate from any business's own
// WhatsApp sender (Business.TwilioNumber), since this runs before any business is identified.
// Registered in Program.cs only when Twilio:FromNumber is configured; otherwise DevOtpSender is
// used instead (local/test environments unaffected). Unlike email SMTP, this is a plain HTTPS
// REST call, so it isn't subject to Railway's outbound SMTP port restriction.
public class TwilioOtpSender(IConfiguration config) : IOtpSender
{
    public Task SendAsync(string phone, string code)
    {
        var accountSid = config["Twilio:AccountSid"]!;
        var authToken = config["Twilio:AuthToken"]!;
        var fromNumber = config["Twilio:FromNumber"]!;

        TwilioClient.Init(accountSid, authToken);
        return MessageResource.CreateAsync(
            from: new Twilio.Types.PhoneNumber(fromNumber),
            to: new Twilio.Types.PhoneNumber(PhoneNormalizer.ToE164(phone)),
            body: $"Your EsayWeek verification code is {code}");
    }
}
