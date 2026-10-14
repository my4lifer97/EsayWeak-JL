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
}
