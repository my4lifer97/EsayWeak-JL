using System.Collections.Concurrent;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;

namespace BarberSaas.Api.Tests.Fakes;

public class FakeWhatsAppSender : IWhatsAppSender
{
    public record SentMessage(string BusinessId, string Phone, string Message);

    public ConcurrentBag<SentMessage> Sent { get; } = [];

    // Configurable failure, same style as FakeCardcomService.NextChargeSucceeds -- lets a test
    // simulate a bridge outage/expired session and assert the caller degrades gracefully instead
    // of throwing an unhandled exception up to the controller.
    public bool ShouldFail { get; set; } = false;

    public Task SendAsync(Business business, string toPhone, string message)
    {
        if (ShouldFail)
            throw new HttpRequestException("Response status code does not indicate success: 401 (Unauthorized).");

        Sent.Add(new SentMessage(business.Id, toPhone, message));
        return Task.CompletedTask;
    }
}
