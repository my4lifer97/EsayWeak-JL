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

    // QA: two quick messages from the same customer often got a 500 and no reply -- both tried to
    // create the conversation row and the second hit its unique index.
    [Fact]
    public async Task MessagesArrivingTogether_AllGetAReply()
    {
        var (businessId, _) = await SeedBusinessWithService("wa-bridge-race@example.com", "wa-bridge-race");

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostInbound(businessId, "+15559990010", "hi")));

        foreach (var r in responses)
            Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        using var db = Db();
        Assert.Equal(1, await db.WhatsAppConversationStates.CountAsync(s => s.BusinessId == businessId && s.Phone == "+15559990010"));
    }

    // QA: for 24 h after a booking link was sent, the bot ignored everything, "cancel" included.
    [Fact]
    public async Task Cancel_StillWorksWhileABookingLinkIsPending()
    {
        var (businessId, _) = await SeedBusinessWithService("wa-bridge-pending@example.com", "wa-bridge-pending");
        var phone = "+15559990011";
        await PostInbound(businessId, phone, "hi");
        await PostInbound(businessId, phone, "1"); // booking link issued -> quiet mode

        string appointmentId;
        using (var db = Db())
        {
            var item = await db.Items.FirstAsync(i => i.BusinessId == businessId);
            var customer = new Customer { BusinessId = businessId, Name = "Dana", Phone = phone };
            var appt = new Appointment
            {
                BusinessId = businessId, CustomerId = customer.Id, ItemId = item.Id,
                Date = DateTime.Now.Date.AddDays(2), StartTime = "10:00", EndTime = "10:30", Status = AppointmentStatus.CONFIRMED,
            };
            db.Customers.Add(customer);
            db.Appointments.Add(appt);
            await db.SaveChangesAsync();
            appointmentId = appt.Id;
        }

        // Ordinary chatter is still ignored while the link is pending...
        var chatter = await (await PostInbound(businessId, phone, "thanks")).Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Null(chatter!.Reply);

        // ...but "cancel" asks to confirm, and the "1" answer goes through too.
        var ask = await (await PostInbound(businessId, phone, "cancel")).Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.False(string.IsNullOrWhiteSpace(ask!.Reply));
        await PostInbound(businessId, phone, "1");

        using var check = Db();
        Assert.Equal(AppointmentStatus.CANCELLED, (await check.Appointments.SingleAsync(a => a.Id == appointmentId)).Status);
    }

    // QA: "$1" at the opening Book/Ask menu answered "didn't recognize that" and counted toward
    // the lockout, instead of going to booking.
    [Fact]
    public async Task DollarOne_AtTheOpeningMenu_ShowsTheServiceList()
    {
        var (businessId, _) = await SeedBusinessWithService("wa-bridge-gate@example.com", "wa-bridge-gate");
        using (var db = Db())
        {
            (await db.Businesses.FirstAsync(b => b.Id == businessId)).ChatbotInquiryEnabled = true;
            await db.SaveChangesAsync();
        }
        var phone = "+15559990012";
        await PostInbound(businessId, phone, "hi"); // opening Book/Ask menu

        var resp = await (await PostInbound(businessId, phone, "$1")).Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();

        Assert.Contains("1. Haircut", resp!.Reply);
        using var check = Db();
        Assert.Equal(0, (await check.WhatsAppConversationStates.SingleAsync(s => s.BusinessId == businessId && s.Phone == phone)).InvalidAttempts);
    }

    private async Task<Dictionary<string, string>> AddServices(string businessId, params string[] names)
    {
        using var db = Db();
        var items = names.Select(n => new Item { BusinessId = businessId, NameEn = n, NameAr = n, NameHe = n, DurationMinutes = 30 }).ToList();
        db.Items.AddRange(items);
        await db.SaveChangesAsync();
        return items.ToDictionary(i => i.NameEn, i => i.Id);
    }

    private async Task TurnOff(string itemId)
    {
        using var db = Db();
        (await db.Items.FirstAsync(i => i.Id == itemId)).IsActive = false;
        await db.SaveChangesAsync();
    }

    // QA: the list was ordered by internal id (looked random), and a reply was matched against the
    // *current* list -- after the owner turned off "Kids Cut", "2" for Haircut booked Beard Trim.
    [Fact]
    public async Task ServiceList_IsAlphabetical_AndAReplyMeansWhatTheCustomerSaw()
    {
        var (businessId, _) = await SeedBusinessWithService("wa-bridge-shown@example.com", "wa-bridge-shown"); // "Haircut"
        var ids = await AddServices(businessId, "Kids Cut", "Beard Trim");
        var phone = "+15559990013";

        var list = (await (await PostInbound(businessId, phone, "hi")).Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>())!.Reply!;
        Assert.Contains("1. Beard Trim\n2. Haircut\n3. Kids Cut", list);

        await TurnOff(ids["Beard Trim"]); // the current list would now start with Haircut
        await PostInbound(businessId, phone, "3");

        using var db = Db();
        var token = await db.WhatsAppBookingTokens.SingleAsync(t => t.BusinessId == businessId && t.Phone == phone);
        Assert.Equal(ids["Kids Cut"], token.ItemId);
    }

    [Fact]
    public async Task ReplyForAServiceTurnedOffSinceTheList_ShowsTheCurrentList()
    {
        var (businessId, _) = await SeedBusinessWithService("wa-bridge-gone@example.com", "wa-bridge-gone"); // "Haircut"
        var ids = await AddServices(businessId, "Beard Trim");
        var phone = "+15559990014";
        await PostInbound(businessId, phone, "hi"); // 1. Beard Trim, 2. Haircut

        await TurnOff(ids["Beard Trim"]);
        var reply = (await (await PostInbound(businessId, phone, "1")).Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>())!.Reply!;

        Assert.Contains("no longer available", reply);
        Assert.Contains("1. Haircut", reply);
        using var db = Db();
        Assert.False(await db.WhatsAppBookingTokens.AnyAsync(t => t.BusinessId == businessId && t.Phone == phone));
    }

    [Fact]
    public async Task UnknownBusinessId_Returns404()
    {
        var resp = await PostInbound("not-a-real-business-id", "+15559990005", "hi");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
