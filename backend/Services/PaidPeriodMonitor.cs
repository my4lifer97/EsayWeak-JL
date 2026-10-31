using BarberSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

// Emails the platform admin once when a business's Business.PaidUntil has passed. Only tells them:
// the business keeps taking bookings until the admin renews or disables it. A disabled business is
// skipped, since that decision is already made. A failed send isn't recorded, so the next run
// retries it; that business doesn't hold up the others.
public class PaidPeriodMonitor(
    AppDbContext db, PlatformAdminAlerts alerts, IConfiguration config, ILogger<PaidPeriodMonitor> logger)
{
    public async Task NotifyEndedAsync()
    {
        var today = DateOnly.FromDateTime(AppClock.Now);
        var ended = await db.Businesses
            .Where(b => !b.IsDisabled && b.PaidUntil != null && b.PaidUntil < today
                && (b.PaidUntilAlertSentFor == null || b.PaidUntilAlertSentFor != b.PaidUntil))
            .ToListAsync();

        var appUrl = (config["AppUrl"] ?? "").TrimEnd('/');
        foreach (var business in ended)
        {
            var paidUntil = business.PaidUntil!.Value;
            var contact = string.Join(" · ", new[] { business.Email, business.Phone }.Where(v => !string.IsNullOrWhiteSpace(v)));
            try
            {
                var sent = await alerts.SendAsync($"Payment period ended: {business.Name}",
                    $"{business.Name} ({business.Slug}) was paid until {paidUntil:yyyy-MM-dd}, and that period has ended.\n\n" +
                    $"Owner: {contact}\n\n" +
                    "The business keeps working until you act. If they paid, set a new date. If not, you can disable the account.\n\n" +
                    $"{appUrl}/platform-admin/businesses/{business.Id}");
                if (!sent) return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send the paid-period alert for business {BusinessId}", business.Id);
                continue;
            }
            business.PaidUntilAlertSentFor = paidUntil;
            await db.SaveChangesAsync();
        }
    }
}
