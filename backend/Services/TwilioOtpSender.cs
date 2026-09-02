using Twilio;
using Twilio.Rest.Api.V2010.Account;

namespace BarberSaas.Api.Services;

// Sends the customer login OTP as a real SMS via Twilio's Programmable SMS API, using a
// platform-level Twilio account (Twilio:AccountSid/AuthToken/FromNumber) -- separate from each
// barber's own WhatsApp Twilio credentials (TwilioWhatsAppSender), since this runs before any
// barber is selected. Registered in Program.cs only when Twilio:AccountSid is configured;
// otherwise DevOtpSender is used instead (local/test environments unaffected).
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
            to: new Twilio.Types.PhoneNumber(ToE164(phone)),
            body: $"Your EsayWeek verification code is {code}");
    }

    // PhoneNormalizer.Normalize (used for every stored phone, so it can't change without
    // breaking existing phone-matching everywhere else) keeps a "+" only if the customer typed
    // one -- a bare local number like "0501234567" comes through with no country code. Twilio's
    // "to" number must be E.164. The business is Israel-based (Hebrew/Arabic UI, ILS pricing,
    // Cardcom), so a country-code-less number is assumed to be a local Israeli mobile number
    // (leading 0 dropped in favor of +972).
    private static string ToE164(string normalizedPhone) =>
        normalizedPhone.StartsWith('+') ? normalizedPhone : $"+972{normalizedPhone.TrimStart('0')}";
}
