using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Locks in the BarberOnly / CustomerOnly policy separation added alongside the customer
// accounts feature: a barber JWT must never satisfy a customer-only endpoint and vice versa.
public class AuthorizationPolicyTests : IntegrationTestBase
{
    private record OtpRequestResponse(bool IsNewCustomer, string? DevOtp);
    private record VerifyOtpResponse(string Token);

    private async Task<string> GetBarberToken()
    {
        await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Barber", "policy-barber@example.com", "password123", "policy-barber"));
        var login = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("policy-barber@example.com", "password123"));
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    private async Task<string> GetCustomerToken()
    {
        const string phone = "+15552220001";
        var otpResp = await Client.PostAsJsonAsync("/api/customer/auth/otp", new RequestCustomerOtpRequest(phone));
        var otpBody = await otpResp.Content.ReadFromJsonAsync<OtpRequestResponse>();
        var verify = await Client.PostAsJsonAsync("/api/customer/auth/verify", new VerifyCustomerOtpRequest(phone, otpBody!.DevOtp!, "First", "Last"));
        var verifyBody = await verify.Content.ReadFromJsonAsync<VerifyOtpResponse>();
        return verifyBody!.Token;
    }

    [Fact]
    public async Task AdminEndpoint_WithBarberToken_Succeeds()
    {
        Authorize(Client, await GetBarberToken());

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
    public async Task CustomerEndpoint_WithBarberToken_ReturnsForbidden()
    {
        Authorize(Client, await GetBarberToken());

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
