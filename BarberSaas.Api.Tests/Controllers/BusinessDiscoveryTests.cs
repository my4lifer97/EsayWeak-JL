using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

public class BusinessDiscoveryTests : IntegrationTestBase
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

    private string SeedBusinessType(string key, string displayEn)
    {
        using var db = Db();
        var existing = db.BusinessTypeDefinitions.FirstOrDefault(t => t.Key == key);
        if (existing is not null) return existing.Id;
        var type = new BusinessTypeDefinition { Key = key, DisplayNameEn = displayEn, DisplayNameAr = displayEn, DisplayNameHe = displayEn };
        db.BusinessTypeDefinitions.Add(type);
        db.SaveChanges();
        return type.Id;
    }

    private void SeedBusiness(
        string slug, string? name = null, string? businessTypeId = null, string? city = null,
        double ratingAverage = 0, int ratingCount = 0, int followers = 0,
        bool isListed = true, SubStatus status = SubStatus.TRIAL, DateTime? createdAt = null)
    {
        using var db = Db();
        var business = new Business
        {
            Name = name ?? slug,
            Email = $"{slug}@discovery.test",
            Slug = slug,
            BusinessTypeId = businessTypeId,
            City = city,
            RatingAverage = ratingAverage,
            RatingCount = ratingCount,
            IsListed = isListed,
            SubscriptionStatus = status,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };
        db.Businesses.Add(business);

        for (var i = 0; i < followers; i++)
        {
            var account = new CustomerAccount { Phone = $"+1555{Guid.NewGuid():N}".Substring(0, 12), Name = "F", FamilyName = "F" };
            db.CustomerAccounts.Add(account);
            db.Follows.Add(new Follow { CustomerAccountId = account.Id, BusinessId = business.Id });
        }

        db.SaveChanges();
    }

    private async Task<PagedResult<BusinessSearchResultDto>> Search(string queryString) =>
        (await Client.GetFromJsonAsync<PagedResult<BusinessSearchResultDto>>($"/api/businesses/search{queryString}"))!;

    [Fact]
    public async Task Search_FiltersByBusinessType()
    {
        var barber = SeedBusinessType("disc-barber", "Barber");
        var salon = SeedBusinessType("disc-salon", "Salon");
        SeedBusiness("disc-type-a", businessTypeId: barber);
        SeedBusiness("disc-type-b", businessTypeId: salon);

        var result = await Search("?businessTypeKey=disc-barber");

        Assert.Equal("disc-type-a", Assert.Single(result.Items).Slug);
        Assert.Equal("disc-barber", result.Items[0].BusinessTypeKey);
    }

    [Fact]
    public async Task Search_FiltersByCity_CaseInsensitiveAndTrimmed()
    {
        SeedBusiness("disc-city-a", city: "Haifa");
        SeedBusiness("disc-city-b", city: "Tel Aviv");

        var result = await Search("?city=%20haifa%20");

        Assert.Equal("disc-city-a", Assert.Single(result.Items).Slug);
    }

    [Fact]
    public async Task Search_FiltersByQuery_CaseInsensitiveSubstring()
    {
        SeedBusiness("disc-q-cuts", name: "Downtown Cuts");
        SeedBusiness("disc-q-nails", name: "Nail Bar");

        var result = await Search("?query=downtown");

        Assert.Equal("disc-q-cuts", Assert.Single(result.Items).Slug);
    }

    [Fact]
    public async Task Search_DefaultSort_IsHighestRatedFirst()
    {
        SeedBusiness("disc-sort-low", ratingAverage: 2.0, ratingCount: 3);
        SeedBusiness("disc-sort-high", ratingAverage: 4.8, ratingCount: 10);
        SeedBusiness("disc-sort-mid", ratingAverage: 3.5, ratingCount: 5);

        var result = await Search("?pageSize=50");
        var ordered = result.Items.Select(i => i.Slug).Where(s => s.StartsWith("disc-sort-")).ToList();

        Assert.Equal(new[] { "disc-sort-high", "disc-sort-mid", "disc-sort-low" }, ordered);
    }

    [Fact]
    public async Task Search_SortPopular_OrdersByFollowerCount()
    {
        SeedBusiness("disc-pop-few", followers: 1);
        SeedBusiness("disc-pop-many", followers: 5);

        var result = await Search("?sort=popular&pageSize=50");
        var ordered = result.Items.Select(i => i.Slug).Where(s => s.StartsWith("disc-pop-")).ToList();

        Assert.Equal(new[] { "disc-pop-many", "disc-pop-few" }, ordered);
        Assert.Equal(5, result.Items.First(i => i.Slug == "disc-pop-many").FollowerCount);
    }

    [Fact]
    public async Task Search_SortNewest_OrdersByCreatedAtDesc()
    {
        SeedBusiness("disc-new-old", createdAt: DateTime.UtcNow.AddDays(-10));
        SeedBusiness("disc-new-new", createdAt: DateTime.UtcNow.AddDays(-1));

        var result = await Search("?sort=newest&pageSize=50");
        var ordered = result.Items.Select(i => i.Slug).Where(s => s.StartsWith("disc-new-")).ToList();

        Assert.Equal(new[] { "disc-new-new", "disc-new-old" }, ordered);
    }

    [Fact]
    public async Task Search_Pagination_EnvelopeAndHasMore()
    {
        for (var i = 0; i < 3; i++) SeedBusiness($"disc-page-{i}");

        var page1 = await Search("?query=disc-page&sort=name&page=1&pageSize=2");
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(3, page1.Total);
        Assert.True(page1.HasMore);

        var page2 = await Search("?query=disc-page&sort=name&page=2&pageSize=2");
        Assert.Single(page2.Items);
        Assert.False(page2.HasMore);
    }

    [Fact]
    public async Task Search_PageSize_ClampedToFifty()
    {
        SeedBusiness("disc-clamp");
        var result = await Search("?pageSize=999");
        Assert.Equal(50, result.PageSize);
    }

    [Fact]
    public async Task Search_ExcludesUnlistedAndExpiredBusinesses()
    {
        SeedBusiness("disc-visible");
        SeedBusiness("disc-unlisted", isListed: false);
        SeedBusiness("disc-expired", status: SubStatus.EXPIRED);

        var result = await Search("?query=disc-&pageSize=50");
        var slugs = result.Items.Select(i => i.Slug).ToList();

        Assert.Contains("disc-visible", slugs);
        Assert.DoesNotContain("disc-unlisted", slugs);
        Assert.DoesNotContain("disc-expired", slugs);
    }

    [Fact]
    public async Task Cities_ReturnsDistinctSorted_AmongListedNonExpiredOnly()
    {
        SeedBusiness("disc-cities-1", city: "Nazareth");
        SeedBusiness("disc-cities-2", city: "Acre");
        SeedBusiness("disc-cities-3", city: "Nazareth");
        SeedBusiness("disc-cities-hidden", city: "Eilat", isListed: false);
        SeedBusiness("disc-cities-expired", city: "Ramla", status: SubStatus.EXPIRED);

        var cities = await Client.GetFromJsonAsync<List<string>>("/api/businesses/cities");

        Assert.Equal(new[] { "Acre", "Nazareth" }, cities);
    }

    [Fact]
    public async Task Info_UnlistedBusiness_StillReturns200()
    {
        SeedBusiness("disc-direct-link", city: "Jaffa", isListed: false);

        var resp = await Client.GetAsync("/api/disc-direct-link/info");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<PublicBusinessDto>();
        Assert.Equal("Jaffa", dto!.City);
    }

    [Fact]
    public async Task Settings_RoundTripsDiscoveryFields()
    {
        var token = await RegisterAndLoginBusiness("disc-settings@example.com", "disc-settings");
        Authorize(Client, token);

        var patch = await Client.PatchAsJsonAsync("/api/admin/settings", new UpdateSettingsRequest(
            Name: null, Phone: null, Description: null, Language: null,
            MaxBookingsPerDay: null, MaxBookingsPerWeek: null,
            City: "  Bethlehem  ", AddressLine: "1 Star St", MapUrl: "https://maps.example/x", IsListed: false));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var settings = await Client.GetFromJsonAsync<SettingsDto>("/api/admin/settings");
        Assert.Equal("Bethlehem", settings!.City);
        Assert.Equal("1 Star St", settings.AddressLine);
        Assert.Equal("https://maps.example/x", settings.MapUrl);
        Assert.False(settings.IsListed);
    }

    [Fact]
    public async Task Settings_RejectsMapUrlWithoutScheme()
    {
        var token = await RegisterAndLoginBusiness("disc-badmap@example.com", "disc-badmap");
        Authorize(Client, token);

        var patch = await Client.PatchAsJsonAsync("/api/admin/settings", new UpdateSettingsRequest(
            Name: null, Phone: null, Description: null, Language: null,
            MaxBookingsPerDay: null, MaxBookingsPerWeek: null,
            City: null, AddressLine: null, MapUrl: "maps.example/x", IsListed: true));

        Assert.Equal(HttpStatusCode.BadRequest, patch.StatusCode);
    }
}
