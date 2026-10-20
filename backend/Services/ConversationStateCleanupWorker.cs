using BarberSaas.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Services;

// Deletes WhatsAppConversationState rows that expired long ago. Every read already ignores (or
// resets, WhatsAppController.ResetIfExpired) an expired row, so they're dead weight: production had
// 38 rows, all but one expired weeks earlier. A row is kept for a day past its ExpiresAt so a
// message being handled right as it expires never has its row deleted under it.
public class ConversationStateCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<ConversationStateCleanupWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Grace = TimeSpan.FromDays(1);

    public static Task<int> DeleteExpiredAsync(AppDbContext db, DateTime utcNow, CancellationToken ct = default)
    {
        var cutoff = utcNow - Grace;
        return db.WhatsAppConversationStates.Where(s => s.ExpiresAt < cutoff).ExecuteDeleteAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First pass soon after startup, then hourly.
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var deleted = await DeleteExpiredAsync(db, DateTime.UtcNow, stoppingToken);
                if (deleted > 0)
                    logger.LogInformation("Deleted {Count} expired WhatsApp conversation rows", deleted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Conversation state cleanup failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
