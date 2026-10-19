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
        var results = await Client.GetFromJsonAsync<PagedResult<PlatformAdminCustomerSummaryDto>>("/api/platform-admin/customers?search=Waitlist%20Customer");

        Assert.Contains(results!.Items, c => c.Phone == "+15559991111");
    }

    // QA run 2: search was case-sensitive, and the lists stopped silently at 50.
    [Fact]
    public async Task Search_IgnoresCase_AndMatchesLocalPhoneFormat()
    {
        var adminToken = await BootstrapAdmin();
        await GetCustomerToken("+972501239876", "Noa", "Mizrahi");
        await RegisterAndLoginBusiness("CaseShop@example.com", "case-shop-pa");

        Authorize(Client, adminToken);
        var byName = await Client.GetFromJsonAsync<PagedResult<PlatformAdminCustomerSummaryDto>>("/api/platform-admin/customers?search=noa%20MIZRAHI");
        var byLocalPhone = await Client.GetFromJsonAsync<PagedResult<PlatformAdminCustomerSummaryDto>>("/api/platform-admin/customers?search=050-123-9876");
        var business = await Client.GetFromJsonAsync<PagedResult<PlatformAdminBusinessSummaryDto>>("/api/platform-admin/businesses?search=CASE-SHOP");

        Assert.Contains(byName!.Items, c => c.Phone == "+972501239876");
        Assert.Contains(byLocalPhone!.Items, c => c.Phone == "+972501239876");
        Assert.Contains(business!.Items, b => b.Slug == "case-shop-pa");
    }

    [Fact]
    public async Task SearchBusinesses_Pages_WithTotalAndHasMore()
    {
        var adminToken = await BootstrapAdmin();
        for (var i = 0; i < 3; i++)
            await RegisterAndLoginBusiness($"pager{i}@example.com", $"pager-shop-{i}");

        Authorize(Client, adminToken);
        var first = await Client.GetFromJsonAsync<PagedResult<PlatformAdminBusinessSummaryDto>>("/api/platform-admin/businesses?search=pager-shop&pageSize=2");
        var second = await Client.GetFromJsonAsync<PagedResult<PlatformAdminBusinessSummaryDto>>("/api/platform-admin/businesses?search=pager-shop&pageSize=2&page=2");

        Assert.Equal(3, first!.Total);
        Assert.Equal(2, first.Items.Count);
        Assert.True(first.HasMore);
        Assert.Single(second!.Items);
        Assert.False(second.HasMore);
        Assert.Empty(first.Items.Select(b => b.Id).Intersect(second.Items.Select(b => b.Id)));
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
        var searchResp = await Client.GetFromJsonAsync<PagedResult<PlatformAdminBusinessSummaryDto>>("/api/platform-admin/businesses?search=target-business");
        var found = Assert.Single(searchResp!.Items);
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
        Assert.Contains(activity!, a => a.Action.Contains("ImpersonateBusiness") && a.ByPlatformAdmin && !a.Impersonated);
    }

    [Fact]
    public async Task AdminOwnAction_IsLabelledByPlatformAdmin_NotImpersonation()
    {
        var adminToken = await BootstrapAdmin();
        var businessToken = await RegisterAndLoginBusiness("admin-action@example.com", "admin-action-shop");
        Authorize(Client, businessToken);
        var settings = await Client.GetFromJsonAsync<SettingsDto>("/api/admin/settings");

        Authorize(Client, adminToken);
        await Client.PostAsync($"/api/platform-admin/businesses/{settings!.Id}/disable", null);

        var activity = await Client.GetFromJsonAsync<List<PlatformAdminActivityLogDto>>($"/api/platform-admin/businesses/{settings.Id}/activity");
        var row = Assert.Single(activity!, a => a.Action.Contains("DisableBusiness"));
        Assert.True(row.ByPlatformAdmin);
        Assert.False(row.Impersonated);
    }

    // ─── WhatsApp linking ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateWhatsAppLinkToken_ReturnsAShareableUrl()
    {
        var adminToken = await BootstrapAdmin();
        var businessToken = await RegisterAndLoginBusiness("wa-link-business@example.com", "wa-link-business-shop");
        Authorize(Client, businessToken);
        var settings = await Client.GetFromJsonAsync<SettingsDto>("/api/admin/settings");

        Authorize(Client, adminToken);
        var resp = await Client.PostAsync($"/api/platform-admin/businesses/{settings!.Id}/whatsapp/link-token", null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Contains("/wa-link/", body!["url"]);
        Assert.False(string.IsNullOrWhiteSpace(body["token"]));
    }

    [Fact]
    public async Task WaLinkController_WithValidToken_ReturnsLiveStatusWithNoAuthNeeded()
    {
        var adminToken = await BootstrapAdmin();
        var businessToken = await RegisterAndLoginBusiness("wa-link-scan@example.com", "wa-link-scan-shop");
        Authorize(Client, businessToken);
        var settings = await Client.GetFromJsonAsync<SettingsDto>("/api/admin/settings");

        Authorize(Client, adminToken);
        var createResp = await Client.PostAsync($"/api/platform-admin/businesses/{settings!.Id}/whatsapp/link-token", null);
        var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, string>>();

        Client.DefaultRequestHeaders.Authorization = null; // the owner's browser has no session at all
        var resp = await Client.GetAsync($"/api/wa-link/{created!["token"]}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(settings.Name, body.GetProperty("businessName").GetString());
        Assert.Equal("qr", body.GetProperty("state").GetString());
    }

    [Fact]
    public async Task WaLinkController_WithUnknownToken_ReturnsNotFound()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var resp = await Client.GetAsync("/api/wa-link/not-a-real-token");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task WaLinkController_WhenBridgeReportsConnected_WritesWhatsAppNumberAndLogsIt()
    {
        var adminToken = await BootstrapAdmin();
        var businessToken = await RegisterAndLoginBusiness("wa-link-connected@example.com", "wa-link-connected-shop");
        Authorize(Client, businessToken);
        var settings = await Client.GetFromJsonAsync<SettingsDto>("/api/admin/settings");

        Authorize(Client, adminToken);
        var createResp = await Client.PostAsync($"/api/platform-admin/businesses/{settings!.Id}/whatsapp/link-token", null);
        var created = await createResp.Content.ReadFromJsonAsync<Dictionary<string, string>>();

        Factory.WhatsAppBridge.StatusToReturn = new("connected", null, "+15551234567");
        Client.DefaultRequestHeaders.Authorization = null;
        var resp = await Client.GetAsync($"/api/wa-link/{created!["token"]}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var db = Db();
        var business = db.Businesses.Single(b => b.Id == settings.Id);
        Assert.Equal("+15551234567", business.WhatsAppNumber);
        var log = db.ActivityLogs.Single(a => a.BusinessId == settings.Id && a.Action == "WhatsAppLink.Connected");
        Assert.NotNull(log.ImpersonatedByPlatformAdminId);
    }

    [Fact]
    public async Task StartWhatsAppLink_BridgeDown_Returns503WithAReadableMessage()
    {
        var adminToken = await BootstrapAdmin();
        var businessToken = await RegisterAndLoginBusiness("wa-bridge-down@example.com", "wa-bridge-down-shop");
        Authorize(Client, businessToken);
        var settings = await Client.GetFromJsonAsync<SettingsDto>("/api/admin/settings");

        Authorize(Client, adminToken);
        Factory.WhatsAppBridge.Unreachable = true;
        try
        {
            var resp = await Client.PostAsync($"/api/platform-admin/businesses/{settings!.Id}/whatsapp/link", null);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            Assert.Contains("WhatsApp service is unreachable", body!["error"]);
        }
        finally { Factory.WhatsAppBridge.Unreachable = false; }
    }

    [Fact]
    public async Task UnlinkWhatsApp_BridgeDown_StillClearsTheNumber()
    {
        var adminToken = await BootstrapAdmin();
        var businessToken = await RegisterAndLoginBusiness("wa-unlink-down@example.com", "wa-unlink-down-shop");
        Authorize(Client, businessToken);
        var settings = await Client.GetFromJsonAsync<SettingsDto>("/api/admin/settings");
        using (var db = Db())
        {
            db.Businesses.Single(b => b.Id == settings!.Id).WhatsAppNumber = "+15550007777";
            db.SaveChanges();
        }

        Authorize(Client, adminToken);
        Factory.WhatsAppBridge.Unreachable = true;
        try
        {
            var resp = await Client.DeleteAsync($"/api/platform-admin/businesses/{settings!.Id}/whatsapp/link");

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.True(body.GetProperty("bridgeUnreachable").GetBoolean());
            using var db = Db();
            Assert.Null(db.Businesses.Single(b => b.Id == settings.Id).WhatsAppNumber);
        }
        finally { Factory.WhatsAppBridge.Unreachable = false; }
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

    // ─── Account disable/enable + password reset ────────────────────────────

    private async Task<(string Id, string Email)> RegisterAndVerifyBusiness(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var body = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        return (body!.Id, email);
    }

    [Fact]
    public async Task DisableBusiness_BlocksLoginWithAccountDisabled()
    {
        var adminToken = await BootstrapAdmin();
        var (businessId, email) = await RegisterAndVerifyBusiness("disable-me@example.com", "disable-me-shop");

        Authorize(Client, adminToken);
        var disableResp = await Client.PostAsync($"/api/platform-admin/businesses/{businessId}/disable", null);
        Assert.Equal(HttpStatusCode.OK, disableResp.StatusCode);

        Client.DefaultRequestHeaders.Authorization = null;
        var loginResp = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "password123"));

        Assert.Equal(HttpStatusCode.Forbidden, loginResp.StatusCode);
        var body = await loginResp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.True(body!["accountDisabled"] is System.Text.Json.JsonElement je && je.GetBoolean());
    }

    [Fact]
    public async Task EnableBusiness_RestoresLogin()
    {
        var adminToken = await BootstrapAdmin();
        var (businessId, email) = await RegisterAndVerifyBusiness("re-enable-me@example.com", "re-enable-me-shop");

        Authorize(Client, adminToken);
        await Client.PostAsync($"/api/platform-admin/businesses/{businessId}/disable", null);
        var enableResp = await Client.PostAsync($"/api/platform-admin/businesses/{businessId}/enable", null);
        Assert.Equal(HttpStatusCode.OK, enableResp.StatusCode);

        Client.DefaultRequestHeaders.Authorization = null;
        var loginResp = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "password123"));

        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
    }

    private record DevCodeResponse(string? DevCode);

    private static async Task AssertAccountDisabled(HttpResponseMessage resp)
    {
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.True(body!["accountDisabled"] is System.Text.Json.JsonElement je && je.GetBoolean());
    }

    // A business JWT lives 30 days -- disabling must also cut off a session that's already open.
    [Fact]
    public async Task DisableBusiness_BlocksAnAlreadyLoggedInOwner_UntilReEnabled()
    {
        var adminToken = await BootstrapAdmin();
        var ownerToken = await RegisterAndLoginBusiness("open-session@example.com", "open-session-shop");
        Authorize(Client, ownerToken);
        var businessId = (await Client.GetFromJsonAsync<Dictionary<string, object>>("/api/admin/settings"))!["id"].ToString();

        Authorize(Client, adminToken);
        await Client.PostAsync($"/api/platform-admin/businesses/{businessId}/disable", null);
        Authorize(Client, ownerToken);
        await AssertAccountDisabled(await Client.GetAsync("/api/admin/settings"));
        await AssertAccountDisabled(await Client.PatchAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("whatever", "newpassword123")));

        Authorize(Client, adminToken);
        await Client.PostAsync($"/api/platform-admin/businesses/{businessId}/enable", null);
        Authorize(Client, ownerToken);
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/admin/settings")).StatusCode);
    }

    [Fact]
    public async Task DisableBusiness_ForgotPasswordSendsNoCode()
    {
        var adminToken = await BootstrapAdmin();
        var (businessId, email) = await RegisterAndVerifyBusiness("disabled-forgot@example.com", "disabled-forgot-shop");
        Authorize(Client, adminToken);
        await Client.PostAsync($"/api/platform-admin/businesses/{businessId}/disable", null);
        Client.DefaultRequestHeaders.Authorization = null;

        // Same silent 200 as an unknown email (forgot-password never reveals account state).
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email))).StatusCode);
        using var db = Db();
        Assert.False(db.BusinessPasswordResetOtps.Any(o => o.Email == email));
    }

    // A reset code requested before the disable must not turn into a fresh login afterwards.
    [Fact]
    public async Task DisableBusiness_ResetCodeIssuedBeforehand_IsRejected()
    {
        var adminToken = await BootstrapAdmin();
        var (businessId, email) = await RegisterAndVerifyBusiness("disabled-reset@example.com", "disabled-reset-shop");
        var forgot = await Client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        var code = (await forgot.Content.ReadFromJsonAsync<DevCodeResponse>())!.DevCode!;

        Authorize(Client, adminToken);
        await Client.PostAsync($"/api/platform-admin/businesses/{businessId}/disable", null);
        Client.DefaultRequestHeaders.Authorization = null;

        await AssertAccountDisabled(await Client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(email, code, "brandnew123")));
        using var db = Db();
        Assert.True(BCrypt.Net.BCrypt.Verify("password123", db.Businesses.Single(b => b.Id == businessId).PasswordHash));
    }

    // Support can still look inside a disabled account.
    [Fact]
    public async Task DisableBusiness_ImpersonationStillWorks()
    {
        var adminToken = await BootstrapAdmin();
        var (businessId, _) = await RegisterAndVerifyBusiness("disabled-impersonate@example.com", "disabled-impersonate-shop");
        Authorize(Client, adminToken);
        await Client.PostAsync($"/api/platform-admin/businesses/{businessId}/disable", null);

        var impersonate = await Client.PostAsync($"/api/platform-admin/businesses/{businessId}/impersonate", null);
        Authorize(Client, (await impersonate.Content.ReadFromJsonAsync<PlatformAdminImpersonateResponse>())!.Token);

        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/admin/settings")).StatusCode);
    }

    [Fact]
    public async Task OwnerRoutes_RejectAPlatformAdminToken()
    {
        Authorize(Client, await BootstrapAdmin());

        var resp = await Client.GetAsync("/api/admin/settings");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_Temporary_ChangesPasswordAndForcesChange()
    {
        var adminToken = await BootstrapAdmin();
        var (businessId, email) = await RegisterAndVerifyBusiness("temp-reset@example.com", "temp-reset-shop");

        Authorize(Client, adminToken);
        var resetResp = await Client.PostAsJsonAsync($"/api/platform-admin/businesses/{businessId}/reset-password",
            new PlatformAdminResetPasswordRequest(true, null));
        Assert.Equal(HttpStatusCode.OK, resetResp.StatusCode);
        var resetBody = await resetResp.Content.ReadFromJsonAsync<PlatformAdminResetPasswordResponse>();
        Assert.False(string.IsNullOrEmpty(resetBody!.TempPassword));

        Client.DefaultRequestHeaders.Authorization = null;
        var oldLoginResp = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "password123"));
        Assert.Equal(HttpStatusCode.Unauthorized, oldLoginResp.StatusCode);

        var newLoginResp = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, resetBody.TempPassword!));
        Assert.Equal(HttpStatusCode.OK, newLoginResp.StatusCode);
        var newLoginBody = await newLoginResp.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.True(newLoginBody!.MustChangePassword);
    }

    [Fact]
    public async Task ResetPassword_Custom_SetsExactPasswordWithoutForcingChange()
    {
        var adminToken = await BootstrapAdmin();
        var (businessId, email) = await RegisterAndVerifyBusiness("custom-reset@example.com", "custom-reset-shop");

        Authorize(Client, adminToken);
        var resetResp = await Client.PostAsJsonAsync($"/api/platform-admin/businesses/{businessId}/reset-password",
            new PlatformAdminResetPasswordRequest(false, "brandNewPass1"));
        Assert.Equal(HttpStatusCode.OK, resetResp.StatusCode);

        Client.DefaultRequestHeaders.Authorization = null;
        var loginResp = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "brandNewPass1"));

        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var loginBody = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(loginBody!.MustChangePassword);
    }

    [Fact]
    public async Task ResetPassword_Silent_DoesNotSendTheSystemEmail()
    {
        // Same reasoning as Approve's Silent flag -- the owner-email composer decides what (if
        // anything) gets sent, so the automatic reset email must not fire in parallel.
        var adminToken = await BootstrapAdmin();
        var (businessId, email) = await RegisterAndVerifyBusiness("silent-reset@example.com", "silent-reset-shop");
        // RegisterAndVerifyBusiness itself sends a verification-code email to this address --
        // capture that baseline so the assertion below checks for a *new* reset email, not just
        // any email ever sent to it.
        var sentBefore = Factory.Email.Sent.Count(e => e.Email == email);

        Authorize(Client, adminToken);
        var resetResp = await Client.PostAsJsonAsync($"/api/platform-admin/businesses/{businessId}/reset-password",
            new PlatformAdminResetPasswordRequest(true, null, true));
        var resetBody = await resetResp.Content.ReadFromJsonAsync<PlatformAdminResetPasswordResponse>();

        Assert.False(resetBody!.EmailSent);
        Assert.False(string.IsNullOrEmpty(resetBody.TempPassword));
        Assert.Equal(sentBefore, Factory.Email.Sent.Count(e => e.Email == email));
    }

    // ─── Owner-email composer ────────────────────────────────────────────────

    [Fact]
    public async Task EmailOwner_SendsViaOwnerEmailSender_AndLogsIt()
    {
        var adminToken = await BootstrapAdmin();
        var (businessId, email) = await RegisterAndVerifyBusiness("compose-email@example.com", "compose-email-shop");

        Authorize(Client, adminToken);
        var resp = await Client.PostAsJsonAsync($"/api/platform-admin/businesses/{businessId}/email",
            new EmailOwnerRequest("Your account", "Username: composeemail\nPassword: abc123"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var sent = Assert.Single(Factory.OwnerEmail.Sent);
        Assert.Equal(email, sent.Email);
        Assert.Equal("Your account", sent.Subject);
        Assert.Contains("abc123", sent.Body);

        var activity = await Client.GetFromJsonAsync<List<PlatformAdminActivityLogDto>>($"/api/platform-admin/businesses/{businessId}/activity");
        Assert.Contains(activity!, a => a.Action == "PlatformAdminController.EmailOwner");
    }

    [Fact]
    public async Task EmailOwner_MissingSubjectOrBody_ReturnsBadRequest()
    {
        var adminToken = await BootstrapAdmin();
        var (businessId, _) = await RegisterAndVerifyBusiness("compose-blank@example.com", "compose-blank-shop");

        Authorize(Client, adminToken);
        var resp = await Client.PostAsJsonAsync($"/api/platform-admin/businesses/{businessId}/email",
            new EmailOwnerRequest("", "some body"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(Factory.OwnerEmail.Sent);
    }

    // ─── Composer temp password: applied only once it's actually emailed ────────

    private record TempPasswordResponse(string TempPassword);

    private async Task<(string BusinessId, string TempPassword)> AdminWithGeneratedTempPassword(string email, string slug)
    {
        var adminToken = await BootstrapAdmin();
        var (businessId, _) = await RegisterAndVerifyBusiness(email, slug);
        Authorize(Client, adminToken);
        var resp = await Client.PostAsync($"/api/platform-admin/businesses/{businessId}/generate-temp-password", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (businessId, (await resp.Content.ReadFromJsonAsync<TempPasswordResponse>())!.TempPassword);
    }

    private bool PasswordIs(string businessId, string password)
    {
        using var db = Db();
        return BCrypt.Net.BCrypt.Verify(password, db.Businesses.Single(b => b.Id == businessId).PasswordHash);
    }

    [Fact]
    public async Task GenerateTempPassword_DoesNotTouchTheAccount()
    {
        var (businessId, temp) = await AdminWithGeneratedTempPassword("gen-only@example.com", "gen-only-shop");

        Assert.True(PasswordIs(businessId, "password123"));
        Assert.False(PasswordIs(businessId, temp));
    }

    [Fact]
    public async Task EmailOwner_WithTempPassword_AppliesItOnlyAfterSending()
    {
        var (businessId, temp) = await AdminWithGeneratedTempPassword("gen-send@example.com", "gen-send-shop");

        var resp = await Client.PostAsJsonAsync($"/api/platform-admin/businesses/{businessId}/email",
            new EmailOwnerRequest("Your account", $"Password: {temp}", temp));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Single(Factory.OwnerEmail.Sent);
        Assert.True(PasswordIs(businessId, temp));
        using var db = Db();
        Assert.True(db.Businesses.Single(b => b.Id == businessId).MustChangePassword);
    }

    // The QA bug: Gmail failing used to leave the owner with a password nobody received.
    [Fact]
    public async Task EmailOwner_WithTempPassword_SendFails_KeepsTheOldPassword()
    {
        var (businessId, temp) = await AdminWithGeneratedTempPassword("gen-fail@example.com", "gen-fail-shop");
        Factory.OwnerEmail.SendError = "invalid_grant";

        var resp = await Client.PostAsJsonAsync($"/api/platform-admin/businesses/{businessId}/email",
            new EmailOwnerRequest("Your account", $"Password: {temp}", temp));

        Assert.Equal(HttpStatusCode.BadGateway, resp.StatusCode);
        Assert.Contains("NOT changed", await resp.Content.ReadAsStringAsync());
        Assert.True(PasswordIs(businessId, "password123"));
    }

    [Fact]
    public async Task EmailOwner_TempPasswordMissingFromBody_IsRejectedAndNothingSent()
    {
        var (businessId, temp) = await AdminWithGeneratedTempPassword("gen-missing@example.com", "gen-missing-shop");

        var resp = await Client.PostAsJsonAsync($"/api/platform-admin/businesses/{businessId}/email",
            new EmailOwnerRequest("Your account", "Username only", temp));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(Factory.OwnerEmail.Sent);
        Assert.True(PasswordIs(businessId, "password123"));
    }

    // "Set password now" -- for copying it or reading it out on a call.
    [Fact]
    public async Task ResetPassword_TemporaryWithGivenPassword_SetsExactlyThatOne()
    {
        var (businessId, temp) = await AdminWithGeneratedTempPassword("gen-setnow@example.com", "gen-setnow-shop");

        var resp = await Client.PostAsJsonAsync($"/api/platform-admin/businesses/{businessId}/reset-password",
            new PlatformAdminResetPasswordRequest(true, temp, Silent: true));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(PasswordIs(businessId, temp));
        Assert.DoesNotContain(Factory.Email.Sent, e => e.Subject.Contains("password was reset"));
    }

    [Fact]
    public async Task ResetPassword_ActivityLog_NeverContainsTheRawPassword()
    {
        var adminToken = await BootstrapAdmin();
        var (businessId, _) = await RegisterAndVerifyBusiness("logged-reset@example.com", "logged-reset-shop");

        Authorize(Client, adminToken);
        await Client.PostAsJsonAsync($"/api/platform-admin/businesses/{businessId}/reset-password",
            new PlatformAdminResetPasswordRequest(false, "shouldNeverAppearInLogs1"));

        var activity = await Client.GetFromJsonAsync<List<PlatformAdminActivityLogDto>>($"/api/platform-admin/businesses/{businessId}/activity");

        Assert.Contains(activity!, a => a.Action.Contains("ResetBusinessPassword"));
        Assert.DoesNotContain(activity!, a => a.Description.Contains("shouldNeverAppearInLogs1"));
    }

    // QA run 3 PA-2: the reset email went through the system chain (Brevo), not the owner Gmail sender.
    [Fact]
    public async Task ResetPassword_EmailsThroughTheOwnerGmailSender()
    {
        var adminToken = await BootstrapAdmin();
        var (businessId, email) = await RegisterAndVerifyBusiness("gmail-reset@example.com", "gmail-reset-shop");

        Authorize(Client, adminToken);
        var resetResp = await Client.PostAsJsonAsync($"/api/platform-admin/businesses/{businessId}/reset-password",
            new PlatformAdminResetPasswordRequest(true, null));
        var resetBody = await resetResp.Content.ReadFromJsonAsync<PlatformAdminResetPasswordResponse>();

        Assert.True(resetBody!.EmailSent);
        var sent = Assert.Single(Factory.OwnerEmail.Sent, e => e.Email == email);
        Assert.Contains(resetBody.TempPassword!, sent.Body);
    }

    // QA run 3 PA-3: six spaces worked as a temporary password.
    [Fact]
    public async Task ResetPassword_WhitespaceTemporaryPassword_IsRejected()
    {
        var adminToken = await BootstrapAdmin();
        var (businessId, _) = await RegisterAndVerifyBusiness("pa-spaces@example.com", "pa-spaces-shop");
        Authorize(Client, adminToken);

        var resp = await Client.PostAsJsonAsync($"/api/platform-admin/businesses/{businessId}/reset-password",
            new PlatformAdminResetPasswordRequest(true, "      ", true));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // QA run 3 PA-5/6: a repeated disable wrote a second "enabled → disabled" row; admin rows had no device.
    [Fact]
    public async Task DisableTwice_LogsOnce_WithTheDevice()
    {
        var adminToken = await BootstrapAdmin();
        var (businessId, _) = await RegisterAndVerifyBusiness("pa-twice@example.com", "pa-twice-shop");
        Authorize(Client, adminToken);
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("QaBrowser/1.0");

        await Client.PostAsync($"/api/platform-admin/businesses/{businessId}/disable", null);
        await Client.PostAsync($"/api/platform-admin/businesses/{businessId}/disable", null);

        using var db = Db();
        var rows = db.ActivityLogs.Where(l => l.BusinessId == businessId && l.Action.EndsWith("DisableBusiness")).ToList();
        var row = Assert.Single(rows);
        Assert.Contains("QaBrowser", row.UserAgent);
    }

    // QA run 3 PA-9: owners sign in with a generated username, which search didn't match.
    [Fact]
    public async Task BusinessSearch_MatchesTheUsername()
    {
        var adminToken = await BootstrapAdmin();
        var (businessId, _) = await RegisterAndVerifyBusiness("pa-user@example.com", "pa-user-shop");
        using (var db = Db())
        {
            db.Businesses.First(b => b.Id == businessId).Username = "danaco";
            db.SaveChanges();
        }
        Authorize(Client, adminToken);

        var page = await Client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/platform-admin/businesses?search=DanaCo");

        Assert.Equal(1, page.GetProperty("total").GetInt32());
    }
}
