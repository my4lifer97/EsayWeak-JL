using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

public class BookingFlowTests : IntegrationTestBase
{
    // 2026-07-06 is a Monday; AuthController.Register seeds default Mon-Fri 09:00-18:00 hours.
    private const string TestDate = "2026-07-06";

    private record OtpRequestResponse(bool IsNewCustomer, string? DevOtp);
    private record VerifyOtpResponse(string Token, string CustomerId, string Phone);
    private record AvailabilityResponse(List<TimeSlot> Slots);
    private record RegisterResponse(string? DevCode);

    private async Task<(string Token, string Slug)> RegisterAndLoginBarber(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Barber", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var body = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        return (body!.Token, slug);
    }

    private async Task<string> CreateService(string barberToken)
    {
        Authorize(Client, barberToken);
        var resp = await Client.PostAsJsonAsync("/api/admin/services", new CreateServiceRequest("Haircut", "Haircut", "Haircut", 30, 50m));
        var service = await resp.Content.ReadFromJsonAsync<ServiceDto>();
        Client.DefaultRequestHeaders.Authorization = null;
        return service!.Id;
    }

    private async Task<string> GetCustomerToken(string phone, string name = "First", string familyName = "Last")
    {
        var otpResp = await Client.PostAsJsonAsync("/api/customer/auth/otp", new RequestCustomerOtpRequest(phone));
        var otpBody = await otpResp.Content.ReadFromJsonAsync<OtpRequestResponse>();
        var verify = await Client.PostAsJsonAsync("/api/customer/auth/verify", new VerifyCustomerOtpRequest(phone, otpBody!.DevOtp!, name, familyName));
        var verifyBody = await verify.Content.ReadFromJsonAsync<VerifyOtpResponse>();
        return verifyBody!.Token;
    }

    private async Task<List<TimeSlot>> AvailableSlots(string slug, string serviceId, string date = TestDate)
    {
        var resp = await Client.GetAsync($"/api/{slug}/availability?date={date}&serviceId={serviceId}");
        var body = await resp.Content.ReadFromJsonAsync<AvailabilityResponse>();
        return body!.Slots;
    }

    private async Task<string> FirstAvailableSlot(string slug, string serviceId)
    {
        var slots = await AvailableSlots(slug, serviceId);
        Assert.NotEmpty(slots);
        return slots[0].Start;
    }

    private async Task SetBookingLimits(string barberToken, int? perDay, int? perWeek)
    {
        Authorize(Client, barberToken);
        var resp = await Client.PatchAsJsonAsync("/api/admin/settings",
            new UpdateSettingsRequest(null, null, null, null, null, null, null, perDay, perWeek));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Client.DefaultRequestHeaders.Authorization = null;
    }

    private Task<HttpResponseMessage> Book(string slug, string serviceId, string date, string startTime, string phone) =>
        Client.PostAsJsonAsync($"/api/{slug}/appointments", new BookAppointmentRequest(serviceId, date, startTime, "Customer", phone, null));

    [Fact]
    public async Task GuestBooking_CanBookViewAndCancel_WithoutAuth()
    {
        var (barberToken, slug) = await RegisterAndLoginBarber("guest-flow@example.com", "guest-flow-shop");
        var serviceId = await CreateService(barberToken);
        var slot = await FirstAvailableSlot(slug, serviceId);

        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(serviceId, TestDate, slot, "Guest Person", "+15553330001", "note"));
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
        var (barberToken, slug) = await RegisterAndLoginBarber("auth-flow@example.com", "auth-flow-shop");
        var serviceId = await CreateService(barberToken);
        var slot = await FirstAvailableSlot(slug, serviceId);

        const string verifiedPhone = "+15553330002";
        const string spoofedPhone = "+19998887777";
        var customerToken = await GetCustomerToken(verifiedPhone);

