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

    // QA run 3: "بعد 8 دقيقة", "בעוד 1 דקות" and "15 minute(s)".
    [Theory]
    [InlineData(1, "Too many sign-in attempts. Try again in 1 minute.", "دقيقة واحدة", "בעוד דקה.")]
    [InlineData(2, "Too many sign-in attempts. Try again in 2 minutes.", "دقيقتين", "בעוד שתי דקות.")]
    [InlineData(8, "Too many sign-in attempts. Try again in 8 minutes.", "8 دقائق", "בעוד 8 דקות.")]
    [InlineData(15, "Too many sign-in attempts. Try again in 15 minutes.", "15 دقيقة", "בעוד 15 דקות.")]
    public void LockoutWait_UsesPluralForms(int minutes, string en, string arWait, string heEnd)
    {
        var message = LoginThrottle.TooManyAttemptsMessage(TimeSpan.FromMinutes(minutes));
        Assert.Equal(en, message);
        Assert.EndsWith($"بعد {arWait}.", ApiErrorTranslations.Translate("AR", message));
        Assert.EndsWith(heEnd, ApiErrorTranslations.Translate("HE", message));
    }

    // QA run 3: model-binding failures were English ProblemDetails with no `error` key.
    [Fact]
    public async Task MissingRequiredQueryValue_IsATranslatedError()
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/some-shop/appointments/some-id");
        req.Headers.Add("X-Lang", "AR");

        var resp = await Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("طلب غير صالح", body.GetProperty("error").GetString());
        Assert.True(body.GetProperty("errors").TryGetProperty("token", out _));
    }

    // QA run 4 OWN-9: `errors` carried the framework's English messages, some naming our DTO types,
    // and a huge dashboard week overflowed into a 500.
    [Fact]
    public async Task BadInput_NamesOnlyTheField_AndAHugeWeekIsA400()
    {
        var email = "err-owner@example.com";
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", "err-owner-shop"));
        var code = (await register.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("devCode").GetString();
        var login = await (await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code!))).Content.ReadFromJsonAsync<LoginResponse>();
        Authorize(Client, login!.Token);

        var badType = await Client.PostAsync("/api/admin/items",
            new StringContent("""{"nameEn":"Cut","nameAr":"Cut","nameHe":"Cut","durationMinutes":"thirty","price":10}""", System.Text.Encoding.UTF8, "application/json"));
        var hugeWeek = await Client.GetAsync("/api/admin/dashboard?week=100000000");

        Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);
        var text = await badType.Content.ReadAsStringAsync();
        Assert.DoesNotContain("BarberSaas", text);
        Assert.True((await badType.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("durationMinutes", out _));
        Assert.Equal(HttpStatusCode.BadRequest, hugeWeek.StatusCode);
    }
}
