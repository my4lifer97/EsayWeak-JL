using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Covers the active inbound transport (whatsapp-bridge -> POST /api/whatsapp/bridge/inbound) --
// JSON in/out, businessId-direct resolution, and the shared-secret auth. The underlying
// chatbot conversation logic (cancel/reschedule/language detection/etc, shared via
// WhatsAppController.ProcessMessageAsync) is already covered end-to-end through the legacy Twilio
// webhook in WhatsAppControllerTests -- no need to duplicate every scenario here.
public class WhatsAppBridgeInboundTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    private async Task<(string BusinessId, string Slug)> SeedBusinessWithService(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var businessBody = await verify.Content.ReadFromJsonAsync<LoginResponse>();

        Authorize(Client, businessBody!.Token);
        await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Haircut", "Haircut", "Haircut", 30, 50m));
        Client.DefaultRequestHeaders.Authorization = null;

        using var db = Db();
        var businessId = await db.Businesses.Where(b => b.Slug == slug).Select(b => b.Id).FirstAsync();
        return (businessId, slug);
    }

    private Task<HttpResponseMessage> PostInbound(string businessId, string fromPhone, string message, string? profileName = null, string? secret = TestWebApplicationFactory.BridgeSecret)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/bridge/inbound")
        {
            Content = JsonContent.Create(new WhatsAppController.BridgeInboundRequest(businessId, fromPhone, profileName, message)),
        };
        if (secret is not null) req.Headers.Add("X-Bridge-Secret", secret);
        return Client.SendAsync(req);
    }

    [Fact]
    public async Task FirstMessage_ReturnsServiceListAndOpensConversationState()
    {
        var (businessId, _) = await SeedBusinessWithService("wa-bridge-1@example.com", "wa-bridge-1");

        var resp = await PostInbound(businessId, "+15559990001", "hi");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("1. Haircut", body!.Reply);
        using var db = Db();
        Assert.True(await db.WhatsAppConversationStates.AnyAsync(s => s.BusinessId == businessId && s.Phone == "+15559990001"));
    }

    [Fact]
    public async Task ValidNumericReply_ReturnsBookingLink()
    {
        var (businessId, slug) = await SeedBusinessWithService("wa-bridge-2@example.com", "wa-bridge-2");
        var phone = "+15559990002";
        await PostInbound(businessId, phone, "hi");

        var resp = await PostInbound(businessId, phone, "1", profileName: "Jane Doe");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains($"/{slug}/w/", body!.Reply);
        using var db = Db();
        var token = await db.WhatsAppBookingTokens.SingleAsync(t => t.BusinessId == businessId && t.Phone == phone);
        Assert.Equal("Jane Doe", token.ProfileName);
    }

    [Fact]
    public async Task ChatbotDisabled_ReturnsNullReply()
    {
        var (businessId, _) = await SeedBusinessWithService("wa-bridge-3@example.com", "wa-bridge-3");
        using (var db = Db())
        {
            var business = await db.Businesses.FirstAsync(b => b.Id == businessId);
            business.ChatbotEnabled = false;
            await db.SaveChangesAsync();
        }

        var resp = await PostInbound(businessId, "+15559990003", "hi");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Null(body!.Reply);
    }

    [Fact]
    public async Task MissingOrWrongSecret_IsRejected()
    {
        var (businessId, _) = await SeedBusinessWithService("wa-bridge-4@example.com", "wa-bridge-4");

        var wrongSecret = await PostInbound(businessId, "+15559990004", "hi", secret: "not-the-real-secret");
        var noSecret = await PostInbound(businessId, "+15559990004", "hi", secret: null);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongSecret.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, noSecret.StatusCode);
    }

    [Fact]
    public async Task UnknownBusinessId_Returns404()
    {
        var resp = await PostInbound("not-a-real-business-id", "+15559990005", "hi");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
