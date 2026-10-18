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
    private readonly ConcurrentDictionary<string, List<DateTime>> _calls = new();

    private int Limit => config.GetValue("OpenAI:MaxMessagesPerPhonePerDay", 50);

    // Counts this message and says whether it may use OpenAI.
    public bool TryUse(string businessId, string phone, DateTime now)
    {
        if (_calls.Count > 50_000) Prune(now);
        var list = _calls.GetOrAdd($"{businessId}|{phone}", _ => []);
        lock (list)
        {
            list.RemoveAll(t => now - t >= Window);
            if (list.Count >= Limit) return false;
            list.Add(now);
            return true;
        }
    }

    private void Prune(DateTime now)
    {
        foreach (var (key, list) in _calls)
        {
            bool stale;
            lock (list) stale = list.Count == 0 || now - list[^1] >= Window;
            if (stale) _calls.TryRemove(key, out _);
        }
    }
}
