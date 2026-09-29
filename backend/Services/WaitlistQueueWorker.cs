using BarberSaas.Api.Data;

namespace BarberSaas.Api.Services;

// Drives the waitlist queue forward (see WaitlistService.AdvanceQueues): the 5-minute gap
// between one waiting customer and the next is too fine for the 15-minute external cron, so
// this checks in-process every Waitlist:WorkerPollSeconds (default 30). The cron endpoint
// /api/cron/retry-waitlist-notifications still calls the same method as a backstop.
public class WaitlistQueueWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<WaitlistQueueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromSeconds(config.GetValue("Waitlist:WorkerPollSeconds", 30));
        using var timer = new PeriodicTimer(poll);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var waitlist = scope.ServiceProvider.GetRequiredService<WaitlistService>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var (_, sent, failed) = await waitlist.AdvanceQueues();
                if (sent + failed > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                    logger.LogInformation("Waitlist queue advanced: {Sent} sent, {Failed} failed", sent, failed);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Waitlist queue worker tick failed");
            }
        }
    }
}
