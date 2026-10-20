using BarberSaas.Api.Services;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// Every other integration test runs sends inline (Notifications__Inline). This one runs the real
// background queue to check the QA run 2 complaint: an owner cancel that offered the slot to the
// waitlist waited ~8s for the WhatsApp bridge. Notifications__Inline is a process-wide env var
// read when the host is built, so this class runs on its own, after the parallel ones (every
// factory sets it again for itself).
[CollectionDefinition(nameof(BackgroundNotificationTests), DisableParallelization = true)]
public class BackgroundNotificationCollection;

[Collection(nameof(BackgroundNotificationTests))]
public class BackgroundNotificationTests : IntegrationTestBase
{
    public BackgroundNotificationTests() : base(configureCardcom: false, inlineNotifications: false) { }

    private record RegisterResponse(string? DevCode);

    [Fact]
    public async Task OwnerCancel_DoesNotWaitForTheWaitlistMessage()
    {
        const string slug = "bg-notify-shop";
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", "bg-notify@example.com", "password123", slug));
        var code = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.DevCode!;
        var owner = (await (await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest("bg-notify@example.com", code))).Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        Authorize(Client, owner);
        var item = await (await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Cut", "Cut", "Cut", 30, 20m))).Content.ReadFromJsonAsync<ItemDto>();
        var date = AppClock.Now.Date.AddDays(1);
        await Client.PostAsJsonAsync("/api/admin/schedule", new List<WorkingHoursDto> { new(null, (int)date.DayOfWeek, "09:00", "18:00", true) });
        await Client.PatchAsJsonAsync("/api/admin/settings", new UpdateSettingsRequest(null, null, null, null, null, null, WaitlistEnabled: true));
        Client.DefaultRequestHeaders.Authorization = null;
        using (var db = Db())
        {
            db.Businesses.First(b => b.Slug == slug).WhatsAppNumber = "+15550009999";
            db.SaveChanges();
        }

        var bookerToken = (await LoginCustomerViaWhatsAppAsync("+15552000001")).Token;
        var bookReq = new HttpRequestMessage(HttpMethod.Post, $"/api/{slug}/appointments")
        {
            Content = JsonContent.Create(new BookAppointmentRequest(item!.Id, date.ToString("yyyy-MM-dd"), "09:00", "Booker", "", null)),
        };
        bookReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bookerToken);
        var appointmentId = (await (await Client.SendAsync(bookReq)).Content.ReadFromJsonAsync<BookAppointmentResponse>())!.AppointmentId;

        const string waiterPhone = "+15552000002";
        var waiterToken = (await LoginCustomerViaWhatsAppAsync(waiterPhone)).Token;
        var joinReq = new HttpRequestMessage(HttpMethod.Post, $"/api/{slug}/waitlist/{appointmentId}");
        joinReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", waiterToken);
        Assert.True((await Client.SendAsync(joinReq)).IsSuccessStatusCode);

        Factory.WhatsAppSender.Delay = TimeSpan.FromSeconds(3);
        Authorize(Client, owner);
        var timer = Stopwatch.StartNew();
        var cancel = await Client.PatchAsJsonAsync($"/api/admin/appointments/{appointmentId}", new { status = "CANCELLED", notifyWaitlist = true });
        timer.Stop();

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), $"cancel took {timer.Elapsed}");
        Assert.DoesNotContain(Factory.WhatsAppSender.Sent, s => s.Phone == waiterPhone);
        using (var db = Db())
            Assert.Equal(WaitlistEntryStatus.NOTIFIED, db.WaitlistEntries.Single(w => w.AppointmentId == appointmentId).Status);

        // ...and the message still goes out, once the slow send finishes.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!Factory.WhatsAppSender.Sent.Any(s => s.Phone == waiterPhone) && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        Assert.Contains(Factory.WhatsAppSender.Sent, s => s.Phone == waiterPhone);
    }
}
