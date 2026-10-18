using BarberSaas.Api.DTOs;
using BarberSaas.Api.Services;

namespace BarberSaas.Api.Tests.Fakes;

// Real whatsapp-bridge calls would dial out to a service that doesn't exist in tests -- this
// records/returns canned responses instead. Tests that need specific link-flow behavior can cast
// and set these directly (mirrors FakeCardcomService's configurable-response style).
public class FakeWhatsAppBridgeClient : IWhatsAppBridgeClient
{
    public record SentMessage(string BusinessId, string ToPhone, string Message);

    public List<SentMessage> Sent { get; } = [];
    public WhatsAppLinkStatusDto StatusToReturn { get; set; } = new("qr", "data:image/png;base64,fake", null);

    public Task SendAsync(string businessId, string toPhone, string message)
    {
        Sent.Add(new SentMessage(businessId, toPhone, message));
        return Task.CompletedTask;
    }

    // Simulates the bridge being down for the linking calls.
    public bool Unreachable { get; set; }
    private Task<T> Linking<T>(T result) => Unreachable
        ? Task.FromException<T>(new WhatsAppBridgeUnavailableException(new HttpRequestException("connection refused")))
        : Task.FromResult(result);

    public Task<WhatsAppLinkStatusDto> StartLinkAsync(string businessId) => Linking(StatusToReturn);
    public Task<WhatsAppLinkStatusDto> GetStatusAsync(string businessId) => Linking(StatusToReturn);
    public Task UnlinkAsync(string businessId) => Linking(true);
}
