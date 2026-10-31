using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Data;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Owners who pay the platform admin directly: the admin sets the last paid day and hears about it
// once when it passes, with the business still working.
public class PaidPeriodTests : IntegrationTestBase
{
    private static DateOnly Today => DateOnly.FromDateTime(AppClock.Now);

    private async Task BootstrapAdmin()
    {
        var resp = await Client.PostAsJsonAsync("/api/platform-admin/bootstrap",
            new PlatformAdminBootstrapRequest("admin@example.com", "supersecret123", "Admin"));
        Authorize(Client, (await resp.Content.ReadFromJsonAsync<PlatformAdminLoginResponse>())!.Token);
    }

    private async Task<string> AddBusiness(string slug = "paid-shop", Action<Business>? configure = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var business = new Business
        {
            Name = "Paid Shop", Email = $"{slug}@example.com", Slug = slug, PasswordHash = "x", Phone = "+972500000000",
            TrialEndsAt = DateTime.UtcNow.AddDays(30), EmailVerified = true,
        };
        configure?.Invoke(business);
        db.Businesses.Add(business);
        await db.SaveChangesAsync();
        return business.Id;
    }

    private async Task<Business> Load(string id)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Businesses.AsNoTracking().FirstAsync(b => b.Id == id);
    }

    private async Task Notify()
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PaidPeriodMonitor>().NotifyEndedAsync();
    }

    private Task<HttpResponseMessage> SetPaidUntil(string id, string? date) =>
        Client.PutAsJsonAsync($"/api/platform-admin/businesses/{id}/paid-until", new SetPaidUntilRequest(date));

    private int AlertsFor(string name) => Factory.Email.Sent.Count(e => e.Subject == $"Payment period ended: {name}");

    [Fact]
    public async Task SetPaidUntil_RequiresPlatformAdmin()
    {
        var id = await AddBusiness();
        var resp = await SetPaidUntil(id, Today.AddDays(30).ToString("yyyy-MM-dd"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task SetPaidUntil_MakesBusinessActive_AndLogsIt()
    {
        await BootstrapAdmin();
        var id = await AddBusiness();
        var date = Today.AddDays(30);

        Assert.Equal(HttpStatusCode.OK, (await SetPaidUntil(id, date.ToString("yyyy-MM-dd"))).StatusCode);

        var business = await Load(id);
        Assert.Equal(date, business.PaidUntil);
        Assert.Equal(SubStatus.ACTIVE, business.SubscriptionStatus);
        using var scope = Factory.Services.CreateScope();
        var log = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ActivityLogs
            .SingleAsync(a => a.BusinessId == id && a.Action.EndsWith(".SetPaidUntil"));
        Assert.Contains(date.ToString("yyyy-MM-dd"), log.Description);
    }

    [Fact]
    public async Task SetPaidUntil_LastPaidDayCanBeToday()
    {
        await BootstrapAdmin();
        var id = await AddBusiness();
        Assert.Equal(HttpStatusCode.OK, (await SetPaidUntil(id, Today.ToString("yyyy-MM-dd"))).StatusCode);
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("2026-02-30")]
    public async Task SetPaidUntil_RejectsInvalidDate(string date)
    {
        await BootstrapAdmin();
        var id = await AddBusiness();
        Assert.Equal(HttpStatusCode.BadRequest, (await SetPaidUntil(id, date)).StatusCode);
    }

    [Fact]
    public async Task SetPaidUntil_RejectsPastDate()
    {
        await BootstrapAdmin();
        var id = await AddBusiness();
        var resp = await SetPaidUntil(id, Today.AddDays(-1).ToString("yyyy-MM-dd"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Null((await Load(id)).PaidUntil);
    }

    [Fact]
    public async Task ClearingPaidUntil_ReturnsToTrial()
    {
        await BootstrapAdmin();
        var id = await AddBusiness(configure: b => { b.PaidUntil = Today.AddDays(5); b.SubscriptionStatus = SubStatus.ACTIVE; });

        Assert.Equal(HttpStatusCode.OK, (await SetPaidUntil(id, null)).StatusCode);

        var business = await Load(id);
        Assert.Null(business.PaidUntil);
        Assert.Equal(SubStatus.TRIAL, business.SubscriptionStatus);
    }

    [Fact]
    public async Task ClearingPaidUntil_KeepsCardPayingBusinessActive()
    {
        await BootstrapAdmin();
        var id = await AddBusiness(configure: b => { b.PaidUntil = Today.AddDays(5); b.SubscriptionStatus = SubStatus.ACTIVE; b.CardcomToken = "token"; });

        await SetPaidUntil(id, null);

        Assert.Equal(SubStatus.ACTIVE, (await Load(id)).SubscriptionStatus);
    }

    [Fact]
    public async Task EndedPeriod_AlertsAdminOnce_WithLinkAndOwnerContact()
    {
        await BootstrapAdmin();
        var id = await AddBusiness(configure: b => { b.PaidUntil = Today.AddDays(-1); b.SubscriptionStatus = SubStatus.ACTIVE; });

        await Notify();
        await Notify();

        var email = Assert.Single(Factory.Email.Sent, e => e.Subject == "Payment period ended: Paid Shop");
        Assert.Equal("admin@example.com", email.Email);
        Assert.Contains($"/platform-admin/businesses/{id}", email.Body);
        Assert.Contains("paid-shop@example.com", email.Body);
        Assert.Contains("+972500000000", email.Body);
    }

    [Fact]
    public async Task EndedPeriod_KeepsBusinessTakingBookings()
    {
        await BootstrapAdmin();
        var id = await AddBusiness(configure: b => { b.PaidUntil = Today.AddDays(-1); b.SubscriptionStatus = SubStatus.ACTIVE; });

        await Notify();

        var business = await Load(id);
        Assert.Equal(SubStatus.ACTIVE, business.SubscriptionStatus);
        Assert.True(business.AcceptsBookings(DateTime.UtcNow, enforceTrial: true));
    }

    [Fact]
    public async Task LastPaidDay_IsNotEndedYet()
    {
        await BootstrapAdmin();
        await AddBusiness(configure: b => b.PaidUntil = Today);

        await Notify();

        Assert.Equal(0, AlertsFor("Paid Shop"));
    }

    [Fact]
    public async Task BusinessWithoutPaidUntil_NeverAlerts()
    {
        await BootstrapAdmin();
        await AddBusiness();

        await Notify();

        Assert.Equal(0, AlertsFor("Paid Shop"));
    }

    [Fact]
    public async Task DisabledBusiness_DoesNotAlert()
    {
        await BootstrapAdmin();
        await AddBusiness(configure: b => { b.PaidUntil = Today.AddDays(-1); b.IsDisabled = true; });

        await Notify();

        Assert.Equal(0, AlertsFor("Paid Shop"));
    }

    [Fact]
    public async Task NewDate_ThatEndsAgain_AlertsAgain()
    {
        await BootstrapAdmin();
        var id = await AddBusiness(configure: b => b.PaidUntil = Today.AddDays(-40));
        await Notify();
        Assert.Equal(1, AlertsFor("Paid Shop"));

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var business = await db.Businesses.FirstAsync(b => b.Id == id);
            business.PaidUntil = Today.AddDays(-1);
            await db.SaveChangesAsync();
        }
        await Notify();

        Assert.Equal(2, AlertsFor("Paid Shop"));
    }

    [Fact]
    public async Task EndedList_ShowsEndedOnly_AndClearsOnRenewal()
    {
        await BootstrapAdmin();
        var endedId = await AddBusiness("ended-shop", b => b.PaidUntil = Today.AddDays(-2));
        await AddBusiness("current-shop", b => b.PaidUntil = Today.AddDays(2));
        await AddBusiness("disabled-shop", b => { b.PaidUntil = Today.AddDays(-2); b.IsDisabled = true; });

        var list = await Client.GetFromJsonAsync<List<PaidPeriodEndedDto>>("/api/platform-admin/paid-period-ended");
        Assert.Equal(endedId, Assert.Single(list!).Id);

        await SetPaidUntil(endedId, Today.AddDays(30).ToString("yyyy-MM-dd"));
        list = await Client.GetFromJsonAsync<List<PaidPeriodEndedDto>>("/api/platform-admin/paid-period-ended");
        Assert.Empty(list!);
    }

    [Fact]
    public async Task OwnerSettings_IncludeTheDate()
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Owner Shop", "owner-paid@example.com", "password123", "owner-paid"));
        var body = await register.Content.ReadFromJsonAsync<RegisterDevCode>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest("owner-paid@example.com", body!.DevCode!));
        var login = await verify.Content.ReadFromJsonAsync<LoginResponse>();

        var date = Today.AddDays(30);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var business = await db.Businesses.FirstAsync(b => b.Slug == "owner-paid");
            business.PaidUntil = date;
            await db.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/settings");
        request.Headers.Authorization = new("Bearer", login!.Token);
        var settings = await (await Client.SendAsync(request)).Content.ReadFromJsonAsync<SettingsPaidUntil>();
        Assert.Equal(date, settings!.PaidUntil);
    }

    private record RegisterDevCode(string? DevCode);
    private record SettingsPaidUntil(DateOnly? PaidUntil);
}
