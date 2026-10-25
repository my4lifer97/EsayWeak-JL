using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// The owner's Chats page (ChatsController) and what feeds it: messages recorded on the bridge's
// inbound/outgoing calls and on BridgeWhatsAppSender's automated sends.
public class ChatsTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);
    private record UnreadCount(int Count);

    private async Task<(string BusinessId, string Token)> SeedBusiness(string email, string slug, bool linked = true)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var businessBody = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        Authorize(Client, businessBody!.Token);
        await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Haircut", "Haircut", "Haircut", 30, 50m));
        Client.DefaultRequestHeaders.Authorization = null;

        using var db = Db();
        var business = await db.Businesses.SingleAsync(b => b.Slug == slug);
        if (linked) business.WhatsAppNumber = "+15559990000";
        await db.SaveChangesAsync();
        return (business.Id, businessBody.Token);
    }

    private async Task<string?> Inbound(string businessId, string fromPhone, string message, string? profileName = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/bridge/inbound")
        {
            Content = JsonContent.Create(new WhatsAppController.BridgeInboundRequest(businessId, fromPhone, profileName, message)),
        };
        req.Headers.Add("X-Bridge-Secret", TestWebApplicationFactory.BridgeSecret);
        var resp = await Client.SendAsync(req);
        return (await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>())!.Reply;
    }

    private async Task<HttpResponseMessage> Outgoing(string businessId, string toPhone, string message)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/bridge/outgoing")
        {
            Content = JsonContent.Create(new WhatsAppController.BridgeOutgoingRequest(businessId, toPhone, message)),
        };
        req.Headers.Add("X-Bridge-Secret", TestWebApplicationFactory.BridgeSecret);
        return await Client.SendAsync(req);
    }

    private async Task<ChatsController.ChatDto> GetChat(string token, string phone)
    {
        Authorize(Client, token);
        return (await Client.GetFromJsonAsync<ChatsController.ChatDto>($"/api/admin/chats/messages?phone={Uri.EscapeDataString(phone)}"))!;
    }

    private async Task<int> Unread(string token)
    {
        Authorize(Client, token);
        return (await Client.GetFromJsonAsync<UnreadCount>("/api/admin/chats/unread-count"))!.Count;
    }

    [Fact]
    public async Task InboundMessage_AndTheBotsReply_AreRecorded_CustomerMessageUnread()
    {
        var (businessId, token) = await SeedBusiness("chats-inbound@example.com", "chats-inbound");
        var reply = await Inbound(businessId, "+15551110001", "hi", "Dana");

        var chat = await GetChat(token, "+15551110001");
        Assert.Equal(["Customer", "Bot"], chat.Messages.Select(m => m.Sender));
        Assert.Equal("hi", chat.Messages[0].Text);
        Assert.Equal(reply, chat.Messages[1].Text);
        Assert.Equal("Dana", chat.Name);
        Assert.Equal(1, await Unread(token));
    }

    [Fact]
    public async Task ChatList_NewestFirst_WithUnreadCounts()
    {
        var (businessId, token) = await SeedBusiness("chats-list@example.com", "chats-list");
        await Inbound(businessId, "+15551110002", "hi");
        await Inbound(businessId, "+15551110003", "hello");
        await Inbound(businessId, "+15551110003", "99");

        Authorize(Client, token);
        var chats = (await Client.GetFromJsonAsync<List<ChatsController.ChatSummaryDto>>("/api/admin/chats"))!;

        Assert.Equal(["+15551110003", "+15551110002"], chats.Select(c => c.Phone));
        Assert.Equal(2, chats[0].Unread);
        Assert.Equal("Bot", chats[0].LastSender);
        Assert.Equal(3, await Unread(token));
    }

    [Fact]
    public async Task MarkRead_ClearsTheChatsUnreadMessages_AndLogsNoActivity()
    {
        var (businessId, token) = await SeedBusiness("chats-read@example.com", "chats-read");
        await Inbound(businessId, "+15551110004", "hi");
        await Inbound(businessId, "+15551110005", "hi");

        Authorize(Client, token);
        var resp = await Client.PostAsJsonAsync("/api/admin/chats/read", new { phone = "+15551110004" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(1, await Unread(token));
        using var db = Db();
        Assert.False(await db.ActivityLogs.AnyAsync(l => l.BusinessId == businessId && l.Action == "Chats.MarkRead"));
    }

    [Fact]
    public async Task OwnerReply_IsSentThroughTheBridge_Recorded_AndPausesTheBot()
    {
        var (businessId, token) = await SeedBusiness("chats-send@example.com", "chats-send");
        await Inbound(businessId, "+15551110006", "hi");

        Authorize(Client, token);
        var resp = await Client.PostAsJsonAsync("/api/admin/chats/messages", new { phone = "+15551110006", text = "Hi Dana, see you at 10" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains(Factory.WhatsAppBridge.Sent, s => s.BusinessId == businessId && s.ToPhone == "+15551110006" && s.Message == "Hi Dana, see you at 10");
        Assert.Equal(0, await Unread(token)); // replying reads the chat

        // The customer answers the owner: no chatbot reply, but it's in the chat, unread.
        Assert.Null(await Inbound(businessId, "+15551110006", "great thanks"));
        var chat = await GetChat(token, "+15551110006");
        Assert.Equal(["Customer", "Bot", "Owner", "Customer"], chat.Messages.Select(m => m.Sender));
        Assert.NotNull(chat.BotPausedUntil);
        Assert.Equal(1, await Unread(token));
    }

    [Fact]
    public async Task BotAnswersAgain_OnceThePauseIsOver()
    {
        var (businessId, token) = await SeedBusiness("chats-pause-end@example.com", "chats-pause-end");
        await Inbound(businessId, "+15551110007", "hi");
        Authorize(Client, token);
        await Client.PostAsJsonAsync("/api/admin/chats/messages", new { phone = "+15551110007", text = "Hello" });

        using (var db = Db())
        {
            var ownerMessage = await db.ChatMessages.SingleAsync(m => m.BusinessId == businessId && m.Sender == ChatSender.Owner);
            ownerMessage.CreatedAt = DateTime.UtcNow - ChatLog.OwnerChatPause - TimeSpan.FromMinutes(1);
            await db.SaveChangesAsync();
        }

        Assert.NotNull(await Inbound(businessId, "+15551110007", "hi again"));
        Assert.Null((await GetChat(token, "+15551110007")).BotPausedUntil);
    }

    [Fact]
    public async Task OwnerReply_WithoutALinkedBot_Is400()
    {
        var (businessId, token) = await SeedBusiness("chats-unlinked@example.com", "chats-unlinked", linked: false);
        await Inbound(businessId, "+15551110008", "hi");

        Authorize(Client, token);
        var resp = await Client.PostAsJsonAsync("/api/admin/chats/messages", new { phone = "+15551110008", text = "Hello" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(Factory.WhatsAppBridge.Sent);
    }

    [Fact]
    public async Task OwnerReply_ToAPhoneWithNoChat_Is404()
    {
        var (_, token) = await SeedBusiness("chats-nochat@example.com", "chats-nochat");

        Authorize(Client, token);
        var resp = await Client.PostAsJsonAsync("/api/admin/chats/messages", new { phone = "+15551110009", text = "Hello" });

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Empty(Factory.WhatsAppBridge.Sent);
    }

    [Fact]
    public async Task AnotherBusinessesChat_IsNotVisible()
    {
        var (businessA, _) = await SeedBusiness("chats-a@example.com", "chats-a");
        var (_, tokenB) = await SeedBusiness("chats-b@example.com", "chats-b");
        await Inbound(businessA, "+15551110010", "hi");

        Authorize(Client, tokenB);
        var resp = await Client.GetAsync("/api/admin/chats/messages?phone=%2B15551110010");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal(0, await Unread(tokenB));
    }

    [Fact]
    public async Task MessageTypedOnThePhone_IsRecorded_ReadsTheChat_AndPausesTheBot()
    {
        var (businessId, token) = await SeedBusiness("chats-phone@example.com", "chats-phone");
        await Inbound(businessId, "+15551110011", "hi");

        Assert.Equal(HttpStatusCode.OK, (await Outgoing(businessId, "+15551110011", "Typed on the phone")).StatusCode);

        Assert.Equal(0, await Unread(token));
        Assert.Null(await Inbound(businessId, "+15551110011", "ok"));
        var chat = await GetChat(token, "+15551110011");
        Assert.Contains(chat.Messages, m => m.Sender == "OwnerPhone" && m.Text == "Typed on the phone");
    }

    [Fact]
    public async Task OutgoingWithoutTheBridgeSecret_Is401()
    {
        var (businessId, _) = await SeedBusiness("chats-secret@example.com", "chats-secret");
        var resp = await Client.PostAsJsonAsync("/api/whatsapp/bridge/outgoing",
            new WhatsAppController.BridgeOutgoingRequest(businessId, "+15551110012", "x"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task AutomatedSend_IsRecordedAsTheBot_ButAlertsToTheOwnersNumberAreNot()
    {
        var (businessId, _) = await SeedBusiness("chats-auto@example.com", "chats-auto");
        Business business;
        using (var db = Db())
        {
            business = await db.Businesses.SingleAsync(b => b.Id == businessId);
            business.Phone = "0521234567";
            await db.SaveChangesAsync();
        }

        var sender = new BridgeWhatsAppSender(Factory.WhatsAppBridge, Factory.Services.GetRequiredService<ChatLog>());
        await sender.SendAsync(business, "+15551110013", "Reminder: tomorrow at 10:00");
        await sender.SendAsync(business, "+972521234567", "New inquiry from ...");

        using var check = Db();
        var rows = await check.ChatMessages.Where(m => m.BusinessId == businessId).ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(ChatSender.Bot, row.Sender);
        Assert.Equal("+15551110013", row.CustomerPhone);
        Assert.True(row.IsRead);
    }
}
