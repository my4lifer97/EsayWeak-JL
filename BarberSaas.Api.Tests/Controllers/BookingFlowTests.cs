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

    private async Task<(string Token, string Slug)> RegisterAndLoginBarber(string email, string slug)
    {
        await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Barber", email, "password123", slug));
        var login = await Client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "password123"));
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
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

    private async Task<string> FirstAvailableSlot(string slug, string serviceId)
    {
        var resp = await Client.GetAsync($"/api/{slug}/availability?date={TestDate}&serviceId={serviceId}");
        var body = await resp.Content.ReadFromJsonAsync<AvailabilityResponse>();
        Assert.NotEmpty(body!.Slots);
        return body.Slots[0].Start;
    }

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
}
