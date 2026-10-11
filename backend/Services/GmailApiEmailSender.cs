using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

namespace BarberSaas.Api.Services;

// Sends mail through the platform admin's own Gmail account via the Gmail API (a plain HTTPS
// call, so unlike SMTP it isn't affected by Railway's outbound port block) -- see
// IOwnerEmailSender for why this is a separate sender from the system-email precedence chain.
// Auth is OAuth2 with a long-lived refresh token (Gmail:ClientId/ClientSecret/RefreshToken,
// obtained once via Google's OAuth Playground -- see the platform-admin panel's email composer
// for the feature this powers); a fresh short-lived access token is exchanged per send rather
// than cached, since this is a low-volume, admin-triggered action, not worth the complexity of a
// token cache.
//
// Every send also reports into OwnerEmailHealth (platform-admin dashboard warning + alert email):
// an auth/server failure marks the channel broken, a success marks it working. A 400 from the
// send call is left out -- that's one bad recipient address, not a broken channel.
public class GmailApiEmailSender(HttpClient http, IConfiguration config, OwnerEmailHealth health) : IOwnerEmailSender
{
    private record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);

    public async Task SendAsync(string toEmail, string subject, string body)
    {
        var fromEmail = config["Gmail:FromEmail"]!;
        string accessToken;
        try
        {
            accessToken = await GetAccessTokenAsync();
        }
        catch (Exception ex)
        {
            health.RecordFailure(ex.Message);
            throw;
        }

        var raw = BuildRawMessage(fromEmail, toEmail, subject, body);

        using var sendRequest = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send");
        sendRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        sendRequest.Content = JsonContent.Create(new { raw });

        HttpResponseMessage sendResp;
        try
        {
            sendResp = await http.SendAsync(sendRequest);
        }
        catch (Exception ex)
        {
            health.RecordFailure(ex.Message);
            throw;
        }
        if (!sendResp.IsSuccessStatusCode)
        {
            var errorBody = await sendResp.Content.ReadAsStringAsync();
            var message = $"Gmail API returned {(int)sendResp.StatusCode} {sendResp.StatusCode}: {errorBody}";
            if (sendResp.StatusCode != System.Net.HttpStatusCode.BadRequest) health.RecordFailure(message);
            throw new HttpRequestException(message);
        }
        health.RecordSuccess();
    }

    public async Task CheckAuthAsync() => await GetAccessTokenAsync();

    private async Task<string> GetAccessTokenAsync()
    {
        var clientId = config["Gmail:ClientId"];
        var clientSecret = config["Gmail:ClientSecret"];
        var refreshToken = config["Gmail:RefreshToken"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException("Gmail is not configured (Gmail:ClientId/ClientSecret/RefreshToken missing)");

        var tokenResp = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        }));
        if (!tokenResp.IsSuccessStatusCode)
        {
            var errorBody = await tokenResp.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Gmail token refresh returned {(int)tokenResp.StatusCode} {tokenResp.StatusCode}: {errorBody}");
        }
        return (await tokenResp.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
    }

    // RFC 2822 message, base64url-encoded per the Gmail API's `raw` field contract. The subject is
    // RFC 2047-encoded so a Hebrew/Arabic business name doesn't corrupt the header; the body is
    // left as plain UTF-8 8bit text (fine within the outer base64url envelope).
    private static string BuildRawMessage(string from, string to, string subject, string body)
    {
        var encodedSubject = $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(subject))}?=";
        var message =
            $"From: {from}\r\n" +
            $"To: {to}\r\n" +
            $"Subject: {encodedSubject}\r\n" +
            "MIME-Version: 1.0\r\n" +
            "Content-Type: text/plain; charset=\"UTF-8\"\r\n" +
            "Content-Transfer-Encoding: 8bit\r\n" +
            "\r\n" +
            body;

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(message))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
