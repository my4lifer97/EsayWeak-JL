namespace BarberSaas.Api.Services;

// Runs PaidPeriodMonitor about a minute after startup and then every hour. Hourly so a failed
// email is retried the same day; the monitor itself makes sure each date alerts only once.
public class PaidPeriodWorker(IServiceScopeFactory scopeFactory, ILogger<PaidPeriodWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextRun = DateTime.UtcNow.AddMinutes(1);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (DateTime.UtcNow < nextRun) continue;
            nextRun = DateTime.UtcNow.AddHours(1);
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PaidPeriodMonitor>().NotifyEndedAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Paid period worker tick failed");
            }
        }
    }
}
