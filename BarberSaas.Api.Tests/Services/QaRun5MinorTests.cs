using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

// QA run 5 minors that come down to one helper.
public class QaRun5MinorTests : IntegrationTestBase
{
    // BOT-6: a parked question kept its old expiry after a reschedule, and a later "1" confirmed
    // the new time without asking about it.
    [Fact]
    public async Task ForgettingTheQuestion_RemovesItsConversationRow_AndTheStatus()
    {
        string appointmentId;
        using (var db = Db())
        {
            var business = new Business { Name = "Q", Email = $"{Guid.NewGuid():N}@q.test", Slug = $"q-{Guid.NewGuid():N}" };
            var item = new Item { BusinessId = business.Id, NameEn = "Cut", NameAr = "Cut", NameHe = "Cut", DurationMinutes = 30 };
            var customer = new Customer { BusinessId = business.Id, Name = "C", Phone = "+15557770001" };
            var appointment = new Appointment
            {
                BusinessId = business.Id, CustomerId = customer.Id, ItemId = item.Id, Date = AppClock.Now.Date.AddDays(1),
                StartTime = "10:00", EndTime = "10:30", Status = AppointmentStatus.CONFIRMED, AttendanceStatus = AttendanceQuestion.Asked,
            };
            db.AddRange(business, item, customer, appointment);
            db.WhatsAppConversationStates.Add(new WhatsAppConversationState
            {
                BusinessId = business.Id, Phone = customer.Phone, ChatbotMode = AttendanceQuestion.ModePrefix + appointment.Id,
                ExpiresAt = DateTime.UtcNow.AddHours(20),
            });
            await db.SaveChangesAsync();
            appointmentId = appointment.Id;
        }

        using (var db = Db())
        {
            var appointment = await db.Appointments.SingleAsync(a => a.Id == appointmentId);
            await AttendanceQuestion.ForgetAsync(db, appointment);
            await db.SaveChangesAsync();
        }

        using (var db = Db())
        {
            Assert.Null((await db.Appointments.SingleAsync(a => a.Id == appointmentId)).AttendanceStatus);
            Assert.False(await db.WhatsAppConversationStates.AnyAsync(s => s.ChatbotMode.EndsWith(appointmentId)));
        }
    }

    // SEC-3: a right-to-left override let "invoice<RLO>fdp.exe" show as "invoiceexe.pdf".
    [Fact]
    public void SafeFileName_DropsDirectionOverrides()
    {
        Assert.Equal("invoicefdp.exe", ChatFiles.SafeFileName("invoice‮fdp.exe"));
    }

    [Theory]
    [InlineData("image/jpeg", new byte[] { 0x3C, 0x68, 0x74, 0x6D }, "application/octet-stream")] // "<htm" claimed as a photo
    [InlineData("image/jpeg", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData("application/pdf", new byte[] { 0x50, 0x4B, 0x03, 0x04 }, "application/octet-stream")]
    [InlineData("application/pdf", new byte[] { 0x25, 0x50, 0x44, 0x46 }, "application/pdf")]
    public void InboundContentType_ChecksTheBytes(string claimed, byte[] data, string expected)
    {
        Assert.Equal(expected, ChatFiles.InboundContentType(claimed, data));
    }

    // SEC-6: sign-in tokens in links were kept in chat history.
    [Fact]
    public void StoredBotMessages_LeaveOutLinkTokens()
    {
        var message = ChatLog.NewMessage("b", "+1", ChatSender.Bot,
            "Book here: https://x.app/shop/w/AbC123_-x and see https://x.app/shop/appointments/9?token=s3cr3t&lang=he");

        Assert.Equal("Book here: https://x.app/shop/w/... and see https://x.app/shop/appointments/9?token=...&lang=he", message.Text);
    }

    [Fact]
    public void CustomerMessages_AreStoredAsWritten()
    {
        Assert.Equal("my code /w/abc", ChatLog.NewMessage("b", "+1", ChatSender.Customer, "my code /w/abc").Text);
    }

    // CHAT-10: a 300-character address was accepted for chat alerts.
    [Fact]
    public void Email_TooLong_IsInvalid()
    {
        Assert.False(EmailAddress.IsValid(new string('a', 65) + "@example.com"));
        Assert.False(EmailAddress.IsValid("a@" + new string('b', 250) + ".com"));
        Assert.True(EmailAddress.IsValid(new string('a', 64) + "@example.com"));
    }
}
