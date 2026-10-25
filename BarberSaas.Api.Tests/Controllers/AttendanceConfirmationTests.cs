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
    [InlineData("yes cancel")] // a cancel word means cancel, not yes
    public async Task TwoOrNo_CancelsTheAppointment(string answer)
    {
        var (businessId, _, appointmentId) = await Seed($"attend-no-{answer.Replace(' ', '-')}", "+15553330003");
        await RunReminders();

        var reply = await Inbound(businessId, "+15553330003", answer);

        Assert.Contains("cancelled", reply);
        Assert.Equal(AppointmentStatus.CANCELLED, (await Load(appointmentId)).Status);
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
