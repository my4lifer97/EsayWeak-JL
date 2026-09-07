using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using Xunit;

namespace BarberSaas.Api.Tests;

// Confirms an unhandled exception (an invalid date string blows past validation into
// AvailabilityService's unguarded DateTime.Parse) is caught by GlobalExceptionHandler
// rather than surfacing a bare/blank 500 or leaking a raw stack trace.
public class GlobalExceptionHandlerTests : IntegrationTestBase
{
    private record ErrorResponse(string Error);
    private record RegisterResponse(string? DevCode);

    [Fact]
    public async Task UnhandledException_ReturnsConsistentJsonErrorShape()
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", "exc-flow@example.com", "password123", "exc-flow-shop"));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest("exc-flow@example.com", registerBody!.DevCode!));
        var loginBody = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        Authorize(Client, loginBody!.Token);
        var serviceResp = await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Cut", "Cut", "Cut", 30, 20m));
        var service = await serviceResp.Content.ReadFromJsonAsync<ItemDto>();
        Client.DefaultRequestHeaders.Authorization = null;

        var resp = await Client.GetAsync($"/api/exc-flow-shop/availability?date=not-a-date&itemId={service!.Id}");

        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
        Assert.Equal("application/json", resp.Content.Headers.ContentType?.MediaType);
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body?.Error));
    }
}
