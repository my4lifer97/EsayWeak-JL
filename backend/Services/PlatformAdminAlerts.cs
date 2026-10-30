using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
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

    // After a business's WhatsApp number was saved: tells the platform admin when another business
    // has the same one (two sessions on one account would both answer every message). Checked after
    // the save, so two businesses connecting at once still see each other, and from every place
    // that saves the number (the bridge's "connected" event, the admin's status poll, the wa-link
    // page): one of those saving first skipped the check in the others (QA run 6). Never throws.
    public async Task WarnIfNumberSharedAsync(Business business)
    {
        try
        {
            var number = business.WhatsAppNumber;
            if (number is null) return;
            var others = await db.Businesses.Where(b => b.Id != business.Id && b.WhatsAppNumber == number)
                .Select(b => b.Name + " (" + b.Slug + ")").ToListAsync();
            if (others.Count == 0) return;
            logger.LogWarning("WhatsApp number of business {BusinessId} is also saved on {Count} other business(es)", business.Id, others.Count);
            await SendAsync("A WhatsApp number is linked to two businesses",
                $"The bot of {business.Name} ({business.Slug}) connected with a number that is also saved on: {string.Join(", ", others)}. "
                + "Both would answer the same customers. Unlink the one that shouldn't have it.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not send the shared WhatsApp number alert for business {BusinessId}", business.Id);
        }
    }

    // Platform:AdminNotificationEmail when set, otherwise every platform admin's own login email.
    private async Task<List<string>> RecipientsAsync()
    {
        var configured = config["Platform:AdminNotificationEmail"];
        if (!string.IsNullOrWhiteSpace(configured)) return [configured.Trim()];
        return await db.PlatformAdmins.Select(a => a.Email).Where(e => e != "").ToListAsync();
    }
}
