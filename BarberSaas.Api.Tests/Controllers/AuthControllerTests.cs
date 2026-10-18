using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BarberSaas.Api.DTOs;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

public class AuthControllerTests : IntegrationTestBase
{
    private record RegisterResponse(string Id, string Name, string Email, string Slug, string? DevCode);
    private record ErrorResponse(string Error);

    private static RegisterRequest ValidRegister(string email = "business@example.com", string slug = "my-business-shop") =>
        new("Business Name", email, "password123", slug);

    private async Task<string> RegisterAndGetDevCode(string email, string slug)
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/register", ValidRegister(email, slug));
        var body = await resp.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.NotNull(body?.DevCode);
        return body!.DevCode!;
    }

    [Theory]
    [InlineData("A", "a@example.com", "password123", "valid-slug")] // name too short
    [InlineData("Valid Name", "not-an-email", "password123", "valid-slug")] // invalid email
    [InlineData("Valid Name", "a@example.com", "short", "valid-slug")] // password too short
    [InlineData("Valid Name", "a@example.com", "password123", "AB")] // slug too short / uppercase
    [InlineData("Valid Name", "a@example.com", "password123", "has spaces")] // slug invalid chars
    public async Task Register_WithInvalidInput_ReturnsBadRequest(string name, string email, string password, string slug)
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(name, email, password, slug));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("browse")]
    [InlineData("account")]
    [InlineData("cron")]
    public async Task Register_WithReservedSlug_ReturnsBadRequest(string slug)
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/register", ValidRegister(slug: slug));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Register_WithValidInput_Succeeds()
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/register", ValidRegister());

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsBadRequest()
    {
        await Client.PostAsJsonAsync("/api/auth/register", ValidRegister(email: "dupe@example.com", slug: "shop-one"));

        var resp = await Client.PostAsJsonAsync("/api/auth/register", ValidRegister(email: "dupe@example.com", slug: "shop-two"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateSlug_ReturnsBadRequest()
    {
        await Client.PostAsJsonAsync("/api/auth/register", ValidRegister(email: "one@example.com", slug: "same-slug"));

        var resp = await Client.PostAsJsonAsync("/api/auth/register", ValidRegister(email: "two@example.com", slug: "same-slug"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Login_WithCorrectCredentials_ReturnsToken()
    {
        var code = await RegisterAndGetDevCode("login@example.com", "login-shop");
        await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest("login@example.com", code));

        var resp = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("login@example.com", "password123"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body?.Token));
    }

    [Fact]
    public async Task Login_RecordsAnActivityLogEntry_WithIpAndUserAgent()
    {
        // Login is anonymous (no JWT on the request yet), so the generic per-request
        // ActivityLogFilter -- which reads identity off the incoming request -- never sees it.
        // AuthController.LogLoginAsync is the only thing that puts a "signed in" row in the feed.
        var code = await RegisterAndGetDevCode("login-logged@example.com", "login-logged-shop");
        await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest("login-logged@example.com", code));

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("login-logged@example.com", "password123")),
        };
        req.Headers.UserAgent.ParseAdd("TestAgent/1.0");
        var resp = await Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var db = Db();
        var business = db.Businesses.Single(b => b.Email == "login-logged@example.com");
        var log = db.ActivityLogs.Single(a => a.BusinessId == business.Id && a.Action == "AuthController.Login");
        Assert.Equal("Signed in", log.Description);
        Assert.Equal("TestAgent/1.0", log.UserAgent);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        await Client.PostAsJsonAsync("/api/auth/register", ValidRegister(email: "wrongpw@example.com", slug: "wrongpw-shop"));

        var resp = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("wrongpw@example.com", "not-the-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsUnauthorized()
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody@example.com", "password123"));

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Register_ReturnsDevCode_AndAccountStartsUnverified()
    {
        var code = await RegisterAndGetDevCode("unverified@example.com", "unverified-shop");

        Assert.Equal(6, code.Length);

        using var db = Db();
        var business = db.Businesses.Single(b => b.Email == "unverified@example.com");
        Assert.False(business.EmailVerified);
    }

    [Fact]
    public async Task Login_BeforeEmailVerified_ReturnsForbiddenWithFlag()
    {
        await Client.PostAsJsonAsync("/api/auth/register", ValidRegister(email: "notverified@example.com", slug: "notverified-shop"));

        var resp = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("notverified@example.com", "password123"));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("emailNotVerified").GetBoolean());
    }

    [Fact]
    public async Task VerifyEmail_WithCorrectCode_MarksVerifiedAndReturnsToken()
    {
        var code = await RegisterAndGetDevCode("verify-ok@example.com", "verify-ok-shop");

        var resp = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest("verify-ok@example.com", code));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body?.Token));

        using var db = Db();
        Assert.True(db.Businesses.Single(b => b.Email == "verify-ok@example.com").EmailVerified);
    }

    [Fact]
    public async Task VerifyEmail_WithWrongCode_ReturnsBadRequest()
    {
        await RegisterAndGetDevCode("verify-wrong@example.com", "verify-wrong-shop");

        var resp = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest("verify-wrong@example.com", "000000"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task VerifyEmail_ReplayingAConsumedCode_ReturnsBadRequest()
    {
        var code = await RegisterAndGetDevCode("verify-replay@example.com", "verify-replay-shop");
        var first = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest("verify-replay@example.com", code));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var replay = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest("verify-replay@example.com", code));

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task ResendVerification_ImmediateSecondRequest_IsRateLimited()
    {
        await RegisterAndGetDevCode("resend-cooldown@example.com", "resend-cooldown-shop");

        var resp = await Client.PostAsJsonAsync("/api/auth/resend-verification", new ResendVerificationRequest("resend-cooldown@example.com"));

        Assert.Equal((HttpStatusCode)429, resp.StatusCode);
    }

    [Fact]
    public async Task ResendVerification_ForAlreadyVerifiedEmail_ReturnsBadRequest()
    {
        var code = await RegisterAndGetDevCode("resend-verified@example.com", "resend-verified-shop");
        await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest("resend-verified@example.com", code));

        var resp = await Client.PostAsJsonAsync("/api/auth/resend-verification", new ResendVerificationRequest("resend-verified@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ResendVerification_ForUnknownEmail_ReturnsNotFound()
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/resend-verification", new ResendVerificationRequest("nobody@example.com"));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    private record ForgotPasswordResponse(string? DevCode);

    private async Task<string> RegisterVerifyAndGetResetCode(string email, string slug)
    {
        var code = await RegisterAndGetDevCode(email, slug);
        await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code));

        var resp = await Client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email));
        var body = await resp.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
        Assert.NotNull(body?.DevCode);
        return body!.DevCode!;
    }

    // Same answer as a real account, so the form can't be used to find out who has one.
    [Fact]
    public async Task ForgotPassword_ForUnknownEmail_LooksLikeSuccessButSendsNothing()
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest("nobody@example.com"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Null((await resp.Content.ReadFromJsonAsync<ForgotPasswordResponse>())!.DevCode);
        Assert.DoesNotContain(Factory.Email.Sent, m => m.Email == "nobody@example.com");
    }

    [Fact]
    public async Task ResetPassword_ForUnknownEmail_LooksLikeAWrongCode()
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest("nobody@example.com", "123456", "newpassword456"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_ImmediateSecondRequest_IsRateLimited()
    {
        await RegisterVerifyAndGetResetCode("forgot-cooldown@example.com", "forgot-cooldown-shop");

        var resp = await Client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest("forgot-cooldown@example.com"));

        Assert.Equal((HttpStatusCode)429, resp.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithCorrectCode_UpdatesPasswordAndReturnsToken()
    {
        var code = await RegisterVerifyAndGetResetCode("reset-ok@example.com", "reset-ok-shop");

        var resp = await Client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest("reset-ok@example.com", code, "newpassword456"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body?.Token));

        var loginResp = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("reset-ok@example.com", "newpassword456"));
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

        var oldPasswordResp = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("reset-ok@example.com", "password123"));
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordResp.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithWrongCode_ReturnsBadRequest()
    {
        await RegisterVerifyAndGetResetCode("reset-wrong@example.com", "reset-wrong-shop");

        var resp = await Client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest("reset-wrong@example.com", "000000", "newpassword456"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_ReplayingAConsumedCode_ReturnsBadRequest()
    {
        var code = await RegisterVerifyAndGetResetCode("reset-replay@example.com", "reset-replay-shop");
        var first = await Client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest("reset-replay@example.com", code, "newpassword456"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var replay = await Client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest("reset-replay@example.com", code, "anotherpassword789"));

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_WithTooShortNewPassword_ReturnsBadRequest()
    {
        var code = await RegisterVerifyAndGetResetCode("reset-shortpw@example.com", "reset-shortpw-shop");

        var resp = await Client.PostAsJsonAsync("/api/auth/reset-password",
            new ResetPasswordRequest("reset-shortpw@example.com", code, "short"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    private async Task<string> RegisterVerifyAndLogin(string email, string slug)
    {
        var code = await RegisterAndGetDevCode(email, slug);
        await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code));
        var resp = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "password123"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
    }

    private async Task<HttpStatusCode> SettingsStatus(string token)
    {
        var msg = new HttpRequestMessage(HttpMethod.Get, "/api/admin/settings");
        msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return (await Client.SendAsync(msg)).StatusCode;
    }

    [Fact]
    public async Task Email_IsCaseInsensitive_ForRegisterLoginAndForgotPassword()
    {
        var code = await RegisterAndGetDevCode("Mixed.Case@Example.com", "mixed-case-shop");
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest("mixed.case@example.com", code))).StatusCode);

        var dupe = await Client.PostAsJsonAsync("/api/auth/register", ValidRegister("MIXED.CASE@example.com", "mixed-case-two"));
        Assert.Equal(HttpStatusCode.BadRequest, dupe.StatusCode);

        var login = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(" Mixed.case@EXAMPLE.com ", "password123"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var forgot = await Client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest("MIXED.CASE@EXAMPLE.COM"));
        Assert.NotNull((await forgot.Content.ReadFromJsonAsync<ForgotPasswordResponse>())!.DevCode);
    }

    [Fact]
    public async Task ResetPassword_EndsEveryOtherLogin()
    {
        var oldToken = await RegisterVerifyAndLogin("reset-ends@example.com", "reset-ends-shop");
        Assert.Equal(HttpStatusCode.OK, await SettingsStatus(oldToken));

        var forgot = await Client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest("reset-ends@example.com"));
        var code = (await forgot.Content.ReadFromJsonAsync<ForgotPasswordResponse>())!.DevCode!;
        var reset = await Client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest("reset-ends@example.com", code, "newpassword456"));
        var newToken = (await reset.Content.ReadFromJsonAsync<LoginResponse>())!.Token;

        Assert.Equal(HttpStatusCode.Unauthorized, await SettingsStatus(oldToken));
        Assert.Equal(HttpStatusCode.OK, await SettingsStatus(newToken));
    }

    [Fact]
    public async Task ChangePassword_RequiresTheCurrentPassword()
    {
        var token = await RegisterVerifyAndLogin("change-current@example.com", "change-current-shop");
        Authorize(Client, token);

        var wrong = await Client.PatchAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("not-my-password", "newpassword456"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        // The failed attempt didn't change anything.
        Client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest("change-current@example.com", "password123"))).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_RejectsKeepingTheSamePassword()
    {
        var token = await RegisterVerifyAndLogin("change-same@example.com", "change-same-shop");
        Authorize(Client, token);

        var same = await Client.PatchAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("password123", "password123"));
        Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
        Assert.Contains("samePassword", await same.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ChangePassword_EndsOtherLogins_ButReturnsAWorkingToken()
    {
        var otherDevice = await RegisterVerifyAndLogin("change-ends@example.com", "change-ends-shop");
        Authorize(Client, otherDevice);

        var resp = await Client.PatchAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("password123", "newpassword456"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var fresh = (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        Client.DefaultRequestHeaders.Authorization = null;

        Assert.Equal(HttpStatusCode.Unauthorized, await SettingsStatus(otherDevice));
        Assert.Equal(HttpStatusCode.OK, await SettingsStatus(fresh));
    }

    [Fact]
    public async Task ChangePassword_IsThrottledAfterRepeatedWrongCurrentPasswords()
    {
        var token = await RegisterVerifyAndLogin("change-throttle@example.com", "change-throttle-shop");
        Authorize(Client, token);

        for (var i = 0; i < 5; i++)
            await Client.PatchAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("guess" + i, "newpassword456"));
        var blocked = await Client.PatchAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("password123", "newpassword456"));

        Assert.Equal((HttpStatusCode)429, blocked.StatusCode);
    }
}
