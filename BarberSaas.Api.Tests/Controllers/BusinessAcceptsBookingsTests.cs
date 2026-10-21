using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// QA run 4 PUB-1/PUB-2: a disabled business still took bookings nobody at it could see, and an
// ended trial stayed listed and bookable until the final Confirm.
public class BusinessAcceptsBookingsTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);
    private record IdDto(string Id);
    private record InfoResponse(bool AcceptsBookings);
    private record SearchItem(string Slug);
    private record SearchResponse(List<SearchItem> Items);

    private async Task<(string BusinessId, string ItemId)> Seed(string slug)
    {
        var email = $"{slug}@example.com";
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Shop", email, "password123", slug));
        var code = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.DevCode!;
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code));
        Authorize(Client, (await verify.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        var item = await (await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Cut", "Cut", "Cut", 30, 20m))).Content.ReadFromJsonAsync<IdDto>();
        Client.DefaultRequestHeaders.Authorization = null;
        using var db = Db();
        return (await db.Businesses.Where(b => b.Slug == slug).Select(b => b.Id).FirstAsync(), item!.Id);
    }

    private async Task Close(string businessId, string how)
    {
        using var db = Db();
        var b = await db.Businesses.FirstAsync(x => x.Id == businessId);
        if (how == "disabled") b.IsDisabled = true;
        else b.TrialEndsAt = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
    }

    private static string NextWeekday() => AppClock.Now.Date.AddDays(((int)DayOfWeek.Monday - (int)AppClock.Now.DayOfWeek + 7) % 7 + 7).ToString("yyyy-MM-dd");

    [Theory]
    [InlineData("disabled")]
    [InlineData("trial-ended")]
    public async Task ClosedBusiness_IsNotListed_SaysSoOnInfo_AndRefusesBookings(string how)
    {
        var slug = $"closed-{how}";
        var (businessId, itemId) = await Seed(slug);
        await Close(businessId, how);

        var search = await Client.GetFromJsonAsync<SearchResponse>($"/api/businesses/search?query={slug}");
        var info = await Client.GetFromJsonAsync<InfoResponse>($"/api/{slug}/info");
        var session = await LoginCustomerViaWhatsAppAsync("+15554070001");
        Authorize(Client, session.Token);
        var book = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(itemId, NextWeekday(), "10:00", "Dana", "", null, CustomerFamilyName: "Levi"));

        Assert.DoesNotContain(search!.Items, i => i.Slug == slug);
        Assert.False(info!.AcceptsBookings);
        Assert.Equal(HttpStatusCode.Forbidden, book.StatusCode);
        Assert.Contains("isn't taking online bookings", await book.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OpenBusiness_SaysItAcceptsBookings()
    {
        await Seed("open-shop");
        var info = await Client.GetFromJsonAsync<InfoResponse>("/api/open-shop/info");
        Assert.True(info!.AcceptsBookings);
    }

    [Fact]
    public async Task ClosedBusiness_RefusesReschedules_ButStillLetsTheCustomerCancel()
    {
        var (businessId, itemId) = await Seed("closed-later");
        var session = await LoginCustomerViaWhatsAppAsync("+15554070002");
        Authorize(Client, session.Token);
        var date = NextWeekday();
        var book = await Client.PostAsJsonAsync("/api/closed-later/appointments", new BookAppointmentRequest(itemId, date, "10:00", "Dana", "", null, CustomerFamilyName: "Levi"));
        var booked = await book.Content.ReadFromJsonAsync<BookAppointmentResponse>();
        await Close(businessId, "disabled");

        var moveMine = await Client.PatchAsJsonAsync($"/api/customer/appointments/{booked!.AppointmentId}/reschedule", new RescheduleRequest(date, "11:00"));
        var moveByLink = await Client.PatchAsJsonAsync($"/api/closed-later/appointments/{booked.AppointmentId}?token={booked.CancelToken}", new RescheduleRequest(date, "11:00"));
        var cancel = await Client.PostAsync($"/api/customer/appointments/{booked.AppointmentId}/cancel", null);

        Assert.Equal(HttpStatusCode.Forbidden, moveMine.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, moveByLink.StatusCode);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
    }
}
