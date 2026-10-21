using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// The bridge reports a WhatsApp-side logout (POST /api/whatsapp/bridge/session-event). On
// 2026-10-20 the production bot was logged out overnight and nobody knew until the next day.
public class WhatsAppSessionEventTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    private async Task<string> SeedBusiness(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Bot Shop", email, "password123", slug));
        var code = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.DevCode!;
        await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code));
        await Client.PostAsJsonAsync("/api/platform-admin/bootstrap",
            new PlatformAdminBootstrapRequest("admin@example.com", "supersecret123", "Admin"));
        Factory.Email.Sent.Clear(); // the verification code
        using var db = Db();
        return await db.Businesses.Where(b => b.Slug == slug).Select(b => b.Id).FirstAsync();
    }

    private Task<HttpResponseMessage> PostEvent(string businessId, string evt = "loggedOut", string? secret = TestWebApplicationFactory.BridgeSecret)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/bridge/session-event")
        {
            Content = JsonContent.Create(new WhatsAppController.BridgeSessionEventRequest(businessId, evt, "+972501112233")),
        };
        if (secret is not null) req.Headers.Add("X-Bridge-Secret", secret);
        return Client.SendAsync(req);
    }

    [Fact]
    public async Task LoggedOut_EmailsThePlatformAdminAndLogsActivity()
    {
        var businessId = await SeedBusiness("wa-event-1@example.com", "wa-event-1");

        var resp = await PostEvent(businessId);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var alert = Assert.Single(Factory.Email.Sent);
        Assert.Equal("admin@example.com", alert.Email);
        Assert.Contains("logged out", alert.Subject);
        Assert.Contains("+972501112233", alert.Body);
        Assert.Contains($"/platform-admin/businesses/{businessId}", alert.Body);
        Assert.Empty(Factory.OwnerEmail.Sent);
        using var db = Db();
        Assert.True(await db.ActivityLogs.AnyAsync(a => a.BusinessId == businessId && a.Action == "WhatsAppLink.LoggedOut"));
    }

    [Fact]
    public async Task RepeatWithinTenMinutes_SendsOneEmail()
    {
        var businessId = await SeedBusiness("wa-event-2@example.com", "wa-event-2");

        await PostEvent(businessId);
        var again = await PostEvent(businessId);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Single(Factory.Email.Sent);
    }

    [Fact]
    public async Task WithoutTheBridgeSecret_IsUnauthorized()
    {
        var businessId = await SeedBusiness("wa-event-3@example.com", "wa-event-3");

        var resp = await PostEvent(businessId, secret: null);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Empty(Factory.Email.Sent);
    }

    [Fact]
    public async Task UnknownEventOrBusiness_IsRejected()
    {
        var businessId = await SeedBusiness("wa-event-4@example.com", "wa-event-4");

        Assert.Equal(HttpStatusCode.BadRequest, (await PostEvent(businessId, evt: "connected")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostEvent("no-such-business")).StatusCode);
        Assert.Empty(Factory.Email.Sent);
    }
}
