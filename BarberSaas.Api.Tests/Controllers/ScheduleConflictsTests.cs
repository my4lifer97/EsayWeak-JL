using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// QA run 4 OWN-1 (a block or schedule change left its appointments booked with no warning) and
// OWN-2 (inquiry alerts switched on with an empty contact sent nothing).
public class ScheduleConflictsTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);
    private record IdDto(string Id);

    private async Task<(string BusinessId, string ItemId)> SeedOwner(string slug)
    {
        var email = $"{slug}@example.com";
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Shop", email, "password123", slug));
        var code = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.DevCode!;
        var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code));
        Authorize(Client, (await verify.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        var item = await (await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Cut", "Cut", "Cut", 30, 20m))).Content.ReadFromJsonAsync<IdDto>();
        using var db = Db();
        return (await db.Businesses.Where(b => b.Slug == slug).Select(b => b.Id).FirstAsync(), item!.Id);
    }

    // The next Monday at least a week out, as stored.
    private static DateTime NextMonday() =>
        DateTime.Parse(AppClock.Now.Date.AddDays(((int)DayOfWeek.Monday - (int)AppClock.Now.DayOfWeek + 7) % 7 + 7).ToString("yyyy-MM-dd") + "T00:00:00Z").ToUniversalTime();

    private async Task<string> AddAppointment(string businessId, string itemId, DateTime date, string start, string end)
    {
        using var db = Db();
        var customer = new Customer { BusinessId = businessId, Phone = $"+1555408{Random.Shared.Next(1000, 9999)}", Name = "Dana", FamilyName = "Levi" };
        db.Customers.Add(customer);
        var a = new Appointment { BusinessId = businessId, CustomerId = customer.Id, ItemId = itemId, Date = date, StartTime = start, EndTime = end, Status = AppointmentStatus.CONFIRMED };
        db.Appointments.Add(a);
        await db.SaveChangesAsync();
        return a.Id;
    }

    [Fact]
    public async Task BlockPreview_ListsAppointmentsInsideTheBlock_Only()
    {
        var (businessId, itemId) = await SeedOwner("conflicts-block");
        var monday = NextMonday();
        var morning = await AddAppointment(businessId, itemId, monday, "10:00", "10:30");
        await AddAppointment(businessId, itemId, monday, "15:00", "15:30");
        var day = monday.ToString("yyyy-MM-dd");

        var fullDay = await (await Client.PostAsJsonAsync("/api/admin/schedule/blocked/conflicts", new BlockedConflictsRequest(day, null, null, null)))
            .Content.ReadFromJsonAsync<List<ScheduleConflictDto>>();
        var morningBlock = await (await Client.PostAsJsonAsync("/api/admin/schedule/blocked/conflicts", new BlockedConflictsRequest(day, null, "09:00", "12:00")))
            .Content.ReadFromJsonAsync<List<ScheduleConflictDto>>();

        Assert.Equal(2, fullDay!.Count);
        Assert.Equal(morning, Assert.Single(morningBlock!).Id);
        Assert.Equal("Dana Levi", morningBlock![0].CustomerName);
    }

    [Fact]
    public async Task PresetPreview_ListsAppointmentsOnClosedDaysOrInBreaks()
    {
        var (businessId, itemId) = await SeedOwner("conflicts-preset");
        var monday = NextMonday();
        var onMonday = await AddAppointment(businessId, itemId, monday, "10:00", "10:30");
        var inBreak = await AddAppointment(businessId, itemId, monday.AddDays(1), "13:00", "13:30");
        await AddAppointment(businessId, itemId, monday.AddDays(1), "10:00", "10:30");
        var days = Enumerable.Range(0, 7).Select(d => new SchedulePresetDayDto(d, "09:00", "18:00", d != (int)DayOfWeek.Monday)).ToList();
        var breaks = new List<SchedulePresetBreakDto> { new((int)DayOfWeek.Tuesday, "13:00", "14:00") };

        var range = await (await Client.PostAsJsonAsync("/api/admin/schedule/presets/conflicts",
            new PresetConflictsRequest(days, breaks, monday.ToString("yyyy-MM-dd"), monday.AddDays(6).ToString("yyyy-MM-dd"))))
            .Content.ReadFromJsonAsync<List<ScheduleConflictDto>>();
        var permanent = await (await Client.PostAsJsonAsync("/api/admin/schedule/presets/conflicts", new PresetConflictsRequest(days, breaks, null, null)))
            .Content.ReadFromJsonAsync<List<ScheduleConflictDto>>();

        Assert.Equal([onMonday, inBreak], range!.Select(c => c.Id));
        Assert.Equal([onMonday, inBreak], permanent!.Select(c => c.Id));
    }

    [Fact]
    public async Task InquiryAlert_WithAnEmptyContact_GoesToTheBusinessEmail()
    {
        var (businessId, _) = await SeedOwner("inquiry-fallback");
        await Client.PatchAsJsonAsync("/api/admin/settings", new { chatbotInquiryEnabled = true, inquiryNotifyViaEmail = true, inquiryEmail = (string?)null });
        Client.DefaultRequestHeaders.Authorization = null;

        foreach (var message in new[] { "hi", "2", "Do you open on Fridays?" })
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/bridge/inbound")
            {
                Content = JsonContent.Create(new WhatsAppController.BridgeInboundRequest(businessId, "+15554080001", null, message)),
            };
            req.Headers.Add("X-Bridge-Secret", TestWebApplicationFactory.BridgeSecret);
            (await Client.SendAsync(req)).EnsureSuccessStatusCode();
        }

        Assert.Contains(Factory.OwnerEmail.Sent, e => e.Email == "inquiry-fallback@example.com" && e.Body.Contains("Fridays"));
    }
}
