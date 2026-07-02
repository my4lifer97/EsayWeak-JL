using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Locks in the fix for CronController accepting an empty Bearer token whenever CronSecret
// happened to be unconfigured (auth != $"Bearer {cronSecret}" is true for an empty header
// when cronSecret is null/empty too) — see Program.cs secret-rotation history.
public class CronControllerTests : IntegrationTestBase
{
    [Fact]
    public async Task NoAuthHeader_ReturnsUnauthorized()
    {
        var resp = await Client.GetAsync("/api/cron/reminders");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task EmptyBearerToken_ReturnsUnauthorized()
    {
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer ");

        var resp = await Client.GetAsync("/api/cron/reminders");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task WrongSecret_ReturnsUnauthorized()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-secret");

        var resp = await Client.GetAsync("/api/cron/reminders");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task CorrectSecret_ReturnsOk()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestWebApplicationFactory.CronSecret);

        var resp = await Client.GetAsync("/api/cron/reminders");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
