using System.Collections.Concurrent;
using BarberSaas.Api.Services;

namespace BarberSaas.Api.Tests.Fakes;

// Real Gmail API calls would need live OAuth credentials and would actually send mail -- swap in
// a fake that just records, mirroring FakeEmailSender's pattern. See IOwnerEmailSender for why
// this is a separate sender from the system IEmailSender chain.
public class FakeOwnerEmailSender : IOwnerEmailSender
{
    public record SentEmail(string Email, string Subject, string Body);

    public ConcurrentBag<SentEmail> Sent { get; } = [];

    public Task SendAsync(string toEmail, string subject, string body)
    {
        Sent.Add(new SentEmail(toEmail, subject, body));
        return Task.CompletedTask;
    }
}
