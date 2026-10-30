using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// The owner's Chats page (ChatsController): only customers who chose to talk to the owner
// (Inquiry mode) show up, the owner answers / sends a booking link / finishes the chat.
public class ChatsTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);
    private record UnreadCount(int Count);

    private async Task<(string BusinessId, string Token, string ItemId)> SeedBusiness(string slug, bool linked = true)
    {
        var email = $"{slug}@example.com";
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var businessBody = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        Authorize(Client, businessBody!.Token);
        var item = await (await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Haircut", "תספורת", "قص شعر", 30, 50m)))
            .Content.ReadFromJsonAsync<ItemDto>();
        await Client.PatchAsJsonAsync("/api/admin/settings", new { chatbotInquiryEnabled = true });
        Client.DefaultRequestHeaders.Authorization = null;

        using var db = Db();
        var business = await db.Businesses.SingleAsync(b => b.Slug == slug);
        if (linked) business.WhatsAppNumber = "+15559990000";
        await db.SaveChangesAsync();
        return (business.Id, businessBody.Token, item!.Id);
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

    // "hi" (the Book/Ask menu), then "2": talking to the owner.
    private async Task OpenChat(string businessId, string phone, string? profileName = null, string hello = "hi")
    {
        await Inbound(businessId, phone, hello, profileName);
        await Inbound(businessId, phone, "2", profileName);
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

    private async Task<HttpResponseMessage> Post(string token, string url, object body)
    {
        Authorize(Client, token);
        return await Client.PostAsJsonAsync(url, body);
    }

    [Fact]
    public async Task ABookingConversation_IsNotAChat()
    {
        var (businessId, token, _) = await SeedBusiness("chats-bot-only");
        await Inbound(businessId, "+15551110001", "hi");
        await Inbound(businessId, "+15551110001", "1");

        Authorize(Client, token);
        Assert.Empty((await Client.GetFromJsonAsync<List<ChatsController.ChatSummaryDto>>("/api/admin/chats"))!);
        Assert.Equal(0, await Unread(token));
    }

    // QA run 5: the newest 100 chats were taken before open ones were sorted first, so an older
    // open chat disappeared while its unread messages still counted in the badge.
    [Fact]
    public async Task OlderOpenChat_StaysInTheList_PastTheNewest100()
    {
        var (businessId, token, _) = await SeedBusiness("chats-past-100");
        await OpenChat(businessId, "+15551110099", "Dana");
        await Inbound(businessId, "+15551110099", "are you open friday?", "Dana");
        using (var db = Db())
        {
            for (var i = 0; i < 101; i++)
            {
                var message = BarberSaas.Api.Services.ChatLog.NewMessage(businessId, $"+1555220{i:0000}", BarberSaas.Api.Models.ChatSender.Owner, "hello");
                message.CreatedAt = DateTime.UtcNow.AddMinutes(5 + i);
                db.ChatMessages.Add(message);
            }
            await db.SaveChangesAsync();
        }

        Authorize(Client, token);
        var chats = (await Client.GetFromJsonAsync<List<ChatsController.ChatSummaryDto>>("/api/admin/chats"))!;

        Assert.Equal("+15551110099", chats[0].Phone);
        Assert.True(chats.Count > 100);
    }

    [Fact]
    public async Task TalkingToTheOwner_OpensAChat_WithTheCustomersMessagesUnread()
    {
        var (businessId, token, _) = await SeedBusiness("chats-open");
        await OpenChat(businessId, "+15551110002", "Dana");

        Assert.Null(await Inbound(businessId, "+15551110002", "Do you cut kids' hair?", "Dana"));

        var chat = await GetChat(token, "+15551110002");
        Assert.True(chat.IsOpen);
        Assert.Equal("Dana", chat.Name);
        Assert.Null(chat.CustomerId); // never booked here: booking from the chat creates the customer
        Assert.Equal(["Bot", "Customer"], chat.Messages.Select(m => m.Sender)); // "you're now chatting..." then the question
        Assert.Equal("Do you cut kids' hair?", chat.Messages[1].Text);
        Assert.Equal(1, await Unread(token));
    }

    [Fact]
    public async Task NewChat_ShowsTheWhatsAppName_BeforeTheCustomerWritesAnything()
    {
        var (businessId, token, _) = await SeedBusiness("chats-name");
        await OpenChat(businessId, "+15551110020", "Noa Bar");

        Assert.Equal("Noa Bar", (await GetChat(token, "+15551110020")).Name);
    }

    [Fact]
    public async Task Chat_CarriesTheCustomerId_ForAKnownCustomer()
    {
        var (businessId, token, _) = await SeedBusiness("chats-known");
        string customerId;
        using (var db = Db())
        {
            var customer = new BarberSaas.Api.Models.Customer { BusinessId = businessId, Name = "Rami", FamilyName = "Haddad", Phone = "+15551110019" };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
        }
        await OpenChat(businessId, "+15551110019", "rami wa");

        var chat = await GetChat(token, "+15551110019");

        Assert.Equal(customerId, chat.CustomerId);
        Assert.Equal("Rami Haddad", chat.Name); // the business's own name for them, not the WhatsApp one
    }

    [Fact]
    public async Task OwnerMessage_StartsWithTheOwnerLine_InTheCustomersLanguage()
    {
        var (businessId, token, _) = await SeedBusiness("chats-send");
        await OpenChat(businessId, "+15551110003", hello: "שלום");

        var resp = await Post(token, "/api/admin/chats/messages", new { phone = "+15551110003", text = "כן, בטח" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var sent = Assert.Single(Factory.WhatsAppBridge.Sent, s => s.BusinessId == businessId);
        Assert.Equal("💬 הודעה מבעל העסק:\nכן, בטח", sent.Message);
        var chat = await GetChat(token, "+15551110003");
        Assert.Equal("כן, בטח", chat.Messages[^1].Text); // stored without the WhatsApp-only line
        Assert.Equal("Owner", chat.Messages[^1].Sender);
        Assert.True(chat.IsOpen);
    }

    [Fact]
    public async Task ReplyingReadsTheChat()
    {
        var (businessId, token, _) = await SeedBusiness("chats-reply-read");
        await OpenChat(businessId, "+15551110004");
        await Inbound(businessId, "+15551110004", "hello?");
        Assert.Equal(1, await Unread(token));

        await Post(token, "/api/admin/chats/messages", new { phone = "+15551110004", text = "Hi!" });

        Assert.Equal(0, await Unread(token));
    }

    [Fact]
    public async Task BookingLink_ForTheChosenService_SignsTheCustomerIn()
    {
        var (businessId, token, itemId) = await SeedBusiness("chats-link");
        await OpenChat(businessId, "+15551110005");

        var resp = await Post(token, "/api/admin/chats/booking-link", new { phone = "+15551110005", itemId });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var sent = Assert.Single(Factory.WhatsAppBridge.Sent, s => s.BusinessId == businessId);
        Assert.StartsWith("💬 Message from the business owner:\nHere's your link to book Haircut:", sent.Message);
        using var db = Db();
        var tokenRow = await db.WhatsAppBookingTokens.SingleAsync(t => t.BusinessId == businessId && t.Phone == "+15551110005");
        Assert.Equal(itemId, tokenRow.ItemId);
        Assert.Contains($"/chats-link/w/{tokenRow.Id}", sent.Message);
        Assert.True((await GetChat(token, "+15551110005")).IsOpen); // sending a link doesn't end the chat
    }

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 1, 2, 3];

    private async Task<HttpResponseMessage> SendFile(string token, string phone, string fileName, byte[] bytes, string? caption = null)
    {
        Authorize(Client, token);
        var form = new MultipartFormDataContent
        {
            { new StringContent(phone), "phone" },
            { new ByteArrayContent(bytes), "file", fileName },
        };
        if (caption is not null) form.Add(new StringContent(caption), "caption");
        return await Client.PostAsync("/api/admin/chats/attachment", form);
    }

    [Fact]
    public async Task Photo_IsSentAsMedia_WithTheOwnerLine_AndShownInTheChat()
    {
        var (businessId, token, _) = await SeedBusiness("chats-photo");
        await OpenChat(businessId, "+15551110021");

        var resp = await SendFile(token, "+15551110021", "price list.png", Png, "Our prices");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var sent = Assert.Single(Factory.WhatsAppBridge.SentMediaItems);
        Assert.Equal("💬 Message from the business owner:\nOur prices", sent.Caption);
        Assert.Equal(("price list.png", "image/png", Png.Length), (sent.FileName, sent.ContentType, sent.Bytes));

        var last = (await GetChat(token, "+15551110021")).Messages[^1];
        Assert.Equal(("Owner", "Our prices"), (last.Sender, last.Text));
        Assert.Equal("price list.png", last.Attachment!.FileName);

        var file = await Client.GetAsync($"/api/admin/chats/attachments/{last.Attachment.Id}");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("image/png", file.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await file.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task FileWithoutACaption_ShowsTheFileNameInTheList()
    {
        var (businessId, token, _) = await SeedBusiness("chats-pdf");
        await OpenChat(businessId, "+15551110022");

        var resp = await SendFile(token, "+15551110022", "menu.pdf", "%PDF-1.4 test"u8.ToArray());

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("💬 Message from the business owner:", Assert.Single(Factory.WhatsAppBridge.SentMediaItems).Caption);
        Authorize(Client, token);
        var chats = (await Client.GetFromJsonAsync<List<ChatsController.ChatSummaryDto>>("/api/admin/chats"))!;
        Assert.Equal("📎 menu.pdf", chats[0].LastText);
    }

    [Theory]
    [InlineData("tool.exe", "MZ")]
    [InlineData("fake.png", "<html>")] // renamed: the bytes aren't a PNG
    [InlineData("doc.docx", "not a zip")]
    public async Task UnsupportedOrDisguisedFile_Is400(string fileName, string content)
    {
        var (businessId, token, _) = await SeedBusiness($"chats-bad-{fileName.Replace('.', '-')}");
        await OpenChat(businessId, "+15551110023");

        var resp = await SendFile(token, "+15551110023", fileName, System.Text.Encoding.ASCII.GetBytes(content));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(Factory.WhatsAppBridge.SentMediaItems);
    }

    [Fact]
    public async Task File_ToAFinishedChat_Is409()
    {
        var (businessId, token, _) = await SeedBusiness("chats-file-ended");
        await OpenChat(businessId, "+15551110024");
        await Inbound(businessId, "+15551110024", "#1");

        var resp = await SendFile(token, "+15551110024", "a.png", Png);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    private async Task<string?> InboundMedia(string businessId, string fromPhone, string caption, WhatsAppController.BridgeInboundMedia media)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/bridge/inbound")
        {
            Content = JsonContent.Create(new WhatsAppController.BridgeInboundRequest(businessId, fromPhone, "Dana", caption, media)),
        };
        req.Headers.Add("X-Bridge-Secret", TestWebApplicationFactory.BridgeSecret);
        var resp = await Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>())!.Reply;
    }

    [Fact]
    public async Task CustomersPhoto_IsShownInTheChat_WithItsCaption()
    {
        var (businessId, token, _) = await SeedBusiness("chats-in-photo");
        await OpenChat(businessId, "+15551110031");

        var reply = await InboundMedia(businessId, "+15551110031", "Like this one",
            new("image", "image/jpeg", null, Convert.ToBase64String(Png)));

        Assert.Null(reply);
        var last = (await GetChat(token, "+15551110031")).Messages[^1];
        Assert.Equal(("Customer", "Like this one"), (last.Sender, last.Text));
        Assert.Equal(("photo.jpg", "image/jpeg"), (last.Attachment!.FileName, last.Attachment.ContentType));
        Assert.Equal(1, await Unread(token));

        var file = await Client.GetAsync($"/api/admin/chats/attachments/{last.Attachment.Id}");
        Assert.Equal(Png, await file.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", file.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task CustomersFile_WithoutACaption_ShowsItsNameInTheList()
    {
        var (businessId, token, _) = await SeedBusiness("chats-in-doc");
        await OpenChat(businessId, "+15551110032");

        await InboundMedia(businessId, "+15551110032", "",
            new("document", "application/pdf", "../id card.pdf", Convert.ToBase64String("%PDF-1.4"u8.ToArray())));

        Authorize(Client, token);
        var chats = (await Client.GetFromJsonAsync<List<ChatsController.ChatSummaryDto>>("/api/admin/chats"))!;
        Assert.Equal("📎 id card.pdf", chats[0].LastText);
    }

    [Fact]
    public async Task CustomersPageLikeFile_IsStoredAsAPlainDownload()
    {
        var (businessId, token, _) = await SeedBusiness("chats-in-html");
        await OpenChat(businessId, "+15551110033");

        await InboundMedia(businessId, "+15551110033", "",
            new("document", "text/html", "page.html", Convert.ToBase64String("<script>1</script>"u8.ToArray())));

        var attachment = (await GetChat(token, "+15551110033")).Messages[^1].Attachment!;
        Assert.Equal("application/octet-stream", attachment.ContentType);
    }

    [Fact]
    public async Task CustomersFile_NotDownloaded_IsShownByName()
    {
        var (businessId, token, _) = await SeedBusiness("chats-in-big");
        await OpenChat(businessId, "+15551110034");

        await InboundMedia(businessId, "+15551110034", "my video", new("video", "video/mp4", null, null));

        var last = (await GetChat(token, "+15551110034")).Messages[^1];
        Assert.Null(last.Attachment);
        Assert.Equal("📎 video.mp4\nmy video", last.Text);
    }

    [Fact]
    public async Task PhotoWithoutACaption_OutsideAChat_GetsNoReply_AndIsNotKept()
    {
        var (businessId, token, _) = await SeedBusiness("chats-in-nochat");

        var reply = await InboundMedia(businessId, "+15551110035", "", new("image", "image/jpeg", null, Convert.ToBase64String(Png)));

        Assert.Null(reply);
        using var db = Db();
        Assert.False(await db.ChatAttachments.AnyAsync(a => a.BusinessId == businessId));
        Assert.Equal(0, await Unread(token));
    }

    [Fact]
    public async Task AnotherBusinessesAttachment_IsNotServed()
    {
        var (businessA, tokenA, _) = await SeedBusiness("chats-att-a");
        var (_, tokenB, _) = await SeedBusiness("chats-att-b");
        await OpenChat(businessA, "+15551110025");
        await SendFile(tokenA, "+15551110025", "a.png", Png);
        var attachmentId = (await GetChat(tokenA, "+15551110025")).Messages[^1].Attachment!.Id;

        Authorize(Client, tokenB);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"/api/admin/chats/attachments/{attachmentId}")).StatusCode);
    }

    [Fact]
    public async Task BookingLink_ForAnUnknownService_Is400()
    {
        var (businessId, token, _) = await SeedBusiness("chats-link-bad");
        await OpenChat(businessId, "+15551110006");

        var resp = await Post(token, "/api/admin/chats/booking-link", new { phone = "+15551110006", itemId = "nope" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(Factory.WhatsAppBridge.Sent);
    }

    [Fact]
    public async Task Finish_EndsTheChat_AndTheBotAnswersAgain()
    {
        var (businessId, token, _) = await SeedBusiness("chats-finish");
        await OpenChat(businessId, "+15551110007");

        var resp = await Post(token, "/api/admin/chats/finish", new { phone = "+15551110007", sendServiceList = false });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("has ended", Assert.Single(Factory.WhatsAppBridge.Sent).Message);
        Assert.False((await GetChat(token, "+15551110007")).IsOpen);

        // The bot again (the Book/Ask menu), and it's no longer recorded.
        var reply = await Inbound(businessId, "+15551110007", "hi again");
        Assert.Contains("1. Book a service", reply);
        Assert.DoesNotContain((await GetChat(token, "+15551110007")).Messages, m => m.Text == "hi again");

        // The owner can't keep writing to a finished chat.
        var late = await Post(token, "/api/admin/chats/messages", new { phone = "+15551110007", text = "one more thing" });
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
    }

    [Fact]
    public async Task FinishWithTheServiceList_LetsTheCustomerPickANumber()
    {
        var (businessId, token, _) = await SeedBusiness("chats-finish-list");
        await OpenChat(businessId, "+15551110008");

        await Post(token, "/api/admin/chats/finish", new { phone = "+15551110008", sendServiceList = true });

        var sent = Assert.Single(Factory.WhatsAppBridge.Sent).Message;
        Assert.Contains("has ended", sent);
        Assert.Contains("1. Haircut", sent);
        var reply = await Inbound(businessId, "+15551110008", "1");
        Assert.Contains("/chats-finish-list/w/", reply); // the bot's booking link for Haircut
    }

    [Fact]
    public async Task CustomerLeavingWithHashOne_EndsTheChat()
    {
        var (businessId, token, _) = await SeedBusiness("chats-leave");
        await OpenChat(businessId, "+15551110009");
        await Inbound(businessId, "+15551110009", "#1");

        Assert.False((await GetChat(token, "+15551110009")).IsOpen);
        var resp = await Post(token, "/api/admin/chats/messages", new { phone = "+15551110009", text = "Hello?" });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task ChatList_OpenChatsFirst()
    {
        var (businessId, token, _) = await SeedBusiness("chats-list");
        await OpenChat(businessId, "+15551110010");
        await OpenChat(businessId, "+15551110011");
        await Post(token, "/api/admin/chats/finish", new { phone = "+15551110011", sendServiceList = false });

        Authorize(Client, token);
        var chats = (await Client.GetFromJsonAsync<List<ChatsController.ChatSummaryDto>>("/api/admin/chats"))!;

        Assert.Equal(["+15551110010", "+15551110011"], chats.Select(c => c.Phone));
        Assert.True(chats[0].IsOpen);
        Assert.False(chats[1].IsOpen);
    }

    [Fact]
    public async Task MarkRead_LogsNoActivity()
    {
        var (businessId, token, _) = await SeedBusiness("chats-read");
        await OpenChat(businessId, "+15551110012");
        await Inbound(businessId, "+15551110012", "hello");

        var resp = await Post(token, "/api/admin/chats/read", new { phone = "+15551110012" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(0, await Unread(token));
        using var db = Db();
        Assert.False(await db.ActivityLogs.AnyAsync(l => l.BusinessId == businessId && l.Action == "Chats.MarkRead"));
    }

    [Fact]
    public async Task OwnerMessage_WithoutALinkedBot_Is400()
    {
        var (businessId, token, _) = await SeedBusiness("chats-unlinked", linked: false);
        await OpenChat(businessId, "+15551110013");

        var resp = await Post(token, "/api/admin/chats/messages", new { phone = "+15551110013", text = "Hello" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(Factory.WhatsAppBridge.Sent);
    }

    [Fact]
    public async Task OwnerMessage_ToAPhoneWithNoChat_Is404()
    {
        var (_, token, _) = await SeedBusiness("chats-nochat");

        var resp = await Post(token, "/api/admin/chats/messages", new { phone = "+15551110014", text = "Hello" });

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task AnotherBusinessesChat_IsNotVisible()
    {
        var (businessA, _, _) = await SeedBusiness("chats-a");
        var (_, tokenB, _) = await SeedBusiness("chats-b");
        await OpenChat(businessA, "+15551110015");

        Authorize(Client, tokenB);
        var resp = await Client.GetAsync("/api/admin/chats/messages?phone=%2B15551110015");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal(0, await Unread(tokenB));
    }

    [Fact]
    public async Task MessageTypedOnThePhone_IsRecordedOnlyInAnOpenChat()
    {
        var (businessId, token, _) = await SeedBusiness("chats-phone");
        await Inbound(businessId, "+15551110016", "hi"); // no chat
        await Outgoing(businessId, "+15551110016", "not a chat");
        await OpenChat(businessId, "+15551110017");
        await Inbound(businessId, "+15551110017", "hello");

        Assert.Equal(HttpStatusCode.OK, (await Outgoing(businessId, "+15551110017", "Typed on the phone")).StatusCode);

        Assert.Equal(0, await Unread(token)); // answering on the phone reads it
        Assert.Contains((await GetChat(token, "+15551110017")).Messages, m => m.Sender == "OwnerPhone" && m.Text == "Typed on the phone");
        using var db = Db();
        Assert.False(await db.ChatMessages.AnyAsync(m => m.CustomerPhone == "+15551110016"));
    }

    [Fact]
    public async Task OutgoingWithoutTheBridgeSecret_Is401()
    {
        var (businessId, _, _) = await SeedBusiness("chats-secret");
        var resp = await Client.PostAsJsonAsync("/api/whatsapp/bridge/outgoing",
            new WhatsAppController.BridgeOutgoingRequest(businessId, "+15551110018", "x"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
