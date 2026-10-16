using System.Net;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// QA run 2: an owner cancelling, moving, keeping or giving away a customer's appointment told the
// customer nothing. Each of these now sends them a WhatsApp message (when the business has a
// linked number), unless the owner unticks "let the customer know".
public class OwnerActionCustomerNoticeTests : IntegrationTestBase
{
    private const string CustomerPhone = "+15553330001";
    private const string OtherPhone = "+15553330002";

    private record RegisterResponse(string? DevCode);

    private async Task<(string Token, string Slug, string ItemId, string Date, string AppointmentId)> Seed(string slug)
    {
        var email = $"{slug}@example.com";
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Notice Shop", email, "password123", slug));
        var code = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.DevCode!;
        var token = (await (await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code))).Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        Authorize(Client, token);
        var item = await (await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Cut", "Cut", "Cut", 30, 20m))).Content.ReadFromJsonAsync<ItemDto>();
        var date = DateTime.Now.Date.AddDays(1);
        await Client.PostAsJsonAsync("/api/admin/schedule", new List<WorkingHoursDto> { new(null, (int)date.DayOfWeek, "09:00", "18:00", true) });
        using (var db = Db())
        {
            db.Businesses.First(b => b.Slug == slug).WhatsAppNumber = "+15550008888";
            db.SaveChanges();
        }
        var dateStr = date.ToString("yyyy-MM-dd");
        var created = await Client.PostAsJsonAsync("/api/admin/appointments", new CreateAdminAppointmentRequest(
            null, "Dana", CustomerPhone, item!.Id, dateStr, "09:00", null));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var appointmentId = (await created.Content.ReadFromJsonAsync<DashboardAppointmentDto>())!.Id;
        return (token, slug, item.Id, dateStr, appointmentId);
    }

    private List<string> MessagesTo(string phone, string containing) =>
        Factory.WhatsAppSender.Sent.Where(s => s.Phone == phone && s.Message.Contains(containing)).Select(s => s.Message).ToList();

    private void MarkPendingApproval(string appointmentId)
    {
        using var db = Db();
        db.Appointments.First(a => a.Id == appointmentId).PendingCancellationApproval = true;
        db.SaveChanges();
    }

