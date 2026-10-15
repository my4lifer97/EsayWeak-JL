using System.Threading.Channels;
using BarberSaas.Api.Models;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BarberSaas.Api.Services;

// Best-effort WhatsApp/email sends used to run inside the request, so the owner or customer
// waited for the bridge/Gmail round trip (QA run 2: an owner cancel that offered the slot to the
// waitlist took ~8s). Now a request adds them to its NotificationOutbox, and once the action has
// finished (its SaveChanges done) DispatchNotificationsFilter hands them to NotificationQueue,
// which NotificationWorker runs in the background. A job never captures request-scoped services;
// it resolves the sender from its own scope. Sends whose result the user needs to see (login
// codes, platform-admin emails) still run in the request.
public record NotificationJob(string Description, Func<IServiceProvider, Task> Send);

// Scoped: what this request wants sent once it has succeeded.
public class NotificationOutbox(NotificationQueue queue)
{
    private readonly List<NotificationJob> pending = [];

    public void Add(string description, Func<IServiceProvider, Task> send) => pending.Add(new(description, send));

    public void AddWhatsApp(Business business, string toPhone, string message, string description) =>
        Add(description, sp => sp.GetRequiredService<IWhatsAppSender>().SendAsync(business, toPhone, message));

    public void AddOwnerEmail(string toEmail, string subject, string body, string description) =>
        Add(description, sp => sp.GetRequiredService<IOwnerEmailSender>().SendAsync(toEmail, subject, body));

    public Task FlushAsync()
    {
        var jobs = pending.ToList();
        pending.Clear();
        return queue.DispatchAsync(jobs);
    }

    public void Discard() => pending.Clear();
}

// Singleton. Notifications:Inline (tests) runs each job right away instead, so a test can assert
// on what was sent as soon as the response arrives.
public class NotificationQueue(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<NotificationQueue> logger)
{
    private readonly Channel<NotificationJob> channel = Channel.CreateUnbounded<NotificationJob>();

    public bool Inline { get; } = config.GetValue("Notifications:Inline", false);

    public ChannelReader<NotificationJob> Reader => channel.Reader;

    public async Task DispatchAsync(IEnumerable<NotificationJob> jobs)
    {
        foreach (var job in jobs)
        {
            if (Inline) await RunAsync(job);
            else channel.Writer.TryWrite(job);
        }
    }

    public async Task RunAsync(NotificationJob job)
    {
        using var scope = scopeFactory.CreateScope();
        try
        {
            await job.Send(scope.ServiceProvider);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background notification failed: {Description}", job.Description);
        }
    }
}

// Runs queued jobs, a few at a time so one slow bridge call doesn't hold up the rest. Jobs still
// queued when the app stops (a redeploy) are lost -- they were best-effort before too.
public class NotificationWorker(NotificationQueue queue) : BackgroundService
{
    private const int MaxConcurrent = 4;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var slots = new SemaphoreSlim(MaxConcurrent);
        try
        {
            await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
            {
                await slots.WaitAsync(stoppingToken);
                _ = Task.Run(async () =>
                {
                    try { await queue.RunAsync(job); }
                    finally { slots.Release(); }
                }, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) { }
    }
}

// Global: dispatches the request's outbox after the action ran (before the response is written),
// or drops it if the action threw.
public class DispatchNotificationsFilter(NotificationOutbox outbox) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();
        if (executed.Exception is null || executed.ExceptionHandled) await outbox.FlushAsync();
        else outbox.Discard();
    }
}