        Authorize(Client, customerToken);
        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(serviceId, TestDate, slot, "Spoofed Name", spoofedPhone, null));
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
    public async Task Booking_AutomaticallyFollowsTheBarber_ForAnAuthenticatedCustomer()
    {
        var (barberToken, slug) = await RegisterAndLoginBarber("autofollow-flow@example.com", "autofollow-flow-shop");
        var serviceId = await CreateService(barberToken);
        var slot = await FirstAvailableSlot(slug, serviceId);
        var customerToken = await GetCustomerToken("+15553330006");

        Authorize(Client, customerToken);
        var followedBefore = await Client.GetFromJsonAsync<List<BarberSearchResultDto>>("/api/barbers/followed");
        Assert.DoesNotContain(followedBefore!, b => b.Slug == slug);

        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(serviceId, TestDate, slot, "Auto Follow", "+15553330006", null));
        Assert.Equal(HttpStatusCode.Created, bookResp.StatusCode);

        var followedAfter = await Client.GetFromJsonAsync<List<BarberSearchResultDto>>("/api/barbers/followed");
        Assert.Contains(followedAfter!, b => b.Slug == slug);
    }

    [Fact]
    public async Task GuestBooking_DoesNotCreateAFollow_NoAccountToAttachItTo()
    {
        var (barberToken, slug) = await RegisterAndLoginBarber("guestfollow-flow@example.com", "guestfollow-flow-shop");
        var serviceId = await CreateService(barberToken);
        var slot = await FirstAvailableSlot(slug, serviceId);

        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(serviceId, TestDate, slot, "Guest", "+15553330007", null));
        Assert.Equal(HttpStatusCode.Created, bookResp.StatusCode);

        // Same phone later creates an account — should NOT have inherited a follow from the
        // earlier guest booking (there was no account to attach it to at the time).
        var customerToken = await GetCustomerToken("+15553330007");
        Authorize(Client, customerToken);
        var followed = await Client.GetFromJsonAsync<List<BarberSearchResultDto>>("/api/barbers/followed");
        Assert.DoesNotContain(followed!, b => b.Slug == slug);
    }

    [Fact]
    public async Task FollowUnfollow_UpdatesIsFollowedAcrossEndpoints()
    {
        var (_, slug) = await RegisterAndLoginBarber("follow-flow@example.com", "follow-flow-shop");
        var customerToken = await GetCustomerToken("+15553330003");
        Authorize(Client, customerToken);

        var follow = await Client.PostAsync($"/api/barbers/{slug}/follow", null);
        Assert.Equal(HttpStatusCode.OK, follow.StatusCode);

        var followed = await Client.GetFromJsonAsync<List<BarberSearchResultDto>>("/api/barbers/followed");
        Assert.Contains(followed!, b => b.Slug == slug);

        var info = await Client.GetFromJsonAsync<PublicBarberDto>($"/api/{slug}/info");
        Assert.True(info!.IsFollowed);

        var unfollow = await Client.DeleteAsync($"/api/barbers/{slug}/follow");
        Assert.Equal(HttpStatusCode.OK, unfollow.StatusCode);

        var followedAfter = await Client.GetFromJsonAsync<List<BarberSearchResultDto>>("/api/barbers/followed");
        Assert.DoesNotContain(followedAfter!, b => b.Slug == slug);
    }

    [Fact]
    public async Task CustomerAppointments_OwnershipIsEnforced()
    {
        var (barberToken, slug) = await RegisterAndLoginBarber("owner-flow@example.com", "owner-flow-shop");
        var serviceId = await CreateService(barberToken);
        var slot = await FirstAvailableSlot(slug, serviceId);

        var ownerToken = await GetCustomerToken("+15553330004");
        Authorize(Client, ownerToken);
        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(serviceId, TestDate, slot, "Owner", "+15553330004", null));
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
        var (barberToken, slug) = await RegisterAndLoginBarber("nolimit-flow@example.com", "nolimit-flow-shop");
        var serviceId = await CreateService(barberToken);
        var slots = await AvailableSlots(slug, serviceId);
        Assert.True(slots.Count >= 2, "test needs at least two available slots the same day");

        var first = await Book(slug, serviceId, TestDate, slots[0].Start, "+15553330010");
        var second = await Book(slug, serviceId, TestDate, slots[1].Start, "+15553330010");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task MaxBookingsPerDay_RejectsOnceLimitReached()
    {
        var (barberToken, slug) = await RegisterAndLoginBarber("perday-flow@example.com", "perday-flow-shop");
        var serviceId = await CreateService(barberToken);
        var slots = await AvailableSlots(slug, serviceId);
        Assert.True(slots.Count >= 2, "test needs at least two available slots the same day");
        await SetBookingLimits(barberToken, perDay: 1, perWeek: null);

        var first = await Book(slug, serviceId, TestDate, slots[0].Start, "+15553330011");
        var second = await Book(slug, serviceId, TestDate, slots[1].Start, "+15553330011");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task MaxBookingsPerWeek_RejectsOnceLimitReachedAcrossDifferentDays()
    {
        var (barberToken, slug) = await RegisterAndLoginBarber("perweek-flow@example.com", "perweek-flow-shop");
        var serviceId = await CreateService(barberToken);
        const string tuesdaySameWeek = "2026-07-07"; // Monday 2026-07-06's week (Sun 07-05 - Sat 07-11)
        await SetBookingLimits(barberToken, perDay: null, perWeek: 1);

        var monday = await Book(slug, serviceId, TestDate, (await FirstAvailableSlot(slug, serviceId)), "+15553330012");
        var tuesday = await Book(slug, serviceId, tuesdaySameWeek, (await FirstAvailableSlot(slug, serviceId)), "+15553330012");

        Assert.Equal(HttpStatusCode.Created, monday.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, tuesday.StatusCode);
    }

    [Fact]
    public async Task MaxBookingsPerDay_AppliesRegardlessOfLoginState_MatchedByPhone()
    {
        var (barberToken, slug) = await RegisterAndLoginBarber("mixed-flow@example.com", "mixed-flow-shop");
        var serviceId = await CreateService(barberToken);
        var slots = await AvailableSlots(slug, serviceId);
        Assert.True(slots.Count >= 2, "test needs at least two available slots the same day");
        await SetBookingLimits(barberToken, perDay: 1, perWeek: null);
        const string phone = "+15553330013";

        var customerToken = await GetCustomerToken(phone);
        Authorize(Client, customerToken);
        var authenticated = await Book(slug, serviceId, TestDate, slots[0].Start, phone);
        Client.DefaultRequestHeaders.Authorization = null;

        // Same phone, now booking as a guest — must still count toward the same limit.
        var guest = await Book(slug, serviceId, TestDate, slots[1].Start, phone);

        Assert.Equal(HttpStatusCode.Created, authenticated.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, guest.StatusCode);
    }
}
