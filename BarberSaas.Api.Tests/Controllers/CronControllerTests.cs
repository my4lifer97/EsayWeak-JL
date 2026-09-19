using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Locks in the fix for CronController accepting an empty Bearer token whenever CronSecret
// happened to be unconfigured (auth != $"Bearer {cronSecret}" is true for an empty header
// when cronSecret is null/empty too) — see Program.cs secret-rotation history.
public class CronControllerTests : IntegrationTestBase
{
    [Fact]
    public async Task NoAuthHeader_ReturnsUnauthorized()
    {
        var resp = await Client.GetAsync("/api/cron/reminders");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task EmptyBearerToken_ReturnsUnauthorized()
    {
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer ");

        var resp = await Client.GetAsync("/api/cron/reminders");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task WrongSecret_ReturnsUnauthorized()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-secret");

        var resp = await Client.GetAsync("/api/cron/reminders");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task CorrectSecret_ReturnsOk()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestWebApplicationFactory.CronSecret);

        var resp = await Client.GetAsync("/api/cron/reminders");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    private async Task<(string businessId, string phone, string appointmentId)> SeedConfirmedAppointment(
        string email, string slug, DateTime date, string startTime)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));

        var phone = "+15550001111";
        using var db = Factory.CreateDbContext();
        var business = await db.Businesses.SingleAsync(b => b.Email == email);
        business.WhatsAppNumber = "+15559990000";

        var item = new Item { BusinessId = business.Id, NameEn = "Haircut", NameAr = "قص شعر", NameHe = "תספורת", DurationMinutes = 30 };
        var customer = new Customer { BusinessId = business.Id, Name = "Dana", FamilyName = "Cohen", Phone = phone };
        var appointment = new Appointment
        {
            BusinessId = business.Id, CustomerId = customer.Id, ItemId = item.Id,
            Date = date, StartTime = startTime, EndTime = startTime, Status = AppointmentStatus.CONFIRMED,
        };
        db.Items.Add(item);
        db.Customers.Add(customer);
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        return (business.Id, phone, appointment.Id);
    }

    [Fact]
    public async Task Reminders_AppointmentTomorrow_SendsDayBeforeReminderAndSetsFlag()
    {
        var tomorrow = DateTime.Now.AddDays(1).Date;
        var (businessId, phone, appointmentId) = await SeedConfirmedAppointment(
            "cron-reminder-tomorrow@example.com", "cron-reminder-tomorrow-shop", tomorrow, "10:00");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestWebApplicationFactory.CronSecret);

        var resp = await Client.GetAsync("/api/cron/reminders");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains(Factory.WhatsAppSender.Sent, s => s.BusinessId == businessId && s.Phone == phone && s.Message.Contains("tomorrow"));

        using var db = Db();
        var appt = await db.Appointments.SingleAsync(a => a.Id == appointmentId);
        Assert.True(appt.ReminderSent);
        Assert.False(appt.ReminderSentSoon);
    }

    [Fact]
    public async Task Reminders_AppointmentStartingWithinThreeHours_SendsSoonReminderAndSetsFlag()
    {
        var soon = DateTime.Now.AddHours(2);
        var (businessId, phone, appointmentId) = await SeedConfirmedAppointment(
            "cron-reminder-soon@example.com", "cron-reminder-soon-shop", soon.Date, soon.ToString("HH:mm"));
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestWebApplicationFactory.CronSecret);

        var resp = await Client.GetAsync("/api/cron/reminders");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains(Factory.WhatsAppSender.Sent, s => s.BusinessId == businessId && s.Phone == phone && s.Message.Contains("few hours"));

        using var db = Db();
        var appt = await db.Appointments.SingleAsync(a => a.Id == appointmentId);
        Assert.True(appt.ReminderSentSoon);
    }

    [Fact]
    public async Task Reminders_AppointmentMoreThanThreeHoursAway_DoesNotSendSoonReminder()
    {
        var later = DateTime.Now.AddHours(5);
        var (businessId, phone, appointmentId) = await SeedConfirmedAppointment(
            "cron-reminder-later@example.com", "cron-reminder-later-shop", later.Date, later.ToString("HH:mm"));
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestWebApplicationFactory.CronSecret);

        var resp = await Client.GetAsync("/api/cron/reminders");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        // Not asserting zero messages overall: if "5 hours from now" happens to roll past midnight,
        // the appointment's date can incidentally equal "tomorrow" too, legitimately firing the
        // separate day-before reminder -- this test is only about the 3-hour window specifically.
        Assert.DoesNotContain(Factory.WhatsAppSender.Sent, s => s.BusinessId == businessId && s.Phone == phone && s.Message.Contains("few hours"));

        using var db = Db();
        var appt = await db.Appointments.SingleAsync(a => a.Id == appointmentId);
        Assert.False(appt.ReminderSentSoon);
    }

    // ─── generate-recurring: same auth contract as reminders above ─────────

    [Fact]
    public async Task GenerateRecurring_NoAuthHeader_ReturnsUnauthorized()
    {
        var resp = await Client.GetAsync("/api/cron/generate-recurring");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GenerateRecurring_EmptyBearerToken_ReturnsUnauthorized()
    {
        Client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer ");

        var resp = await Client.GetAsync("/api/cron/generate-recurring");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GenerateRecurring_WrongSecret_ReturnsUnauthorized()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-secret");

        var resp = await Client.GetAsync("/api/cron/generate-recurring");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GenerateRecurring_CorrectSecret_ReturnsOk()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestWebApplicationFactory.CronSecret);

        var resp = await Client.GetAsync("/api/cron/generate-recurring");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ─── charge-subscriptions: same auth contract, plus Cardcom charge outcomes ─────────

    [Fact]
    public async Task ChargeSubscriptions_NoAuthHeader_ReturnsUnauthorized()
    {
        var resp = await Client.GetAsync("/api/cron/charge-subscriptions");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ChargeSubscriptions_CorrectSecret_ReturnsOk()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestWebApplicationFactory.CronSecret);

        var resp = await Client.GetAsync("/api/cron/charge-subscriptions");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    private record RegisterResponse(string? DevCode);

    private async Task<string> SeedActiveBusinessDueForCharge(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));

        using var db = Factory.CreateDbContext();
        var business = await db.Businesses.SingleAsync(b => b.Email == email);
        business.SubscriptionStatus = SubStatus.ACTIVE;
        business.CardcomToken = "tok-existing";
        business.CardcomNextChargeAt = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        return business.Id;
    }

    [Fact]
    public async Task ChargeSubscriptions_SuccessfulCharge_BumpsNextChargeDateAndStaysActive()
    {
        var businessId = await SeedActiveBusinessDueForCharge("cron-charge-success@example.com", "cron-charge-success-shop");
        Factory.Cardcom.NextChargeSucceeds = true;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestWebApplicationFactory.CronSecret);

        var resp = await Client.GetAsync("/api/cron/charge-subscriptions");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, int>>();
        Assert.Equal(1, body!["total"]);
        Assert.Equal(1, body["charged"]);
        Assert.Equal(0, body["failed"]);

        using var db = Factory.CreateDbContext();
        var business = await db.Businesses.SingleAsync(b => b.Id == businessId);
        Assert.Equal(SubStatus.ACTIVE, business.SubscriptionStatus);
        Assert.True(business.CardcomNextChargeAt > DateTime.UtcNow.AddDays(25));
    }

    [Fact]
    public async Task ChargeSubscriptions_FailedCharge_SetsExpired()
    {
        var businessId = await SeedActiveBusinessDueForCharge("cron-charge-failed@example.com", "cron-charge-failed-shop");
        Factory.Cardcom.NextChargeSucceeds = false;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestWebApplicationFactory.CronSecret);

        var resp = await Client.GetAsync("/api/cron/charge-subscriptions");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, int>>();
        Assert.Equal(1, body!["total"]);
        Assert.Equal(0, body["charged"]);
        Assert.Equal(1, body["failed"]);

        using var db = Factory.CreateDbContext();
        var business = await db.Businesses.SingleAsync(b => b.Id == businessId);
        Assert.Equal(SubStatus.EXPIRED, business.SubscriptionStatus);
    }
}
