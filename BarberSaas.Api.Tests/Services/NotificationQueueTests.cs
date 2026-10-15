using BarberSaas.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

// The integration tests run the queue inline (Notifications__Inline); these cover the real,
// background mode: the request doesn't wait for a slow send, and a failing send doesn't stop the
// ones after it.
public class NotificationQueueTests
{
    private static (NotificationQueue Queue, NotificationWorker Worker) Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<NotificationQueue>();
        var provider = services.BuildServiceProvider();
        var queue = provider.GetRequiredService<NotificationQueue>();
        return (queue, new NotificationWorker(queue));
    }

    [Fact]
    public async Task Dispatch_DoesNotWaitForTheSend()
    {
        var (queue, worker) = Build();
        await worker.StartAsync(CancellationToken.None);
        var release = new TaskCompletionSource();
        var ran = new TaskCompletionSource();

        var dispatch = queue.DispatchAsync([new NotificationJob("slow", async _ => { await release.Task; ran.SetResult(); })]);

        Assert.False(queue.Inline);
        Assert.True(dispatch.IsCompletedSuccessfully);
        Assert.False(ran.Task.IsCompleted);
        release.SetResult();
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task FailingJob_DoesNotStopTheNextOnes()
    {
        var (queue, worker) = Build();
        await worker.StartAsync(CancellationToken.None);
        var ran = new TaskCompletionSource();

        await queue.DispatchAsync([
            new NotificationJob("fails", _ => throw new HttpRequestException("bridge down")),
            new NotificationJob("works", _ => { ran.SetResult(); return Task.CompletedTask; }),
        ]);

        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Outbox_SendsNothingUntilFlushed_AndNothingAfterDiscard()
    {
        var (queue, worker) = Build();
        await worker.StartAsync(CancellationToken.None);
        var sent = 0;
        var outbox = new NotificationOutbox(queue);

        outbox.Add("discarded", _ => { Interlocked.Increment(ref sent); return Task.CompletedTask; });
        outbox.Discard();
        var flushed = new TaskCompletionSource();
        outbox.Add("flushed", _ => { Interlocked.Increment(ref sent); flushed.SetResult(); return Task.CompletedTask; });
        await Task.Delay(100);
        Assert.Equal(0, sent);

        await outbox.FlushAsync();
        await flushed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, sent);
        await worker.StopAsync(CancellationToken.None);
    }
}
