using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

public class BookingFlowTests : IntegrationTestBase
{
    // A Mon-Fri date, always after "today" -- AuthController.Register seeds default Mon-Fri
    // 09:00-18:00 hours, and a same-day date would be rejected by AvailabilityService's
    // "isToday" past-time cutoff depending on when the suite happens to run. Computed rather
    // than hardcoded so this doesn't go stale the way a fixed date eventually would.
    private static readonly string TestDate = NextWeekday(DateTime.Now.Date.AddDays(1)).ToString("yyyy-MM-dd");

    private static DateTime NextWeekday(DateTime from)
    {
        var d = from;
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
        return d;
    }

    // Two Mon-Fri dates in the same Sun-Sat calendar week, both strictly after "today" --
    // anchored on the next future Monday (rather than TestDate above) so there's always a
    // Tuesday afterward in that same week regardless of which weekday TestDate lands on.
    private static (string First, string Second) TwoWeekdaysInSameFutureWeek()
    {
        var tomorrow = DateTime.Now.Date.AddDays(1);
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)tomorrow.DayOfWeek + 7) % 7;
        var monday = tomorrow.AddDays(daysUntilMonday);
        return (monday.ToString("yyyy-MM-dd"), monday.AddDays(1).ToString("yyyy-MM-dd"));
    }

    private record AvailabilityResponse(List<TimeSlot> Slots);
    private record RegisterResponse(string? DevCode);

    private async Task<(string Token, string Slug)> RegisterAndLoginBusiness(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var body = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        return (body!.Token, slug);
    }

    private async Task<string> CreateService(string businessToken)
    {
        Authorize(Client, businessToken);
        var resp = await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Haircut", "Haircut", "Haircut", 30, 50m));
        var service = await resp.Content.ReadFromJsonAsync<ItemDto>();
        Client.DefaultRequestHeaders.Authorization = null;
        return service!.Id;
    }

    private async Task<string> GetCustomerToken(string phone, string name = "First", string familyName = "Last") =>
        (await LoginCustomerViaWhatsAppAsync(phone, name, familyName)).Token;

    private async Task<List<TimeSlot>> AvailableSlots(string slug, string itemId, string? date = null)
    {
        date ??= TestDate;
        var resp = await Client.GetAsync($"/api/{slug}/availability?date={date}&itemId={itemId}");
        var body = await resp.Content.ReadFromJsonAsync<AvailabilityResponse>();
        return body!.Slots;
    }

    private async Task<string> FirstAvailableSlot(string slug, string itemId, string? date = null)
    {
        var slots = await AvailableSlots(slug, itemId, date);
        Assert.NotEmpty(slots);
        return slots[0].Start;
    }

    private async Task SetBookingLimits(string businessToken, int? perDay, int? perWeek)
    {
        Authorize(Client, businessToken);
        var resp = await Client.PatchAsJsonAsync("/api/admin/settings",
            new UpdateSettingsRequest(null, null, null, null, perDay, perWeek));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Client.DefaultRequestHeaders.Authorization = null;
    }

    private Task<HttpResponseMessage> Book(string slug, string itemId, string date, string startTime, string phone) =>
        Client.PostAsJsonAsync($"/api/{slug}/appointments", new BookAppointmentRequest(itemId, date, startTime, "Customer", phone, null));

    [Fact]
    public async Task Booking_StoresFirstAndFamilyNameSeparately()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("split-name-flow@example.com", "split-name-flow-shop");
        var itemId = await CreateService(businessToken);
        var slot = await FirstAvailableSlot(slug, itemId);

        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments", new BookAppointmentRequest(
            itemId, TestDate, slot, "Jane", "+15553330099", null, CustomerFamilyName: "Doe"));
        Assert.Equal(HttpStatusCode.Created, bookResp.StatusCode);
        var booked = await bookResp.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var detail = await (await Client.GetAsync($"/api/{slug}/appointments/{booked!.AppointmentId}")).Content.ReadFromJsonAsync<AppointmentDetailDto>();
        Assert.Equal("Jane", detail!.Customer.Name);
        Assert.Equal("Doe", detail.Customer.FamilyName);
    }

    [Fact]
    public async Task GuestBooking_CanBookViewAndCancel_WithoutAuth()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("guest-flow@example.com", "guest-flow-shop");
        var itemId = await CreateService(businessToken);
        var slot = await FirstAvailableSlot(slug, itemId);

        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(itemId, TestDate, slot, "Guest Person", "+15553330001", "note"));
        Assert.Equal(HttpStatusCode.Created, bookResp.StatusCode);
        var booked = await bookResp.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var view = await Client.GetAsync($"/api/{slug}/appointments/{booked!.AppointmentId}");
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        var detail = await view.Content.ReadFromJsonAsync<AppointmentDetailDto>();
        Assert.Equal("+15553330001", detail!.Customer.Phone);

        var wrongToken = await Client.DeleteAsync($"/api/{slug}/appointments/{booked.AppointmentId}?token=wrong-token");
        Assert.Equal(HttpStatusCode.Forbidden, wrongToken.StatusCode);

        var cancel = await Client.DeleteAsync($"/api/{slug}/appointments/{booked.AppointmentId}?token={booked.CancelToken}");
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedBooking_PhoneIsOverriddenFromTokenNotSpoofedBody()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("auth-flow@example.com", "auth-flow-shop");
        var itemId = await CreateService(businessToken);
        var slot = await FirstAvailableSlot(slug, itemId);

        const string verifiedPhone = "+15553330002";
        const string spoofedPhone = "+19998887777";
        var customerToken = await GetCustomerToken(verifiedPhone);

        Authorize(Client, customerToken);
        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(itemId, TestDate, slot, "Spoofed Name", spoofedPhone, null));
        Assert.Equal(HttpStatusCode.Created, bookResp.StatusCode);
        var booked = await bookResp.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        Client.DefaultRequestHeaders.Authorization = null;
        var detail = await (await Client.GetAsync($"/api/{slug}/appointments/{booked!.AppointmentId}")).Content.ReadFromJsonAsync<AppointmentDetailDto>();
        Assert.Equal(verifiedPhone, detail!.Customer.Phone);

        Authorize(Client, customerToken);
        var mine = await Client.GetFromJsonAsync<List<CustomerAppointmentDto>>("/api/customer/appointments");
        Assert.Contains(mine!, a => a.Id == booked.AppointmentId);
    }

    [Fact]
    public async Task Booking_AutomaticallyFollowsTheBusiness_ForAnAuthenticatedCustomer()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("autofollow-flow@example.com", "autofollow-flow-shop");
        var itemId = await CreateService(businessToken);
        var slot = await FirstAvailableSlot(slug, itemId);
        var customerToken = await GetCustomerToken("+15553330006");

        Authorize(Client, customerToken);
        var followedBefore = await Client.GetFromJsonAsync<List<BusinessSearchResultDto>>("/api/businesses/followed");
        Assert.DoesNotContain(followedBefore!, b => b.Slug == slug);

        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(itemId, TestDate, slot, "Auto Follow", "+15553330006", null));
        Assert.Equal(HttpStatusCode.Created, bookResp.StatusCode);

        var followedAfter = await Client.GetFromJsonAsync<List<BusinessSearchResultDto>>("/api/businesses/followed");
        Assert.Contains(followedAfter!, b => b.Slug == slug);
    }

    [Fact]
    public async Task GuestBooking_DoesNotCreateAFollow_NoAccountToAttachItTo()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("guestfollow-flow@example.com", "guestfollow-flow-shop");
        var itemId = await CreateService(businessToken);
        var slot = await FirstAvailableSlot(slug, itemId);

        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(itemId, TestDate, slot, "Guest", "+15553330007", null));
        Assert.Equal(HttpStatusCode.Created, bookResp.StatusCode);

        // Same phone later creates an account — should NOT have inherited a follow from the
        // earlier guest booking (there was no account to attach it to at the time).
        var customerToken = await GetCustomerToken("+15553330007");
        Authorize(Client, customerToken);
        var followed = await Client.GetFromJsonAsync<List<BusinessSearchResultDto>>("/api/businesses/followed");
        Assert.DoesNotContain(followed!, b => b.Slug == slug);
    }

    [Fact]
    public async Task FollowUnfollow_UpdatesIsFollowedAcrossEndpoints()
    {
        var (_, slug) = await RegisterAndLoginBusiness("follow-flow@example.com", "follow-flow-shop");
        var customerToken = await GetCustomerToken("+15553330003");
        Authorize(Client, customerToken);

        var follow = await Client.PostAsync($"/api/businesses/{slug}/follow", null);
        Assert.Equal(HttpStatusCode.OK, follow.StatusCode);

        var followed = await Client.GetFromJsonAsync<List<BusinessSearchResultDto>>("/api/businesses/followed");
        Assert.Contains(followed!, b => b.Slug == slug);

        var info = await Client.GetFromJsonAsync<PublicBusinessDto>($"/api/{slug}/info");
        Assert.True(info!.IsFollowed);

        var unfollow = await Client.DeleteAsync($"/api/businesses/{slug}/follow");
        Assert.Equal(HttpStatusCode.OK, unfollow.StatusCode);

        var followedAfter = await Client.GetFromJsonAsync<List<BusinessSearchResultDto>>("/api/businesses/followed");
        Assert.DoesNotContain(followedAfter!, b => b.Slug == slug);
    }

    [Fact]
    public async Task CustomerAppointments_OwnershipIsEnforced()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("owner-flow@example.com", "owner-flow-shop");
        var itemId = await CreateService(businessToken);
        var slot = await FirstAvailableSlot(slug, itemId);

        var ownerToken = await GetCustomerToken("+15553330004");
        Authorize(Client, ownerToken);
        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(itemId, TestDate, slot, "Owner", "+15553330004", null));
        var booked = await bookResp.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var intruderToken = await GetCustomerToken("+15553330005");
        Authorize(Client, intruderToken);
        var intruderCancel = await Client.PostAsync($"/api/customer/appointments/{booked!.AppointmentId}/cancel", null);
        Assert.Equal(HttpStatusCode.NotFound, intruderCancel.StatusCode);

        Authorize(Client, ownerToken);
        var note = await Client.PatchAsJsonAsync($"/api/customer/appointments/{booked.AppointmentId}/notes", new { notes = "my note" });
        Assert.Equal(HttpStatusCode.OK, note.StatusCode);

        var ownerCancel = await Client.PostAsync($"/api/customer/appointments/{booked.AppointmentId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, ownerCancel.StatusCode);
    }

    [Fact]
    public async Task NoLimitSet_AllowsMultipleBookingsSameDay()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("nolimit-flow@example.com", "nolimit-flow-shop");
        var itemId = await CreateService(businessToken);
        var slots = await AvailableSlots(slug, itemId);
        Assert.True(slots.Count >= 2, "test needs at least two available slots the same day");

        var first = await Book(slug, itemId, TestDate, slots[0].Start, "+15553330010");
        var second = await Book(slug, itemId, TestDate, slots[1].Start, "+15553330010");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task MaxBookingsPerDay_RejectsOnceLimitReached()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("perday-flow@example.com", "perday-flow-shop");
        var itemId = await CreateService(businessToken);
        var slots = await AvailableSlots(slug, itemId);
        Assert.True(slots.Count >= 2, "test needs at least two available slots the same day");
        await SetBookingLimits(businessToken, perDay: 1, perWeek: null);

        var first = await Book(slug, itemId, TestDate, slots[0].Start, "+15553330011");
        var second = await Book(slug, itemId, TestDate, slots[1].Start, "+15553330011");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task MaxBookingsPerWeek_RejectsOnceLimitReachedAcrossDifferentDays()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("perweek-flow@example.com", "perweek-flow-shop");
        var itemId = await CreateService(businessToken);
        var (mondayDate, tuesdaySameWeek) = TwoWeekdaysInSameFutureWeek();
        await SetBookingLimits(businessToken, perDay: null, perWeek: 1);

        var monday = await Book(slug, itemId, mondayDate, (await FirstAvailableSlot(slug, itemId, mondayDate)), "+15553330012");
        var tuesday = await Book(slug, itemId, tuesdaySameWeek, (await FirstAvailableSlot(slug, itemId, tuesdaySameWeek)), "+15553330012");

        Assert.Equal(HttpStatusCode.Created, monday.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, tuesday.StatusCode);
    }

    [Fact]
    public async Task MaxBookingsPerDay_AppliesRegardlessOfLoginState_MatchedByPhone()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("mixed-flow@example.com", "mixed-flow-shop");
        var itemId = await CreateService(businessToken);
        var slots = await AvailableSlots(slug, itemId);
        Assert.True(slots.Count >= 2, "test needs at least two available slots the same day");
        await SetBookingLimits(businessToken, perDay: 1, perWeek: null);
        const string phone = "+15553330013";

        var customerToken = await GetCustomerToken(phone);
        Authorize(Client, customerToken);
        var authenticated = await Book(slug, itemId, TestDate, slots[0].Start, phone);
        Client.DefaultRequestHeaders.Authorization = null;

        // Same phone, now booking as a guest — must still count toward the same limit.
        var guest = await Book(slug, itemId, TestDate, slots[1].Start, phone);

        Assert.Equal(HttpStatusCode.Created, authenticated.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, guest.StatusCode);
    }
}
