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

    private static RegisterRequest ValidRegister(string email = "barber@example.com", string slug = "my-barber-shop") =>
        new("Barber Name", email, "password123", slug);

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
        var barber = db.Barbers.Single(b => b.Email == "unverified@example.com");
        Assert.False(barber.EmailVerified);
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
        Assert.True(db.Barbers.Single(b => b.Email == "verify-ok@example.com").EmailVerified);
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
}
