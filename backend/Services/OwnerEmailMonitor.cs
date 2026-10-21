namespace BarberSaas.Api.Services;

// Checks the Gmail owner-email channel and tells the platform admin when it breaks (see
// OwnerEmailHealth). The alert goes through the system IEmailSender chain (Brevo in production),
// not Gmail -- the broken channel can't report its own failure.
public class OwnerEmailMonitor(
    OwnerEmailHealth health, IOwnerEmailSender ownerEmailSender, PlatformAdminAlerts alerts,
    IConfiguration config, ILogger<OwnerEmailMonitor> logger)
{
    public async Task CheckNowAsync()
    {
        try
        {
            await ownerEmailSender.CheckAuthAsync();
            health.RecordSuccess();
        }
        catch (Exception ex)
        {
            health.RecordFailure(ex.Message);
            logger.LogError(ex, "Owner email (Gmail) health check failed");
        }
    }

    public async Task NotifyIfNeededAsync()
    {
        var now = DateTime.UtcNow;
        var repeatAfter = TimeSpan.FromHours(config.GetValue("OwnerEmailHealth:AlertRepeatHours", 24));
        var pending = health.PendingNotification(now, repeatAfter);
        if (pending == OwnerEmailHealth.Notification.None) return;

        var snapshot = health.GetSnapshot();
        var (subject, body) = pending == OwnerEmailHealth.Notification.Failing
            ? ("Action needed: business owner emails are failing",
                "Emails to business owners (sent through Gmail) are failing" +
                $"{(snapshot.FailingSince is { } since ? $" since {since:yyyy-MM-dd HH:mm} UTC" : "")}.\n\n" +
                $"Last error: {snapshot.LastError}\n\n" +
                "Until this is fixed, owner login details, business-request email codes, " +
                "cancellation-approval emails and chatbot inquiry emails are not delivered.\n\n" +
                "If the error says invalid_grant, the Gmail refresh token expired or was revoked: get a new one " +
                "from the OAuth Playground and replace Gmail__RefreshToken in Railway (backend service). " +
                "The platform admin dashboard shows the current status.")
            : ("Business owner emails are working again",
                "The Gmail connection for business owner emails is working again. No action needed.");

        try
        {
            if (await alerts.SendAsync(subject, body)) health.MarkNotified(pending, now);
        }
        catch (Exception ex)
        {
            // Not marked as sent, so it's retried -- after a pause, not every tick.
            health.DeferNotification(now.AddMinutes(15));
            logger.LogError(ex, "Failed to send the owner-email health alert; retrying in 15 minutes");
        }
    }

}
