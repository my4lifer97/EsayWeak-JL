using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

public class ServicePhotoTests : IntegrationTestBase
{
    private const string TestDate = "2026-07-06"; // Monday; AuthController.Register seeds Mon-Fri 09:00-18:00 hours.

    private record RegisterResponse(string? DevCode);
    private record AvailabilityResponse(List<TimeSlot> Slots);
    private record UploadPhotoResponse(string Url);
    private async Task<string> GetCustomerToken(string phone, string name = "First", string familyName = "Last") =>
        (await LoginCustomerViaWhatsAppAsync(phone, name, familyName)).Token;

    private async Task<(string Token, string Slug)> RegisterAndLoginBusiness(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var body = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        return (body!.Token, slug);
    }

    private async Task<ItemDto> CreateService(string businessToken, string photoMode)
    {
        Authorize(Client, businessToken);
        var resp = await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Haircut", "Haircut", "Haircut", 30, 50m, photoMode));
        var service = await resp.Content.ReadFromJsonAsync<ItemDto>();
        Client.DefaultRequestHeaders.Authorization = null;
        return service!;
    }

    private static MultipartFormDataContent FakeImage(string fileName = "photo.jpg", string contentType = "image/jpeg")
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([1, 2, 3, 4]);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        return content;
    }

    private async Task<string> FirstAvailableSlot(string slug, string itemId, string date = TestDate)
    {
        var resp = await Client.GetAsync($"/api/{slug}/availability?date={date}&itemId={itemId}");
        var body = await resp.Content.ReadFromJsonAsync<AvailabilityResponse>();
        Assert.NotEmpty(body!.Slots);
        return body.Slots[0].Start;
    }

    // TestDate is a fixed calendar date; the tests below read the appointment's *effective*
    // status (AppointmentStatusHelper), which auto-flips CONFIRMED -> COMPLETED once its end
    // time passes real wall-clock time — so unlike the other tests in this file, they need a
    // date that's still in the future no matter when the suite happens to run.
    private static string NextWeekdayDate()
    {
        var d = DateTime.Now.AddDays(1);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
        return d.ToString("yyyy-MM-dd");
    }

    [Fact]
    public async Task CreateService_WithOwnerGalleryMode_PersistsPhotoMode()
    {
        var (businessToken, _) = await RegisterAndLoginBusiness("photomode-create@example.com", "photomode-create-shop");
        var service = await CreateService(businessToken, "OwnerGallery");

        Assert.Equal("OwnerGallery", service.PhotoMode);
        Assert.Empty(service.GalleryPhotos);
    }

    [Fact]
    public async Task CreateService_WithInvalidPhotoMode_ReturnsBadRequest()
    {
        var (businessToken, _) = await RegisterAndLoginBusiness("photomode-invalid@example.com", "photomode-invalid-shop");
        Authorize(Client, businessToken);

        var resp = await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Haircut", "Haircut", "Haircut", 30, 50m, "NotARealMode"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task UploadGalleryPhoto_ByOwner_Succeeds_AndListedInItemDto()
    {
        var (businessToken, _) = await RegisterAndLoginBusiness("gallery-upload@example.com", "gallery-upload-shop");
        var service = await CreateService(businessToken, "OwnerGallery");

        Authorize(Client, businessToken);
        var uploadResp = await Client.PostAsync($"/api/admin/items/{service.Id}/gallery", FakeImage());
        Assert.Equal(HttpStatusCode.Created, uploadResp.StatusCode);
        var uploaded = await uploadResp.Content.ReadFromJsonAsync<ItemGalleryPhotoDto>();
        Assert.False(string.IsNullOrWhiteSpace(uploaded!.Url));

        var services = await Client.GetFromJsonAsync<List<ItemDto>>("/api/admin/items");
        var updated = services!.Single(s => s.Id == service.Id);
        Assert.Single(updated.GalleryPhotos);
        Assert.Equal(uploaded.Id, updated.GalleryPhotos[0].Id);
    }

    [Fact]
    public async Task UploadGalleryPhoto_RejectsWrongContentType()
    {
        var (businessToken, _) = await RegisterAndLoginBusiness("gallery-badtype@example.com", "gallery-badtype-shop");
        var service = await CreateService(businessToken, "OwnerGallery");

        Authorize(Client, businessToken);
        var resp = await Client.PostAsync($"/api/admin/items/{service.Id}/gallery", FakeImage(fileName: "photo.jpg", contentType: "text/plain"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task UploadGalleryPhoto_ForAnotherBusinessesService_ReturnsNotFound()
    {
        var (ownerToken, _) = await RegisterAndLoginBusiness("gallery-owner@example.com", "gallery-owner-shop");
        var service = await CreateService(ownerToken, "OwnerGallery");
        var (intruderToken, _) = await RegisterAndLoginBusiness("gallery-intruder@example.com", "gallery-intruder-shop");

        Authorize(Client, intruderToken);
        var resp = await Client.PostAsync($"/api/admin/items/{service.Id}/gallery", FakeImage());

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteGalleryPhoto_ByOwner_RemovesIt()
    {
        var (businessToken, _) = await RegisterAndLoginBusiness("gallery-delete@example.com", "gallery-delete-shop");
        var service = await CreateService(businessToken, "OwnerGallery");

        Authorize(Client, businessToken);
        var uploadResp = await Client.PostAsync($"/api/admin/items/{service.Id}/gallery", FakeImage());
        var uploaded = await uploadResp.Content.ReadFromJsonAsync<ItemGalleryPhotoDto>();

        var deleteResp = await Client.DeleteAsync($"/api/admin/items/{service.Id}/gallery/{uploaded!.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResp.StatusCode);

        var services = await Client.GetFromJsonAsync<List<ItemDto>>("/api/admin/items");
        Assert.Empty(services!.Single(s => s.Id == service.Id).GalleryPhotos);
    }

    [Fact]
    public async Task DeleteGalleryPhoto_ForAnotherBusinessesService_ReturnsNotFound()
    {
        var (ownerToken, _) = await RegisterAndLoginBusiness("gallery-del-owner@example.com", "gallery-del-owner-shop");
        var service = await CreateService(ownerToken, "OwnerGallery");
        Authorize(Client, ownerToken);
        var uploadResp = await Client.PostAsync($"/api/admin/items/{service.Id}/gallery", FakeImage());
        var uploaded = await uploadResp.Content.ReadFromJsonAsync<ItemGalleryPhotoDto>();

        var (intruderToken, _) = await RegisterAndLoginBusiness("gallery-del-intruder@example.com", "gallery-del-intruder-shop");
        Authorize(Client, intruderToken);
        var deleteResp = await Client.DeleteAsync($"/api/admin/items/{service.Id}/gallery/{uploaded!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, deleteResp.StatusCode);
    }

    [Fact]
    public async Task BookAppointment_OwnerGalleryMode_WithoutPhotoId_ReturnsBadRequest()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("book-gallery-missing@example.com", "book-gallery-missing-shop");
        var service = await CreateService(businessToken, "OwnerGallery");
        var slot = await FirstAvailableSlot(slug, service.Id);

        var resp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(service.Id, TestDate, slot, "Customer", "+15559990001", null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task BookAppointment_OwnerGalleryMode_WithValidPhotoId_SetsPhotoUrlOnAppointment()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("book-gallery-ok@example.com", "book-gallery-ok-shop");
        var service = await CreateService(businessToken, "OwnerGallery");
        Authorize(Client, businessToken);
        var uploadResp = await Client.PostAsync($"/api/admin/items/{service.Id}/gallery", FakeImage());
        var photo = await uploadResp.Content.ReadFromJsonAsync<ItemGalleryPhotoDto>();
        Client.DefaultRequestHeaders.Authorization = null;

        var slot = await FirstAvailableSlot(slug, service.Id);
        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(service.Id, TestDate, slot, "Customer", "+15559990002", null, GalleryPhotoId: photo!.Id));
        Assert.Equal(HttpStatusCode.Created, bookResp.StatusCode);
        var booked = await bookResp.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var detail = await Client.GetFromJsonAsync<AppointmentDetailDto>($"/api/{slug}/appointments/{booked!.AppointmentId}");
        Assert.Equal(photo.Url, detail!.PhotoUrl);
    }

    [Fact]
    public async Task BookAppointment_OwnerGalleryMode_WithPhotoIdFromDifferentService_ReturnsBadRequest()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("book-gallery-wrong@example.com", "book-gallery-wrong-shop");
        var service = await CreateService(businessToken, "OwnerGallery");
        var otherService = await CreateService(businessToken, "OwnerGallery");
        Authorize(Client, businessToken);
        var uploadResp = await Client.PostAsync($"/api/admin/items/{otherService.Id}/gallery", FakeImage());
        var photoOnOtherService = await uploadResp.Content.ReadFromJsonAsync<ItemGalleryPhotoDto>();
        Client.DefaultRequestHeaders.Authorization = null;

        var slot = await FirstAvailableSlot(slug, service.Id);
        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(service.Id, TestDate, slot, "Customer", "+15559990003", null, GalleryPhotoId: photoOnOtherService!.Id));

        Assert.Equal(HttpStatusCode.BadRequest, bookResp.StatusCode);
    }

    [Fact]
    public async Task BookAppointment_CustomerUploadMode_WithoutPhotoUrl_ReturnsBadRequest()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("book-upload-missing@example.com", "book-upload-missing-shop");
        var service = await CreateService(businessToken, "CustomerUpload");
        var slot = await FirstAvailableSlot(slug, service.Id);

        var resp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(service.Id, TestDate, slot, "Customer", "+15559990004", null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task BookAppointment_CustomerUploadMode_WithUploadedPhoto_SetsPhotoUrlOnAppointment()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("book-upload-ok@example.com", "book-upload-ok-shop");
        var service = await CreateService(businessToken, "CustomerUpload");

        var uploadResp = await Client.PostAsync($"/api/{slug}/appointments/photo", FakeImage());
        Assert.Equal(HttpStatusCode.OK, uploadResp.StatusCode);
        var uploaded = await uploadResp.Content.ReadFromJsonAsync<UploadPhotoResponse>();

        var slot = await FirstAvailableSlot(slug, service.Id);
        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(service.Id, TestDate, slot, "Customer", "+15559990005", null, CustomerPhotoUrl: uploaded!.Url));
        Assert.Equal(HttpStatusCode.Created, bookResp.StatusCode);
        var booked = await bookResp.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var detail = await Client.GetFromJsonAsync<AppointmentDetailDto>($"/api/{slug}/appointments/{booked!.AppointmentId}");
        Assert.Equal(uploaded.Url, detail!.PhotoUrl);
    }

    [Fact]
    public async Task BookAppointment_CustomerUploadMode_RejectsArbitraryUrlNotFromUploadEndpoint()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("book-upload-spoofed@example.com", "book-upload-spoofed-shop");
        var service = await CreateService(businessToken, "CustomerUpload");
        var slot = await FirstAvailableSlot(slug, service.Id);

        var resp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(service.Id, TestDate, slot, "Customer", "+15559990006", null, CustomerPhotoUrl: "https://evil.example.com/x.jpg"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task BookAppointment_NoneMode_DoesNotRequireOrStorePhoto()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("book-none@example.com", "book-none-shop");
        var service = await CreateService(businessToken, "None");
        var slot = await FirstAvailableSlot(slug, service.Id);

        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(service.Id, TestDate, slot, "Customer", "+15559990007", null));
        Assert.Equal(HttpStatusCode.Created, bookResp.StatusCode);
        var booked = await bookResp.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var detail = await Client.GetFromJsonAsync<AppointmentDetailDto>($"/api/{slug}/appointments/{booked!.AppointmentId}");
        Assert.Null(detail!.PhotoUrl);
    }

    [Fact]
    public async Task UpdatePhoto_OwnerGalleryMode_WithValidPhotoId_ChangesPhotoUrl()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("changephoto-gallery@example.com", "changephoto-gallery-shop");
        var service = await CreateService(businessToken, "OwnerGallery");
        Authorize(Client, businessToken);
        var firstUpload = await Client.PostAsync($"/api/admin/items/{service.Id}/gallery", FakeImage());
        var firstPhoto = await firstUpload.Content.ReadFromJsonAsync<ItemGalleryPhotoDto>();
        var secondUpload = await Client.PostAsync($"/api/admin/items/{service.Id}/gallery", FakeImage());
        var secondPhoto = await secondUpload.Content.ReadFromJsonAsync<ItemGalleryPhotoDto>();
        Client.DefaultRequestHeaders.Authorization = null;

        var customerToken = await GetCustomerToken("+15559990101");
        Authorize(Client, customerToken);
        var date = NextWeekdayDate();
        var slot = await FirstAvailableSlot(slug, service.Id, date);
        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(service.Id, date, slot, "Customer", "+15559990101", null, GalleryPhotoId: firstPhoto!.Id));
        var booked = await bookResp.Content.ReadFromJsonAsync<BookAppointmentResponse>();

        var mine = await Client.GetFromJsonAsync<List<CustomerAppointmentDto>>("/api/customer/appointments?filter=all");
        var apptId = mine!.Single(a => a.CancelToken == booked!.CancelToken).Id;

        var changeResp = await Client.PatchAsJsonAsync($"/api/customer/appointments/{apptId}/photo",
            new UpdateAppointmentPhotoRequest(secondPhoto!.Id, null));
        Assert.Equal(HttpStatusCode.OK, changeResp.StatusCode);

        var detail = await Client.GetFromJsonAsync<AppointmentDetailDto>($"/api/{slug}/appointments/{booked!.AppointmentId}");
        Assert.Equal(secondPhoto.Url, detail!.PhotoUrl);
    }

    [Fact]
    public async Task UpdatePhoto_OwnerGalleryMode_WithoutPhotoId_ReturnsBadRequest()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("changephoto-gallery-missing@example.com", "changephoto-gallery-missing-shop");
        var service = await CreateService(businessToken, "OwnerGallery");
        Authorize(Client, businessToken);
        var upload = await Client.PostAsync($"/api/admin/items/{service.Id}/gallery", FakeImage());
        var photo = await upload.Content.ReadFromJsonAsync<ItemGalleryPhotoDto>();
        Client.DefaultRequestHeaders.Authorization = null;

        var customerToken = await GetCustomerToken("+15559990102");
        Authorize(Client, customerToken);
        var date = NextWeekdayDate();
        var slot = await FirstAvailableSlot(slug, service.Id, date);
        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(service.Id, date, slot, "Customer", "+15559990102", null, GalleryPhotoId: photo!.Id));
        var booked = await bookResp.Content.ReadFromJsonAsync<BookAppointmentResponse>();
        var mine = await Client.GetFromJsonAsync<List<CustomerAppointmentDto>>("/api/customer/appointments?filter=all");
        var apptId = mine!.Single(a => a.CancelToken == booked!.CancelToken).Id;

        var changeResp = await Client.PatchAsJsonAsync($"/api/customer/appointments/{apptId}/photo",
            new UpdateAppointmentPhotoRequest(null, null));

        Assert.Equal(HttpStatusCode.BadRequest, changeResp.StatusCode);
    }

    [Fact]
    public async Task UpdatePhoto_CustomerUploadMode_WithNewUpload_ChangesPhotoUrl()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("changephoto-upload@example.com", "changephoto-upload-shop");
        var service = await CreateService(businessToken, "CustomerUpload");

        var firstUpload = await Client.PostAsync($"/api/{slug}/appointments/photo", FakeImage());
        var firstPhoto = await firstUpload.Content.ReadFromJsonAsync<UploadPhotoResponse>();

        var customerToken = await GetCustomerToken("+15559990103");
        Authorize(Client, customerToken);
        var date = NextWeekdayDate();
        var slot = await FirstAvailableSlot(slug, service.Id, date);
        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(service.Id, date, slot, "Customer", "+15559990103", null, CustomerPhotoUrl: firstPhoto!.Url));
        var booked = await bookResp.Content.ReadFromJsonAsync<BookAppointmentResponse>();
        var mine = await Client.GetFromJsonAsync<List<CustomerAppointmentDto>>("/api/customer/appointments?filter=all");
        var apptId = mine!.Single(a => a.CancelToken == booked!.CancelToken).Id;

        var secondUpload = await Client.PostAsync($"/api/{slug}/appointments/photo", FakeImage());
        var secondPhoto = await secondUpload.Content.ReadFromJsonAsync<UploadPhotoResponse>();
        var changeResp = await Client.PatchAsJsonAsync($"/api/customer/appointments/{apptId}/photo",
            new UpdateAppointmentPhotoRequest(null, secondPhoto!.Url));
        Assert.Equal(HttpStatusCode.OK, changeResp.StatusCode);

        var detail = await Client.GetFromJsonAsync<AppointmentDetailDto>($"/api/{slug}/appointments/{booked!.AppointmentId}");
        Assert.Equal(secondPhoto.Url, detail!.PhotoUrl);
    }

    [Fact]
    public async Task UpdatePhoto_ForAnotherCustomersAppointment_ReturnsNotFound()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("changephoto-intruder@example.com", "changephoto-intruder-shop");
        var service = await CreateService(businessToken, "OwnerGallery");
        Authorize(Client, businessToken);
        var upload = await Client.PostAsync($"/api/admin/items/{service.Id}/gallery", FakeImage());
        var photo = await upload.Content.ReadFromJsonAsync<ItemGalleryPhotoDto>();
        Client.DefaultRequestHeaders.Authorization = null;

        var ownerToken = await GetCustomerToken("+15559990104");
        Authorize(Client, ownerToken);
        var date = NextWeekdayDate();
        var slot = await FirstAvailableSlot(slug, service.Id, date);
        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(service.Id, date, slot, "Customer", "+15559990104", null, GalleryPhotoId: photo!.Id));
        var booked = await bookResp.Content.ReadFromJsonAsync<BookAppointmentResponse>();
        var mine = await Client.GetFromJsonAsync<List<CustomerAppointmentDto>>("/api/customer/appointments?filter=all");
        var apptId = mine!.Single(a => a.CancelToken == booked!.CancelToken).Id;

        var intruderToken = await GetCustomerToken("+15559990105");
        Authorize(Client, intruderToken);
        var changeResp = await Client.PatchAsJsonAsync($"/api/customer/appointments/{apptId}/photo",
            new UpdateAppointmentPhotoRequest(photo.Id, null));

        Assert.Equal(HttpStatusCode.NotFound, changeResp.StatusCode);
    }

    [Fact]
    public async Task UpdatePhoto_ForNoneModeService_ReturnsBadRequest()
    {
        var (businessToken, slug) = await RegisterAndLoginBusiness("changephoto-none@example.com", "changephoto-none-shop");
        var service = await CreateService(businessToken, "None");

        var customerToken = await GetCustomerToken("+15559990106");
        Authorize(Client, customerToken);
        var date = NextWeekdayDate();
        var slot = await FirstAvailableSlot(slug, service.Id, date);
        var bookResp = await Client.PostAsJsonAsync($"/api/{slug}/appointments",
            new BookAppointmentRequest(service.Id, date, slot, "Customer", "+15559990106", null));
        var booked = await bookResp.Content.ReadFromJsonAsync<BookAppointmentResponse>();
        var mine = await Client.GetFromJsonAsync<List<CustomerAppointmentDto>>("/api/customer/appointments?filter=all");
        var apptId = mine!.Single(a => a.CancelToken == booked!.CancelToken).Id;

        var changeResp = await Client.PatchAsJsonAsync($"/api/customer/appointments/{apptId}/photo",
            new UpdateAppointmentPhotoRequest("some-id", null));

        Assert.Equal(HttpStatusCode.BadRequest, changeResp.StatusCode);
    }
}
