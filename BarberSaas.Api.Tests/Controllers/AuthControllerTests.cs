using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

public class AuthControllerTests : IntegrationTestBase
{
    private static RegisterRequest ValidRegister(string email = "barber@example.com", string slug = "my-barber-shop") =>
        new("Barber Name", email, "password123", slug);

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
        await Client.PostAsJsonAsync("/api/auth/register", ValidRegister(email: "login@example.com", slug: "login-shop"));

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
}
