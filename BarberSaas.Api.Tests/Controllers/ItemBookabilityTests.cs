using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Covers Item.IsBookable -- the per-item switch that lets a Both-model business mix bookable
// appointment items with purely showcase (non-bookable) ones. Every booking-adjacent endpoint
// must reject a non-bookable item instead of assuming every item can be scheduled.
public class ItemBookabilityTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    private async Task<(string Token, string Slug)> RegisterAndLoginBusiness(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var body = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        return (body!.Token, slug);
    }

    private async Task<ItemDto> CreateNonBookableItem(string businessToken)
    {
        Authorize(Client, businessToken);
        var resp = await Client.PostAsJsonAsync("/api/admin/items",
            new CreateItemRequest("Showcase Car", "Showcase Car", "Showcase Car", null, 50000m, "None", IsBookable: false));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var item = await resp.Content.ReadFromJsonAsync<ItemDto>();
        Client.DefaultRequestHeaders.Authorization = null;
        return item!;
    }

    [Fact]
    public async Task CreateItem_NonBookable_DoesNotRequireDuration()
    {
        var (token, _) = await RegisterAndLoginBusiness("nonbookable-create@example.com", "nonbookable-create-shop");
        var item = await CreateNonBookableItem(token);

        Assert.False(item.IsBookable);
        Assert.Null(item.DurationMinutes);
        Assert.Equal(50000m, item.Price);
    }

    [Fact]
    public async Task PublicAvailability_ForNonBookableItem_ReturnsBadRequest()
    {
        var (token, slug) = await RegisterAndLoginBusiness("nonbookable-avail@example.com", "nonbookable-avail-shop");
        var item = await CreateNonBookableItem(token);

        var resp = await Client.GetAsync($"/api/{slug}/availability?date=2026-07-06&itemId={item.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task PublicBooking_AgainstNonBookableItem_ReturnsBadRequest()
    {
        var (token, slug) = await RegisterAndLoginBusiness("nonbookable-book@example.com", "nonbookable-book-shop");
        var item = await CreateNonBookableItem(token);

        var resp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(item.Id, "2026-07-06", "09:00", "Customer", "+15550001234", null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task AdminManualBooking_AgainstNonBookableItem_ReturnsBadRequest()
    {
        var (token, _) = await RegisterAndLoginBusiness("nonbookable-admin-book@example.com", "nonbookable-admin-book-shop");
        var item = await CreateNonBookableItem(token);

        Authorize(Client, token);
        var resp = await Client.PostAsJsonAsync("/api/admin/appointments", new CreateAdminAppointmentRequest(
            null, "Walk-in", "+15550005678", item.Id, "2026-07-06", "09:00", null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task RecurringSeries_AgainstNonBookableItem_ReturnsBadRequest()
    {
        var (token, _) = await RegisterAndLoginBusiness("nonbookable-recurring@example.com", "nonbookable-recurring-shop");
        var item = await CreateNonBookableItem(token);

        Authorize(Client, token);
        var resp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15550009999", item.Id, 0, "13:00", null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ExistingBookableItem_DefaultsIsBookableTrue_UnaffectedByGeneralization()
    {
        var (token, slug) = await RegisterAndLoginBusiness("bookable-unaffected@example.com", "bookable-unaffected-shop");
        Authorize(Client, token);
        var createResp = await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Haircut", "Haircut", "Haircut", 30, 50m));
        var item = await createResp.Content.ReadFromJsonAsync<ItemDto>();
        Client.DefaultRequestHeaders.Authorization = null;

        Assert.True(item!.IsBookable);
        Assert.Equal(30, item.DurationMinutes);

        var resp = await Client.GetAsync($"/api/{slug}/availability?date=2026-07-06&itemId={item.Id}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
