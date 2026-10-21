using BarberSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

// Emails the platform admin about something that needs their action (Gmail owner emails failing,
// a WhatsApp bot logged out). Goes through the system IEmailSender chain (Brevo in production),
// never Gmail -- that's one of the channels these alerts report on.
public class PlatformAdminAlerts(IEmailSender emailSender, AppDbContext db, IConfiguration config, ILogger<PlatformAdminAlerts> logger)
{
    // False when there's nobody to send to. A failed send throws, so the caller decides whether
    // to retry.
    public async Task<bool> SendAsync(string subject, string body)
    {
        var recipients = await RecipientsAsync();
        if (recipients.Count == 0)
        {
            logger.LogWarning("No platform admin email to send the alert \"{Subject}\" to", subject);
            return false;
        }
        foreach (var to in recipients) await emailSender.SendAsync(to, subject, body);
        return true;
    }

    // Platform:AdminNotificationEmail when set, otherwise every platform admin's own login email.
    private async Task<List<string>> RecipientsAsync()
    {
        var configured = config["Platform:AdminNotificationEmail"];
        if (!string.IsNullOrWhiteSpace(configured)) return [configured.Trim()];
        return await db.PlatformAdmins.Select(a => a.Email).Where(e => e != "").ToListAsync();
    }
}
