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

    private async Task<string> SeedActiveBarberDueForCharge(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Barber", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));

        using var db = Factory.CreateDbContext();
        var barber = await db.Barbers.SingleAsync(b => b.Email == email);
        barber.SubscriptionStatus = SubStatus.ACTIVE;
        barber.CardcomToken = "tok-existing";
        barber.CardcomNextChargeAt = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        return barber.Id;
    }

    [Fact]
    public async Task ChargeSubscriptions_SuccessfulCharge_BumpsNextChargeDateAndStaysActive()
    {
        var barberId = await SeedActiveBarberDueForCharge("cron-charge-success@example.com", "cron-charge-success-shop");
        Factory.Cardcom.NextChargeSucceeds = true;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestWebApplicationFactory.CronSecret);

        var resp = await Client.GetAsync("/api/cron/charge-subscriptions");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, int>>();
        Assert.Equal(1, body!["total"]);
        Assert.Equal(1, body["charged"]);
        Assert.Equal(0, body["failed"]);

        using var db = Factory.CreateDbContext();
        var barber = await db.Barbers.SingleAsync(b => b.Id == barberId);
        Assert.Equal(SubStatus.ACTIVE, barber.SubscriptionStatus);
        Assert.True(barber.CardcomNextChargeAt > DateTime.UtcNow.AddDays(25));
    }

    [Fact]
    public async Task ChargeSubscriptions_FailedCharge_SetsExpired()
    {
        var barberId = await SeedActiveBarberDueForCharge("cron-charge-failed@example.com", "cron-charge-failed-shop");
        Factory.Cardcom.NextChargeSucceeds = false;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestWebApplicationFactory.CronSecret);

        var resp = await Client.GetAsync("/api/cron/charge-subscriptions");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, int>>();
        Assert.Equal(1, body!["total"]);
        Assert.Equal(0, body["charged"]);
        Assert.Equal(1, body["failed"]);

        using var db = Factory.CreateDbContext();
        var barber = await db.Barbers.SingleAsync(b => b.Id == barberId);
        Assert.Equal(SubStatus.EXPIRED, barber.SubscriptionStatus);
    }
}
