using System.Collections.Concurrent;

namespace BarberSaas.Api.Services;

// The owner wrote to a customer from the linked phone (the bridge reports it on bridge/outgoing):
// the bot stays out of that conversation for a while, so it doesn't answer over the owner with its
// menu (QA run 11). In memory, like LoginThrottle: a restart only ends the quiet early.
// Chatbot:OwnerQuietMinutes, default 30.
public class OwnerTalkingWindow(IConfiguration config)
{
    private readonly ConcurrentDictionary<(string BusinessId, string Phone), DateTime> _until = new();

    private TimeSpan Length => TimeSpan.FromMinutes(config.GetValue("Chatbot:OwnerQuietMinutes", 30.0));

    public void OwnerWrote(string businessId, string phone, DateTime utcNow)
    {
        _until[(businessId, phone)] = utcNow + Length;
        // Drop old entries now and then so the map doesn't grow forever.
        if (_until.Count > 1000)
            foreach (var kv in _until.Where(kv => kv.Value <= utcNow).ToList()) _until.TryRemove(kv.Key, out _);
    }

    public bool IsQuiet(string businessId, string phone, DateTime utcNow) =>
        _until.TryGetValue((businessId, phone), out var until) && until > utcNow;
}
