using System.Collections.Concurrent;

namespace BarberSaas.Api.Services;

// Caps how many messages per customer phone (per business) go to OpenAI in a rolling 24 hours,
// default OpenAI:MaxMessagesPerPhonePerDay = 50. The "$" lockout only counts turns without a
// booking/cancel, so a phone could still loop forever on paid calls (QA run 2, "Not covered").
// Past the cap that phone gets the free rule-based bot until older messages age out; nothing is
// refused. In memory like LoginThrottle: one backend instance, and a restart only resets counts.
public class AiUsageLimiter(IConfiguration config)
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, Usage> _calls = new();
    private long _lastPruneTicks;

    private int Limit => config.GetValue("OpenAI:MaxMessagesPerPhonePerDay", 50);

    // Removed is set under the lock when Prune drops the entry, so a TryUse that got it just before
    // starts over on a fresh one instead of counting into an orphan (QA run 4).
    private sealed class Usage
    {
        public readonly List<DateTime> Times = [];
        public bool Removed;
    }

    // Counts this message and says whether it may use OpenAI.
    public bool TryUse(string businessId, string phone, DateTime now)
    {
        // At most once a minute: past 50k keys it used to scan everything on every message.
        if (_calls.Count > 50_000)
        {
            var last = Interlocked.Read(ref _lastPruneTicks);
            if (now.Ticks - last >= PruneInterval.Ticks && Interlocked.CompareExchange(ref _lastPruneTicks, now.Ticks, last) == last)
                Prune(now);
        }
        while (true)
        {
            var usage = _calls.GetOrAdd($"{businessId}|{phone}", _ => new Usage());
            lock (usage)
            {
                if (usage.Removed) continue;
                usage.Times.RemoveAll(t => now - t >= Window);
                if (usage.Times.Count >= Limit) return false;
                usage.Times.Add(now);
                return true;
            }
        }
    }

    private void Prune(DateTime now)
    {
        foreach (var (key, usage) in _calls)
        {
            lock (usage)
            {
                if (usage.Times.Count > 0 && now - usage.Times[^1] < Window) continue;
                usage.Removed = true;
                _calls.TryRemove(key, out _);
            }
        }
    }
}
