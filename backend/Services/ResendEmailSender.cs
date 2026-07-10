using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BarberSaas.Api.Services;

// Sends real email via the Resend API (https://resend.com). Registered in Program.cs only when
// Resend:ApiKey is configured; otherwise DevEmailSender is used instead, so local/test
// environments without an API key are unaffected. Resend:FromEmail defaults to its shared
// onboarding@resend.dev sender, which works without verifying a custom domain first.
public class ResendEmailSender(HttpClient http, IConfiguration config) : IEmailSender
{
    public async Task SendAsync(string email, string subject, string body)
    {
        var apiKey = config["Resend:ApiKey"]!;
        var fromEmail = config["Resend:FromEmail"] ?? "onboarding@resend.dev";
        var fromName = config["Resend:FromName"] ?? "EsayWeek";

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new
        {
            from = $"{fromName} <{fromEmail}>",
            to = new[] { email },
            subject,
            text = body,
        });

        var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
