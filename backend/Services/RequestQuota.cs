using System.Collections.Concurrent;

namespace BarberSaas.Api.Services;

// In-memory per-key request quota over a sliding window (like LoginThrottle), for anonymous
// endpoints that cost money or space: an SMS login code per phone was the only limit, so a script
// could send codes to any number of phones, and anonymous photo uploads had none (QA run 6).
// Per server instance and lost on restart, which is fine for slowing abuse down.
public class RequestQuota(IConfiguration config)
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _hits = new();

    public int OtpPerIpPerHour => config.GetValue("RequestQuota:OtpPerIpPerHour", 10);
    public int PhotoUploadsPerIpPerHour => config.GetValue("RequestQuota:PhotoUploadsPerIpPerHour", 30);

    // Counts one request for `key` and says whether it's within `limit` per `window`; when it
    // isn't, also how long until the oldest counted request leaves the window. A refused request
    // isn't counted.
    public (bool Allowed, TimeSpan RetryAfter) TryTake(string key, int limit, TimeSpan window, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        var hits = _hits.GetOrAdd(key, _ => new Queue<DateTime>());
        lock (hits)
        {
            while (hits.Count > 0 && now - hits.Peek() >= window) hits.Dequeue();
            if (hits.Count >= limit) return (false, window - (now - hits.Peek()));
            hits.Enqueue(now);
            return (true, TimeSpan.Zero);
        }
    }
}
