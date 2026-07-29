using System.Collections.Concurrent;
using BarberSaas.Api.Services;

namespace BarberSaas.Api.Tests.Fakes;

public class FakeCardcomService : ICardcomService
{
    public ConcurrentBag<CardcomLowProfileCreateParams> CreateCalls { get; } = [];
    // Tests seed this before hitting the webhook endpoint to control what
    // GetLowProfileResultAsync returns for a given LowProfileId.
    public ConcurrentDictionary<string, CardcomLowProfileResult> Results { get; } = new();
    public bool NextChargeSucceeds { get; set; } = true;

    public Task<CardcomLowProfileCreateResult> CreateLowProfileAsync(CardcomLowProfileCreateParams p)
    {
        CreateCalls.Add(p);
        var lowProfileId = Guid.NewGuid().ToString("N");
        return Task.FromResult(new CardcomLowProfileCreateResult(0, null, lowProfileId, $"https://fake-cardcom.test/pay/{lowProfileId}"));
    }

    public Task<CardcomLowProfileResult> GetLowProfileResultAsync(string lowProfileId) =>
        Task.FromResult(Results.TryGetValue(lowProfileId, out var r)
            ? r
            : new CardcomLowProfileResult(1, "not found", lowProfileId, null, null, null, null));

    public Task<CardcomChargeResult> ChargeByTokenAsync(string token, decimal amount, string productName) =>
        Task.FromResult(NextChargeSucceeds
            ? new CardcomChargeResult(0, null, Guid.NewGuid().ToString("N"))
            : new CardcomChargeResult(1, "declined", null));
}
