using System.Collections.Concurrent;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;

namespace BarberSaas.Api.Tests.Fakes;

public class FakeWhatsAppSender : IWhatsAppSender
{
    public record SentMessage(string BusinessId, string Phone, string Message);

    public ConcurrentBag<SentMessage> Sent { get; } = [];

    public Task SendAsync(Business business, string toPhone, string message)
    {
        Sent.Add(new SentMessage(business.Id, toPhone, message));
        return Task.CompletedTask;
    }
}
