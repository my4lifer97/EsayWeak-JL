using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

public class ClientIpTests
{
    private static string? Resolve(string? xff = null, string? realIp = null)
    {
        var headers = new HeaderDictionary();
        if (xff is not null) headers["X-Forwarded-For"] = xff;
        if (realIp is not null) headers["X-Real-IP"] = realIp;
        return ClientIp.Resolve(headers)?.ToString();
    }

    [Fact]
    public void FirstForwardedForEntry_IsTheClient_NotTheEdge() =>
        Assert.Equal("203.0.113.5", Resolve(xff: "203.0.113.5, 66.33.22.11", realIp: "66.33.22.11"));

    [Fact]
    public void PrivateAndRailwayInternalAddresses_AreSkipped() =>
        Assert.Equal("198.51.100.7", Resolve(xff: "10.0.0.4, 100.64.0.9, 198.51.100.7"));

    [Fact]
    public void RealIp_IsUsed_WhenForwardedForIsMissing() =>
        Assert.Equal("203.0.113.1", Resolve(realIp: "203.0.113.1"));

    [Fact]
    public void OnlyPrivateAddresses_FallBackToTheFirst() =>
        Assert.Equal("100.64.0.2", Resolve(xff: "100.64.0.2", realIp: "10.1.1.1"));

    [Fact]
    public void NoHeaders_ResolveToNothing() => Assert.Null(Resolve());

    [Fact]
    public void Ipv6Client_IsKept() =>
        Assert.Equal("2001:db8::1", Resolve(xff: "2001:db8::1, 66.33.22.11"));

    private static bool Trust(params (string Key, string Value)[] values) =>
        ClientIp.TrustForwardedHeaders(new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build());

    [Fact]
    public void ForwardedHeaders_AreIgnored_OffRailway() => Assert.False(Trust());

    [Fact]
    public void ForwardedHeaders_AreTrusted_OnRailway() => Assert.True(Trust(("RAILWAY_ENVIRONMENT", "production")));

    [Fact]
    public void Setting_OverridesTheRailwayDefault()
    {
        Assert.True(Trust(("ClientIp:TrustForwardedHeaders", "true")));
        Assert.False(Trust(("RAILWAY_ENVIRONMENT", "production"), ("ClientIp:TrustForwardedHeaders", "false")));
    }
}
