using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

public class PlatformAdminControllerTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    private async Task<string> RegisterAndLoginBusiness(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var body = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    private async Task<(string Token, string CustomerId)> GetCustomerToken(string phone, string name = "Test", string familyName = "Customer")
    {
        var result = await LoginCustomerViaWhatsAppAsync(phone, name, familyName);
        return (result.Token, result.CustomerId);
    }

    private async Task<string> BootstrapAdmin(string email = "owner@example.com")
    {
        var resp = await Client.PostAsJsonAsync("/api/platform-admin/bootstrap",
            new PlatformAdminBootstrapRequest(email, "supersecret123", "Owner"));
        var body = await resp.Content.ReadFromJsonAsync<PlatformAdminLoginResponse>();
        return body!.Token;
    }

    // ─── Bootstrap & login ──────────────────────────────────────────────────

    [Fact]
    public async Task Bootstrap_FirstTime_Succeeds()
    {
        var resp = await Client.PostAsJsonAsync("/api/platform-admin/bootstrap",
            new PlatformAdminBootstrapRequest("owner@example.com", "supersecret123", "Owner"));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<PlatformAdminLoginResponse>();
        Assert.False(string.IsNullOrEmpty(body!.Token));
    }

    [Fact]
    public async Task Bootstrap_SecondTime_ReturnsForbidden()
    {
        await BootstrapAdmin();

        var resp = await Client.PostAsJsonAsync("/api/platform-admin/bootstrap",
            new PlatformAdminBootstrapRequest("someoneElse@example.com", "supersecret123", "Someone Else"));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_ShortPassword_ReturnsBadRequest()
    {
        var resp = await Client.PostAsJsonAsync("/api/platform-admin/bootstrap",
            new PlatformAdminBootstrapRequest("owner@example.com", "short", "Owner"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Login_CorrectCredentials_ReturnsOk()
    {
        await BootstrapAdmin();

        var resp = await Client.PostAsJsonAsync("/api/platform-admin/login",
            new PlatformAdminLoginRequest("owner@example.com", "supersecret123"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        await BootstrapAdmin();

        var resp = await Client.PostAsJsonAsync("/api/platform-admin/login",
            new PlatformAdminLoginRequest("owner@example.com", "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    // ─── PlatformAdminOnly gating ───────────────────────────────────────────

    [Fact]
    public async Task SearchBusinesses_NoAuth_ReturnsUnauthorized()
    {
        var resp = await Client.GetAsync("/api/platform-admin/businesses");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task SearchBusinesses_WithRealBusinessToken_ReturnsForbidden()
    {
        var businessToken = await RegisterAndLoginBusiness("notanadmin@example.com", "not-an-admin-shop");
        Authorize(Client, businessToken);

        var resp = await Client.GetAsync("/api/platform-admin/businesses");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ─── Search ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchCustomers_FullNameQuery_MatchesAcrossNameAndFamilyName()
    {
        var adminToken = await BootstrapAdmin();
        await GetCustomerToken("+15559991111", "Waitlist", "Customer");

        Authorize(Client, adminToken);
        var results = await Client.GetFromJsonAsync<List<PlatformAdminCustomerSummaryDto>>("/api/platform-admin/customers?search=Waitlist%20Customer");

        Assert.Contains(results!, c => c.Phone == "+15559991111");
    }

    // ─── Impersonation ──────────────────────────────────────────────────────

    [Fact]
    public async Task ImpersonateBusiness_TokenWorksOnBusinessOnlyEndpoint()
    {
        var adminToken = await BootstrapAdmin();
        var businessToken = await RegisterAndLoginBusiness("target-business@example.com", "target-business-shop");
        Authorize(Client, businessToken);
        var settings = await Client.GetFromJsonAsync<SettingsDto>("/api/admin/settings");

        Authorize(Client, adminToken);
        var searchResp = await Client.GetFromJsonAsync<List<PlatformAdminBusinessSummaryDto>>("/api/platform-admin/businesses?search=target-business");
        var found = Assert.Single(searchResp!);
        Assert.Equal(settings!.Id, found.Id);

        var impersonateResp = await Client.PostAsync($"/api/platform-admin/businesses/{found.Id}/impersonate", null);
        Assert.Equal(HttpStatusCode.OK, impersonateResp.StatusCode);
        var impersonateBody = await impersonateResp.Content.ReadFromJsonAsync<PlatformAdminImpersonateResponse>();

        Client.DefaultRequestHeaders.Authorization = null;
        Authorize(Client, impersonateBody!.Token);
        var impersonatedSettings = await Client.GetFromJsonAsync<SettingsDto>("/api/admin/settings");

        Assert.Equal(settings.Email, impersonatedSettings!.Email);
    }

    [Fact]
    public async Task ImpersonateCustomer_TokenWorksOnCustomerOnlyEndpoint()
    {
        var adminToken = await BootstrapAdmin();
        var (_, customerId) = await GetCustomerToken("+15550001234");

        Authorize(Client, adminToken);
        var impersonateResp = await Client.PostAsync($"/api/platform-admin/customers/{customerId}/impersonate", null);
        Assert.Equal(HttpStatusCode.OK, impersonateResp.StatusCode);
        var impersonateBody = await impersonateResp.Content.ReadFromJsonAsync<PlatformAdminImpersonateResponse>();

        Client.DefaultRequestHeaders.Authorization = null;
        Authorize(Client, impersonateBody!.Token);
        var resp = await Client.GetAsync("/api/customer/appointments");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task ImpersonateBusiness_IsRecordedInActivityLog()
    {
        var adminToken = await BootstrapAdmin();
        var businessToken = await RegisterAndLoginBusiness("logged-business@example.com", "logged-business-shop");
        Authorize(Client, businessToken);
        var settings = await Client.GetFromJsonAsync<SettingsDto>("/api/admin/settings");

        Authorize(Client, adminToken);
        await Client.PostAsync($"/api/platform-admin/businesses/{settings!.Id}/impersonate", null);

        var activity = await Client.GetFromJsonAsync<List<PlatformAdminActivityLogDto>>($"/api/platform-admin/businesses/{settings.Id}/activity");
        Assert.Contains(activity!, a => a.Action.Contains("ImpersonateBusiness") && a.Impersonated);
    }

    // ─── Generic activity logging ───────────────────────────────────────────

    [Fact]
    public async Task AuthenticatedWriteAction_IsRecordedInActivityLog()
    {
        var adminToken = await BootstrapAdmin();
        var businessToken = await RegisterAndLoginBusiness("active-business@example.com", "active-business-shop");
        Authorize(Client, businessToken);
        var settings = await Client.GetFromJsonAsync<SettingsDto>("/api/admin/settings");
        await Client.PatchAsJsonAsync("/api/admin/settings", new { name = "Updated Name" });

        Authorize(Client, adminToken);
        var activity = await Client.GetFromJsonAsync<List<PlatformAdminActivityLogDto>>($"/api/platform-admin/businesses/{settings!.Id}/activity");

        Assert.Contains(activity!, a => a.Action == "Admin.UpdateSettings" && !a.Impersonated);
    }

    [Fact]
    public async Task ImpersonatedWriteAction_IsRecordedAsImpersonatedInActivityLog()
    {
        var adminToken = await BootstrapAdmin();
        var businessToken = await RegisterAndLoginBusiness("impersonated-actor@example.com", "impersonated-actor-shop");
        Authorize(Client, businessToken);
        var settings = await Client.GetFromJsonAsync<SettingsDto>("/api/admin/settings");

        Authorize(Client, adminToken);
        var impersonateResp = await Client.PostAsync($"/api/platform-admin/businesses/{settings!.Id}/impersonate", null);
        var impersonateBody = await impersonateResp.Content.ReadFromJsonAsync<PlatformAdminImpersonateResponse>();

        Client.DefaultRequestHeaders.Authorization = null;
        Authorize(Client, impersonateBody!.Token);
        await Client.PatchAsJsonAsync("/api/admin/settings", new { name = "Changed While Impersonating" });

        Authorize(Client, adminToken);
        var activity = await Client.GetFromJsonAsync<List<PlatformAdminActivityLogDto>>($"/api/platform-admin/businesses/{settings.Id}/activity");

        Assert.Contains(activity!, a => a.Action == "Admin.UpdateSettings" && a.Impersonated);
    }
}
