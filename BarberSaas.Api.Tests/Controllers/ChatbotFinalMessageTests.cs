using BarberSaas.Api.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BarberSaas.Api.DTOs;
using Xunit;

namespace BarberSaas.Api.Tests.Controllers;

// The owner's custom "final message" is sent on WhatsApp once a booking is created. QA run 2: it
// replaced the default text entirely, so the customer never got the date and time.
public class ChatbotFinalMessageTests : IntegrationTestBase
{
    private record RegisterResponse(string? DevCode);

    private async Task<(string BusinessId, string ItemId, string Date)> SeedBusiness(string slug, string finalMessage)
    {
        var email = $"{slug}@example.com";
        var register = await Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Business", email, "password123", slug));
        var code = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.DevCode!;
        var login = await (await Client.PostAsJsonAsync("/api/auth/verify-email", new VerifyEmailRequest(email, code))).Content.ReadFromJsonAsync<LoginResponse>();
        Authorize(Client, login!.Token);

        var item = await (await Client.PostAsJsonAsync("/api/admin/items", new CreateItemRequest("Haircut", "Haircut", "Haircut", 30, 20m))).Content.ReadFromJsonAsync<ItemDto>();
        var date = AppClock.Now.Date.AddDays(1);
        await Client.PostAsJsonAsync("/api/admin/schedule", new List<WorkingHoursDto> { new(null, (int)date.DayOfWeek, "09:00", "18:00", true) });
        await Client.PatchAsJsonAsync("/api/admin/settings", new
        {
            chatbotFinalMessageEn = finalMessage, chatbotFinalMessageAr = finalMessage, chatbotFinalMessageHe = finalMessage,
        });
        Client.DefaultRequestHeaders.Authorization = null;

        using var db = Db();
        var business = db.Businesses.First(b => b.Slug == slug);
        business.WhatsAppNumber = "+15550009999";
        db.SaveChanges();
        return (business.Id, item!.Id, date.ToString("yyyy-MM-dd"));
    }

    private async Task<string> BookAndGetConfirmation(string slug, string businessId, string itemId, string date, string phone)
    {
        var customerToken = (await LoginCustomerViaWhatsAppAsync(phone, "Dana", "Levi")).Token;
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/{slug}/appointments")
        {
            Content = JsonContent.Create(new BookAppointmentRequest(itemId, date, "10:00", "Dana", "", null)),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        Assert.Equal(HttpStatusCode.Created, (await Client.SendAsync(req)).StatusCode);
        return Factory.WhatsAppSender.Sent.Last(s => s.BusinessId == businessId && s.Phone == phone).Message;
    }

    [Fact]
    public async Task CustomFinalMessage_FillsPlaceholders()
    {
        var (businessId, itemId, date) = await SeedBusiness("final-msg-placeholders", "Thanks {customerName}! See you {date} at {time} for your {service}.");

        var message = await BookAndGetConfirmation("final-msg-placeholders", businessId, itemId, date, "+15558880001");

        Assert.Equal($"Thanks Dana! See you {date} at 10:00 for your Haircut.", message);
    }

    // QA run 4 (ST-15): a booking that didn't start from a pending chat link was confirmed in the
    // business's language, even though the customer had been chatting with the bot in another one.
    [Fact]
    public async Task Confirmation_UsesTheCustomersLastChatbotLanguage()
    {
        var (businessId, itemId, date) = await SeedBusiness("final-msg-lang", "unused");
        using (var db = Db())
        {
            var business = db.Businesses.First(b => b.Id == businessId);
            business.ChatbotFinalMessageEn = "EN {date}";
            business.ChatbotFinalMessageHe = "HE {date}";
            db.SaveChanges();
        }
        const string phone = "+15558880003";
        var customerToken = (await LoginCustomerViaWhatsAppAsync(phone, "Dana", "Levi")).Token;
        using (var db = Db())
        {
            db.CustomerAccounts.First(a => a.Phone == phone).LastMessageLanguage = "HE";
            db.SaveChanges();
        }

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/final-msg-lang/appointments")
        {
            Content = JsonContent.Create(new BookAppointmentRequest(itemId, date, "10:00", "Dana", "", null)),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        Assert.Equal(HttpStatusCode.Created, (await Client.SendAsync(req)).StatusCode);

        var message = Factory.WhatsAppSender.Sent.Last(s => s.BusinessId == businessId && s.Phone == phone).Message;
        Assert.StartsWith($"HE {date}", message);
    }

    [Fact]
    public async Task CustomFinalMessage_WithoutDateOrTime_GetsTheDetailsAppended()
    {
        var (businessId, itemId, date) = await SeedBusiness("final-msg-plain", "Thank you for booking with us!");

        var message = await BookAndGetConfirmation("final-msg-plain", businessId, itemId, date, "+15558880002");

        Assert.StartsWith("Thank you for booking with us!", message);
        Assert.Contains(date, message);
        Assert.Contains("10:00", message);
        Assert.Contains("Haircut", message);
    }
}
