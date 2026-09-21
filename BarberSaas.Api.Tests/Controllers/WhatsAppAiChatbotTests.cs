using System.Net.Http.Json;
using System.Text.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Covers WhatsAppController's OpenAI-driven path (ProcessMessageWithAiAsync) via the bridge-inbound
// endpoint, using FakeOpenAiChatClient in place of a real OpenAI call. Only this test class sets
// OpenAI:ApiKey (via configureOpenAi: true) -- every other WhatsApp test leaves it unset and
// exercises the rule-based path, unaffected by any of this.
public class WhatsAppAiChatbotTests : IntegrationTestBase
{
    public WhatsAppAiChatbotTests() : base(configureCardcom: false, configureOpenAi: true) { }

    private record RegisterResponse(string? DevCode);

    private async Task<(string BusinessId, string Slug, string ItemId)> SeedBusinessWithService(string email, string slug)
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
        var itemId = await db.Items.Where(i => i.BusinessId == businessId).Select(i => i.Id).FirstAsync();
        return (businessId, slug, itemId);
    }

    private Task<HttpResponseMessage> PostInbound(string businessId, string fromPhone, string message)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/bridge/inbound")
        {
            Content = JsonContent.Create(new WhatsAppController.BridgeInboundRequest(businessId, fromPhone, "Jane Doe", message)),
        };
        req.Headers.Add("X-Bridge-Secret", TestWebApplicationFactory.BridgeSecret);
        return Client.SendAsync(req);
    }

    [Fact]
    public async Task FreeFormBookingRequest_ResolvesViaCreateBookingLinkTool()
    {
        var (businessId, slug, itemId) = await SeedBusinessWithService("wa-ai-1@example.com", "wa-ai-1");
        Factory.OpenAi.ToolCallName = "create_booking_link";
        Factory.OpenAi.ToolCallArgsJson = JsonSerializer.Serialize(new { itemId });

        var resp = await PostInbound(businessId, "+15559991001", "I'd like to get a haircut please");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains($"/{slug}/w/", body!.Reply);
        using var db = Db();
        var token = await db.WhatsAppBookingTokens.SingleAsync(t => t.BusinessId == businessId && t.Phone == "+15559991001");
        Assert.Equal(itemId, token.ItemId);
    }

    [Fact]
    public async Task FreeFormCancelRequest_ResolvesViaCancelTool()
    {
        var (businessId, _, itemId) = await SeedBusinessWithService("wa-ai-2@example.com", "wa-ai-2");
        var phone = "+15559991002";
        string appointmentId;
        using (var db = Db())
        {
            var customer = new Customer { BusinessId = businessId, Phone = phone, Name = "Test", FamilyName = "Customer" };
            db.Customers.Add(customer);
            var appointment = new Appointment
            {
                BusinessId = businessId,
                CustomerId = customer.Id,
                ItemId = itemId,
                Date = DateTime.Now.Date.AddDays(1),
                StartTime = "10:00",
                EndTime = "10:30",
                Status = AppointmentStatus.CONFIRMED,
            };
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            appointmentId = appointment.Id;
        }
        Factory.OpenAi.ToolCallName = "cancel_upcoming_appointment";

        var resp = await PostInbound(businessId, phone, "actually I can't make it, please cancel");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("cancelled", body!.Reply, StringComparison.OrdinalIgnoreCase);
        using var verifyDb = Db();
        var appt = await verifyDb.Appointments.SingleAsync(a => a.Id == appointmentId);
        Assert.Equal(AppointmentStatus.CANCELLED, appt.Status);
    }

    [Fact]
    public async Task ThreeNonCompletingRepliesInARow_LocksOutUntilUnlockKeyword()
    {
        var (businessId, _, _) = await SeedBusinessWithService("wa-ai-4@example.com", "wa-ai-4");
        var phone = "+15559991004";
        Factory.OpenAi.PlainTextReply = "Sure, we're open 9-6 Mon-Fri.";

        await PostInbound(businessId, phone, "what are your hours?");
        await PostInbound(businessId, phone, "and do you take walk-ins?");
        var thirdResp = await PostInbound(businessId, phone, "cool, one more question");
        var thirdBody = await thirdResp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();

        // The 3rd non-completing reply is replaced with the lockout message, not the AI's own text.
        Assert.Contains("$", thirdBody!.Reply);
        Assert.DoesNotContain("9-6", thirdBody.Reply);

        var fourthResp = await PostInbound(businessId, phone, "hello?");
        var fourthBody = await fourthResp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Null(fourthBody!.Reply); // silently ignored -- not the literal "$"

        var unlockResp = await PostInbound(businessId, phone, "$");
        var unlockBody = await unlockResp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("start fresh", unlockBody!.Reply, StringComparison.OrdinalIgnoreCase);

        // Unlocking clears the conversation state entirely (mirrors the rule-based path's unlock) --
        // a fresh row only gets created once the customer actually sends a real next message.
        using var db = Db();
        Assert.False(await db.WhatsAppConversationStates.AnyAsync(s => s.BusinessId == businessId && s.Phone == phone));
    }

    [Fact]
    public async Task SuccessfulToolCall_ResetsInvalidAttemptCounter()
    {
        var (businessId, slug, itemId) = await SeedBusinessWithService("wa-ai-5@example.com", "wa-ai-5");
        var phone = "+15559991005";
        Factory.OpenAi.PlainTextReply = "Not sure I follow.";

        await PostInbound(businessId, phone, "random message one");
        await PostInbound(businessId, phone, "random message two");

        Factory.OpenAi.PlainTextReply = null;
        Factory.OpenAi.ToolCallName = "create_booking_link";
        Factory.OpenAi.ToolCallArgsJson = JsonSerializer.Serialize(new { itemId });
        var bookingResp = await PostInbound(businessId, phone, "ok let's book a haircut");
        var bookingBody = await bookingResp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains($"/{slug}/w/", bookingBody!.Reply);

        using var db = Db();
        var state = await db.WhatsAppConversationStates.SingleAsync(s => s.BusinessId == businessId && s.Phone == phone);
        Assert.Equal(0, state.InvalidAttempts); // reset by the successful tool call, not left at 2
    }

    [Fact]
    public async Task OpenAiThrows_FallsBackToRuleBasedReply()
    {
        var (businessId, _, _) = await SeedBusinessWithService("wa-ai-3@example.com", "wa-ai-3");
        Factory.OpenAi.ThrowOnCall = new InvalidOperationException("simulated OpenAI outage");

        var resp = await PostInbound(businessId, "+15559991003", "hi");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        // Same shape ProcessMessageRuleBasedAsync produces for a fresh conversation -- proves the
        // fallback actually engaged, not just that some reply came back.
        Assert.Contains("1. Haircut", body!.Reply);
        using var db = Db();
        Assert.True(await db.WhatsAppConversationStates.AnyAsync(s => s.BusinessId == businessId && s.Phone == "+15559991003"));
    }
}
