using System.Collections.Concurrent;

namespace BarberSaas.Api.Services;

// Limits failed password logins (owner + platform admin) -- QA found 25 wrong passwords in 4s all
// checked. Failures within LoginThrottle:WindowMinutes (default 15) are counted three ways:
//   - per account per IP (default 5): stops guessing one password, while a stranger on another
//     network can't lock the real owner out of their own account;
//   - per account, any IP (default 20): stops guessing spread across many addresses;
//   - per IP, any account (default 50): stops one address sweeping many accounts.
// A successful login clears its account+IP count. In memory on purpose -- one backend instance, and
// a restart only resets the counters. The IP is the one Railway's proxy saw (UseForwardedHeaders).
public class LoginThrottle(IConfiguration config)
{
    private readonly ConcurrentDictionary<string, List<DateTime>> _failures = new();

    private TimeSpan Window => TimeSpan.FromMinutes(config.GetValue("LoginThrottle:WindowMinutes", 15));

    private IEnumerable<(string Key, int Limit)> Keys(string scope, string identifier, string? ip)
    {
        var id = identifier.Trim().ToLowerInvariant();
        ip ??= "unknown";
        yield return ($"{scope}|acct-ip|{id}|{ip}", config.GetValue("LoginThrottle:PerAccountPerIp", 5));
        yield return ($"{scope}|acct|{id}", config.GetValue("LoginThrottle:PerAccount", 20));
        yield return ($"{scope}|ip|{ip}", config.GetValue("LoginThrottle:PerIp", 50));
    }

    // How long until this login may be tried again, or null if it's allowed now.
    public TimeSpan? RetryAfter(string scope, string identifier, string? ip, DateTime now)
    {
        TimeSpan? longest = null;
        foreach (var (key, limit) in Keys(scope, identifier, ip))
        {
            if (!_failures.TryGetValue(key, out var list)) continue;
            lock (list)
            {
                list.RemoveAll(t => now - t >= Window);
                if (list.Count < limit) continue;
                // Allowed again once enough of the oldest failures age out of the window.
                var wait = list[list.Count - limit] + Window - now;
                if (longest is null || wait > longest) longest = wait;
            }
        }
        return longest;
    }

    public void RecordFailure(string scope, string identifier, string? ip, DateTime now)
    {
        if (_failures.Count > 50_000) Prune(now);
        foreach (var (key, _) in Keys(scope, identifier, ip))
        {
            var list = _failures.GetOrAdd(key, _ => []);
            lock (list) list.Add(now);
        }
    }

    public void RecordSuccess(string scope, string identifier, string? ip) =>
        _failures.TryRemove(Keys(scope, identifier, ip).First().Key, out _);

    private void Prune(DateTime now)
    {
        foreach (var (key, list) in _failures)
        {
            bool stale;
            lock (list) stale = list.Count == 0 || now - list[^1] >= Window;
            if (stale) _failures.TryRemove(key, out _);
        }
    }

    public static string TooManyAttemptsMessage(TimeSpan retryAfter) =>
        Math.Max(1, (int)Math.Ceiling(retryAfter.TotalMinutes)) is var minutes && minutes == 1
            ? "Too many sign-in attempts. Try again in 1 minute."
            : $"Too many sign-in attempts. Try again in {minutes} minutes.";
}
