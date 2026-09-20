using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Covers the optional "Inquiry" section of the WhatsApp chatbot (WhatsAppController.HandleInquiryModeAsync)
// -- the $1/$2 mode-switch commands, in-app logging, and owner notification. Off by default; a
// business that never enables it should see zero behavior change (covered explicitly below).
public class ChatbotInquiryTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    private async Task<(string BusinessId, string Token)> SeedBusinessWithService(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var businessBody = await verify.Content.ReadFromJsonAsync<LoginResponse>();

        Authorize(Client, businessBody!.Token);
        await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Haircut", "Haircut", "Haircut", 30, 50m));

        using var db = Db();
        var businessId = await db.Businesses.Where(b => b.Slug == slug).Select(b => b.Id).FirstAsync();
        return (businessId, businessBody.Token);
    }

    private async Task EnableInquiry(string token, bool notifyWhatsApp = false, string? whatsAppNumber = null, bool notifyEmail = false, string? email = null)
    {
        Authorize(Client, token);
        await Client.PatchAsJsonAsync("/api/admin/settings", new
        {
            chatbotInquiryEnabled = true,
            inquiryNotifyViaWhatsApp = notifyWhatsApp,
            inquiryWhatsAppNumber = whatsAppNumber,
            inquiryNotifyViaEmail = notifyEmail,
            inquiryEmail = email,
        });
        Client.DefaultRequestHeaders.Authorization = null;
    }

    private Task<HttpResponseMessage> PostInboundWithAuth(string businessId, string fromPhone, string message, string? profileName = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/bridge/inbound")
        {
            Content = JsonContent.Create(new WhatsAppController.BridgeInboundRequest(businessId, fromPhone, profileName, message)),
        };
        req.Headers.Add("X-Bridge-Secret", TestWebApplicationFactory.BridgeSecret);
        return Client.SendAsync(req);
    }

    [Fact]
    public async Task InquiryDisabled_DollarCommandsAreJustTreatedAsUnrecognizedText()
    {
        var (businessId, _) = await SeedBusinessWithService("inquiry-off@example.com", "inquiry-off");
        Client.DefaultRequestHeaders.Authorization = null;
        await PostInboundWithAuth(businessId, "+15550001", "hi");

        var resp = await PostInboundWithAuth(businessId, "+15550001", "$2");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        // Falls into the normal invalid-service-selection path, not inquiry mode.
        Assert.DoesNotContain("chatting directly", body!.Reply);
        using var db = Db();
        Assert.False(await db.ChatbotInquiries.AnyAsync(i => i.BusinessId == businessId));
    }

    [Fact]
    public async Task WelcomeMessage_WhenInquiryEnabled_ShowsModeGateFirst()
    {
        var (businessId, token) = await SeedBusinessWithService("inquiry-welcome@example.com", "inquiry-welcome");
        await EnableInquiry(token);

        var resp = await PostInboundWithAuth(businessId, "+15550002", "hi");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("1. Book a service", body!.Reply);
        Assert.Contains("2. Ask a question", body.Reply);
        Assert.DoesNotContain("Haircut", body.Reply); // service list not shown until "1" is chosen
    }

    [Fact]
    public async Task ChoosingOne_AtGate_ShowsServiceListWithEscapeHatchNote()
    {
        var (businessId, token) = await SeedBusinessWithService("inquiry-gate-one@example.com", "inquiry-gate-one");
        await EnableInquiry(token);
        await PostInboundWithAuth(businessId, "+15550010", "hi");

        var resp = await PostInboundWithAuth(businessId, "+15550010", "1");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("1. Haircut", body!.Reply);
        Assert.Contains("$2", body.Reply);
    }

    [Fact]
    public async Task ChoosingTwo_AtGate_EntersInquiryModeDirectly()
    {
        var (businessId, token) = await SeedBusinessWithService("inquiry-gate-two@example.com", "inquiry-gate-two");
        await EnableInquiry(token);
        await PostInboundWithAuth(businessId, "+15550011", "hi");

        var resp = await PostInboundWithAuth(businessId, "+15550011", "2");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("$1", body!.Reply);
        using var db = Db();
        var state = await db.WhatsAppConversationStates.SingleAsync(s => s.BusinessId == businessId && s.Phone == "+15550011");
        Assert.Equal("Inquiry", state.ChatbotMode);
    }

    [Fact]
    public async Task UnrecognizedReply_AtGate_ReShowsGate()
    {
        var (businessId, token) = await SeedBusinessWithService("inquiry-gate-bad@example.com", "inquiry-gate-bad");
        await EnableInquiry(token);
        await PostInboundWithAuth(businessId, "+15550012", "hi");

        var resp = await PostInboundWithAuth(businessId, "+15550012", "blah");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("1. Book a service", body!.Reply);
    }

    [Fact]
    public async Task DollarTwo_EntersInquiryMode()
    {
        var (businessId, token) = await SeedBusinessWithService("inquiry-enter@example.com", "inquiry-enter");
        await EnableInquiry(token);
        await PostInboundWithAuth(businessId, "+15550003", "hi");

        var resp = await PostInboundWithAuth(businessId, "+15550003", "$2");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("$1", body!.Reply);
        using var db = Db();
        var state = await db.WhatsAppConversationStates.SingleAsync(s => s.BusinessId == businessId && s.Phone == "+15550003");
        Assert.Equal("Inquiry", state.ChatbotMode);
    }

    [Fact]
    public async Task MessageWhileInInquiryMode_IsLoggedAndAcknowledged_NotDispatchedToBooking()
    {
        var (businessId, token) = await SeedBusinessWithService("inquiry-log@example.com", "inquiry-log");
        await EnableInquiry(token);
        await PostInboundWithAuth(businessId, "+15550004", "hi");
        await PostInboundWithAuth(businessId, "+15550004", "$2");

        var resp = await PostInboundWithAuth(businessId, "+15550004", "Do you have parking nearby?", profileName: "Dana");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("$1", body!.Reply);
        Assert.DoesNotContain("Haircut", body.Reply); // never fell through to the service list

        using var db = Db();
        var inquiry = await db.ChatbotInquiries.SingleAsync(i => i.BusinessId == businessId);
        Assert.Equal("+15550004", inquiry.CustomerPhone);
        Assert.Equal("Dana", inquiry.CustomerName);
        Assert.Equal("Do you have parking nearby?", inquiry.Message);
    }

    [Fact]
    public async Task DollarOne_ReturnsToBookingAndShowsServiceListAgain()
    {
        var (businessId, token) = await SeedBusinessWithService("inquiry-exit@example.com", "inquiry-exit");
        await EnableInquiry(token);
        await PostInboundWithAuth(businessId, "+15550005", "hi");
        await PostInboundWithAuth(businessId, "+15550005", "$2");
        await PostInboundWithAuth(businessId, "+15550005", "just asking");

        var resp = await PostInboundWithAuth(businessId, "+15550005", "$1");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("1. Haircut", body!.Reply);
        using var db = Db();
        var state = await db.WhatsAppConversationStates.SingleAsync(s => s.BusinessId == businessId && s.Phone == "+15550005");
        Assert.Equal("Booking", state.ChatbotMode);
    }

    [Fact]
    public async Task SecondBookingRound_AfterReturningFromInquiry_ServiceNumberIsNotMistakenForInquiryContent()
    {
        // Regression guard for the ChatbotMode-not-reset bug: once back in Booking mode, a normal
        // numeric service-selection reply must reach TryHandleServiceSelectionReply, not get
        // swallowed as inquiry content.
        var (businessId, token) = await SeedBusinessWithService("inquiry-regression@example.com", "inquiry-regression");
        await EnableInquiry(token);
        await PostInboundWithAuth(businessId, "+15550006", "hi");
        await PostInboundWithAuth(businessId, "+15550006", "$2");
        await PostInboundWithAuth(businessId, "+15550006", "$1");

        var resp = await PostInboundWithAuth(businessId, "+15550006", "1");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("/inquiry-regression/w/", body!.Reply);
    }

    [Fact]
    public async Task OwnerIsNotifiedOnce_PerInquirySession_NotPerMessage()
    {
        var (businessId, token) = await SeedBusinessWithService("inquiry-notify@example.com", "inquiry-notify");
        await EnableInquiry(token, notifyWhatsApp: true, whatsAppNumber: "+972500000001", notifyEmail: true, email: "owner-inbox@example.com");
        await PostInboundWithAuth(businessId, "+15550007", "hi");
        await PostInboundWithAuth(businessId, "+15550007", "$2");

        await PostInboundWithAuth(businessId, "+15550007", "first question");
        await PostInboundWithAuth(businessId, "+15550007", "a follow-up question");

        var waSent = Factory.WhatsAppSender.Sent.Where(s => s.BusinessId == businessId).ToList();
        var emailSent = Factory.Email.Sent.Where(s => s.Email == "owner-inbox@example.com").ToList();
        Assert.Single(waSent);
        Assert.Contains("first question", waSent[0].Message);
        Assert.Single(emailSent);
        Assert.Contains("first question", emailSent[0].Body);

        using var db = Db();
        Assert.Equal(2, await db.ChatbotInquiries.CountAsync(i => i.BusinessId == businessId));
    }

    [Fact]
    public async Task NewInquirySession_AfterReturningToBooking_NotifiesAgain()
    {
        var (businessId, token) = await SeedBusinessWithService("inquiry-renotify@example.com", "inquiry-renotify");
        await EnableInquiry(token, notifyWhatsApp: true, whatsAppNumber: "+972500000002");
        await PostInboundWithAuth(businessId, "+15550008", "hi");
        await PostInboundWithAuth(businessId, "+15550008", "$2");
        await PostInboundWithAuth(businessId, "+15550008", "question one");
        await PostInboundWithAuth(businessId, "+15550008", "$1");

        await PostInboundWithAuth(businessId, "+15550008", "$2");
        await PostInboundWithAuth(businessId, "+15550008", "question two");

        var waSent = Factory.WhatsAppSender.Sent.Where(s => s.BusinessId == businessId).ToList();
        Assert.Equal(2, waSent.Count);
    }

    [Fact]
    public async Task AdminInquiriesEndpoint_ListsAndMarksRead()
    {
        var (businessId, token) = await SeedBusinessWithService("inquiry-inbox@example.com", "inquiry-inbox");
        await EnableInquiry(token);
        await PostInboundWithAuth(businessId, "+15550009", "hi");
        await PostInboundWithAuth(businessId, "+15550009", "$2");
        await PostInboundWithAuth(businessId, "+15550009", "hello there");

        Authorize(Client, token);
        var list = await Client.GetFromJsonAsync<List<ChatbotInquiryDto>>("/api/admin/chatbot-inquiries");
        var inquiry = Assert.Single(list!);
        Assert.False(inquiry.IsRead);

        var readResp = await Client.PostAsync($"/api/admin/chatbot-inquiries/{inquiry.Id}/read", null);
        Assert.Equal(HttpStatusCode.OK, readResp.StatusCode);

        var listAfter = await Client.GetFromJsonAsync<List<ChatbotInquiryDto>>("/api/admin/chatbot-inquiries");
        Assert.True(listAfter!.Single().IsRead);

        var unreadOnly = await Client.GetFromJsonAsync<List<ChatbotInquiryDto>>("/api/admin/chatbot-inquiries?unreadOnly=true");
        Assert.Empty(unreadOnly!);
    }
}
