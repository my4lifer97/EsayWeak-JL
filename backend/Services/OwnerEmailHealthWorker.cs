namespace BarberSaas.Api.Services;

// Probes the Gmail owner-email channel shortly after startup and then every
// OwnerEmailHealth:CheckIntervalHours (default 6), so a dead refresh token is noticed even when
// no owner email happens to be sent. Every minute it also sends any pending alert -- that covers
// failures recorded by real sends (GmailApiEmailSender) between probes.
public class OwnerEmailHealthWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<OwnerEmailHealthWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var checkInterval = TimeSpan.FromHours(config.GetValue("OwnerEmailHealth:CheckIntervalHours", 6.0));
        var nextCheck = DateTime.UtcNow.AddSeconds(30);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var monitor = scope.ServiceProvider.GetRequiredService<OwnerEmailMonitor>();
                if (DateTime.UtcNow >= nextCheck)
                {
                    await monitor.CheckNowAsync();
                    nextCheck = DateTime.UtcNow + checkInterval;
                }
                await monitor.NotifyIfNeededAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Owner email health worker tick failed");
            }
        }
    }
}
