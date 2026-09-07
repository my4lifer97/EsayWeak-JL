namespace BarberSaas.Api.Services;

// No real delivery — the fallback when neither SMTP nor Resend is configured (local dev, tests).
// Logs the whole message so a developer can see what would have gone out; flows that need the
// code/password itself back (email verification, password reset) also return it in the response
// as `devCode` when the environment is Development.
public class DevEmailSender(ILogger<DevEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string email, string subject, string body)
    {
        logger.LogInformation(
            "[DevEmailSender] email not sent (no SMTP/Resend configured)\n  To: {Email}\n  Subject: {Subject}\n  Body:\n{Body}",
            email, subject, body);
        return Task.CompletedTask;
    }
}
