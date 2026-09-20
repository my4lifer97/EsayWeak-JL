using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

public class BusinessOwnerRequestsTests : IntegrationTestBase
{
    // EnsureCreated() builds the test schema from the model, so the migration's seeded business
    // types don't exist here -- each test seeds its own and uses that id.
    private async Task<string> SeedBusinessType(string key = "barber")
    {
        using var db = Db();
        var type = new BusinessTypeDefinition { Key = key, DisplayNameEn = "Barber Shop", DisplayNameAr = "x", DisplayNameHe = "x" };
        db.BusinessTypeDefinitions.Add(type);
        await db.SaveChangesAsync();
        return type.Id;
    }

    // Seeds an already-verified OTP row directly (rather than going through the real
    // send-email-code endpoint, which is rate-limited) so tests that reuse the same email across
    // multiple ValidRequest calls -- reapplying after rejection, duplicate-pending checks, etc. --
    // aren't tripped up by the 45s cooldown a real repeat send would hit. SendEmailCode's own
    // behavior (rate limiting, real send, devCode) gets its own dedicated tests below.
    private async Task<string> SeedVerifiedEmailCode(string email)
    {
        const string code = "123456";
        using var db = Db();
        db.BusinessOwnerRequestEmailOtps.Add(new BusinessOwnerRequestEmailOtp
        {
            Email = email,
            CodeHash = BCrypt.Net.BCrypt.HashPassword(code),
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
        });
        await db.SaveChangesAsync();
        return code;
    }

    private async Task<CreateBusinessOwnerRequestRequest> ValidRequest(
        string typeId, string email = "prospect@example.com", string first = "Jamel", string family = "Marie")
    {
        var code = await SeedVerifiedEmailCode(email);
        return new("Prospect Barbershop", first, family, email, "+15551230000", typeId, "We cut hair", "Online booking", code);
    }

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
        var typeId = await SeedBusinessType();

        var resp = await Client.PostAsJsonAsync("/api/business-owner-requests", await ValidRequest(typeId));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    // ─── Email verification (send-email-code) ────────────────────────────────

