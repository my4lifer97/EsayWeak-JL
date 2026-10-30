using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.DTOs;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

public class QaRun10Tests : IntegrationTestBase
{
    private const string Phone = "+15551010001";
    private record RegisterResponse(string? DevCode);

    private async Task<(string BusinessId, string Slug, string Token, string ItemId)> SeedBusiness(string slug, bool verified = true)
    {
        var email = $"{slug}@example.com";
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Shop", email, "password123", slug));
        var code = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.DevCode!;
        var token = "";
        if (verified)
        {
            var verify = await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code));
            token = (await verify.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        }

        using var db = Db();
        var business = await db.Businesses.SingleAsync(b => b.Slug == slug);
        business.WhatsAppNumber = "+15559990000";
        var item = new Item { BusinessId = business.Id, NameEn = "Haircut", NameAr = "قص شعر", NameHe = "תספורת", DurationMinutes = 30 };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return (business.Id, slug, token, item.Id);
    }

    private async Task<string> AddAppointment(string businessId, string itemId, string start, int days = 1)
    {
        using var db = Db();
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.BusinessId == businessId && c.Phone == Phone);
        if (customer is null)
        {
            customer = new Customer { BusinessId = businessId, Name = "Dana", FamilyName = "Cohen", Phone = Phone };
            db.Customers.Add(customer);
        }
        var appointment = new Appointment
        {
            BusinessId = businessId, CustomerId = customer.Id, ItemId = itemId, Date = AppClock.Now.AddDays(days).Date,
            StartTime = start, EndTime = AvailabilityService.AddMinutes(start, 30), Status = AppointmentStatus.CONFIRMED,
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }

    private async Task<(string BusinessId, string AppointmentId)> SeedAsked()
    {
        var (businessId, _, _, itemId) = await SeedBusiness("q10-" + Guid.NewGuid().ToString("N")[..8]);
        var appointmentId = await AddAppointment(businessId, itemId, "10:00");
        await RunReminders();
        return (businessId, appointmentId);
    }

    private async Task RunReminders()
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestWebApplicationFactory.CronSecret);
        await Client.GetAsync("/api/cron/reminders");
        Client.DefaultRequestHeaders.Authorization = null;
    }

    private async Task<string?> Inbound(string businessId, string message)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/bridge/inbound")
        {
            Content = JsonContent.Create(new WhatsAppController.BridgeInboundRequest(businessId, Phone, "Dana", message)),
        };
        req.Headers.Add("X-Bridge-Secret", TestWebApplicationFactory.BridgeSecret);
        var resp = await Client.SendAsync(req);
        return (await resp.Content.ReadFromJsonAsync<WhatsAppController.BridgeInboundResponse>())!.Reply;
    }

    private async Task<Appointment> Load(string id)
    {
        using var db = Db();
        return await db.Appointments.SingleAsync(a => a.Id == id);
    }

    // BOT-51
    [Theory]
    [InlineData("cancel my 11:00 appointment")]
    [InlineData("I want to cancel the second one")]
    [InlineData("cancel")]
    public async Task CancelSentence_WithTwoAppointments_AsksWhichOne(string message)
    {
        var (businessId, _, _, itemId) = await SeedBusiness("q10-two");
        var first = await AddAppointment(businessId, itemId, "09:00");
        var second = await AddAppointment(businessId, itemId, "11:00");
        await RunReminders();

        await Inbound(businessId, message);
        var reply = await Inbound(businessId, message);

        Assert.Contains("11:00", reply);
        Assert.Equal(AppointmentStatus.CONFIRMED, (await Load(first)).Status);
        Assert.Equal(AppointmentStatus.CONFIRMED, (await Load(second)).Status);
    }

    [Fact]
    public async Task BareTwo_WithTwoAppointments_StillAnswersTheQuestion()
    {
        var (businessId, _, _, itemId) = await SeedBusiness("q10-two2");
        var first = await AddAppointment(businessId, itemId, "09:00");
        var second = await AddAppointment(businessId, itemId, "11:00");
        await RunReminders();

        await Inbound(businessId, "2");

        Assert.Equal(AppointmentStatus.CANCELLED, (await Load(first)).Status);
        Assert.Equal(AppointmentStatus.CONFIRMED, (await Load(second)).Status);
    }

    // BOT-50
    [Theory]
    [InlineData("I will not be cancelling")]
    [InlineData("I don't think I need to cancel")]
    [InlineData("Please don't let them cancel")]
    [InlineData("no cancelling please")]
    [InlineData("hold off on the cancel")]
    [InlineData("Disregard cancel")]
    [InlineData("don't you cancel")]
    [InlineData("i havent decided to cancel")]
    [InlineData("not planning on cancelling")]
    [InlineData("I have no intention to cancel")]
    [InlineData("I do not wish to cancel")]
    [InlineData("I refuse to cancel")]
    [InlineData("without cancel")]
    [InlineData("forget cancel")]
    [InlineData("ignore the cancel")]
    [InlineData("I wanted to cancel, but I'm coming")]
    [InlineData("I don't know why you'd cancel")]
    [InlineData("בלי לבטל")]
    [InlineData("מבלי לבטל")]
    [InlineData("שלא תבטל")]
    [InlineData("אין סיבה לבטל")]
    [InlineData("لا أنوي الإلغاء")]
    [InlineData("مافي الغاء")]
    [InlineData("الغاء لا")]
    [InlineData("ما ابغى الغي")]
    [InlineData("لا ترجو الغاء")]
    public async Task Negation_SentTwice_NeverCancels(string message)
    {
        var (businessId, appointmentId) = await SeedAsked();

        await Inbound(businessId, message);
        await Inbound(businessId, message);

        Assert.Equal(AppointmentStatus.CONFIRMED, (await Load(appointmentId)).Status);
    }

    [Theory]
    [InlineData("cancel it")]
    [InlineData("no, cancel")]
    [InlineData("I'm not coming, please cancel")]
    [InlineData("I'm not coming please cancel")]
    [InlineData("can't make it, cancel")]
    [InlineData("לא מגיע תבטל")]
    [InlineData("ما رح اجي الغي")]
    [InlineData("please delete my appointment")]
    [InlineData("נבטל את התור")]
    [InlineData("نلغي الموعد")]
    public async Task ClearCancel_SentTwice_Cancels(string message)
    {
        var (businessId, appointmentId) = await SeedAsked();

        await Inbound(businessId, message);
        await Inbound(businessId, message);

        Assert.Equal(AppointmentStatus.CANCELLED, (await Load(appointmentId)).Status);
    }

    // BOT-52 / BOT-53
    [Theory]
    [InlineData("لا مشكلة جاي")]
    [InlineData("ما في مشكلة جاي")]
    [InlineData("ما شاء الله رح اجي")]
    [InlineData("לא בעיה אגיע")]
    [InlineData("לא אאחר, אגיע")]
    [InlineData("I won't forget, coming")]
    [InlineData("wouldn't miss it, coming")]
    [InlineData("إن شاء الله سآتي")]
    [InlineData("نعم سأحضر")]
    [InlineData("لا، سآتي")]
    [InlineData("لن أتأخر سآتي")]
    [InlineData("בטח שאגיע")]
    [InlineData("אישור הגעה")]
    [InlineData("הכל בסדר מגיע")]
    [InlineData("sounds good")]
    [InlineData("will do")]
    [InlineData("count me in")]
    [InlineData("I will attend")]
    [InlineData("no problem I'll come")]
    [InlineData("Don't worry, I'll come")]
    public async Task ComingReplies_Confirm(string message)
    {
        var (businessId, appointmentId) = await SeedAsked();

        var reply = await Inbound(businessId, message);

        Assert.DoesNotContain("reply 2", reply, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AttendanceQuestion.Confirmed, (await Load(appointmentId)).AttendanceStatus);
    }

    [Theory]
    [InlineData("אי אפשר להגיע")]
    [InlineData("מצטער לא אוכל")]
    [InlineData("בלתי אפשרי להגיע")]
    [InlineData("I'm out of town")]
    [InlineData("im out")]
    [InlineData("sorry im unable")]
    [InlineData("not tomorrow")]
    [InlineData("of course not")]
    [InlineData("I'm not sure I'm coming")]
    [InlineData("maybe I'm coming")]
    [InlineData("can't come, sorry for the late notice")]
    public async Task DeclinedOrUnsureReplies_KeepTheQuestionOpen(string message)
    {
        var (businessId, appointmentId) = await SeedAsked();

        await Inbound(businessId, message);

        var appointment = await Load(appointmentId);
        Assert.Equal(AppointmentStatus.CONFIRMED, appointment.Status);
        Assert.Equal(AttendanceQuestion.Asked, appointment.AttendanceStatus);
    }

    // ST-50
    [Fact]
    public async Task CancelledAppointment_DoesNotBlockTheQuestionForTheNextOne()
    {
        var (businessId, _, _, itemId) = await SeedBusiness("q10-stale");
        var first = await AddAppointment(businessId, itemId, "09:00");
        await RunReminders();
        using (var db = Db())
        {
            var appointment = await db.Appointments.SingleAsync(a => a.Id == first);
            appointment.Status = AppointmentStatus.CANCELLED;
            await db.SaveChangesAsync();
        }
        var second = await AddAppointment(businessId, itemId, "11:00");

        await RunReminders();

        Assert.Equal(AttendanceQuestion.Asked, (await Load(second)).AttendanceStatus);
    }

    // ST-51
    [Fact]
    public async Task OneWhileACancelRequestIsPending_LeavesTheQuestionForAfterTheOwnerKeeps()
    {
        var (businessId, appointmentId) = await SeedAsked();
        using (var db = Db())
        {
            var appointment = await db.Appointments.SingleAsync(a => a.Id == appointmentId);
            appointment.PendingCancellationApproval = true;
            await db.SaveChangesAsync();
        }
        await Inbound(businessId, "1");
        using (var db = Db())
        {
            var appointment = await db.Appointments.SingleAsync(a => a.Id == appointmentId);
            appointment.PendingCancellationApproval = false;
            await db.SaveChangesAsync();
        }

        var reply = await Inbound(businessId, "1");

        Assert.StartsWith("Thanks for confirming!", reply);
        Assert.Equal(AttendanceQuestion.Confirmed, (await Load(appointmentId)).AttendanceStatus);
    }

    // OWN-54
    [Theory]
    [InlineData(null, "Dana", "abc", HttpStatusCode.BadRequest)]
    [InlineData("nobody", null, null, HttpStatusCode.NotFound)]
    public async Task OwnerBooking_OnATakenSlot_AnswersTheCustomerError(string? customerId, string? name, string? phone, HttpStatusCode expected)
    {
        var (_, _, token, itemId) = await SeedBusiness("q10-input");
        Authorize(Client, token);
        var date = NextMonday();
        await Client.PostAsJsonAsync("/api/admin/appointments", new { itemId, date, startTime = "10:00", customerName = "First", customerPhone = "+15551010002" });

        var resp = await Client.PostAsJsonAsync("/api/admin/appointments", new { itemId, date, startTime = "10:00", customerId, customerName = name, customerPhone = phone });

        Assert.Equal(expected, resp.StatusCode);
    }

    private static string NextMonday()
    {
        var day = AppClock.Now.Date.AddDays(1);
        while (day.DayOfWeek != DayOfWeek.Monday) day = day.AddDays(1);
        return day.ToString("yyyy-MM-dd");
    }

    // OWN-55
    [Fact]
    public async Task Settings_CleansCityAddressDescriptionAndMapLink()
    {
        var (businessId, _, token, _) = await SeedBusiness("q10-settings");
        Authorize(Client, token);

        var resp = await Client.PatchAsJsonAsync("/api/admin/settings", new
        {
            city = "Tel​\nAviv‮",
            addressLine = "  Herzl\t1 ",
            description = "a​b‮",
            mapUrl = "https://x.co/​map",
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var db = Db();
        var business = await db.Businesses.SingleAsync(b => b.Id == businessId);
        Assert.Equal("Tel Aviv", business.City);
        Assert.Equal("Herzl 1", business.AddressLine);
        Assert.Equal("ab", business.Description);
        Assert.Equal("https://x.co/map", business.MapUrl);
    }

    // OWN-56
    [Fact]
    public async Task SaveWorkingHours_NullElement_Is400()
    {
        var (_, _, token, _) = await SeedBusiness("q10-null");
        Authorize(Client, token);

        var resp = await Client.PostAsync("/api/admin/schedule", new StringContent("[null]", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // PUB-50
    [Fact]
    public async Task CustomerNotesEdit_IsCleaned()
    {
        var (businessId, _, _, itemId) = await SeedBusiness("q10-notes");
        var appointmentId = await AddAppointment(businessId, itemId, "10:00");
        Authorize(Client, (await LoginCustomerViaWhatsAppAsync(Phone)).Token);
        using (var db = Db())
        {
            var customer = await db.Customers.SingleAsync(c => c.Phone == Phone);
            customer.CustomerAccountId = (await db.CustomerAccounts.SingleAsync(a => a.Phone == Phone)).Id;
            await db.SaveChangesAsync();
        }

        var resp = await Client.PatchAsJsonAsync($"/api/customer/appointments/{appointmentId}/notes", new { notes = "a​b‮" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("ab", (await Load(appointmentId)).Notes);
    }

    // PUB-51
    [Fact]
    public async Task UnverifiedBusiness_LooksLikeAnUnknownSlug()
    {
        var (_, slug, _, itemId) = await SeedBusiness("q10-unverified", verified: false);

        var known = await Client.GetAsync($"/api/{slug}/availability?itemId={itemId}&date={NextMonday()}");
        var unknown = await Client.GetAsync($"/api/q10-nothing-here/availability?itemId={itemId}&date={NextMonday()}");

        Assert.Equal(HttpStatusCode.NotFound, known.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await known.Content.ReadAsStringAsync());
    }

    // OWN-50
    [Fact]
    public async Task Deadlock_WrappedByEf_Is409()
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, new FakeEnv());
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var deadlock = new PostgresException("deadlock detected", "ERROR", "ERROR", "40P01");

        await handler.TryHandleAsync(ctx, new InvalidOperationException("transient failure", new DbUpdateException("save", deadlock)), CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, ctx.Response.StatusCode);
    }

    private class FakeEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
