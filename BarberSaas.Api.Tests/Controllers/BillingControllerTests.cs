using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Stripe:SecretKey/PriceId/WebhookSecret ship as empty strings in appsettings.json (no Stripe
// account exists yet) and TestWebApplicationFactory doesn't set them either -- these tests lock
// in that the endpoints fail gracefully (503) rather than letting the Stripe SDK throw.
public class BillingControllerTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    private async Task<string> RegisterAndLoginBarber(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Barber", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var body = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    [Fact]
    public async Task CheckoutSession_NoAuth_ReturnsUnauthorized()
    {
        var resp = await Client.PostAsync("/api/billing/checkout-session", null);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task CheckoutSession_StripeNotConfigured_ReturnsServiceUnavailable()
    {
        var token = await RegisterAndLoginBarber("billing-unconfigured@example.com", "billing-unconfigured-shop");
        Authorize(Client, token);

        var resp = await Client.PostAsync("/api/billing/checkout-session", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Webhook_NotConfigured_ReturnsServiceUnavailable()
    {
        var resp = await Client.PostAsync("/api/billing/webhook", new StringContent(""));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }
}
