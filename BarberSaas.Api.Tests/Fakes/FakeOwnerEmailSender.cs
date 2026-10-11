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

    // Set to make SendAsync fail the way Gmail does with a dead token.
    public string? SendError { get; set; }

    public Task SendAsync(string toEmail, string subject, string body)
    {
        if (SendError is not null) return Task.FromException(new HttpRequestException(SendError));
        Sent.Add(new SentEmail(toEmail, subject, body));
        return Task.CompletedTask;
    }

    // Set to make CheckAuthAsync fail the way a dead Gmail refresh token does.
    public string? AuthError { get; set; }

    public Task CheckAuthAsync() =>
        AuthError is null ? Task.CompletedTask : Task.FromException(new HttpRequestException(AuthError));
}
