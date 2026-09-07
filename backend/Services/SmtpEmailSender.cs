using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace BarberSaas.Api.Services;

// Sends real email through a plain SMTP server. Built for Gmail (smtp.gmail.com:587 + a Google
// account "app password") but works with any SMTP host. Registered in Program.cs when
// Smtp:Username and Smtp:Password are both set, taking precedence over ResendEmailSender.
//
// Uses MailKit rather than System.Net.Mail.SmtpClient: the BCL client mishandles Gmail's STARTTLS
// negotiation ("MustIssueStartTlsFirst" / "5.7.0 Authentication Required") and Microsoft marks it
// obsolete for new code. MailKit is the standard .NET SMTP library.
public class SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string email, string subject, string body)
    {
        var host = config["Smtp:Host"] ?? "smtp.gmail.com";
        var port = int.TryParse(config["Smtp:Port"], out var parsed) ? parsed : 587;
        var username = config["Smtp:Username"]!;
        var password = config["Smtp:Password"]!;
        var fromEmail = config["Smtp:FromEmail"] ?? username;
        var fromName = config["Smtp:FromName"] ?? "EsayWeek";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromEmail));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        try
        {
            // 587 -> STARTTLS (upgrade a plaintext connection); 465 -> implicit TLS.
            var socketOptions = port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
            await client.ConnectAsync(host, port, socketOptions);
            await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(quit: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SMTP send to {Email} via {Host}:{Port} failed", email, host, port);
            throw;
        }
    }
}
