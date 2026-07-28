using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

public class RecurringAppointmentsTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    private async Task<string> RegisterAndLoginBarber(string email, string slug)
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Barber", email, "password123", slug));
        var registerBody = await register.Content.ReadFromJsonAsync<RegisterResponse>();
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, registerBody!.DevCode!));
        var body = await verify.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.Token;
    }

    private async Task<string> SeedService(string token)
    {
        Authorize(Client, token);
        var resp = await Client.PostAsJsonAsync("/api/admin/services", new CreateServiceRequest("Cut", "Cut", "Cut", 30, 20m));
        var service = await resp.Content.ReadFromJsonAsync<ServiceDto>();
        return service!.Id;
    }

    [Fact]
    public async Task Create_NewCustomer_CreatesActiveSeries()
    {
        var slug = "recurring-create";
        var token = await RegisterAndLoginBarber("recurring-create@example.com", slug);
        var serviceId = await SeedService(token);

        Authorize(Client, token);
        var resp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551112222", serviceId, 0, "13:00", null));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<RecurringSeriesDto>();
        Assert.True(dto!.IsActive);
        Assert.Equal("Mohamed", dto.Customer.Name);
        Assert.Equal(0, dto.DayOfWeek);
        Assert.Equal("13:00", dto.StartTime);
    }

    private record AvailabilityWrapper(List<TimeSlot> Slots);

    [Fact]
    public async Task Create_ImmediatelyGeneratesFirstOccurrence_VisibleOnDashboardAndBlocksSlot()
    {
        var slug = "recurring-immediate";
        var token = await RegisterAndLoginBarber("recurring-immediate@example.com", slug);
        var serviceId = await SeedService(token);

        Authorize(Client, token);
        const int monday = 1; // AuthController.Register seeds default Mon-Fri 09:00-18:00 hours
        var resp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551119999", serviceId, monday, "09:00", null));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var series = await resp.Content.ReadFromJsonAsync<RecurringSeriesDto>();

        // Occurrences must exist right away -- not only after the next cron run -- so they
        // show up on the dashboard and block their slots from other bookings immediately.
        // Series creation fills the whole rolling horizon (several weeks), not just the very
        // next Monday, so assert on the soonest one rather than expecting exactly one row.
        var appointments = await Client.GetFromJsonAsync<List<DashboardAppointmentDto>>("/api/admin/appointments?filter=all");
        var generatedForSeries = appointments!.Where(a => a.RecurringSeriesId == series!.Id).ToList();
        Assert.NotEmpty(generatedForSeries);
        var generated = generatedForSeries.OrderBy(a => a.Date).First();
        Assert.Equal("09:00", generated.StartTime);

        var slots = await Client.GetFromJsonAsync<AvailabilityWrapper>(
            $"/api/admin/appointments/availability?date={generated.Date}&serviceId={serviceId}");
        Assert.DoesNotContain(slots!.Slots, s => s.Start == "09:00");
    }

    [Fact]
    public async Task Create_ExistingCustomer_LinksToThatCustomer()
    {
        var slug = "recurring-existing";
        var token = await RegisterAndLoginBarber("recurring-existing@example.com", slug);
        var serviceId = await SeedService(token);

        Authorize(Client, token);
        var bookResp = await Client.PostAsJsonAsync("/api/admin/appointments", new CreateAdminAppointmentRequest(
            null, "Mohamed", "+15551112222", serviceId, DateTime.Now.Date.AddDays(30).ToString("yyyy-MM-dd"), "09:00", null, Force: true));
        var booked = await bookResp.Content.ReadFromJsonAsync<DashboardAppointmentDto>();

        var resp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            booked!.Customer.Id, null, null, serviceId, 0, "13:00", null));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<RecurringSeriesDto>();
        Assert.Equal(booked.Customer.Id, dto!.Customer.Id);
    }

    [Fact]
    public async Task Create_InvalidDayOfWeek_ReturnsBadRequest()
    {
        var slug = "recurring-bad-day";
        var token = await RegisterAndLoginBarber("recurring-bad-day@example.com", slug);
        var serviceId = await SeedService(token);

        Authorize(Client, token);
        var resp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551112222", serviceId, 7, "13:00", null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_PastStartDate_ReturnsBadRequest()
    {
        var slug = "recurring-past-start";
        var token = await RegisterAndLoginBarber("recurring-past-start@example.com", slug);
        var serviceId = await SeedService(token);

        Authorize(Client, token);
        var pastDate = DateTime.Now.Date.AddDays(-7).ToString("yyyy-MM-dd");
        var resp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551112222", serviceId, 0, "13:00", null, pastDate));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_EndDateBeforeStartDate_ReturnsBadRequest()
    {
        var slug = "recurring-bad-range";
        var token = await RegisterAndLoginBarber("recurring-bad-range@example.com", slug);
        var serviceId = await SeedService(token);

        Authorize(Client, token);
        var startDate = DateTime.Now.Date.AddDays(7).ToString("yyyy-MM-dd");
        var endDate = DateTime.Now.Date.AddDays(1).ToString("yyyy-MM-dd");
        var resp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551112222", serviceId, 0, "13:00", null, startDate, endDate));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsCreatedSeries()
    {
        var slug = "recurring-list";
        var token = await RegisterAndLoginBarber("recurring-list@example.com", slug);
        var serviceId = await SeedService(token);

        Authorize(Client, token);
        await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551112222", serviceId, 0, "13:00", null));

        var list = await Client.GetFromJsonAsync<List<RecurringSeriesDto>>("/api/admin/recurring");

        Assert.Single(list!);
    }

    [Fact]
    public async Task Delete_RemovesSeries()
    {
        var slug = "recurring-delete";
        var token = await RegisterAndLoginBarber("recurring-delete@example.com", slug);
        var serviceId = await SeedService(token);

        Authorize(Client, token);
        var createResp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551112222", serviceId, 0, "13:00", null));
        var created = await createResp.Content.ReadFromJsonAsync<RecurringSeriesDto>();

        var deleteResp = await Client.DeleteAsync($"/api/admin/recurring/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResp.StatusCode);

        using var db = Db();
        Assert.Empty(db.RecurringSeries.Where(s => s.Id == created.Id));
    }

    [Fact]
    public async Task Delete_CancelsUpcomingLinkedAppointments()
    {
        var slug = "recurring-delete-cancels";
        var token = await RegisterAndLoginBarber("recurring-delete-cancels@example.com", slug);
        var serviceId = await SeedService(token);

        Authorize(Client, token);
        const int monday = 1; // AuthController.Register seeds default Mon-Fri 09:00-18:00 hours
        var createResp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551112222", serviceId, monday, "09:00", null));
        var created = await createResp.Content.ReadFromJsonAsync<RecurringSeriesDto>();

        // Creation generates real appointments immediately (see the earlier test) -- confirm
        // at least one exists before deleting, so this test actually exercises the cancel path.
        var beforeDelete = await Client.GetFromJsonAsync<List<DashboardAppointmentDto>>("/api/admin/appointments?filter=all");
        var linkedBefore = beforeDelete!.Where(a => a.RecurringSeriesId == created!.Id).ToList();
        Assert.NotEmpty(linkedBefore);
        Assert.All(linkedBefore, a => Assert.Equal("CONFIRMED", a.Status));

        var deleteResp = await Client.DeleteAsync($"/api/admin/recurring/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResp.StatusCode);

        // The series row is gone, but its appointments must still exist -- just cancelled,
        // not deleted -- so the calendar reflects the freed-up slots without losing history.
        var afterDelete = await Client.GetFromJsonAsync<List<DashboardAppointmentDto>>("/api/admin/appointments?filter=all");
        var linkedAfter = afterDelete!.Where(a => linkedBefore.Select(x => x.Id).Contains(a.Id)).ToList();
        Assert.Equal(linkedBefore.Count, linkedAfter.Count);
        Assert.All(linkedAfter, a => Assert.Equal("CANCELLED", a.Status));

        var slots = await Client.GetFromJsonAsync<AvailabilityWrapper>(
            $"/api/admin/appointments/availability?date={linkedBefore[0].Date}&serviceId={serviceId}");
        Assert.Contains(slots!.Slots, s => s.Start == "09:00");
    }

    [Fact]
    public async Task OtherBarber_CannotAccessAnotherBarbersSeries()
    {
        var slugA = "recurring-owner";
        var tokenA = await RegisterAndLoginBarber("recurring-owner@example.com", slugA);
        var serviceIdA = await SeedService(tokenA);

        Authorize(Client, tokenA);
        var createResp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551112222", serviceIdA, 0, "13:00", null));
        var created = await createResp.Content.ReadFromJsonAsync<RecurringSeriesDto>();

        var slugB = "recurring-intruder";
        var tokenB = await RegisterAndLoginBarber("recurring-intruder@example.com", slugB);
        Authorize(Client, tokenB);

        var resp = await Client.DeleteAsync($"/api/admin/recurring/{created!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
