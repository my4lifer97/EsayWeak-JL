using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Services;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// TranslateErrorFilter: X-Lang AR/HE turns the English `error` text into that language.
public class ErrorTranslationTests : IntegrationTestBase
{
    private async Task<JsonElement> PostLogin(string? lang)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("nobody@example.com", "password123")),
        };
        if (lang is not null) req.Headers.Add("X-Lang", lang);
        var resp = await Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        return await resp.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Error_IsTranslated_ForHebrewAndArabic()
    {
        Assert.Equal("אימייל או סיסמה שגויים", (await PostLogin("HE")).GetProperty("error").GetString());
        Assert.Equal("البريد الإلكتروني أو كلمة المرور غير صحيحة", (await PostLogin("ar")).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Error_StaysEnglish_WithoutTheHeaderOrForEnglish()
    {
        Assert.Equal("Invalid email or password", (await PostLogin(null)).GetProperty("error").GetString());
        Assert.Equal("Invalid email or password", (await PostLogin("EN")).GetProperty("error").GetString());
    }

    [Fact]
    public async Task ShortCircuitedValidationError_IsTranslatedToo()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/some-shop/availability?date=2026-02-30&itemId=x");
        req.Headers.Add("X-Lang", "HE");

        var resp = await Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("תאריך לא תקין. יש להשתמש בפורמט YYYY-MM-DD.", body.GetProperty("error").GetString());
    }

    [Fact]
    public void Patterns_KeepTheValue_AndUnknownMessagesPassThrough()
    {
        Assert.Equal("יותר מדי ניסיונות התחברות. נסו שוב בעוד 7 דקות.",
            ApiErrorTranslations.Translate("HE", LoginThrottle.TooManyAttemptsMessage(TimeSpan.FromMinutes(7))));
        Assert.Equal("Something brand new", ApiErrorTranslations.Translate("AR", "Something brand new"));
    }
}
