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

    // A slow bridge, for checking a request doesn't wait on the send (BackgroundNotificationTests).
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    // Runs during a send, before it completes: lets a test change the database mid-run (a customer
    // writing in while the reminder cron is going, AttendanceConfirmationTests).
    public Func<Task>? OnSend { get; set; }

    public async Task SendAsync(Business business, string toPhone, string message)
    {
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay);
        if (OnSend is not null) await OnSend();
        if (ShouldFail)
            throw new HttpRequestException("Response status code does not indicate success: 401 (Unauthorized).");

        Sent.Add(new SentMessage(business.Id, toPhone, message));
    }
}
