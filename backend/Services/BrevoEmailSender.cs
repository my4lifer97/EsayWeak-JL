using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BarberSaas.Api.Services;

// Sends real email via Brevo's transactional API (https://api.brevo.com/v3/smtp/email). Unlike
// Resend, Brevo can send to *any* recipient once a single sender address is verified (a 6-digit
// code emailed to that address) -- no custom domain required, so this is the option for real
// customer delivery until a domain gets verified in Resend. Also sidesteps Railway's outbound
// SMTP port block entirely, since this is a plain HTTPS call. Registered in Program.cs only when
// Brevo:ApiKey is configured; Brevo:FromEmail must be the address verified in Brevo's dashboard
// (Settings > Senders), not an arbitrary address -- Brevo rejects sends from an unverified sender.
public class BrevoEmailSender(HttpClient http, IConfiguration config) : IEmailSender
{
    public async Task SendAsync(string email, string subject, string body)
    {
        var apiKey = config["Brevo:ApiKey"]!;
        var fromEmail = config["Brevo:FromEmail"]!;
        var fromName = config["Brevo:FromName"] ?? "EsayWeek";

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        request.Headers.TryAddWithoutValidation("api-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(new
        {
            sender = new { name = fromName, email = fromEmail },
            to = new[] { new { email } },
            subject,
            textContent = body,
        });

        var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Brevo API returned {(int)response.StatusCode} {response.StatusCode}: {errorBody}");
        }
    }
}
