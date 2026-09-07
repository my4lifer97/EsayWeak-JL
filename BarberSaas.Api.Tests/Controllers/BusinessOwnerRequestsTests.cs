using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

public class BusinessOwnerRequestsTests : IntegrationTestBase
{
    private static CreateBusinessOwnerRequestRequest ValidRequest(string email = "prospect@example.com") =>
        new("Prospect Barbershop", "Prospect Owner", email, "+15551230000", BusinessTypeId: null);

    private async Task<string> BootstrapAdmin(string email = "owner@example.com")
    {
        var resp = await Client.PostAsJsonAsync("/api/platform-admin/bootstrap",
            new PlatformAdminBootstrapRequest(email, "supersecret123", "Owner"));
        var body = await resp.Content.ReadFromJsonAsync<PlatformAdminLoginResponse>();
        return body!.Token;
    }

    [Fact]
    public async Task Create_ValidRequest_Succeeds()
    {
        var resp = await Client.PostAsJsonAsync("/api/business-owner-requests", ValidRequest());

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicatePending_ReturnsConflict()
    {
        await Client.PostAsJsonAsync("/api/business-owner-requests", ValidRequest(email: "dupe-pending@example.com"));

        var resp = await Client.PostAsJsonAsync("/api/business-owner-requests", ValidRequest(email: "dupe-pending@example.com"));

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Create_EmailAlreadyHasRealAccount_ReturnsBadRequest()
    {
        await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Existing Business", "existing@example.com", "password123", "existing-shop"));

        var resp = await Client.PostAsJsonAsync("/api/business-owner-requests", ValidRequest(email: "existing@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ApproveThenLogin_ReturnsTempPasswordAndBlocksUntilPasswordChanged()
    {
        var adminToken = await BootstrapAdmin();
        var createResp = await Client.PostAsJsonAsync("/api/business-owner-requests", ValidRequest(email: "approve-me@example.com"));
        var created = await createResp.Content.ReadFromJsonAsync<JsonRequestId>();

        Authorize(Client, adminToken);
        var approveResp = await Client.PostAsJsonAsync(
            $"/api/platform-admin/business-owner-requests/{created!.Id}/approve",
            new ApproveBusinessOwnerRequestRequest("approve-me-shop"));
        Assert.Equal(HttpStatusCode.OK, approveResp.StatusCode);
        var approved = await approveResp.Content.ReadFromJsonAsync<ApproveBusinessOwnerRequestResponse>();
        Assert.False(string.IsNullOrWhiteSpace(approved!.TempPassword));

        Client.DefaultRequestHeaders.Authorization = null;
        var loginResp = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("approve-me@example.com", approved.TempPassword));
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.True(login!.MustChangePassword);

        Authorize(Client, login.Token);
        var blockedResp = await Client.GetAsync("/api/admin/settings");
        Assert.Equal(HttpStatusCode.Forbidden, blockedResp.StatusCode);
        var blockedBody = await blockedResp.Content.ReadFromJsonAsync<MustChangePasswordError>();
        Assert.True(blockedBody!.MustChangePassword);

        var changeResp = await Client.PatchAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("brandnewpassword456"));
        Assert.Equal(HttpStatusCode.OK, changeResp.StatusCode);
        var changed = await changeResp.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(changed!.MustChangePassword);

        Client.DefaultRequestHeaders.Authorization = null;
        Authorize(Client, changed.Token);
        var settingsResp = await Client.GetAsync("/api/admin/settings");
        Assert.Equal(HttpStatusCode.OK, settingsResp.StatusCode);
    }

    [Fact]
    public async Task RejectThenReapply_NewRequestIsAccepted()
    {
        var adminToken = await BootstrapAdmin();
        var createResp = await Client.PostAsJsonAsync("/api/business-owner-requests", ValidRequest(email: "reject-then-reapply@example.com"));
        var created = await createResp.Content.ReadFromJsonAsync<JsonRequestId>();

        Authorize(Client, adminToken);
        var rejectResp = await Client.PostAsJsonAsync(
            $"/api/platform-admin/business-owner-requests/{created!.Id}/reject",
            new RejectBusinessOwnerRequestRequest("Incomplete information"));
        Assert.Equal(HttpStatusCode.OK, rejectResp.StatusCode);

        Client.DefaultRequestHeaders.Authorization = null;
        var reapplyResp = await Client.PostAsJsonAsync("/api/business-owner-requests", ValidRequest(email: "reject-then-reapply@example.com"));

        Assert.Equal(HttpStatusCode.Created, reapplyResp.StatusCode);
    }

    private record JsonRequestId(string Id);
    private record MustChangePasswordError(string Error, bool MustChangePassword);
}
