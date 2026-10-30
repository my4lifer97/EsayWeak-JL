using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

// QA run 7 fixes that come down to one endpoint or helper.
public class QaRun7Tests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    private async Task<string> RegisterAndVerify(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Shop", email, "password123", slug));
        var code = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.DevCode!;
        return (await (await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code)))
            .Content.ReadFromJsonAsync<LoginResponse>())!.Token;
    }

    // SEC-3: a line break in the address reached the email's raw To: header.
    [Fact]
    public async Task OwnerRequestEmailCode_LineBreakInTheAddress_IsRefused()
    {
        var resp = await Client.PostAsJsonAsync("/api/business-owner-requests/send-email-code",
            new { email = "a@x.com\r\nBcc: v@y.com\r\n\r\nhello" });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // BOT-20 / PUB-11: .NET's \d matches every script's digits; int.Parse doesn't.
    [Theory]
    [InlineData("२", "2")]
    [InlineData("๒๓", "23")]
    [InlineData("٢", "2")]
    [InlineData("２", "2")]
    [InlineData("a1", "a1")]
    public void Digits_ToAscii(string input, string expected)
    {
        Assert.Equal(expected, Digits.ToAscii(input));
    }

    // OWN-14: AddMinutes parsed the time before anything checked it.
    [Theory]
    [InlineData("zz")]
    [InlineData("ab:cd")]
    [InlineData("9")]
    public async Task OwnerBooking_MalformedStartTime_IsABadRequest(string startTime)
    {
        Authorize(Client, await RegisterAndVerify($"qa7-time-{startTime.Length}@example.com", $"qa7-time-{startTime.Length}"));
        var item = (await (await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Cut", "Cut", "Cut", 30, 20m)))
            .Content.ReadFromJsonAsync<ItemDto>())!;

        var resp = await Client.PostAsJsonAsync("/api/admin/appointments", new
        {
            itemId = item.Id, date = AppClock.Now.AddDays(1).ToString("yyyy-MM-dd"), startTime, customerName = "Dana", customerPhone = "+15557100001",
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // OWN-11: the QA6 filter ran after the framework had already made the 404 a ProblemDetails.
    [Fact]
    public async Task BareNotFound_HasAnErrorBody()
    {
        Authorize(Client, await RegisterAndVerify("qa7-404@example.com", "qa7-404"));

        var resp = await Client.PatchAsJsonAsync("/api/admin/appointments/nope/customer", new { customerName = "Dana", customerPhone = "+15557100002" });

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("Not found", body.GetProperty("error").GetString());
    }

    // OWN-16: an unverified sign-up had a storefront and held its URL and email forever.
    [Fact]
    public async Task UnverifiedSignUp_HasNoStorefront_AndGivesUpItsUrlAfterADay()
    {
        await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Shop", "qa7-typo@exmaple.com", "password123", "qa7-held"));
        var info = await Client.GetAsync("/api/qa7-held/info");
        var tooSoon = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Shop", "qa7-real@example.com", "password123", "qa7-held"));
        using (var db = Db())
        {
            var held = await db.Businesses.SingleAsync(b => b.Slug == "qa7-held");
            held.CreatedAt = DateTime.UtcNow.AddDays(-2);
            await db.SaveChangesAsync();
        }

        var later = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Shop", "qa7-real@example.com", "password123", "qa7-held"));

        Assert.Equal(HttpStatusCode.NotFound, info.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooSoon.StatusCode);
        Assert.Equal(HttpStatusCode.Created, later.StatusCode);
    }

    // OWN-15: the second request for the same new phone hit the unique index and 500'd. Here the
    // other request's row is already saved when this one tries to add its own.
    [Fact]
    public async Task CustomerRows_LosingTheInsert_TakesTheRowThatWon()
    {
        await RegisterAndVerify("qa7-samephone@example.com", "qa7-samephone");
        using var db = Db();
        var businessId = (await db.Businesses.SingleAsync(b => b.Slug == "qa7-samephone")).Id;
        using (var other = Db())
        {
            other.Customers.Add(new BarberSaas.Api.Models.Customer { BusinessId = businessId, Name = "Dana", Phone = "+15557100003" });
            await other.SaveChangesAsync();
        }

        var customer = await CustomerRows.AddOrGetExistingAsync(db,
            new BarberSaas.Api.Models.Customer { BusinessId = businessId, Name = "Dana", Phone = "+15557100003" });

        using var check = Db();
        Assert.Equal((await check.Customers.SingleAsync(c => c.Phone == "+15557100003")).Id, customer.Id);
    }
}
