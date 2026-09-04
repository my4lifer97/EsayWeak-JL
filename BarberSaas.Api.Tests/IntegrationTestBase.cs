using System.Net.Http.Headers;
using System.Net.Http.Json;
using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BarberSaas.Api.Tests;

// Each test class gets its own factory (and therefore its own isolated InMemory database),
// since xUnit creates a fresh instance of the test class per [Fact].
public abstract class IntegrationTestBase : IDisposable
{
    protected readonly TestWebApplicationFactory Factory;
    protected readonly HttpClient Client;

    // A throwaway barber+service, seeded lazily on first use, that LoginCustomerViaWhatsAppAsync
    // issues its booking-link tokens against. Its identity never leaks into the returned customer
    // JWT (CustomerJwtService.Generate only encodes the CustomerAccount, not the barber/service the
    // login token happened to be minted for), so every test in a class can safely share the same
    // one instead of each call seeding its own barber.
    private string? _loginBarberId;
    private string? _loginServiceId;

    protected IntegrationTestBase() : this(configureCardcom: false) { }

    protected IntegrationTestBase(bool configureCardcom)
    {
        Factory = new TestWebApplicationFactory(configureCardcom);
        Client = Factory.CreateClient();
    }

    protected AppDbContext Db() => Factory.CreateDbContext();

    protected static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    // Replaces the old phone+OTP two-step (request code, verify code) test helper: mints a
    // WhatsAppBookingToken directly (bypassing the WhatsApp webhook itself, which needs a real
    // Twilio signature -- see WhatsAppControllerTests for webhook-level coverage) and redeems it
    // through the real POST /api/customer/auth/whatsapp endpoint, exactly like WhatsAppLandingPage
    // does. `name`/`familyName` are combined into a single WhatsApp "profile name" so the
    // endpoint's name-splitting reproduces them exactly, keeping every existing call site
    // (phone, name, familyName) unchanged.
    protected async Task<WhatsAppLoginResult> LoginCustomerViaWhatsAppAsync(string phone, string name = "First", string familyName = "Last")
    {
        await EnsureLoginBarberSeededAsync();

        using var scope = Factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<WhatsAppBookingTokenService>();
        var profileName = string.IsNullOrWhiteSpace(familyName) ? name : $"{name} {familyName}";
        var tokenRow = await tokens.CreateAsync(_loginBarberId!, _loginServiceId!, phone, profileName);

        var resp = await Client.PostAsJsonAsync("/api/customer/auth/whatsapp", new { token = tokenRow.Id });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<WhatsAppLoginResult>())!;
    }

    private async Task EnsureLoginBarberSeededAsync()
    {
        if (_loginBarberId is not null) return;

        using var db = Db();
        var barber = new Barber { Name = "Login Seed Barber", Email = $"{Guid.NewGuid():N}@login-seed.test", Slug = $"login-seed-{Guid.NewGuid():N}" };
        db.Barbers.Add(barber);
        var service = new Service { BarberId = barber.Id, NameEn = "Seed Service", NameAr = "Seed Service", NameHe = "Seed Service", DurationMinutes = 30, Price = 0 };
        db.Services.Add(service);
        await db.SaveChangesAsync();

        _loginBarberId = barber.Id;
        _loginServiceId = service.Id;
    }

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }
}

public record WhatsAppLoginResult(string Token, string CustomerId, string Name, string FamilyName, string Phone, string BarberSlug, string ServiceId);
