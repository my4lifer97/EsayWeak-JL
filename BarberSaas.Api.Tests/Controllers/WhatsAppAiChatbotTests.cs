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

    private async Task<string?> Reply(string businessId, string phone, string message) =>
        (await (await PostInbound(businessId, phone, message)).Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>())!.Reply;

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

        // Asks 1/2 first, like the rule-based path; nothing is cancelled yet.
        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("Reply 1 to cancel", body!.Reply);
        using (var db = Db())
            Assert.Equal(AppointmentStatus.CONFIRMED, (await db.Appointments.SingleAsync(a => a.Id == appointmentId)).Status);

        var callsBefore = Factory.OpenAi.Calls.Count;
        var confirmResp = await PostInbound(businessId, phone, "1");

        var confirmBody = await confirmResp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("cancelled", confirmBody!.Reply, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(callsBefore, Factory.OpenAi.Calls.Count); // answered without OpenAI
        using var verifyDb = Db();
        var appt = await verifyDb.Appointments.SingleAsync(a => a.Id == appointmentId);
        Assert.Equal(AppointmentStatus.CANCELLED, appt.Status);
        var state = await verifyDb.WhatsAppConversationStates.SingleAsync(s => s.BusinessId == businessId && s.Phone == phone);
        Assert.Equal("Booking", state.ChatbotMode);
    }

    [Fact]
    public async Task AnsweredQuestions_DoNotCountTowardTheLockout()
    {
        var (businessId, _, _) = await SeedBusinessWithService("wa-ai-8@example.com", "wa-ai-8");
        var phone = "+15559991008";
        Factory.OpenAi.PlainTextReply = "Sure, we're open 9-6 Mon-Fri.";

        for (var i = 0; i < 4; i++)
        {
            var resp = await PostInbound(businessId, phone, $"question {i}");
            var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
            Assert.Contains("9-6", body!.Reply);
        }

        using var db = Db();
        var state = await db.WhatsAppConversationStates.SingleAsync(s => s.BusinessId == businessId && s.Phone == phone);
        Assert.Equal(0, state.InvalidAttempts);
    }

    [Fact]
    public async Task ThreeUnclearMessagesInARow_LocksOutUntilUnlockKeyword()
    {
        var (businessId, _, _) = await SeedBusinessWithService("wa-ai-4@example.com", "wa-ai-4");
        var phone = "+15559991004";
        Factory.OpenAi.ToolCallName = "mark_unclear";

        await PostInbound(businessId, phone, "asdkjh");
        await PostInbound(businessId, phone, "qwpoe");
        var thirdResp = await PostInbound(businessId, phone, "zzzz");
        var thirdBody = await thirdResp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();

        // The 3rd unclear reply is replaced with the lockout message, not the AI's own text.
        Assert.Contains("$", thirdBody!.Reply);
        Assert.DoesNotContain("AI:", thirdBody.Reply);

        var fourthResp = await PostInbound(businessId, phone, "hello?");
        var fourthBody = await fourthResp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        // Re-sent, not silently ignored -- not the literal "$" -- so a customer who missed the
        // first lockout message still gets told how to unlock, instead of the bot going quiet.
        Assert.Contains("$", fourthBody!.Reply);
        Assert.DoesNotContain("AI:", fourthBody.Reply);

        var unlockResp = await PostInbound(businessId, phone, "$");
        var unlockBody = await unlockResp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("start fresh", unlockBody!.Reply, StringComparison.OrdinalIgnoreCase);

        // Unlocking clears the conversation state entirely (mirrors the rule-based path's unlock) --
        // a fresh row only gets created once the customer actually sends a real next message.
        using var db = Db();
        Assert.False(await db.WhatsAppConversationStates.AnyAsync(s => s.BusinessId == businessId && s.Phone == phone));
    }

    [Fact]
    public async Task UnderstoodMessage_ResetsInvalidAttemptCounter()
    {
        var (businessId, slug, itemId) = await SeedBusinessWithService("wa-ai-5@example.com", "wa-ai-5");
        var phone = "+15559991005";
        Factory.OpenAi.ToolCallName = "mark_unclear";

        await PostInbound(businessId, phone, "random message one");
        await PostInbound(businessId, phone, "random message two");

        Factory.OpenAi.ToolCallName = "create_booking_link";
        Factory.OpenAi.ToolCallArgsJson = JsonSerializer.Serialize(new { itemId });
        var bookingResp = await PostInbound(businessId, phone, "ok let's book a haircut");
        var bookingBody = await bookingResp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains($"/{slug}/w/", bookingBody!.Reply);

        using var db = Db();
        var state = await db.WhatsAppConversationStates.SingleAsync(s => s.BusinessId == businessId && s.Phone == phone);
        Assert.Equal(0, state.InvalidAttempts); // reset by the successful tool call, not left at 2
    }

    private async Task<List<string>> SeedUpcomingAppointments(string businessId, string itemId, string phone, int count)
    {
        using var db = Db();
        var customer = new Customer { BusinessId = businessId, Phone = phone, Name = "Test", FamilyName = "Customer" };
        db.Customers.Add(customer);
        var ids = new List<string>();
        for (var i = 1; i <= count; i++)
        {
            var appointment = new Appointment
            {
                BusinessId = businessId, CustomerId = customer.Id, ItemId = itemId,
                Date = DateTime.Now.Date.AddDays(i), StartTime = "10:00", EndTime = "10:30",
                Status = AppointmentStatus.CONFIRMED,
            };
            db.Appointments.Add(appointment);
            ids.Add(appointment.Id);
        }
        await db.SaveChangesAsync();
        return ids;
    }

    [Fact]
    public async Task RepeatedCancelToolCalls_CancelNothing_ThenOneOnConfirm()
    {
        var (businessId, _, itemId) = await SeedBusinessWithService("wa-ai-6@example.com", "wa-ai-6");
        var phone = "+15559991006";
        var ids = await SeedUpcomingAppointments(businessId, itemId, phone, 3);
        Factory.OpenAi.ToolCallName = "cancel_upcoming_appointment";
        Factory.OpenAi.ToolCallRepeat = 3;

        await PostInbound(businessId, phone, "cancel");

        using (var db = Db())
            Assert.DoesNotContain(AppointmentStatus.CANCELLED, await db.Appointments.Where(a => ids.Contains(a.Id)).Select(a => a.Status).ToListAsync());

        await PostInbound(businessId, phone, "1");

        using var verifyDb = Db();
        var rows = await verifyDb.Appointments.Where(a => ids.Contains(a.Id)).Select(a => new { a.Id, a.Status }).ToListAsync();
        Assert.Equal(ids[0], rows.Single(r => r.Status == AppointmentStatus.CANCELLED).Id); // the nearest one
    }

    [Fact]
    public async Task CancelQuestion_ReplyTwo_KeepsTheAppointment()
    {
        var (businessId, _, itemId) = await SeedBusinessWithService("wa-ai-9@example.com", "wa-ai-9");
        var phone = "+15559991009";
        var ids = await SeedUpcomingAppointments(businessId, itemId, phone, 1);
        Factory.OpenAi.ToolCallName = "cancel_upcoming_appointment";
        await PostInbound(businessId, phone, "cancel please");

        var resp = await PostInbound(businessId, phone, "2");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("stays", body!.Reply);
        using var db = Db();
        Assert.Equal(AppointmentStatus.CONFIRMED, (await db.Appointments.SingleAsync(a => a.Id == ids[0])).Status);
    }

    [Fact]
    public async Task CancelQuestion_OtherReply_DropsTheQuestionAndAsksTheModel()
    {
        var (businessId, _, itemId) = await SeedBusinessWithService("wa-ai-10@example.com", "wa-ai-10");
        var phone = "+15559991010";
        var ids = await SeedUpcomingAppointments(businessId, itemId, phone, 1);
        Factory.OpenAi.ToolCallName = "cancel_upcoming_appointment";
        await PostInbound(businessId, phone, "cancel please");

        Factory.OpenAi.ToolCallName = null;
        Factory.OpenAi.PlainTextReply = "No problem, anything else?";
        var resp = await PostInbound(businessId, phone, "actually never mind");
        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Equal("No problem, anything else?", body!.Reply);

        // A later "1" is no longer an answer to the dropped question.
        await PostInbound(businessId, phone, "1");
        using var db = Db();
        Assert.Equal(AppointmentStatus.CONFIRMED, (await db.Appointments.SingleAsync(a => a.Id == ids[0])).Status);
    }

    [Fact]
    public async Task OpenAiFailsAfterTheCancelTool_StillAsksTheQuestion_NotTheRuleBasedFlow()
    {
        var (businessId, _, itemId) = await SeedBusinessWithService("wa-ai-7@example.com", "wa-ai-7");
        var phone = "+15559991007";
        var ids = await SeedUpcomingAppointments(businessId, itemId, phone, 1);
        Factory.OpenAi.ToolCallName = "cancel_upcoming_appointment";
        Factory.OpenAi.ThrowAfterTool = new InvalidOperationException("simulated OpenAI outage on the follow-up call");

        var resp = await PostInbound(businessId, phone, "please cancel my appointment");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("Reply 1 to cancel", body!.Reply);
        Assert.DoesNotContain("1. Haircut", body.Reply);

        Factory.OpenAi.ThrowAfterTool = null;
        await PostInbound(businessId, phone, "1");
        using var db = Db();
        Assert.Equal(AppointmentStatus.CANCELLED, (await db.Appointments.SingleAsync(a => a.Id == ids[0])).Status);
    }

    [Fact]
    public async Task FallbackAfterAnAiTurn_DoesNotReadABareNumberAsAServiceChoice()
    {
        var (businessId, _, _) = await SeedBusinessWithService("wa-ai-11@example.com", "wa-ai-11");
        var phone = "+15559991011";
        Factory.OpenAi.PlainTextReply = "How many people are coming?";
        await PostInbound(businessId, phone, "I want to book for my kids");

        // OpenAI goes down: the AI conversation row must not look like an open service list.
        Factory.OpenAi.ThrowOnCall = new InvalidOperationException("simulated OpenAI outage");
        var resp = await PostInbound(businessId, phone, "1");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("1. Haircut", body!.Reply);
        using var db = Db();
        Assert.False(await db.WhatsAppBookingTokens.AnyAsync(t => t.BusinessId == businessId && t.Phone == phone));
        var state = await db.WhatsAppConversationStates.SingleAsync(s => s.BusinessId == businessId && s.Phone == phone);
        Assert.Equal(0, state.InvalidAttempts);
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

    // QA run 3 AI-2: "yes" went to the model, which could only claim a cancellation it never did.
    [Theory]
    [InlineData("yes")]
    [InlineData("כן, תבטל")]
    [InlineData("نعم")]
    public async Task CancelQuestion_YesWord_CancelsWithoutAskingTheModel(string answer)
    {
        var (businessId, _, itemId) = await SeedBusinessWithService($"wa-ai-yes-{answer.Length}@example.com", $"wa-ai-yes-{answer.Length}");
        var phone = $"+1555999120{answer.Length % 10}";
        var ids = await SeedUpcomingAppointments(businessId, itemId, phone, 1);
        Factory.OpenAi.ToolCallName = "cancel_upcoming_appointment";
        await PostInbound(businessId, phone, "cancel please");
        var callsBefore = Factory.OpenAi.Calls.Count;

        await PostInbound(businessId, phone, answer);

        Assert.Equal(callsBefore, Factory.OpenAi.Calls.Count);
        using var db = Db();
        Assert.Equal(AppointmentStatus.CANCELLED, (await db.Appointments.SingleAsync(a => a.Id == ids[0])).Status);
    }

    // QA run 3 AI-3: the prompt had only service names.
    [Fact]
    public async Task SystemPrompt_HasPricesDurationsAndHours()
    {
        var (businessId, _, _) = await SeedBusinessWithService("wa-ai-prompt@example.com", "wa-ai-prompt");
        Factory.OpenAi.PlainTextReply = "ok";

        await PostInbound(businessId, "+15559991210", "how much is a haircut?");

        var prompt = Factory.OpenAi.Calls[^1].SystemPrompt;
        Assert.Contains("Haircut (₪50, 30 min)", prompt);
        Assert.Contains("09:00-18:00", prompt);
    }

    // QA run 3 AI-4: no cap, so a few huge messages broke every later turn.
    [Fact]
    public async Task LongMessage_IsCappedBeforeOpenAiAndInHistory()
    {
        var (businessId, _, _) = await SeedBusinessWithService("wa-ai-long@example.com", "wa-ai-long");
        Factory.OpenAi.PlainTextReply = "ok";

        await PostInbound(businessId, "+15559991211", new string('x', 60_000));

        Assert.Equal(1000, Factory.OpenAi.Calls[^1].UserMessage.Length);
        using var db = Db();
        var state = await db.WhatsAppConversationStates.SingleAsync(s => s.BusinessId == businessId && s.Phone == "+15559991211");
        Assert.True(state.HistoryJson!.Length < 2000);
    }

    // QA run 3 AI-6: {"itemId":123} threw and the whole turn fell back to the rule-based bot.
    [Fact]
    public async Task NonStringToolArgument_DoesNotThrow()
    {
        var (businessId, _, _) = await SeedBusinessWithService("wa-ai-badargs@example.com", "wa-ai-badargs");
        Factory.OpenAi.ToolCallName = "create_booking_link";
        Factory.OpenAi.ToolCallArgsJson = """{"itemId":123}""";

        var resp = await PostInbound(businessId, "+15559991220", "book me");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.StartsWith("AI:", body!.Reply); // the model's turn, not the rule-based fallback
        Assert.Contains("unknown itemId", body.Reply);
    }

    // QA run 3 AI-8: a link the model wrote itself (not from a tool) is removed.
    [Fact]
    public async Task UrlTheModelMadeUp_IsStripped()
    {
        var (businessId, _, _) = await SeedBusinessWithService("wa-ai-url@example.com", "wa-ai-url");
        Factory.OpenAi.PlainTextReply = "Book here: https://evil.example/pay now!";

        var resp = await PostInbound(businessId, "+15559991221", "how do I book?");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.DoesNotContain("evil.example", body!.Reply);
    }

    // QA run 3 AI-7: the no-OpenAI 1/2 answer used up the daily allowance.
    [Fact]
    public async Task CancelAnswer_DoesNotCallOpenAi()
    {
        var (businessId, _, itemId) = await SeedBusinessWithService("wa-ai-cap@example.com", "wa-ai-cap");
        var phone = "+15559991222";
        await SeedUpcomingAppointments(businessId, itemId, phone, 1);
        Factory.OpenAi.ToolCallName = "cancel_upcoming_appointment";
        await PostInbound(businessId, phone, "cancel");
        var calls = Factory.OpenAi.Calls.Count;

        await PostInbound(businessId, phone, "2");

        Assert.Equal(calls, Factory.OpenAi.Calls.Count);
    }

    [Fact]
    public async Task TheBusinessesOwnMapLink_IsKept()
    {
        var (businessId, _, _) = await SeedBusinessWithService("wa-ai-map@example.com", "wa-ai-map");
        using (var db = Db())
        {
            (await db.Businesses.FirstAsync(b => b.Id == businessId)).MapUrl = "https://maps.example/shop";
            await db.SaveChangesAsync();
        }
        Factory.OpenAi.PlainTextReply = "We're here: https://maps.example/shop.";

        var resp = await PostInbound(businessId, "+15559991223", "where are you?");

        var body = await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>();
        Assert.Contains("https://maps.example/shop", body!.Reply);
    }
    // An emoji right after a real link made the whole match unknown, and the link was removed.
    [Fact]
    public async Task ALinkFollowedByAnEmoji_IsKept()
    {
        var (businessId, _, _) = await SeedBusinessWithService("wa-ai-emoji@example.com", "wa-ai-emoji");
        using (var db = Db())
        {
            (await db.Businesses.FirstAsync(b => b.Id == businessId)).MapUrl = "https://maps.example/shop";
            await db.SaveChangesAsync();
        }
        Factory.OpenAi.PlainTextReply = "We're here: https://maps.example/shop🙂";

        var reply = await Reply(businessId, "+15559991231", "where are you?");

        Assert.Contains("https://maps.example/shop🙂", reply);
    }

    // A reply that was only a made-up link used to go out empty.
    [Fact]
    public async Task AReplyThatWasOnlyAnUnknownLink_FallsBackToTheRuleBasedBot()
    {
        var (businessId, _, _) = await SeedBusinessWithService("wa-ai-onlyurl@example.com", "wa-ai-onlyurl");
        Factory.OpenAi.PlainTextReply = "https://evil.example/phish";

        var reply = await Reply(businessId, "+15559991232", "hi");

        Assert.False(string.IsNullOrWhiteSpace(reply));
        Assert.DoesNotContain("evil.example", reply);
    }

    // Expired rows are never deleted; the AI path skipped them and inserted a second row for the
    // same phone, which hit the unique index.
    [Fact]
    public async Task AnExpiredConversation_IsReused_NotDuplicated()
    {
        var (businessId, _, _) = await SeedBusinessWithService("wa-ai-expired@example.com", "wa-ai-expired");
        var phone = "+15559991233";
        Factory.OpenAi.PlainTextReply = "Hello!";
        await PostInbound(businessId, phone, "hi");
        using (var db = Db())
        {
            var state = await db.WhatsAppConversationStates.SingleAsync(s => s.BusinessId == businessId && s.Phone == phone);
            state.ExpiresAt = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        Assert.Equal("Hello!", await Reply(businessId, phone, "hi again"));

        using var check = Db();
        var row = await check.WhatsAppConversationStates.SingleAsync(s => s.BusinessId == businessId && s.Phone == phone);
        Assert.True(row.ExpiresAt > DateTime.UtcNow);
    }

    // "Already requested" has nothing to confirm, so it no longer blocks a booking link in the same turn.
    [Fact]
    public async Task AlreadyRequestedCancel_DoesNotBlockABookingLink()
    {
        var (businessId, _, itemId) = await SeedBusinessWithService("wa-ai-pendingcancel@example.com", "wa-ai-pendingcancel");
        var phone = "+15559991234";
        await SeedUpcomingAppointments(businessId, itemId, phone, 1);
        using (var db = Db())
        {
            (await db.Appointments.SingleAsync(a => a.BusinessId == businessId)).PendingCancellationApproval = true;
            await db.SaveChangesAsync();
        }
        Factory.OpenAi.ToolCallName = "cancel_upcoming_appointment";
        Factory.OpenAi.ThenToolCalls.Add(("create_booking_link", JsonSerializer.Serialize(new { itemId })));

        var reply = await Reply(businessId, phone, "cancel it and book me a haircut");

        Assert.Contains("/w/", reply);
    }
}
