using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

public class CustomerAuthControllerTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);
    private record ServiceDto(string Id);
    private record ErrorResponse(string Error);

    private async Task<(string BarberId, string Slug, string ServiceId)> SeedBarberAndService(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Barber", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var barberBody = await verify.Content.ReadFromJsonAsync<LoginResponse>();

        Authorize(Client, barberBody!.Token);
        var svcResp = await Client.PostAsJsonAsync("/api/admin/services", new CreateServiceRequest("Haircut", "Haircut", "Haircut", 30, 50m));
        var svc = await svcResp.Content.ReadFromJsonAsync<ServiceDto>();
        Client.DefaultRequestHeaders.Authorization = null;

        using var db = Db();
        var barberId = await db.Barbers.Where(b => b.Slug == slug).Select(b => b.Id).FirstAsync();
        return (barberId, slug, svc!.Id);
    }

    private async Task<string> CreateBookingToken(string barberId, string serviceId, string phone, string? profileName = null)
    {
        using var scope = Factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<WhatsAppBookingTokenService>();
        var token = await tokens.CreateAsync(barberId, serviceId, phone, profileName);
        return token.Id;
    }

    [Fact]
    public async Task LoginWithWhatsApp_ValidToken_NewCustomer_ReturnsSessionAndSplitsProfileName()
    {
        var (barberId, slug, serviceId) = await SeedBarberAndService("wa-login-1@example.com", "wa-login-1");
        var token = await CreateBookingToken(barberId, serviceId, "+15557770001", "Jane Doe");

        var resp = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<WhatsAppLoginResult>();
        Assert.False(string.IsNullOrWhiteSpace(body?.Token));
        Assert.Equal("Jane", body!.Name);
        Assert.Equal("Doe", body.FamilyName);
        Assert.Equal(slug, body.BarberSlug);
        Assert.Equal(serviceId, body.ServiceId);
    }

    [Fact]
    public async Task LoginWithWhatsApp_NoProfileName_FallsBackToGenericName()
    {
        var (barberId, _, serviceId) = await SeedBarberAndService("wa-login-2@example.com", "wa-login-2");
        var token = await CreateBookingToken(barberId, serviceId, "+15557770002", null);

        var resp = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(token));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<WhatsAppLoginResult>();
        Assert.Equal("Customer", body!.Name);
        Assert.Equal("", body.FamilyName);
    }

    [Fact]
    public async Task LoginWithWhatsApp_ExistingAccount_ReusesIt()
    {
        var (barberId, _, serviceId) = await SeedBarberAndService("wa-login-3@example.com", "wa-login-3");
        var phone = "+15557770003";
        var firstToken = await CreateBookingToken(barberId, serviceId, phone, "Jane Doe");
        var first = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(firstToken));
        var firstBody = await first.Content.ReadFromJsonAsync<WhatsAppLoginResult>();

        // A second, unrelated token for the same phone (e.g. picking a different service later)
        // must resolve to the same CustomerAccount, not create a duplicate.
        var secondToken = await CreateBookingToken(barberId, serviceId, phone, "Someone Else");
        var second = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(secondToken));
        var secondBody = await second.Content.ReadFromJsonAsync<WhatsAppLoginResult>();

        Assert.Equal(firstBody!.CustomerId, secondBody!.CustomerId);
        Assert.Equal("Jane", secondBody.Name); // unchanged by the second (unused) profile name
    }

    [Fact]
    public async Task LoginWithWhatsApp_ReusableWithinWindow_BothRequestsSucceed()
    {
        var (barberId, _, serviceId) = await SeedBarberAndService("wa-login-4@example.com", "wa-login-4");
        var token = await CreateBookingToken(barberId, serviceId, "+15557770004", "Jane Doe");

        var first = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(token));
        var second = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(token));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task LoginWithWhatsApp_ExpiredToken_ReturnsBadRequest()
    {
        var (barberId, _, serviceId) = await SeedBarberAndService("wa-login-5@example.com", "wa-login-5");
        var token = await CreateBookingToken(barberId, serviceId, "+15557770005", "Jane Doe");

        using (var db = Db())
        {
            var row = await db.WhatsAppBookingTokens.FirstAsync(t => t.Id == token);
            row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var resp = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(token));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task LoginWithWhatsApp_UnknownToken_ReturnsBadRequest()
    {
        var resp = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest("not-a-real-token"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task LoginWithWhatsApp_ServiceDeactivatedSinceLinkSent_ReturnsNotFound()
    {
        // Services are soft-deleted (IsActive = false) by the barber, never hard-deleted --
        // this is the realistic "service no longer bookable" case, not a hard row delete.
        var (barberId, _, serviceId) = await SeedBarberAndService("wa-login-6@example.com", "wa-login-6");
        var token = await CreateBookingToken(barberId, serviceId, "+15557770006", "Jane Doe");

        using (var db = Db())
        {
            var service = await db.Services.FirstAsync(s => s.Id == serviceId);
            service.IsActive = false;
            await db.SaveChangesAsync();
        }

        var resp = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new WhatsAppLoginRequest(token));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