    [Fact]
    public async Task OwnerCancel_TellsTheCustomer_WithALinkToBookAgain()
    {
        var (_, slug, _, date, id) = await Seed("notice-cancel");

        var resp = await Client.PatchAsJsonAsync($"/api/admin/appointments/{id}", new { status = "CANCELLED" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var message = Assert.Single(MessagesTo(CustomerPhone, "had to cancel"));
        Assert.Contains($"{date} at 09:00", message);
        Assert.EndsWith($"/{slug}", message);
    }

    [Fact]
    public async Task OwnerCancel_NotifyCustomerFalse_SendsNothing()
    {
        var (_, _, _, _, id) = await Seed("notice-cancel-quiet");
        var before = Factory.WhatsAppSender.Sent.Count(s => s.Phone == CustomerPhone);

        var resp = await Client.PatchAsJsonAsync($"/api/admin/appointments/{id}", new { status = "CANCELLED", notifyCustomer = false });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(before, Factory.WhatsAppSender.Sent.Count(s => s.Phone == CustomerPhone));
    }

    [Fact]
    public async Task OwnerCancel_OfACancellationRequest_SaysItWasApproved()
    {
        var (_, _, _, _, id) = await Seed("notice-approve");
        MarkPendingApproval(id);

        await Client.PatchAsJsonAsync($"/api/admin/appointments/{id}", new { status = "CANCELLED" });

        Assert.Single(MessagesTo(CustomerPhone, "approved your cancellation"));
        Assert.Empty(MessagesTo(CustomerPhone, "had to cancel"));
    }

    [Fact]
    public async Task Keep_TellsTheCustomer_TheAppointmentIsStillOn()
    {
        var (_, slug, _, _, id) = await Seed("notice-keep");
        MarkPendingApproval(id);
        string cancelToken;
        using (var db = Db()) cancelToken = db.Appointments.First(a => a.Id == id).CancelToken;

        var resp = await Client.PostAsync($"/api/admin/appointments/{id}/keep", null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var message = Assert.Single(MessagesTo(CustomerPhone, "didn't approve"));
        Assert.EndsWith($"/{slug}/appointments/{id}?token={cancelToken}", message);
    }

    [Fact]
    public async Task Keep_NotifyCustomerFalse_SendsNothing()
    {
        var (_, _, _, _, id) = await Seed("notice-keep-quiet");
        MarkPendingApproval(id);

        await Client.PostAsync($"/api/admin/appointments/{id}/keep?notifyCustomer=false", null);

        Assert.Empty(MessagesTo(CustomerPhone, "didn't approve"));
    }

    [Fact]
    public async Task Reschedule_TellsTheCustomer_OldAndNewTime()
    {
        var (_, _, _, date, id) = await Seed("notice-move");

        var resp = await Client.PatchAsJsonAsync($"/api/admin/appointments/{id}/reschedule", new RescheduleRequest(date, "10:00"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var message = Assert.Single(MessagesTo(CustomerPhone, "moved your"));
        Assert.Contains($"from {date} at 09:00 to {date} at 10:00", message);
    }

    [Fact]
    public async Task Reschedule_ThatFails_SendsNothing()
    {
        var (_, _, _, date, id) = await Seed("notice-move-fail");

        var resp = await Client.PatchAsJsonAsync($"/api/admin/appointments/{id}/reschedule", new RescheduleRequest(date, "19:00"));

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Empty(MessagesTo(CustomerPhone, "moved your"));
    }

    [Fact]
    public async Task ReplaceCustomer_TellsBoth()
    {
        var (_, _, _, date, id) = await Seed("notice-replace");

        var resp = await Client.PatchAsJsonAsync($"/api/admin/appointments/{id}/customer",
            new ReplaceCustomerRequest(null, "Noa", OtherPhone));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Single(MessagesTo(CustomerPhone, "had to cancel"));
        var booked = Assert.Single(MessagesTo(OtherPhone, "You're booked"));
        Assert.Contains($"{date} at 09:00", booked);
    }

    [Fact]
    public async Task ReplaceCustomer_OfACancellationRequest_SaysItWasApproved()
    {
        var (_, _, _, _, id) = await Seed("notice-replace-req");
        MarkPendingApproval(id);

        await Client.PatchAsJsonAsync($"/api/admin/appointments/{id}/customer", new ReplaceCustomerRequest(null, "Noa", OtherPhone));

        Assert.Single(MessagesTo(CustomerPhone, "approved your cancellation"));
        Assert.Single(MessagesTo(OtherPhone, "You're booked"));
    }

    [Fact]
    public async Task Notice_IsInTheLanguageTheCustomerLastChattedIn()
    {
        using (var db = Db())
        {
            db.CustomerAccounts.Add(new CustomerAccount { Phone = CustomerPhone, Name = "Dana", LastMessageLanguage = "HE" });
            db.SaveChanges();
        }
        var (_, _, _, _, id) = await Seed("notice-hebrew");

        await Client.PatchAsJsonAsync($"/api/admin/appointments/{id}", new { status = "CANCELLED" });

        Assert.Single(MessagesTo(CustomerPhone, "נאלץ לבטל"));
    }

    [Fact]
    public async Task NoLinkedWhatsAppNumber_SendsNothing()
    {
        var (_, slug, _, _, id) = await Seed("notice-no-number");
        using (var db = Db())
        {
            db.Businesses.First(b => b.Slug == slug).WhatsAppNumber = null;
            db.SaveChanges();
        }

        var resp = await Client.PatchAsJsonAsync($"/api/admin/appointments/{id}", new { status = "CANCELLED" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Empty(MessagesTo(CustomerPhone, "had to cancel"));
    }
}
