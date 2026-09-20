using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// PATCH /api/admin/settings must treat a key genuinely absent from the request body as "leave
// this field alone", not "reset it to its default" -- the current SettingsPage always submits
// every field, so this was never reachable from that client, but any narrower caller (a future
// mobile app, a script) hitting this endpoint with a true partial payload would otherwise wipe
// every other field.
public class AdminSettingsTests : IntegrationTestBase
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

    [Fact]
    public async Task PartialUpdate_OmittedFields_AreLeftUntouched()
    {
        var token = await RegisterAndLoginBusiness("settings-partial@example.com", "settings-partial-shop");
        Authorize(Client, token);

        var full = await Client.PatchAsJsonAsync("/api/admin/settings", new
        {
            city = "Tel Aviv", addressLine = "1 Rothschild", waitlistEnabled = false,
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, full.StatusCode);

        // A true partial payload -- only this one key present in the JSON body.
        var partial = await Client.PatchAsJsonAsync("/api/admin/settings", new { waitlistEnabled = true });
        Assert.Equal(System.Net.HttpStatusCode.OK, partial.StatusCode);

        var settings = await Client.GetFromJsonAsync<SettingsDto>("/api/admin/settings");
        Assert.True(settings!.WaitlistEnabled);
        Assert.Equal("Tel Aviv", settings.City);
        Assert.Equal("1 Rothschild", settings.AddressLine);
    }

    [Fact]
    public async Task ExplicitNull_StillClearsAField()
    {
        var token = await RegisterAndLoginBusiness("settings-explicit-null@example.com", "settings-explicit-null-shop");
        Authorize(Client, token);

        await Client.PatchAsJsonAsync("/api/admin/settings", new { city = "Haifa" });

        // The real SettingsPage sends an explicit null (not an omitted key) when a field is
        // cleared -- that must still work exactly as before.
        var clear = await Client.PatchAsJsonAsync("/api/admin/settings", new { city = (string?)null });
        Assert.Equal(System.Net.HttpStatusCode.OK, clear.StatusCode);

        var settings = await Client.GetFromJsonAsync<SettingsDto>("/api/admin/settings");
        Assert.Null(settings!.City);
    }
}
