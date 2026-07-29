using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Exercises the Cardcom checkout/webhook flow end-to-end against FakeCardcomService (never the
// real gateway) -- opts into Cardcom:* config being present via configureCardcom: true, unlike
// BillingControllerTests above which deliberately leaves it unset.
public class BillingControllerCardcomTests : IntegrationTestBase
{
    public BillingControllerCardcomTests() : base(configureCardcom: true) { }

    private record RegisterResponse(string? DevCode);

    private async Task<(string Token, string BarberId)> RegisterAndLoginBarber(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Barber", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var body = await verify.Content.ReadFromJsonAsync<LoginResponse>();

        using var db = Factory.CreateDbContext();
        var barber = await db.Barbers.SingleAsync(b => b.Email == email);
        return (body!.Token, barber.Id);
    }

    [Fact]
    public async Task CheckoutSession_Configured_ReturnsUrlAndPassesBarberIdAsReturnValue()
    {
        var (token, barberId) = await RegisterAndLoginBarber("cardcom-checkout@example.com", "cardcom-checkout-shop");
        Authorize(Client, token);

        var resp = await Client.PostAsync("/api/billing/checkout-session", null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.False(string.IsNullOrEmpty(body!["url"]));

        var call = Assert.Single(Factory.Cardcom.CreateCalls);
        Assert.Equal(barberId, call.ReturnValue);
    }

    [Fact]
    public async Task Webhook_VerifiedResultWithToken_ActivatesSubscriptionAndStoresToken()
    {
        var (_, barberId) = await RegisterAndLoginBarber("cardcom-webhook@example.com", "cardcom-webhook-shop");

        var lowProfileId = Guid.NewGuid().ToString("N");
        Factory.Cardcom.Results[lowProfileId] = new CardcomLowProfileResult(0, null, lowProfileId, "txn-1", "tok-1", 120m, barberId);

        var resp = await Client.PostAsync($"/api/billing/webhook?LowProfileId={lowProfileId}", new StringContent(""));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var db = Factory.CreateDbContext();
        var barber = await db.Barbers.SingleAsync(b => b.Id == barberId);
        Assert.Equal(SubStatus.ACTIVE, barber.SubscriptionStatus);
        Assert.Equal("tok-1", barber.CardcomToken);
        Assert.Equal(lowProfileId, barber.CardcomLastLowProfileId);
        Assert.NotNull(barber.CardcomNextChargeAt);
        Assert.True(barber.CardcomNextChargeAt > DateTime.UtcNow.AddDays(25));
    }

    [Fact]
    public async Task Webhook_DuplicateDeliveryForSameLowProfileId_IsNoOp()
    {
        var (_, barberId) = await RegisterAndLoginBarber("cardcom-webhook-dup@example.com", "cardcom-webhook-dup-shop");

        var lowProfileId = Guid.NewGuid().ToString("N");
        Factory.Cardcom.Results[lowProfileId] = new CardcomLowProfileResult(0, null, lowProfileId, "txn-1", "tok-1", 120m, barberId);

        await Client.PostAsync($"/api/billing/webhook?LowProfileId={lowProfileId}", new StringContent(""));

        DateTime? nextChargeAfterFirst;
        using (var db = Factory.CreateDbContext())
            nextChargeAfterFirst = (await db.Barbers.SingleAsync(b => b.Id == barberId)).CardcomNextChargeAt;

        var resp = await Client.PostAsync($"/api/billing/webhook?LowProfileId={lowProfileId}", new StringContent(""));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var db2 = Factory.CreateDbContext();
        var barber = await db2.Barbers.SingleAsync(b => b.Id == barberId);
        Assert.Equal(nextChargeAfterFirst, barber.CardcomNextChargeAt);
    }
}
