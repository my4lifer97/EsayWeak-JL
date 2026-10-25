using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// The Chats page's ⚡ Ready messages: "From the system" texts built from the business's data
// (ChatTemplates), and the owner's own saved ones (quick replies).
public class ChatReadyMessagesTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);
    private record TextResponse(string Text);

    private async Task<(string BusinessId, string Token, string ItemId)> SeedBusiness(string slug)
    {
        var email = $"{slug}@example.com";
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Ready Shop", email, "password123", slug));
        var code = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.DevCode!;
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code));
        var token = (await verify.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        Authorize(Client, token);
        var item = (await (await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Haircut", "قص شعر", "תספורת", 30, 50m)))
            .Content.ReadFromJsonAsync<ItemDto>())!;
        await Client.PatchAsJsonAsync("/api/admin/settings", new { chatbotInquiryEnabled = true });
        Client.DefaultRequestHeaders.Authorization = null;
        using var db = Db();
        return (await db.Businesses.Where(b => b.Slug == slug).Select(b => b.Id).FirstAsync(), token, item.Id);
    }

    private async Task OpenChat(string businessId, string phone, string hello = "hi")
    {
        foreach (var message in new[] { hello, "2" })
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/bridge/inbound")
            {
                Content = JsonContent.Create(new WhatsAppController.BridgeInboundRequest(businessId, phone, "Dana", message)),
            };
            req.Headers.Add("X-Bridge-Secret", TestWebApplicationFactory.BridgeSecret);
            await Client.SendAsync(req);
        }
    }

    private async Task<HttpResponseMessage> System(string token, string phone, string kind, string? date = null, string? itemId = null)
    {
        Authorize(Client, token);
        var url = $"/api/admin/chats/system-message?phone={Uri.EscapeDataString(phone)}&kind={kind}";
        if (date is not null) url += $"&date={date}";
        if (itemId is not null) url += $"&itemId={itemId}";
        return await Client.GetAsync(url);
    }

    private async Task<string> SystemText(string token, string phone, string kind, string? date = null, string? itemId = null)
    {
        var resp = await System(token, phone, kind, date, itemId);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<TextResponse>())!.Text;
    }

    private static string Iso(DateTime d) => d.ToString("yyyy-MM-dd");

    [Fact]
    public async Task Services_ListsEachWithDurationAndPrice()
    {
        var (businessId, token, _) = await SeedBusiness("ready-services");
        await OpenChat(businessId, "+15552220001");

        var text = await SystemText(token, "+15552220001", "services");

        Assert.Contains("Our services at Ready Shop:", text);
        Assert.Contains("• Haircut — 30 min — ₪50", text);
    }

    [Fact]
    public async Task Texts_AreInTheCustomersLanguage()
    {
        var (businessId, token, itemId) = await SeedBusiness("ready-hebrew");
        await OpenChat(businessId, "+15552220002", hello: "שלום");

        Assert.Equal("תספורת — 30 דק׳ — ₪50", await SystemText(token, "+15552220002", "service", itemId: itemId));
    }

    [Fact]
    public async Task Week_ShowsSevenDays_WithHoursOrClosed()
    {
        var (businessId, token, _) = await SeedBusiness("ready-week");
        await OpenChat(businessId, "+15552220003");

        var lines = (await SystemText(token, "+15552220003", "week")).Split('\n');

        Assert.Equal("Our opening hours for the coming week:", lines[0]);
        Assert.Equal(8, lines.Length);
        // Registration opens Mon-Fri 09:00-18:00.
        Assert.Contains(lines, l => l.StartsWith("• Saturday") && l.EndsWith("closed"));
        Assert.Contains(lines, l => l.StartsWith("• Monday") && l.Contains("09:00–18:00"));
    }

    [Fact]
    public async Task Day_OpenOrClosed()
    {
        var (businessId, token, _) = await SeedBusiness("ready-day");
        await OpenChat(businessId, "+15552220004");
        var nextMonday = AppClock.Now.Date.AddDays(((int)DayOfWeek.Monday - (int)AppClock.Now.DayOfWeek + 7) % 7 + 7);
        var nextSaturday = nextMonday.AddDays(5);

        Assert.Contains("we're open ‎09:00–18:00‎", await SystemText(token, "+15552220004", "day", Iso(nextMonday)));
        Assert.Contains("we're closed", await SystemText(token, "+15552220004", "day", Iso(nextSaturday)));
        Assert.Equal(HttpStatusCode.BadRequest, (await System(token, "+15552220004", "day", Iso(AppClock.Now.Date.AddDays(-1)))).StatusCode);
    }

    [Fact]
    public async Task FreeTimes_LeavesOutBookedSlots()
    {
        var (businessId, token, itemId) = await SeedBusiness("ready-free");
        await OpenChat(businessId, "+15552220005");
        var nextMonday = AppClock.Now.Date.AddDays(((int)DayOfWeek.Monday - (int)AppClock.Now.DayOfWeek + 7) % 7 + 7);
        Authorize(Client, token);
        await Client.PostAsJsonAsync("/api/admin/appointments", new
        {
            customerName = "Booked", customerFamilyName = "One", customerPhone = "+15552229999",
            itemId, date = Iso(nextMonday), startTime = "10:00",
        });

        var text = await SystemText(token, "+15552220005", "freeTimes", Iso(nextMonday), itemId);

        Assert.StartsWith("Free times on Monday", text);
        Assert.Contains("09:30", text);
        Assert.DoesNotContain("10:00,", text);
        Assert.Contains("10:30", text);
    }

    [Fact]
    public async Task Address_NeedsOne_ThenShowsItWithTheMap()
    {
        var (businessId, token, _) = await SeedBusiness("ready-address");
        await OpenChat(businessId, "+15552220006");
        Assert.Equal(HttpStatusCode.BadRequest, (await System(token, "+15552220006", "address")).StatusCode);

        Authorize(Client, token);
        await Client.PatchAsJsonAsync("/api/admin/settings", new { addressLine = "Herzl 5", city = "Haifa", mapUrl = "https://maps.example.com/x" });

        var text = await SystemText(token, "+15552220006", "address");
        Assert.Equal("📍 Our address: Herzl 5, Haifa\n🗺 Directions: https://maps.example.com/x", text);
    }

    [Fact]
    public async Task Page_LinksToTheStorefront()
    {
        var (businessId, token, _) = await SeedBusiness("ready-page");
        await OpenChat(businessId, "+15552220007");

        Assert.EndsWith("/ready-page", await SystemText(token, "+15552220007", "page"));
    }

    [Fact]
    public async Task Appointments_OnlyThisCustomersUpcomingOnes()
    {
        var (businessId, token, itemId) = await SeedBusiness("ready-appts");
        await OpenChat(businessId, "+15552220008");
        Assert.Contains("no upcoming appointments", await SystemText(token, "+15552220008", "appointments"));

        var nextMonday = AppClock.Now.Date.AddDays(((int)DayOfWeek.Monday - (int)AppClock.Now.DayOfWeek + 7) % 7 + 7);
        Authorize(Client, token);
        await Client.PostAsJsonAsync("/api/admin/appointments", new
        {
            customerName = "Dana", customerFamilyName = "L", customerPhone = "+15552220008", itemId, date = Iso(nextMonday), startTime = "11:00",
        });
        await Client.PostAsJsonAsync("/api/admin/appointments", new
        {
            customerName = "Other", customerFamilyName = "P", customerPhone = "+15552220099", itemId, date = Iso(nextMonday), startTime = "12:00",
        });

        var text = await SystemText(token, "+15552220008", "appointments");
        Assert.Contains($"Monday {nextMonday:dd/MM} ‎11:00‎ — Haircut", text);
        Assert.DoesNotContain("12:00", text);
    }

    [Fact]
    public async Task SystemMessage_ForAPhoneWithNoChat_Is404_AndUnknownKind_Is400()
    {
        var (businessId, token, _) = await SeedBusiness("ready-bad");
        Assert.Equal(HttpStatusCode.NotFound, (await System(token, "+15552220010", "services")).StatusCode);
        await OpenChat(businessId, "+15552220010");
        Assert.Equal(HttpStatusCode.BadRequest, (await System(token, "+15552220010", "nope")).StatusCode);
    }

    [Fact]
    public async Task QuickReplies_AddEditDelete()
    {
        var (_, token, _) = await SeedBusiness("ready-own");
        Authorize(Client, token);

        var created = (await (await Client.PostAsJsonAsync("/api/admin/chats/quick-replies", new { title = "Holiday", text = "We're closed on holidays." }))
            .Content.ReadFromJsonAsync<ChatsController.QuickReplyDto>())!;
        var updated = await Client.PutAsJsonAsync($"/api/admin/chats/quick-replies/{created.Id}", new { title = "Holiday", text = "Closed on all holidays." });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var list = (await Client.GetFromJsonAsync<List<ChatsController.QuickReplyDto>>("/api/admin/chats/quick-replies"))!;
        Assert.Equal("Closed on all holidays.", Assert.Single(list).Text);

        Assert.Equal(HttpStatusCode.OK, (await Client.DeleteAsync($"/api/admin/chats/quick-replies/{created.Id}")).StatusCode);
        Assert.Empty((await Client.GetFromJsonAsync<List<ChatsController.QuickReplyDto>>("/api/admin/chats/quick-replies"))!);
    }

    [Fact]
    public async Task QuickReplies_NeedANameAndText_AndAreThisBusinessesOnly()
    {
        var (_, tokenA, _) = await SeedBusiness("ready-own-a");
        var (_, tokenB, _) = await SeedBusiness("ready-own-b");
        Authorize(Client, tokenA);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("/api/admin/chats/quick-replies", new { title = "", text = "x" })).StatusCode);
        var mine = (await (await Client.PostAsJsonAsync("/api/admin/chats/quick-replies", new { title = "Hi", text = "Hello!" }))
            .Content.ReadFromJsonAsync<ChatsController.QuickReplyDto>())!;

        Authorize(Client, tokenB);
        Assert.Empty((await Client.GetFromJsonAsync<List<ChatsController.QuickReplyDto>>("/api/admin/chats/quick-replies"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync($"/api/admin/chats/quick-replies/{mine.Id}")).StatusCode);
    }
}
