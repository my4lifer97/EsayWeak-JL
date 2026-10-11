using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// The Gmail owner-email channel failed silently for days when its refresh token expired -- these
// cover the dashboard status and the alert email that now make that visible.
public class OwnerEmailHealthTests : IntegrationTestBase
{
    private record HealthResponse(OwnerEmailHealth.Snapshot OwnerEmail);

    private async Task BootstrapAdmin()
    {
        var resp = await Client.PostAsJsonAsync("/api/platform-admin/bootstrap",
            new PlatformAdminBootstrapRequest("owner@example.com", "supersecret123", "Owner"));
        Authorize(Client, (await resp.Content.ReadFromJsonAsync<PlatformAdminLoginResponse>())!.Token);
    }

    private async Task<HealthResponse> CheckNow()
    {
        var resp = await Client.PostAsync("/api/platform-admin/system-health/owner-email/check", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<HealthResponse>())!;
    }

    private async Task Notify()
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<OwnerEmailMonitor>().NotifyIfNeededAsync();
    }

    [Fact]
    public async Task SystemHealth_RequiresPlatformAdmin()
    {
        var resp = await Client.GetAsync("/api/platform-admin/system-health");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task SystemHealth_BeforeAnyCheck_IsUnknown()
    {
        await BootstrapAdmin();
        var health = await Client.GetFromJsonAsync<HealthResponse>("/api/platform-admin/system-health");
        Assert.Equal("unknown", health!.OwnerEmail.Status);
    }

    [Fact]
    public async Task DeadToken_ShowsFailing_AndAlertsAdminOnceViaSystemEmail()
    {
        await BootstrapAdmin();
        Factory.OwnerEmail.AuthError = "Gmail token refresh returned 400 BadRequest: invalid_grant";

        var health = await CheckNow();
        Assert.Equal("failing", health.OwnerEmail.Status);
        Assert.Contains("invalid_grant", health.OwnerEmail.LastError);
        Assert.NotNull(health.OwnerEmail.FailingSince);

        await Notify();
        await CheckNow(); // still broken
        await Notify();   // within the repeat window -- no second email

        var alert = Assert.Single(Factory.Email.Sent);
        Assert.Equal("owner@example.com", alert.Email);
        Assert.Contains("failing", alert.Subject);
        Assert.Contains("invalid_grant", alert.Body);
        Assert.Empty(Factory.OwnerEmail.Sent); // never through the broken Gmail channel
    }

    [Fact]
    public async Task FixedToken_ShowsOk_AndSendsOneRecoveryEmail()
    {
        await BootstrapAdmin();
        Factory.OwnerEmail.AuthError = "invalid_grant";
        await CheckNow();
        await Notify();

        Factory.OwnerEmail.AuthError = null;
        var health = await CheckNow();
        await Notify();
        await Notify();

        Assert.Equal("ok", health.OwnerEmail.Status);
        Assert.Null(health.OwnerEmail.LastError);
        Assert.Equal(2, Factory.Email.Sent.Count);
        Assert.Contains(Factory.Email.Sent, e => e.Subject.Contains("working again"));
    }

    [Fact]
    public async Task HealthyChannel_SendsNothing()
    {
        await BootstrapAdmin();
        await CheckNow();
        await Notify();
        Assert.Empty(Factory.Email.Sent);
    }

    [Fact]
    public void StillFailing_RepeatsTheAlertAfterTheRepeatWindow()
    {
        var health = new OwnerEmailHealth();
        var now = DateTime.UtcNow;
        health.RecordFailure("invalid_grant");
        health.MarkNotified(OwnerEmailHealth.Notification.Failing, now);

        Assert.Equal(OwnerEmailHealth.Notification.None, health.PendingNotification(now.AddHours(23), TimeSpan.FromHours(24)));
        Assert.Equal(OwnerEmailHealth.Notification.Failing, health.PendingNotification(now.AddHours(25), TimeSpan.FromHours(24)));
    }

    [Fact]
    public void RecoveryWithoutAPriorAlert_SendsNoRecoveryEmail()
    {
        var health = new OwnerEmailHealth();
        health.RecordFailure("blip");
        health.RecordSuccess();
        Assert.Equal(OwnerEmailHealth.Notification.None, health.PendingNotification(DateTime.UtcNow, TimeSpan.FromHours(24)));
    }
}
