using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// QA run 2, "Input" and "Owner" rows: values that used to be saved (or crash) and are now rejected.
public class InputValidationTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);
    private record ErrorResponse(string Error);

    private async Task<(string Token, string ItemId)> SetUpBusiness(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var code = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.DevCode!;
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code));
        var token = (await verify.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        Authorize(Client, token);
        var item = await (await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Cut", "Cut", "Cut", 30, 20m))).Content.ReadFromJsonAsync<ItemDto>();
        Client.DefaultRequestHeaders.Authorization = null;
        return (token, item!.Id);
    }

    private static string NextWeekday(int minDaysAhead)
    {
        var d = DateTime.Now.Date.AddDays(minDaysAhead);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
        return d.ToString("yyyy-MM-dd");
    }

    [Fact]
    public async Task Booking_RequiresAName_AndCapsNotes()
    {
        var (_, itemId) = await SetUpBusiness("input-book@example.com", "input-book-shop");
        var date = NextWeekday(2);

        var noName = await BookAsCustomerAsync("input-book-shop", new BookAppointmentRequest(itemId, date, "10:00", " ", "+15550001001", null));
        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);

        var hugeNotes = await BookAsCustomerAsync("input-book-shop", new BookAppointmentRequest(itemId, date, "10:00", "Dana", "+15550001002", new string('x', 100_000)));
        Assert.Equal(HttpStatusCode.BadRequest, hugeNotes.StatusCode);

        var ok = await BookAsCustomerAsync("input-book-shop", new BookAppointmentRequest(itemId, date, "10:00", "Dana", "+15550001003", "Short note"));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task Booking_MoreThanAYearAhead_IsNotOffered()
    {
        var (_, itemId) = await SetUpBusiness("input-far@example.com", "input-far-shop");
        var farDate = NextWeekday(400);

        var slots = await Client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/input-far-shop/availability?date={farDate}&itemId={itemId}");
        Assert.Equal(0, slots.GetProperty("slots").GetArrayLength());

        var book = await BookAsCustomerAsync("input-far-shop", new BookAppointmentRequest(itemId, farDate, "10:00", "Dana", "+15550001004", null));
        Assert.Equal(HttpStatusCode.Conflict, book.StatusCode);
    }

    [Theory]
    [InlineData("platform-admin")]
    [InlineData("request-business-account")]
    [InlineData("wa-link")]
    [InlineData("admin-platform")]
    public async Task Register_RejectsSlugsThatAreFrontendPages(string slug)
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", $"{slug}@example.com", "password123", slug));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Settings_RejectsAnEmptyBusinessName()
    {
        var (token, _) = await SetUpBusiness("input-name@example.com", "input-name-shop");
        Authorize(Client, token);

        var resp = await Client.PatchAsJsonAsync("/api/admin/settings", new { name = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var settings = await Client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/admin/settings");
        Assert.Equal("Business", settings.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("", 30, 20)]          // empty name on edit
    [InlineData("Cut", 30, -5)]       // negative price
    [InlineData("Cut", 30, 99999999)] // price too large
    [InlineData("Cut", 99990, 20)]    // duration over 24 hours
    public async Task EditItem_RejectsInvalidValues(string name, int duration, decimal price)
    {
        var (token, itemId) = await SetUpBusiness($"input-item-{Guid.NewGuid():N}@example.com", $"input-item-{Guid.NewGuid():N}"[..20]);
        Authorize(Client, token);

        var resp = await Client.PatchAsJsonAsync($"/api/admin/items/{itemId}", new CreateItemRequest(name, "Cut", "Cut", duration, price));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // QA run 3: these were 500s (OWN-2/3/4).
    [Theory]
    [InlineData("{\"maxBookingsPerDay\":\"abc\"}")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task Settings_MalformedBody_Returns400(string json)
    {
        var (token, _) = await SetUpBusiness($"input-json-{Guid.NewGuid():N}@example.com", $"input-json-{Guid.NewGuid():N}"[..20]);
        Authorize(Client, token);

        var resp = await Client.PatchAsync("/api/admin/settings", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("Invalid request", (await resp.Content.ReadFromJsonAsync<ErrorResponse>())!.Error);
    }

    [Fact]
    public async Task Register_UnknownBusinessType_Returns400AndCreatesNothing()
    {
        var resp = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", "input-type@example.com", "password123", "input-type-shop", "nonexistent"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var db = Db();
        Assert.False(db.Businesses.Any(b => b.Slug == "input-type-shop"));
        Assert.False(db.BusinessEmailOtps.Any(c => c.Email == "input-type@example.com"));
    }

    [Theory]
    [InlineData("99")]
    [InlineData("1")]
    [InlineData("gallery")]
    public async Task Item_PhotoModeMustBeAName(string photoMode)
    {
        var (token, itemId) = await SetUpBusiness($"input-photo-{Guid.NewGuid():N}@example.com", $"input-photo-{Guid.NewGuid():N}"[..20]);
        Authorize(Client, token);

        var create = await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Cut", "Cut", "Cut", 30, 20m, photoMode));
        var edit = await Client.PatchAsJsonAsync($"/api/admin/items/{itemId}", new CreateItemRequest("Cut", "Cut", "Cut", 30, 20m, photoMode));

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);
    }

    // QA run 3 OWN-9.
    [Fact]
    public async Task Settings_RejectsOverlongTextAndAnInvalidPhone()
    {
        var (token, _) = await SetUpBusiness("input-own9@example.com", "input-own9-shop");
        Authorize(Client, token);

        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PatchAsJsonAsync("/api/admin/settings", new { name = new string('n', 300) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PatchAsJsonAsync("/api/admin/settings", new { description = new string('d', 20_000) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PatchAsJsonAsync("/api/admin/settings", new { city = new string('c', 5_000) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PatchAsJsonAsync("/api/admin/settings", new { phone = "<b>x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest(new string('x', 600), "Cut", "Cut", 30, 20m))).StatusCode);
    }

    [Fact]
    public async Task BlockedDates_PastAndDuplicateFullDay_AreRejected()
    {
        var (token, _) = await SetUpBusiness("input-block@example.com", "input-block-shop");
        Authorize(Client, token);

        var past = await Client.PostAsJsonAsync("/api/admin/schedule/blocked", new CreateBlockedSlotRequest("2020-01-01", null, null, null));
        var first = await Client.PostAsJsonAsync("/api/admin/schedule/blocked", new CreateBlockedSlotRequest("2030-06-03", null, null, null));
        var again = await Client.PostAsJsonAsync("/api/admin/schedule/blocked", new CreateBlockedSlotRequest("2030-06-03", null, null, null));
        var range = await Client.PostAsJsonAsync("/api/admin/schedule/blocked/range", new CreateBlockedRangeRequest("2030-06-02", "2030-06-04", null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Created, range.StatusCode);
        using var db = Db();
        var businessId = db.Businesses.Single(b => b.Slug == "input-block-shop").Id;
        Assert.Equal(3, db.BlockedSlots.Count(b => b.BusinessId == businessId)); // 06-03 not duplicated
    }

    [Fact]
    public async Task Preset_CannotBeNamedDefault()
    {
        var (token, _) = await SetUpBusiness("input-default@example.com", "input-default-shop");
        Authorize(Client, token);
        var days = Enumerable.Range(0, 7).Select(d => new SchedulePresetDayDto(d, "09:00", "18:00", d is >= 1 and <= 5)).ToList();

        var resp = await Client.PostAsJsonAsync("/api/admin/schedule/presets", new SaveSchedulePresetRequest(" default ", days));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
