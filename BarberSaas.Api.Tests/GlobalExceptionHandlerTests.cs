using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BarberSaas.Api.Tests;

public class GlobalExceptionHandlerTests : IntegrationTestBase
{
    private record ErrorResponse(string Error);
    private record RegisterResponse(string? DevCode);

    private class FakeEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    // Any exception a controller doesn't handle comes back as the usual { error } JSON 500, never a
    // bare 500 or a stack trace -- and outside Development, never the exception's own text.
    [Fact]
    public async Task UnhandledException_ReturnsConsistentJsonErrorShape_WithoutLeakingDetails()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, new FakeEnv("Production"));
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(ctx, new InvalidOperationException("secret internals"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, ctx.Response.StatusCode);
        Assert.StartsWith("application/json", ctx.Response.ContentType);
        ctx.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Response.Body);
        var error = body.GetProperty("error").GetString();
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.DoesNotContain("secret internals", error);
    }

    // QA: an invalid date used to reach DateTime.Parse and come back as a 500. ValidateInputFilter
    // now answers 400 before the endpoint runs.
    [Fact]
    public async Task InvalidDate_ReturnsBadRequest_NotAServerError()
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", "exc-flow@example.com", "password123", "exc-flow-shop"));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest("exc-flow@example.com", registerBody!.DevCode!));
        var loginBody = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        Authorize(Client, loginBody!.Token);
        var serviceResp = await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Cut", "Cut", "Cut", 30, 20m));
        var service = await serviceResp.Content.ReadFromJsonAsync<ItemDto>();

        foreach (var bad in new[] { "not-a-date", "2026-02-30", "2026-1-5", "12/10/2026" })
        {
            var blocked = await Client.PostAsJsonAsync("/api/admin/schedule/blocked", new { date = bad, startTime = (string?)null, endTime = (string?)null, reason = "x" });
            Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        }

        Client.DefaultRequestHeaders.Authorization = null;
        var resp = await Client.GetAsync($"/api/exc-flow-shop/availability?date=2026-02-30&itemId={service!.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("YYYY-MM-DD", (await resp.Content.ReadFromJsonAsync<ErrorResponse>())!.Error);
    }
}
