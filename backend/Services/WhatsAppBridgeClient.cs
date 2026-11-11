using System.Net.Http.Json;
using BarberSaas.Api.DTOs;

namespace BarberSaas.Api.Services;

// HttpClient is configured by Program.cs's AddHttpClient<IWhatsAppBridgeClient, WhatsAppBridgeClient>
// registration -- BaseAddress = WhatsAppBridge:Url, default X-Bridge-Secret header =
// WhatsAppBridge:Secret (same shared-secret pattern as CronSecret; checked again on the bridge's
// own side too).
public class WhatsAppBridgeClient(HttpClient http) : IWhatsAppBridgeClient
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    // Up to ~14MB of base64 that the bridge then uploads to WhatsApp (its own WHATSAPP_TIMEOUT_MS
    // is 60 s): 15 s told the owner "may not have been sent" on a slow link (QA run 11).
    public static readonly TimeSpan MediaTimeout = TimeSpan.FromSeconds(60);

    private static CancellationToken Within(TimeSpan timeout) => new CancellationTokenSource(timeout).Token;

    // The linking calls report a bridge that is down, timing out or answering with an error as one
    // WhatsAppBridgeUnavailableException (a clear 503 via GlobalExceptionHandler, not a bare 500).
    // A 200 with an empty or unreadable body counts as the bridge not answering: passed on, the
    // admin page showed "Connecting..." forever (QA run 6).
    private static async Task<WhatsAppLinkStatusDto> ReadStatusAsync(HttpResponseMessage resp)
    {
        var status = await resp.Content.ReadFromJsonAsync<WhatsAppLinkStatusDto>();
        if (status is null || string.IsNullOrEmpty(status.State)) throw new HttpRequestException("The bridge sent no session status");
        return status;
    }

    private static async Task<T> Linking<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            throw new WhatsAppBridgeUnavailableException(ex);
        }
    }

    public async Task SendAsync(string businessId, string toPhone, string message)
    {
        var resp = await http.PostAsJsonAsync("/send", new { businessId, toPhone, message }, Within(DefaultTimeout));
        resp.EnsureSuccessStatusCode();
    }

    // The file goes inline (base64): the bridge is on Railway's private network, and the
    // attachment lives in the database, not at a URL the bridge could fetch.
    public async Task SendMediaAsync(string businessId, string toPhone, string caption, string fileName, string contentType, byte[] data)
    {
        var resp = await http.PostAsJsonAsync("/send", new
        {
            businessId, toPhone, message = caption,
            media = new { base64 = Convert.ToBase64String(data), mimetype = contentType, fileName },
        }, Within(MediaTimeout));
        resp.EnsureSuccessStatusCode();
    }

    public async Task<WhatsAppLinkStatusDto> StartLinkAsync(string businessId)
    {
        return await Linking(async () =>
        {
            var resp = await http.PostAsync($"/sessions/{businessId}/start", null, Within(DefaultTimeout));
            resp.EnsureSuccessStatusCode();
            return await ReadStatusAsync(resp);
        });
    }

    public async Task<WhatsAppLinkStatusDto> GetStatusAsync(string businessId)
    {
        return await Linking(async () =>
        {
            var resp = await http.GetAsync($"/sessions/{businessId}/status", Within(DefaultTimeout));
            resp.EnsureSuccessStatusCode();
            return await ReadStatusAsync(resp);
        });
    }

    public async Task UnlinkAsync(string businessId)
    {
        await Linking(async () =>
        {
            var resp = await http.DeleteAsync($"/sessions/{businessId}", Within(DefaultTimeout));
            resp.EnsureSuccessStatusCode();
            return true;
        });
    }
}

// Subclasses HttpRequestException so existing catches of that still see it.
public class WhatsAppBridgeUnavailableException(Exception inner)
    : HttpRequestException("The WhatsApp service is unreachable right now. Try again in a minute.", inner)
{
}
