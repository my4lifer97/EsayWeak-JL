using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Services;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// QA: 25 wrong passwords in 4 seconds were all checked. Each request sets X-Real-IP (what
// Railway's proxy adds) so these also cover reading the real client IP.
public class LoginThrottleTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    private async Task RegisterVerified(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var code = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.DevCode!;
        await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code));
    }

    private Task<HttpResponseMessage> Login(string email, string password, string ip = "203.0.113.1") =>
        Send("/api/auth/login", new LoginRequest(email, password), ip);

    private Task<HttpResponseMessage> Send(string url, object body, string ip)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        req.Headers.Add("X-Real-IP", ip);
        return Client.SendAsync(req);
    }

    [Fact]
    public async Task FiveWrongPasswords_BlockTheNextAttempt_EvenWithTheRightPassword()
    {
        await RegisterVerified("throttle-1@example.com", "throttle-1");
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login("throttle-1@example.com", "wrong")).StatusCode);

        var blocked = await Login("throttle-1@example.com", "password123");

        Assert.Equal((HttpStatusCode)429, blocked.StatusCode);
        Assert.Contains("Too many sign-in attempts", await blocked.Content.ReadAsStringAsync());
        Assert.True(blocked.Headers.RetryAfter is not null);
    }

    // A stranger guessing from their own network mustn't lock the real owner out.
    [Fact]
    public async Task BlockedFromOneIp_SameAccountStillWorksFromAnother()
    {
        await RegisterVerified("throttle-2@example.com", "throttle-2");
        for (var i = 0; i < 5; i++) await Login("throttle-2@example.com", "wrong", ip: "198.51.100.7");

        Assert.Equal(HttpStatusCode.OK, (await Login("throttle-2@example.com", "password123", ip: "203.0.113.9")).StatusCode);
    }

    [Fact]
    public async Task SuccessfulLogin_ResetsTheCount()
    {
        await RegisterVerified("throttle-3@example.com", "throttle-3");
        for (var i = 0; i < 4; i++) await Login("throttle-3@example.com", "wrong");
        Assert.Equal(HttpStatusCode.OK, (await Login("throttle-3@example.com", "password123")).StatusCode);
        for (var i = 0; i < 4; i++) await Login("throttle-3@example.com", "wrong");

        Assert.Equal(HttpStatusCode.OK, (await Login("throttle-3@example.com", "password123")).StatusCode);
    }

    // Guessing spread across many addresses still hits the per-account cap (20).
    [Fact]
    public async Task ManyIps_HitThePerAccountCap()
    {
        await RegisterVerified("throttle-4@example.com", "throttle-4");
        for (var i = 0; i < 20; i++) await Login("throttle-4@example.com", "wrong", ip: $"192.0.2.{i + 1}");

        Assert.Equal((HttpStatusCode)429, (await Login("throttle-4@example.com", "password123", ip: "192.0.2.200")).StatusCode);
    }

    [Fact]
    public async Task PlatformAdminLogin_IsThrottledToo()
    {
        await Client.PostAsJsonAsync("/api/platform-admin/bootstrap", new PlatformAdminBootstrapRequest("admin@example.com", "supersecret123", "Admin"));
        for (var i = 0; i < 5; i++)
            await Send("/api/platform-admin/login", new PlatformAdminLoginRequest("admin@example.com", "wrong"), "203.0.113.1");

        var blocked = await Send("/api/platform-admin/login", new PlatformAdminLoginRequest("Admin@Example.com", "supersecret123"), "203.0.113.1");

        Assert.Equal((HttpStatusCode)429, blocked.StatusCode);
    }

    [Fact]
    public void BlockLiftsOnceFailuresAgeOutOfTheWindow()
    {
        var throttle = new LoginThrottle(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var start = DateTime.UtcNow;
        for (var i = 0; i < 5; i++) throttle.RecordFailure("owner", "a@example.com", "1.1.1.1", start);

        Assert.NotNull(throttle.RetryAfter("owner", "A@example.com ", "1.1.1.1", start.AddMinutes(14)));
        Assert.Null(throttle.RetryAfter("owner", "a@example.com", "1.1.1.1", start.AddMinutes(16)));
    }
}
