using BarberSaas.Api.Services;
using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

public class RecurringAppointmentsTests : IntegrationTestBase
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

    private async Task<string> SeedService(string token)
    {
        Authorize(Client, token);
        var resp = await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Cut", "Cut", "Cut", 30, 20m));
        var service = await resp.Content.ReadFromJsonAsync<ItemDto>();
        // Open every day: a series without "custom time" has to fit the opening hours, and these
        // tests use Sundays, which a new business has closed.
        await Client.PostAsJsonAsync("/api/admin/schedule",
            Enumerable.Range(0, 7).Select(d => new WorkingHoursDto(null, d, "09:00", "18:00", true)).ToList());
        return service!.Id;
    }

    // QA run 3 OWN-8: an impossible time or a closed day was saved and then skipped every week.
    [Fact]
    public async Task Create_BadTimeOrClosedDay_WithoutForce_IsRejected()
    {
        var token = await RegisterAndLoginBusiness("recurring-closed@example.com", "recurring-closed");
        Authorize(Client, token);
        var item = await (await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Cut", "Cut", "Cut", 30, 20m))).Content.ReadFromJsonAsync<ItemDto>();

        var badTime = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(null, "Dana", "+15551113333", item!.Id, 1, "99:00", null));
        var closedDay = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(null, "Dana", "+15551113333", item.Id, 6, "10:00", null));

        Assert.Equal(HttpStatusCode.BadRequest, badTime.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, closedDay.StatusCode);
    }

    // The hours check read StartDate's weekday, not the series' own day: a Monday series starting on
    // a (closed) Saturday was rejected.
    [Fact]
    public async Task Create_StartDateOnAnotherWeekday_ChecksTheSeriesOwnDay()
    {
        var token = await RegisterAndLoginBusiness("recurring-weekday@example.com", "recurring-weekday");
        Authorize(Client, token);
        var item = await (await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Cut", "Cut", "Cut", 30, 20m))).Content.ReadFromJsonAsync<ItemDto>();
        var today = AppClock.Now.Date;
        var saturday = today.AddDays(((int)DayOfWeek.Saturday - (int)today.DayOfWeek + 7) % 7 + 7);

        var mondaySeries = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Dana", "+15551114444", item!.Id, 1, "10:00", null, saturday.ToString("yyyy-MM-dd")));

        Assert.Equal(HttpStatusCode.Created, mondaySeries.StatusCode);
    }

    // QA run 4 ST-9: a second series on a taken slot was saved (201) with every week skipped, and
    // it later made the daily cron fail for every business.
    [Fact]
    public async Task Create_OnASlotTakenEveryWeek_IsRefusedAndNothingIsSaved()
    {
        var token = await RegisterAndLoginBusiness("recurring-taken@example.com", "recurring-taken");
        var itemId = await SeedService(token);
        Authorize(Client, token);
        var first = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(null, "Dana", "+15551115555", itemId, 2, "11:00", null));

        var second = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(null, "Rami", "+15551116666", itemId, 2, "11:00", null));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var db = Db();
        Assert.Single(db.RecurringSeries);
        Assert.DoesNotContain(db.Customers, c => c.Phone == "+15551116666");
    }

    [Fact]
    public async Task Create_NewCustomer_CreatesActiveSeries()
    {
        var slug = "recurring-create";
        var token = await RegisterAndLoginBusiness("recurring-create@example.com", slug);
        var itemId = await SeedService(token);

        Authorize(Client, token);
        var resp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551112222", itemId, 0, "13:00", null));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<RecurringSeriesDto>();
        Assert.True(dto!.IsActive);
        Assert.Equal("Mohamed", dto.Customer.Name);
        Assert.Equal(0, dto.DayOfWeek);
        Assert.Equal("13:00", dto.StartTime);
    }

    [Fact]
    public async Task Create_ForAlreadyRegisteredCustomer_OccurrencesShowUpInTheirOwnBookings()
    {
        var slug = "recurring-linked";
        var token = await RegisterAndLoginBusiness("recurring-linked@example.com", slug);
        var itemId = await SeedService(token);

        // The customer already has a CustomerAccount before the owner ever types their phone
        // number into the recurring-series form.
        var customer = await LoginCustomerViaWhatsAppAsync("+15551113333", "Riley", "Owner");

        Authorize(Client, token);
        const int monday = 1;
        var resp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Riley", "+15551113333", itemId, monday, "09:00", null));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        Client.DefaultRequestHeaders.Authorization = null;
        Authorize(Client, customer.Token);
        var mine = await Client.GetFromJsonAsync<List<CustomerAppointmentDto>>("/api/customer/appointments?filter=upcoming");

        Assert.NotEmpty(mine!);
    }

    private record AvailabilityWrapper(List<TimeSlot> Slots);

    [Fact]
    public async Task Create_ImmediatelyGeneratesFirstOccurrence_VisibleOnDashboardAndBlocksSlot()
    {
        var slug = "recurring-immediate";
        var token = await RegisterAndLoginBusiness("recurring-immediate@example.com", slug);
        var itemId = await SeedService(token);

        Authorize(Client, token);
        const int monday = 1; // AuthController.Register seeds default Mon-Fri 09:00-18:00 hours
        var resp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551119999", itemId, monday, "09:00", null));
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
            $"/api/admin/appointments/availability?date={generated.Date}&itemId={itemId}");
        Assert.DoesNotContain(slots!.Slots, s => s.Start == "09:00");
    }

    [Fact]
    public async Task Create_ExistingCustomer_LinksToThatCustomer()
    {
        var slug = "recurring-existing";
        var token = await RegisterAndLoginBusiness("recurring-existing@example.com", slug);
        var itemId = await SeedService(token);

        Authorize(Client, token);
        var bookResp = await Client.PostAsJsonAsync("/api/admin/appointments", new CreateAdminAppointmentRequest(
            null, "Mohamed", "+15551112222", itemId, AppClock.Now.Date.AddDays(30).ToString("yyyy-MM-dd"), "09:00", null, Force: true));
        var booked = await bookResp.Content.ReadFromJsonAsync<DashboardAppointmentDto>();

        var resp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            booked!.Customer.Id, null, null, itemId, 0, "13:00", null));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<RecurringSeriesDto>();
        Assert.Equal(booked.Customer.Id, dto!.Customer.Id);
    }

    [Fact]
    public async Task Create_InvalidDayOfWeek_ReturnsBadRequest()
    {
        var slug = "recurring-bad-day";
        var token = await RegisterAndLoginBusiness("recurring-bad-day@example.com", slug);
        var itemId = await SeedService(token);

        Authorize(Client, token);
        var resp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551112222", itemId, 7, "13:00", null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_PastStartDate_ReturnsBadRequest()
    {
        var slug = "recurring-past-start";
        var token = await RegisterAndLoginBusiness("recurring-past-start@example.com", slug);
        var itemId = await SeedService(token);

        Authorize(Client, token);
        var pastDate = AppClock.Now.Date.AddDays(-7).ToString("yyyy-MM-dd");
        var resp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551112222", itemId, 0, "13:00", null, pastDate));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_EndDateBeforeStartDate_ReturnsBadRequest()
    {
        var slug = "recurring-bad-range";
        var token = await RegisterAndLoginBusiness("recurring-bad-range@example.com", slug);
        var itemId = await SeedService(token);

        Authorize(Client, token);
        var startDate = AppClock.Now.Date.AddDays(7).ToString("yyyy-MM-dd");
        var endDate = AppClock.Now.Date.AddDays(1).ToString("yyyy-MM-dd");
        var resp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551112222", itemId, 0, "13:00", null, startDate, endDate));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsCreatedSeries()
    {
        var slug = "recurring-list";
        var token = await RegisterAndLoginBusiness("recurring-list@example.com", slug);
        var itemId = await SeedService(token);

        Authorize(Client, token);
        await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551112222", itemId, 0, "13:00", null));

        var list = await Client.GetFromJsonAsync<List<RecurringSeriesDto>>("/api/admin/recurring");

        Assert.Single(list!);
    }

    [Fact]
    public async Task Delete_RemovesSeries()
    {
        var slug = "recurring-delete";
        var token = await RegisterAndLoginBusiness("recurring-delete@example.com", slug);
        var itemId = await SeedService(token);

        Authorize(Client, token);
        var createResp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551112222", itemId, 0, "13:00", null));
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
        var token = await RegisterAndLoginBusiness("recurring-delete-cancels@example.com", slug);
        var itemId = await SeedService(token);

        Authorize(Client, token);
        const int monday = 1; // AuthController.Register seeds default Mon-Fri 09:00-18:00 hours
        var createResp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551112222", itemId, monday, "09:00", null));
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
            $"/api/admin/appointments/availability?date={linkedBefore[0].Date}&itemId={itemId}");
        Assert.Contains(slots!.Slots, s => s.Start == "09:00");
    }

    [Fact]
    public async Task OtherBusiness_CannotAccessAnotherBusinessesSeries()
    {
        var slugA = "recurring-owner";
        var tokenA = await RegisterAndLoginBusiness("recurring-owner@example.com", slugA);
        var itemIdA = await SeedService(tokenA);

        Authorize(Client, tokenA);
        var createResp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551112222", itemIdA, 0, "13:00", null));
        var created = await createResp.Content.ReadFromJsonAsync<RecurringSeriesDto>();

        var slugB = "recurring-intruder";
        var tokenB = await RegisterAndLoginBusiness("recurring-intruder@example.com", slugB);
        Authorize(Client, tokenB);

        var resp = await Client.DeleteAsync($"/api/admin/recurring/{created!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_ForcedSeriesEndingPastMidnight_ReturnsBadRequest()
    {
        var token = await RegisterAndLoginBusiness("recurring-force-midnight@example.com", "recurring-force-midnight");
        var itemId = await SeedService(token);

        Authorize(Client, token);
        var resp = await Client.PostAsJsonAsync("/api/admin/recurring", new CreateRecurringSeriesRequest(
            null, "Mohamed", "+15551112222", itemId, (int)AppClock.Now.AddDays(1).DayOfWeek, "23:45", null, Force: true));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("midnight", await resp.Content.ReadAsStringAsync());
    }
}