    [Fact]
    public async Task SendEmailCode_Succeeds_AndSendsViaOwnerEmailSender()
    {
        var resp = await Client.PostAsJsonAsync("/api/business-owner-requests/send-email-code",
            new SendBusinessOwnerRequestEmailCodeRequest("verify-me@example.com"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<DevCodeResponse>();
        Assert.NotNull(body!.DevCode);
        Assert.Equal(6, body.DevCode!.Length);

        var sent = Assert.Single(Factory.OwnerEmail.Sent);
        Assert.Equal("verify-me@example.com", sent.Email);
        Assert.Contains(body.DevCode, sent.Body);
    }

    [Fact]
    public async Task SendEmailCode_RepeatWithinCooldown_ReturnsTooManyRequests()
    {
        await Client.PostAsJsonAsync("/api/business-owner-requests/send-email-code",
            new SendBusinessOwnerRequestEmailCodeRequest("cooldown@example.com"));

        var resp = await Client.PostAsJsonAsync("/api/business-owner-requests/send-email-code",
            new SendBusinessOwnerRequestEmailCodeRequest("cooldown@example.com"));

        Assert.Equal((HttpStatusCode)429, resp.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutVerifyingEmailFirst_ReturnsBadRequest()
    {
        var typeId = await SeedBusinessType();

        var resp = await Client.PostAsJsonAsync("/api/business-owner-requests",
            new CreateBusinessOwnerRequestRequest("Prospect Barbershop", "Jamel", "Marie", "unverified@example.com", "+15551230000", typeId, null, null, "000000"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_WithNoCodeAtAll_ReturnsBadRequestWithAClearMessage()
    {
        // Covers a stale frontend bundle (from before this verification step existed) still
        // submitting the old request shape -- Code is nullable specifically so this hits the
        // explicit check below and gets a real "error" field, not [ApiController]'s generic
        // automatic-validation response (which the frontend can't render a message from).
        var typeId = await SeedBusinessType();

        var resp = await Client.PostAsJsonAsync("/api/business-owner-requests",
            new CreateBusinessOwnerRequestRequest("Prospect Barbershop", "Jamel", "Marie", "no-code@example.com", "+15551230000", typeId, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.False(string.IsNullOrEmpty(body!.Error));
    }

    [Fact]
    public async Task Create_WrongCode_ReturnsBadRequest()
    {
        var typeId = await SeedBusinessType();
        var request = await ValidRequest(typeId, email: "wrong-code@example.com");

        var resp = await Client.PostAsJsonAsync("/api/business-owner-requests", request with { Code = "999999" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_MissingBusinessType_ReturnsBadRequest()
    {
        var resp = await Client.PostAsJsonAsync("/api/business-owner-requests", await ValidRequest(typeId: ""));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Theory]
    [InlineData("123")]      // too short
    [InlineData("1234567890123456")]  // too long (16 digits)
    public async Task Create_InvalidPhoneLength_ReturnsBadRequest(string phone)
    {
        var typeId = await SeedBusinessType();

        var resp = await Client.PostAsJsonAsync("/api/business-owner-requests",
            await ValidRequest(typeId) with { Phone = phone });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_NonEnglishName_ReturnsBadRequest()
    {
        var typeId = await SeedBusinessType();

        var resp = await Client.PostAsJsonAsync("/api/business-owner-requests",
            await ValidRequest(typeId, email: "arabic-name@example.com", first: "جمال", family: "Marie"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicatePending_ReturnsConflict()
    {
        var typeId = await SeedBusinessType();
        await Client.PostAsJsonAsync("/api/business-owner-requests", await ValidRequest(typeId, email: "dupe-pending@example.com"));

        var resp = await Client.PostAsJsonAsync("/api/business-owner-requests", await ValidRequest(typeId, email: "dupe-pending@example.com"));

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Create_EmailAlreadyHasRealAccount_ReturnsBadRequest()
    {
        var typeId = await SeedBusinessType();
        await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Existing Business", "existing@example.com", "password123", "existing-shop"));

        var resp = await Client.PostAsJsonAsync("/api/business-owner-requests", await ValidRequest(typeId, email: "existing@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Approve_GeneratesUsernameFromName_FirstNamePlusTwoFamilyLetters()
    {
        var typeId = await SeedBusinessType();
        var adminToken = await BootstrapAdmin();
        var createResp = await Client.PostAsJsonAsync("/api/business-owner-requests",
            await ValidRequest(typeId, email: "jamel@example.com", first: "Jamel", family: "Marie"));
        var created = await createResp.Content.ReadFromJsonAsync<JsonRequestId>();

        Authorize(Client, adminToken);
        var approveResp = await Client.PostAsJsonAsync(
            $"/api/platform-admin/business-owner-requests/{created!.Id}/approve",
            new ApproveBusinessOwnerRequestRequest("jamel-shop"));
        var approved = await approveResp.Content.ReadFromJsonAsync<ApproveBusinessOwnerRequestResponse>();

        Assert.Equal("jamelma", approved!.Username);
    }

    [Fact]
    public async Task Approve_DuplicateUsername_AppendsNumber()
    {
        var typeId = await SeedBusinessType();
        var adminToken = await BootstrapAdmin();

        async Task<string> ApproveOne(string email, string slug)
        {
            Client.DefaultRequestHeaders.Authorization = null;
            var c = await Client.PostAsJsonAsync("/api/business-owner-requests",
                await ValidRequest(typeId, email: email, first: "Jamel", family: "Marie"));
            var id = (await c.Content.ReadFromJsonAsync<JsonRequestId>())!.Id;
            Authorize(Client, adminToken);
            var a = await Client.PostAsJsonAsync($"/api/platform-admin/business-owner-requests/{id}/approve",
                new ApproveBusinessOwnerRequestRequest(slug));
            return (await a.Content.ReadFromJsonAsync<ApproveBusinessOwnerRequestResponse>())!.Username;
        }

        Assert.Equal("jamelma", await ApproveOne("j1@example.com", "j1-shop"));
        Assert.Equal("jamelma1", await ApproveOne("j2@example.com", "j2-shop"));
        Assert.Equal("jamelma2", await ApproveOne("j3@example.com", "j3-shop"));
    }

    [Fact]
    public async Task Approve_EmailsOwnerTheUsernameAndTempPassword()
    {
        var typeId = await SeedBusinessType();
        var adminToken = await BootstrapAdmin();
        var createResp = await Client.PostAsJsonAsync("/api/business-owner-requests",
            await ValidRequest(typeId, email: "mail-me@example.com"));
        var created = await createResp.Content.ReadFromJsonAsync<JsonRequestId>();

        Authorize(Client, adminToken);
        var approveResp = await Client.PostAsJsonAsync(
            $"/api/platform-admin/business-owner-requests/{created!.Id}/approve",
            new ApproveBusinessOwnerRequestRequest("mail-me-shop"));
        var approved = await approveResp.Content.ReadFromJsonAsync<ApproveBusinessOwnerRequestResponse>();

        Assert.True(approved!.EmailSent);
        var sent = Assert.Single(Factory.Email.Sent);
        Assert.Equal("mail-me@example.com", sent.Email);
        Assert.Contains(approved.TempPassword, sent.Body);
        Assert.Contains(approved.Username, sent.Body);
        Assert.Contains("/admin/login", sent.Body);
    }

    [Fact]
    public async Task Approve_Silent_DoesNotEmailOwner_ButStillReturnsCredentials()
    {
        // The owner-email composer (platform-admin panel) opens right after approval and decides
        // what -- if anything -- actually gets sent; the automatic approval email would otherwise
        // fire immediately and duplicate whatever the admin composes.
        var typeId = await SeedBusinessType();
        var adminToken = await BootstrapAdmin();
        var createResp = await Client.PostAsJsonAsync("/api/business-owner-requests",
            await ValidRequest(typeId, email: "silent-approve@example.com"));
        var created = await createResp.Content.ReadFromJsonAsync<JsonRequestId>();

        Authorize(Client, adminToken);
        var approveResp = await Client.PostAsJsonAsync(
            $"/api/platform-admin/business-owner-requests/{created!.Id}/approve",
            new ApproveBusinessOwnerRequestRequest("silent-approve-shop", true));
        var approved = await approveResp.Content.ReadFromJsonAsync<ApproveBusinessOwnerRequestResponse>();

        Assert.False(approved!.EmailSent);
        Assert.False(string.IsNullOrEmpty(approved.TempPassword));
        Assert.DoesNotContain(Factory.Email.Sent, e => e.Email == "silent-approve@example.com");
    }

    [Fact]
    public async Task ApproveThenLogin_WithUsername_ThenForcedPasswordChange()
    {
        var typeId = await SeedBusinessType();
        var adminToken = await BootstrapAdmin();
        var createResp = await Client.PostAsJsonAsync("/api/business-owner-requests",
            await ValidRequest(typeId, email: "approve-me@example.com", first: "Jamel", family: "Marie"));
        var created = await createResp.Content.ReadFromJsonAsync<JsonRequestId>();

        Authorize(Client, adminToken);
        var approveResp = await Client.PostAsJsonAsync(
            $"/api/platform-admin/business-owner-requests/{created!.Id}/approve",
            new ApproveBusinessOwnerRequestRequest("approve-me-shop"));
        Assert.Equal(HttpStatusCode.OK, approveResp.StatusCode);
        var approved = await approveResp.Content.ReadFromJsonAsync<ApproveBusinessOwnerRequestResponse>();

        // Log in with the generated username (not the email).
        Client.DefaultRequestHeaders.Authorization = null;
        var loginResp = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(approved!.Username, approved.TempPassword));
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.True(login!.MustChangePassword);

        Authorize(Client, login.Token);
        var blockedResp = await Client.GetAsync("/api/admin/settings");
        Assert.Equal(HttpStatusCode.Forbidden, blockedResp.StatusCode);

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
    public async Task AdminList_DefaultsToAllStatuses_AndDetailShowsFullSubmission()
    {
        var typeId = await SeedBusinessType();
        var adminToken = await BootstrapAdmin();
        var createResp = await Client.PostAsJsonAsync("/api/business-owner-requests",
            await ValidRequest(typeId, email: "detail@example.com", first: "Jamel", family: "Marie"));
        var created = await createResp.Content.ReadFromJsonAsync<JsonRequestId>();

        Authorize(Client, adminToken);
        var list = await Client.GetFromJsonAsync<List<BusinessOwnerRequestDto>>("/api/platform-admin/business-owner-requests");
        Assert.Contains(list!, r => r.Id == created!.Id && r.Status == "Pending");

        var detail = await Client.GetFromJsonAsync<BusinessOwnerRequestDto>(
            $"/api/platform-admin/business-owner-requests/{created!.Id}");
        Assert.Equal("Jamel", detail!.OwnerFirstName);
        Assert.Equal("Marie", detail.OwnerFamilyName);
        Assert.Equal("We cut hair", detail.BusinessDescription);
        Assert.Equal("Online booking", detail.SystemNeeds);
        Assert.Equal("Barber Shop", detail.BusinessTypeName);
    }

    [Fact]
    public async Task RejectThenReapply_NewRequestIsAccepted()
    {
        var typeId = await SeedBusinessType();
        var adminToken = await BootstrapAdmin();
        var createResp = await Client.PostAsJsonAsync("/api/business-owner-requests", await ValidRequest(typeId, email: "reject-then-reapply@example.com"));
        var created = await createResp.Content.ReadFromJsonAsync<JsonRequestId>();

        Authorize(Client, adminToken);
        var rejectResp = await Client.PostAsJsonAsync(
            $"/api/platform-admin/business-owner-requests/{created!.Id}/reject",
            new RejectBusinessOwnerRequestRequest("Incomplete information"));
        Assert.Equal(HttpStatusCode.OK, rejectResp.StatusCode);

        Client.DefaultRequestHeaders.Authorization = null;
        var reapplyResp = await Client.PostAsJsonAsync("/api/business-owner-requests", await ValidRequest(typeId, email: "reject-then-reapply@example.com"));

        Assert.Equal(HttpStatusCode.Created, reapplyResp.StatusCode);
    }

    private record JsonRequestId(string Id);
    private record DevCodeResponse(string? DevCode);
    private record ErrorResponse(string Error);
}
