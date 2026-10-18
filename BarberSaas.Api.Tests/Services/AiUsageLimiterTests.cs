using BarberSaas.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

public class AiUsageLimiterTests
{
    private static AiUsageLimiter Limiter(int limit) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["OpenAI:MaxMessagesPerPhonePerDay"] = limit.ToString() })
        .Build());

    [Fact]
    public void AllowsUpToTheLimit_ThenRefuses_PerBusinessAndPhone()
    {
        var limiter = Limiter(2);
        var now = DateTime.UtcNow;

        Assert.True(limiter.TryUse("b1", "+972501111111", now));
        Assert.True(limiter.TryUse("b1", "+972501111111", now));
        Assert.False(limiter.TryUse("b1", "+972501111111", now));

        // Another phone, or the same phone at another business, has its own allowance.
        Assert.True(limiter.TryUse("b1", "+972502222222", now));
        Assert.True(limiter.TryUse("b2", "+972501111111", now));
    }

    [Fact]
    public void AllowsAgain_OnceOldMessagesAreADayOld()
    {
        var limiter = Limiter(1);
        var start = DateTime.UtcNow;

        Assert.True(limiter.TryUse("b1", "+972501111111", start));
        Assert.False(limiter.TryUse("b1", "+972501111111", start.AddHours(23)));
        Assert.True(limiter.TryUse("b1", "+972501111111", start.AddHours(24)));
    }
}
