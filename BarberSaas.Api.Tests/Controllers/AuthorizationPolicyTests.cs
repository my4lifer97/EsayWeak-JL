using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Locks in the BusinessOnly / CustomerOnly policy separation added alongside the customer
// accounts feature: a business JWT must never satisfy a customer-only endpoint and vice versa.
public class AuthorizationPolicyTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    private async Task<string> GetBusinessToken()
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", "policy-business@example.com", "password123", "policy-business"));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest("policy-business@example.com", registerBody!.DevCode!));
        var body = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    private async Task<string> GetCustomerToken() => (await LoginCustomerViaWhatsAppAsync("+15552220001")).Token;

    [Fact]
    public async Task AdminEndpoint_WithBusinessToken_Succeeds()
    {
        Authorize(Client, await GetBusinessToken());

        var resp = await Client.GetAsync("/api/admin/settings");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoint_WithCustomerToken_ReturnsForbidden()
    {
        Authorize(Client, await GetCustomerToken());

        var resp = await Client.GetAsync("/api/admin/settings");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoint_WithNoToken_ReturnsUnauthorized()
    {
        var resp = await Client.GetAsync("/api/admin/settings");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task CustomerEndpoint_WithCustomerToken_Succeeds()
    {
        Authorize(Client, await GetCustomerToken());

        var resp = await Client.GetAsync("/api/customer/appointments");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task CustomerEndpoint_WithBusinessToken_ReturnsForbidden()
    {
        Authorize(Client, await GetBusinessToken());

        var resp = await Client.GetAsync("/api/customer/appointments");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task CustomerEndpoint_WithNoToken_ReturnsUnauthorized()
    {
        var resp = await Client.GetAsync("/api/customer/appointments");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
