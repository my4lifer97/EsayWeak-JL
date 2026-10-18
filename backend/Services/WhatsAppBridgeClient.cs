using System.Net.Http.Json;
using BarberSaas.Api.DTOs;

namespace BarberSaas.Api.Services;

// HttpClient is configured by Program.cs's AddHttpClient<IWhatsAppBridgeClient, WhatsAppBridgeClient>
// registration -- BaseAddress = WhatsAppBridge:Url, default X-Bridge-Secret header =
// WhatsAppBridge:Secret (same shared-secret pattern as CronSecret; checked again on the bridge's
// own side too).
public class WhatsAppBridgeClient(HttpClient http) : IWhatsAppBridgeClient
{
    // The linking calls report a bridge that is down, timing out or answering with an error as one
    // WhatsAppBridgeUnavailableException (a clear 503 via GlobalExceptionHandler, not a bare 500).
    private static async Task<T> Linking<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new WhatsAppBridgeUnavailableException(ex);
        }
    }

    public async Task SendAsync(string businessId, string toPhone, string message)
    {
        var resp = await http.PostAsJsonAsync("/send", new { businessId, toPhone, message });
        resp.EnsureSuccessStatusCode();
    }

    public async Task<WhatsAppLinkStatusDto> StartLinkAsync(string businessId)
    {
        return await Linking(async () =>
        {
            var resp = await http.PostAsync($"/sessions/{businessId}/start", null);
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<WhatsAppLinkStatusDto>())!;
        });
    }

    public async Task<WhatsAppLinkStatusDto> GetStatusAsync(string businessId)
    {
        return await Linking(async () =>
        {
            var resp = await http.GetAsync($"/sessions/{businessId}/status");
            resp.EnsureSuccessStatusCode();
            return (await resp.Content.ReadFromJsonAsync<WhatsAppLinkStatusDto>())!;
        });
    }

    public async Task UnlinkAsync(string businessId)
    {
        await Linking(async () =>
        {
            var resp = await http.DeleteAsync($"/sessions/{businessId}");
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
