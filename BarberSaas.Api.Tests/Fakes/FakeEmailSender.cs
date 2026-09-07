using System.Collections.Concurrent;
using BarberSaas.Api.Services;

namespace BarberSaas.Api.Tests.Fakes;

// Records what would have been sent instead of delivering. Swapped in for whichever real
// IEmailSender Program.cs wired up, so tests can assert on approval / verification / reset mail
// without a live SMTP or Resend account.
public class FakeEmailSender : IEmailSender
{
    public record SentEmail(string Email, string Subject, string Body);

    public ConcurrentBag<SentEmail> Sent { get; } = [];

    public Task SendAsync(string email, string subject, string body)
    {
        Sent.Add(new SentEmail(email, subject, body));
        return Task.CompletedTask;
    }
}
