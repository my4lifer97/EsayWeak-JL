using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BarberSaas.Api.Services;

// Sends owner emails through Brevo from the platform's own domain, so they pass the SPF/DKIM
// checks a free Gmail address can't and stay out of junk. Off until OwnerEmail:Provider=brevo is
// set, and that must wait for Brevo to verify the domain: it refuses a From address it hasn't.
// GmailApiEmailSender stays the default, and setting the provider back returns to it.
// OwnerEmail:ReplyTo is where replies go, since the From address has no inbox behind it.
//
// Reports into OwnerEmailHealth the same way as the Gmail sender. A 400 is left out for the same
// reason (one bad recipient), unless it's about the sender, which breaks every email.
public class BrevoOwnerEmailSender(HttpClient http, IConfiguration config, OwnerEmailHealth health) : IOwnerEmailSender
{
    private const string BaseUrl = "https://api.brevo.com/v3";
    private static readonly JsonSerializerOptions SkipNulls = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static bool IsSelected(IConfiguration config) =>
        string.Equals(config["OwnerEmail:Provider"], "brevo", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrEmpty(config["Brevo:ApiKey"])
        && !string.IsNullOrEmpty(config["OwnerEmail:FromEmail"]);

    public async Task SendAsync(string toEmail, string subject, string body)
    {
        var replyTo = config["OwnerEmail:ReplyTo"];
        using var request = NewRequest(HttpMethod.Post, "/smtp/email");
        request.Content = JsonContent.Create(new
        {
            sender = new { name = config["Brevo:FromName"] ?? Brand.Name, email = config["OwnerEmail:FromEmail"] },
            to = new[] { new { email = toEmail } },
            replyTo = string.IsNullOrEmpty(replyTo) ? null : new { email = replyTo },
            subject,
            textContent = body,
        }, options: SkipNulls);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request);
        }
        catch (Exception ex)
        {
            health.RecordFailure(ex.Message);
            throw;
        }
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            var message = $"Brevo API returned {(int)response.StatusCode} {response.StatusCode}: {errorBody}";
            var oneBadRecipient = response.StatusCode == HttpStatusCode.BadRequest
                && !errorBody.Contains("sender", StringComparison.OrdinalIgnoreCase);
            if (!oneBadRecipient) health.RecordFailure(message);
            throw new HttpRequestException(message);
        }
        health.RecordSuccess();
    }

    public async Task CheckAuthAsync()
    {
        using var request = NewRequest(HttpMethod.Get, "/account");
        var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Brevo API returned {(int)response.StatusCode} {response.StatusCode}: {errorBody}");
        }
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, BaseUrl + path);
        request.Headers.TryAddWithoutValidation("api-key", config["Brevo:ApiKey"]);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }
}
