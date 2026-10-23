using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Chatbot findings from QA run 4 (rule-based path, through the bridge inbound endpoint).
public class WhatsAppQaRun4Tests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);
    private record ItemDto(string Id);

    private async Task<(string BusinessId, string ItemId)> Seed(string slug)
    {
        var email = $"{slug}@example.com";
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Bot Shop", email, "password123", slug));
        var code = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.DevCode!;
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code));
        Authorize(Client, (await verify.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        var item = await (await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Haircut", "Haircut", "Haircut", 30, 50m)))
            .Content.ReadFromJsonAsync<ItemDto>();
        Client.DefaultRequestHeaders.Authorization = null;
        using var db = Db();
        return (await db.Businesses.Where(b => b.Slug == slug).Select(b => b.Id).FirstAsync(), item!.Id);
    }

    private async Task<string?> Send(string businessId, string phone, string message)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/bridge/inbound")
        {
            Content = JsonContent.Create(new WhatsAppController.BridgeInboundRequest(businessId, phone, null, message)),
        };
        req.Headers.Add("X-Bridge-Secret", TestWebApplicationFactory.BridgeSecret);
        var resp = await Client.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>())!.Reply;
    }

    private async Task<string> AddAppointment(string businessId, string phone, string itemId, int daysAhead, string start = "10:00")
    {
        using var db = Db();
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.BusinessId == businessId && c.Phone == phone);
        if (customer is null)
        {
            customer = new Customer { BusinessId = businessId, Phone = phone, Name = "Test", FamilyName = "Customer" };
            db.Customers.Add(customer);
        }
        var appointment = new Appointment
        {
            BusinessId = businessId, CustomerId = customer.Id, ItemId = itemId,
            Date = AppClock.Now.Date.AddDays(daysAhead), StartTime = start, EndTime = "10:30",
            Status = AppointmentStatus.CONFIRMED,
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }

    private async Task<AppointmentStatus> StatusOf(string id)
    {
        using var db = Db();
        return (await db.Appointments.FirstAsync(a => a.Id == id)).Status;
    }

    private async Task Update(string businessId, Action<Business> change)
    {
        using var db = Db();
        change(await db.Businesses.FirstAsync(b => b.Id == businessId));
        await db.SaveChangesAsync();
    }

    private async Task UpdateState(string businessId, string phone, Action<WhatsAppConversationState> change)
    {
        using var db = Db();
        change(await db.WhatsAppConversationStates.FirstAsync(s => s.BusinessId == businessId && s.Phone == phone));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task InquiryGate_AfterAnExpiredLockout_StartsFresh()
    {
        var (businessId, _) = await Seed("qa4-t-gate");
        await Update(businessId, b => b.ChatbotInquiryEnabled = true);
        var phone = "+15554060001";
        await Send(businessId, phone, "hi");
        await Send(businessId, phone, "1");
        foreach (var junk in new[] { "a", "b", "c" }) await Send(businessId, phone, junk);
        await UpdateState(businessId, phone, s => s.ExpiresAt = DateTime.UtcNow.AddMinutes(-1));

        await Send(businessId, phone, "hi"); // the gate
        var reply = await Send(businessId, phone, "1");

        Assert.Contains("1. Haircut", reply);
    }

    [Fact]
    public async Task CancelQuestion_WithALinkPending_ExpiresAfterTenMinutes()
    {
        var (businessId, itemId) = await Seed("qa4-t-expire");
        var phone = "+15554060002";
        var appointmentId = await AddAppointment(businessId, phone, itemId, 3);
        await Send(businessId, phone, "hi");
        await Send(businessId, phone, "1"); // booking link: the row now lives 24 h
        await Send(businessId, phone, "cancel");
        // The question was asked 20 minutes ago.
        var old = DateTimeOffset.UtcNow.AddMinutes(-20).ToUnixTimeSeconds();
        await UpdateState(businessId, phone, s => s.ChatbotMode = $"ConfirmCancel:{appointmentId}:{old}:0");

        await Send(businessId, phone, "1");

        Assert.Equal(AppointmentStatus.CONFIRMED, await StatusOf(appointmentId));
    }

    [Theory]
    [InlineData("yes see you")]
    [InlineData("yes but tomorrow")]
    public async Task CancelQuestion_YesWithOtherWords_IsNotAnAnswer(string message)
    {
        var (businessId, itemId) = await Seed("qa4-t-yes-" + message.Length);
        var phone = "+15554060003";
        var appointmentId = await AddAppointment(businessId, phone, itemId, 3);
        await Send(businessId, phone, "cancel");

        await Send(businessId, phone, message);

        Assert.Equal(AppointmentStatus.CONFIRMED, await StatusOf(appointmentId));
    }

    [Fact]
    public async Task CancelQuestion_ShortUnclearAnswer_IsAskedAgain_ThenOneCancels()
    {
        var (businessId, itemId) = await Seed("qa4-t-ok");
        var phone = "+15554060004";
        var appointmentId = await AddAppointment(businessId, phone, itemId, 3);
        await Send(businessId, phone, "cancel");

        var again = await Send(businessId, phone, "ok");
        await Send(businessId, phone, "1");

        Assert.Contains("didn't get that", again);
        Assert.Equal(AppointmentStatus.CANCELLED, await StatusOf(appointmentId));
    }

    [Fact]
    public async Task SeveralAppointments_TheCustomerPicksWhichOneToCancel()
    {
        var (businessId, itemId) = await Seed("qa4-t-pick");
        var phone = "+15554060005";
        var first = await AddAppointment(businessId, phone, itemId, 3);
        var second = await AddAppointment(businessId, phone, itemId, 4);

        var pick = await Send(businessId, phone, "cancel");
        var question = await Send(businessId, phone, "2");
        await Send(businessId, phone, "1");

        Assert.Contains("2. Haircut", pick);
        Assert.Contains(AppClock.Now.Date.AddDays(4).ToString("yyyy-MM-dd"), question);
        Assert.Equal(AppointmentStatus.CONFIRMED, await StatusOf(first));
        Assert.Equal(AppointmentStatus.CANCELLED, await StatusOf(second));
    }

    [Fact]
    public async Task SeveralAppointments_RescheduleSendsEveryLink()
    {
        var (businessId, itemId) = await Seed("qa4-t-move");
        var phone = "+15554060006";
        var first = await AddAppointment(businessId, phone, itemId, 3);
        var second = await AddAppointment(businessId, phone, itemId, 4);

        var reply = await Send(businessId, phone, "can I move my appointment");

        Assert.Contains($"/appointments/{first}", reply);
        Assert.Contains($"/appointments/{second}", reply);
    }

    [Fact]
    public async Task ServiceTurnedOffAfterTheLink_EndsTheQuietMode()
    {
        var (businessId, itemId) = await Seed("qa4-t-off");
        var phone = "+15554060007";
        await Send(businessId, phone, "hi");
        await Send(businessId, phone, "1");
        using (var db = Db())
        {
            (await db.Items.FirstAsync(i => i.Id == itemId)).IsActive = false;
            await db.SaveChangesAsync();
        }

        var reply = await Send(businessId, phone, "hello?");

        Assert.NotNull(reply);
        Assert.Contains("no longer available", reply);
    }

    [Fact]
    public async Task ExpiredSubscription_GetsNoBookingLink()
    {
        var (businessId, _) = await Seed("qa4-t-expired");
        await Update(businessId, b => b.SubscriptionStatus = SubStatus.EXPIRED);
        var phone = "+15554060008";

        var reply = await Send(businessId, phone, "hi");

        Assert.Contains("isn't taking online bookings", reply);
        using var db = Db();
        Assert.False(await db.WhatsAppBookingTokens.AnyAsync(t => t.BusinessId == businessId));
    }

    [Fact]
    public async Task DisabledBusiness_GetsNoReply()
    {
        var (businessId, _) = await Seed("qa4-t-disabled");
        await Update(businessId, b => b.IsDisabled = true);

        Assert.Null(await Send(businessId, "+15554060009", "hi"));
    }

    [Fact]
    public async Task ValidChoice_SavesTheReplyLanguage()
    {
        var (businessId, _) = await Seed("qa4-t-lang");
        var phone = "+15554060010";
        await Send(businessId, phone, "hi");
        await Send(businessId, phone, "١");

        using var db = Db();
        Assert.Equal("AR", (await db.WhatsAppConversationStates.FirstAsync(s => s.BusinessId == businessId && s.Phone == phone)).Language);
    }

    [Theory]
    [InlineData("אני מבטל את התור")]
    [InlineData("أريد الإلغاء")]
    [InlineData("ألغِ الموعد")]
    [InlineData("cancelling please")]
    public async Task MoreCancelWordings_AskTheCancelQuestion(string message)
    {
        var (businessId, itemId) = await Seed("qa4-t-words-" + Math.Abs(message.GetHashCode() % 10000));
        var phone = "+15554060011";
        await AddAppointment(businessId, phone, itemId, 3);

        var reply = await Send(businessId, phone, message);

        Assert.Matches("(1|١)", reply!);
        using var db = Db();
        Assert.StartsWith("ConfirmCancel:", (await db.WhatsAppConversationStates.FirstAsync(s => s.BusinessId == businessId && s.Phone == phone)).ChatbotMode);
    }

    [Theory]
    [InlineData("‎$2‎")]
    [InlineData("$ 2")]
    [InlineData("＄2")]
    [InlineData("#2")]
    [InlineData("2#")]
    [InlineData("‎#2‎")]
    [InlineData("＃2")]
    public async Task DollarCommandVariants_EnterInquiryMode(string message)
    {
        var (businessId, _) = await Seed("qa4-t-dollar-" + string.Concat(message.Select(c => (int)c)));
        await Update(businessId, b => b.ChatbotInquiryEnabled = true);
        var phone = "+15554060012";
        await Send(businessId, phone, "hi");
        await Send(businessId, phone, "1"); // past the gate, on the service list

        await Send(businessId, phone, message);

        using var db = Db();
        Assert.Equal("Inquiry", (await db.WhatsAppConversationStates.FirstAsync(s => s.BusinessId == businessId && s.Phone == phone)).ChatbotMode);
    }

    // 2026-10-23: the commands became #1/#2 (the owner's choice); "#1" leaves Inquiry mode.
    [Theory]
    [InlineData("#1")]
    [InlineData("1#")]
    [InlineData("$1")]
    public async Task HashOne_LeavesInquiryMode(string message)
    {
        var (businessId, _) = await Seed("qa4-t-hash-" + string.Concat(message.Select(c => (int)c)));
        await Update(businessId, b => b.ChatbotInquiryEnabled = true);
        var phone = "+15554060013";
        await Send(businessId, phone, "hi");
        await Send(businessId, phone, "#2");

        await Send(businessId, phone, message);

        using var db = Db();
        Assert.False(await db.WhatsAppConversationStates.AnyAsync(s => s.BusinessId == businessId && s.Phone == phone && s.ChatbotMode == "Inquiry"));
    }

    [Theory]
    [InlineData("1️⃣")]
    [InlineData("1.")]
    [InlineData("#1")]
    [InlineData("１")]
    public async Task NumberStyles_PickTheService(string message)
    {
        var (businessId, _) = await Seed("qa4-t-num-" + (int)message[0] + message.Length);
        var phone = "+15554060013";
        await Send(businessId, phone, "hi");

        var reply = await Send(businessId, phone, message);

        Assert.Contains("/w/", reply);
    }
}
