using System.Net.Http.Json;
using BarberSaas.Api.DTOs;

namespace BarberSaas.Api.Services;

// HttpClient is configured by Program.cs's AddHttpClient<IWhatsAppBridgeClient, WhatsAppBridgeClient>
// registration -- BaseAddress = WhatsAppBridge:Url, default X-Bridge-Secret header =
// WhatsAppBridge:Secret (same shared-secret pattern as CronSecret; checked again on the bridge's
// own side too).
public class WhatsAppBridgeClient(HttpClient http) : IWhatsAppBridgeClient
{
    public async Task SendAsync(string businessId, string toPhone, string message)
    {
        var resp = await http.PostAsJsonAsync("/send", new { businessId, toPhone, message });
        resp.EnsureSuccessStatusCode();
    }

    public async Task<WhatsAppLinkStatusDto> StartLinkAsync(string businessId)
    {
        var resp = await http.PostAsync($"/sessions/{businessId}/start", null);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<WhatsAppLinkStatusDto>())!;
    }

    public async Task<WhatsAppLinkStatusDto> GetStatusAsync(string businessId)
    {
        var resp = await http.GetAsync($"/sessions/{businessId}/status");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<WhatsAppLinkStatusDto>())!;
    }

    public async Task UnlinkAsync(string businessId)
    {
        var resp = await http.DeleteAsync($"/sessions/{businessId}");
        resp.EnsureSuccessStatusCode();
    }
}
