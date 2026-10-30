using System.Net.Http.Headers;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// No-show prevention: the day-before reminder asks "will you come? 1/2" (AttendanceQuestion) and the
// chatbot takes the next reply as the answer.
public class AttendanceConfirmationTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    private async Task<(string BusinessId, string Token, string AppointmentId)> Seed(string slug, string phone, bool inquiry = false)
    {
        var email = $"{slug}@example.com";
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Shop", email, "password123", slug));
        var code = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.DevCode!;
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code));
        var token = (await verify.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        if (inquiry)
        {
            Authorize(Client, token);
            await Client.PatchAsJsonAsync("/api/admin/settings", new { chatbotInquiryEnabled = true });
            Client.DefaultRequestHeaders.Authorization = null;
        }

        using var db = Db();
        var business = await db.Businesses.SingleAsync(b => b.Slug == slug);
        business.WhatsAppNumber = "+15559990000";
        var item = new Item { BusinessId = business.Id, NameEn = "Haircut", NameAr = "قص شعر", NameHe = "תספורת", DurationMinutes = 30 };
        var customer = new Customer { BusinessId = business.Id, Name = "Dana", FamilyName = "Cohen", Phone = phone };
        var appointment = new Appointment
        {
            BusinessId = business.Id, CustomerId = customer.Id, ItemId = item.Id,
            Date = AppClock.Now.AddDays(1).Date, StartTime = "10:00", EndTime = "10:30", Status = AppointmentStatus.CONFIRMED,
        };
        db.Items.Add(item);
        db.Customers.Add(customer);
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return (business.Id, token, appointment.Id);
    }

    private async Task RunReminders()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestWebApplicationFactory.CronSecret);
        await Client.GetAsync("/api/cron/reminders");
        Client.DefaultRequestHeaders.Authorization = null;
    }

    private async Task<string?> Inbound(string businessId, string phone, string message)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/bridge/inbound")
        {
            Content = JsonContent.Create(new WhatsAppController.BridgeInboundRequest(businessId, phone, "Dana", message)),
        };
        req.Headers.Add("X-Bridge-Secret", TestWebApplicationFactory.BridgeSecret);
        var resp = await Client.SendAsync(req);
        return (await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>())!.Reply;
    }

    private async Task<Appointment> Load(string id)
    {
        using var db = Db();
        return await db.Appointments.SingleAsync(a => a.Id == id);
    }

    [Fact]
    public async Task DayBeforeReminder_AsksToConfirm()
    {
        var (businessId, _, appointmentId) = await Seed("attend-ask", "+15553330001");

        await RunReminders();

        var sent = Assert.Single(Factory.WhatsAppSender.Sent, s => s.BusinessId == businessId);
        Assert.EndsWith("Reply 1 to confirm or 2 to cancel.", sent.Message);
        Assert.Equal(AttendanceQuestion.Asked, (await Load(appointmentId)).AttendanceStatus);
    }

    [Fact]
    public async Task One_ConfirmsTheAppointment()
    {
        var (businessId, _, appointmentId) = await Seed("attend-yes", "+15553330002");
        await RunReminders();

        var reply = await Inbound(businessId, "+15553330002", "1");

        Assert.StartsWith("Thanks for confirming!", reply);
        var appointment = await Load(appointmentId);
        Assert.Equal(AttendanceQuestion.Confirmed, appointment.AttendanceStatus);
        Assert.Equal(AppointmentStatus.CONFIRMED, appointment.Status);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("no")]
    public async Task TwoOrNo_CancelsTheAppointment(string answer)
    {
        var (businessId, _, appointmentId) = await Seed($"attend-no-{answer.Replace(' ', '-')}", "+15553330003");
        await RunReminders();

        var reply = await Inbound(businessId, "+15553330003", answer);

        Assert.Contains("cancelled", reply);
        Assert.Equal(AppointmentStatus.CANCELLED, (await Load(appointmentId)).Status);
    }

    [Theory]
    [InlineData("keep it")]
    [InlineData("להשאיר")]
    [InlineData("yes please")]
    public async Task KeepWords_ConfirmTheAppointment(string answer)
    {
        var (businessId, _, appointmentId) = await Seed($"attend-keep-{answer.Length}-{answer.GetHashCode():x}", "+15553330008");
        await RunReminders();

        var reply = await Inbound(businessId, "+15553330008", answer);

        Assert.NotNull(reply); // in the customer's language
        var appointment = await Load(appointmentId);
        Assert.Equal(AttendanceQuestion.Confirmed, appointment.AttendanceStatus);
        Assert.Equal(AppointmentStatus.CONFIRMED, appointment.Status);
    }

    // QA run 5: any message with a cancel word cancelled straight away, "don't cancel" included.
    [Theory]
    [InlineData("please don't cancel")]
    [InlineData("dont cancel it")]
    [InlineData("לא לבטל")]
    [InlineData("لا تلغي")]
    [InlineData("yes cancel")]
    public async Task CancelWord_AsksAgain_InsteadOfCancelling(string answer)
    {
        var (businessId, _, appointmentId) = await Seed($"attend-reask-{answer.Length}-{answer.GetHashCode():x}", "+15553330009");
        await RunReminders();

        var reply = await Inbound(businessId, "+15553330009", answer);

        // In the customer's language: the date is in every version.
        Assert.Contains(AppClock.Now.AddDays(1).ToString("yyyy-MM-dd"), reply);
        Assert.Equal(AppointmentStatus.CONFIRMED, (await Load(appointmentId)).Status);

        await Inbound(businessId, "+15553330009", "1");
        Assert.Equal(AttendanceQuestion.Confirmed, (await Load(appointmentId)).AttendanceStatus);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("cancel")]
    public async Task AfterTheReask_TwoOrACancelWordCancels(string answer)
    {
        var (businessId, _, appointmentId) = await Seed($"attend-reask2-{answer}", "+15553330010");
        await RunReminders();
        await Inbound(businessId, "+15553330010", "cancel please");

        var reply = await Inbound(businessId, "+15553330010", answer);

        Assert.Contains("cancelled", reply);
        Assert.Equal(AppointmentStatus.CANCELLED, (await Load(appointmentId)).Status);
    }

    [Fact]
    public async Task RescheduleRequest_DoesNotCancel()
    {
        var (businessId, _, appointmentId) = await Seed("attend-move", "+15553330011");
        await RunReminders();

        await Inbound(businessId, "+15553330011", "can I move it instead of cancel?");

        Assert.Equal(AppointmentStatus.CONFIRMED, (await Load(appointmentId)).Status);
    }

    [Fact]
    public async Task ChatbotOff_GetsThePlainReminder()
    {
        var (businessId, _, appointmentId) = await Seed("attend-bot-off", "+15553330012");
        using (var db = Db())
        {
            var business = await db.Businesses.SingleAsync(b => b.Id == businessId);
            business.ChatbotEnabled = false;
            await db.SaveChangesAsync();
        }

        await RunReminders();

        var sent = Assert.Single(Factory.WhatsAppSender.Sent, s => s.BusinessId == businessId);
        Assert.DoesNotContain("Reply 1 to confirm", sent.Message);
        Assert.Null((await Load(appointmentId)).AttendanceStatus);
    }

    // QA run 5: the conversation row the customer's message created made the run's single save
    // fail, so no ReminderSent was saved and the next run sent every reminder again.
    [Fact]
    public async Task CustomerWritesInDuringTheRun_ReminderIsNotSentTwice()
    {
        var (businessId, _, appointmentId) = await Seed("attend-race", "+15553330013");
        Factory.WhatsAppSender.OnSend = async () =>
        {
            Factory.WhatsAppSender.OnSend = null;
            using var db = Db();
            db.WhatsAppConversationStates.Add(new WhatsAppConversationState
            {
                BusinessId = businessId, Phone = "+15553330013", ChatbotMode = "Booking", ExpiresAt = DateTime.UtcNow.AddHours(1),
            });
            await db.SaveChangesAsync();
        };
        try
        {
            await RunReminders();
            await RunReminders();
        }
        finally
        {
            Factory.WhatsAppSender.OnSend = null;
        }

        Assert.Single(Factory.WhatsAppSender.Sent, s => s.BusinessId == businessId);
        var appointment = await Load(appointmentId);
        Assert.True(appointment.ReminderSent);
        Assert.Null(appointment.AttendanceStatus);
    }

    // QA run 5: these dropped the question and got the Book/Ask menu.
    [Theory]
    [InlineData("ok")]
    [InlineData("thanks!")]
    [InlineData("👍")]
    [InlineData("I'll be there")]
    [InlineData("ok see you tomorrow")]
    [InlineData("אגיע, תודה")]
    [InlineData("تمام")]
    public async Task Acknowledgement_ConfirmsTheAppointment(string answer)
    {
        var (businessId, _, appointmentId) = await Seed($"attend-ack-{answer.Length}-{answer.GetHashCode():x}", "+15553330014");
        await RunReminders();

        var reply = await Inbound(businessId, "+15553330014", answer);

        Assert.NotNull(reply);
        Assert.Equal(AttendanceQuestion.Confirmed, (await Load(appointmentId)).AttendanceStatus);
    }

    [Theory]
    [InlineData("ok but can it be at 11?")]
    [InlineData("I will be late")]
    public async Task AcknowledgementWithMore_IsNotAnAnswer(string message)
    {
        var (businessId, _, appointmentId) = await Seed($"attend-ack-more-{message.Length}", "+15553330015");
        await RunReminders();

        await Inbound(businessId, "+15553330015", message);

        Assert.Equal(AttendanceQuestion.Asked, (await Load(appointmentId)).AttendanceStatus);
    }

    // QA run 5: a second "1"/"2" was read as the opening menu ("2" opened a chat with the owner).
    [Fact]
    public async Task DoubleTap_AfterConfirming_IsAnsweredAboutTheAppointment()
    {
        var (businessId, _, _) = await Seed("attend-double-1", "+15553330016", inquiry: true);
        await RunReminders();
        await Inbound(businessId, "+15553330016", "1");

        var again = await Inbound(businessId, "+15553330016", "1");

        Assert.StartsWith("Thanks for confirming!", again);
    }

    [Fact]
    public async Task DoubleTap_AfterCancelling_DoesNotOpenAChatWithTheOwner()
    {
        var (businessId, _, appointmentId) = await Seed("attend-double-2", "+15553330017", inquiry: true);
        await RunReminders();
        await Inbound(businessId, "+15553330017", "2");

        var again = await Inbound(businessId, "+15553330017", "2");

        Assert.Contains("cancelled", again);
        Assert.Equal(AppointmentStatus.CANCELLED, (await Load(appointmentId)).Status);
        using var db = Db();
        Assert.False(await db.WhatsAppConversationStates.AnyAsync(s => s.BusinessId == businessId && s.ChatbotMode == ServiceListPrompt.InquiryMode));
    }

    [Fact]
    public async Task AnswerAfterTheOwnerCancelled_IsAnsweredAboutTheAppointment()
    {
        var (businessId, _, appointmentId) = await Seed("attend-gone", "+15553330018");
        await RunReminders();
        using (var db = Db())
        {
            var appointment = await db.Appointments.SingleAsync(a => a.Id == appointmentId);
            appointment.Status = AppointmentStatus.CANCELLED;
            await db.SaveChangesAsync();
        }

        var reply = await Inbound(businessId, "+15553330018", "1");

        Assert.Contains("cancelled", reply);
        Assert.DoesNotContain("1. Haircut", reply);
    }

    [Fact]
    public async Task AfterTheAnswer_AnotherMessageIsHandledNormally()
    {
        var (businessId, _, _) = await Seed("attend-after", "+15553330019");
        await RunReminders();
        await Inbound(businessId, "+15553330019", "1");

        var reply = await Inbound(businessId, "+15553330019", "hi");

        Assert.Contains("1. Haircut", reply);
    }

    [Fact]
    public async Task OtherMessage_DropsTheQuestion_AndIsHandledNormally()
    {
        var (businessId, _, appointmentId) = await Seed("attend-other", "+15553330004");
        await RunReminders();

        var reply = await Inbound(businessId, "+15553330004", "what is the address?");
        await Inbound(businessId, "+15553330004", "1"); // now a service choice, not a confirmation

        Assert.Contains("1. Haircut", reply);
        var appointment = await Load(appointmentId);
        Assert.Equal(AttendanceQuestion.Asked, appointment.AttendanceStatus);
        Assert.Equal(AppointmentStatus.CONFIRMED, appointment.Status);
    }

    [Fact]
    public async Task AtTheBookAskMenu_OneStillAnswersTheQuestion()
    {
        var (businessId, _, appointmentId) = await Seed("attend-gate", "+15553330005", inquiry: true);
        await RunReminders();

        var reply = await Inbound(businessId, "+15553330005", "1");

        Assert.StartsWith("Thanks for confirming!", reply);
        Assert.Equal(AttendanceQuestion.Confirmed, (await Load(appointmentId)).AttendanceStatus);
    }

    [Fact]
    public async Task CustomerInAConversation_GetsThePlainReminder()
    {
        var (businessId, _, appointmentId) = await Seed("attend-busy", "+15553330006");
        await Inbound(businessId, "+15553330006", "hi"); // the service list is waiting for a number

        await RunReminders();

        var sent = Factory.WhatsAppSender.Sent.Single(s => s.BusinessId == businessId && s.Message.Contains("Reminder"));
        Assert.DoesNotContain("Reply 1 to confirm", sent.Message);
        Assert.Null((await Load(appointmentId)).AttendanceStatus);
    }

    [Fact]
    public async Task OwnerSeesTheAnswer()
    {
        var (businessId, token, appointmentId) = await Seed("attend-owner", "+15553330007");
        await RunReminders();
        await Inbound(businessId, "+15553330007", "1");

        Authorize(Client, token);
        var list = (await Client.GetFromJsonAsync<List<DashboardAppointmentDto>>("/api/admin/appointments?filter=upcoming"))!;

        Assert.Equal(AttendanceQuestion.Confirmed, list.Single(a => a.Id == appointmentId).AttendanceStatus);
    }
}
