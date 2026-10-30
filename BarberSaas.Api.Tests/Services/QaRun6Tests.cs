using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

// QA run 6 fixes that come down to one endpoint or helper.
public class QaRun6Tests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    // PUB-2: an SMS code per phone was the only limit.
    [Fact]
    public void RequestQuota_RefusesPastTheLimit_ThenAllowsAfterTheWindow()
    {
        var quota = new RequestQuota(new ConfigurationBuilder().Build());
        var start = DateTime.UtcNow;

        for (var i = 0; i < 3; i++) Assert.True(quota.TryTake("k", 3, TimeSpan.FromHours(1), start.AddMinutes(i)).Allowed);
        var refused = quota.TryTake("k", 3, TimeSpan.FromHours(1), start.AddMinutes(10));

        Assert.False(refused.Allowed);
        Assert.Equal(TimeSpan.FromMinutes(50), refused.RetryAfter);
        Assert.True(quota.TryTake("other", 3, TimeSpan.FromHours(1), start.AddMinutes(10)).Allowed);
        Assert.True(quota.TryTake("k", 3, TimeSpan.FromHours(1), start.AddMinutes(61)).Allowed);
    }

    // PA-1 / PUB-4: (page - 1) * pageSize overflowed into a 500.
    [Fact]
    public async Task HugePageNumber_IsNotAServerError()
    {
        var resp = await Client.GetAsync("/api/businesses/search?page=2147483647&pageSize=50");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // OWN-2: deleting an item said nothing about its upcoming appointments, which then had no
    // times to move to.
    [Fact]
    public async Task DeletedItem_StillHasTimesForMovingItsOwnAppointments()
    {
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Shop", "qa6-del@example.com", "password123", "qa6-del-shop"));
        var code = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.DevCode!;
        var token = (await (await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest("qa6-del@example.com", code)))
            .Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        Authorize(Client, token);
        var item = (await (await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Cut", "Cut", "Cut", 30, 20m)))
            .Content.ReadFromJsonAsync<ItemDto>())!;
        var date = AppClock.Now.Date.AddDays(1);
        await Client.PostAsJsonAsync("/api/admin/schedule", new List<WorkingHoursDto> { new(null, (int)date.DayOfWeek, "09:00", "18:00", true) });
        var day = date.ToString("yyyy-MM-dd");
        var booked = await Client.PostAsJsonAsync("/api/admin/appointments",
            new { itemId = item.Id, date = day, startTime = "10:00", customerName = "Dana", customerPhone = "+15556990001" });
        Assert.True(booked.IsSuccessStatusCode, await booked.Content.ReadAsStringAsync());
        var appointmentId = (await booked.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetString();

        var count = await Client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/admin/items/{item.Id}/upcoming-appointments");
        await Client.DeleteAsync($"/api/admin/items/{item.Id}");
        var forMove = await Client.GetAsync($"/api/admin/appointments/availability?date={day}&itemId={item.Id}&excludeAppointmentId={appointmentId}");
        var forNew = await Client.GetAsync($"/api/admin/appointments/availability?date={day}&itemId={item.Id}");

        Assert.Equal(1, count.GetProperty("count").GetInt32());
        Assert.Equal(HttpStatusCode.OK, forMove.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, forNew.StatusCode);
    }
}
