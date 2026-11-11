using System.Reflection;
using BarberSaas.Api.Controllers;
using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

// QA run 11 fixes that come down to one helper.
public class QaRun11Tests
{
    // SEC-60: .NET's "$" also matches before a final "\n".
    [Theory]
    [InlineData("admin\n", false)]
    [InlineData("shop\n", false)]
    [InlineData("shop", true)]
    public void SlugFormat_RejectsATrailingNewline(string slug, bool valid)
    {
        Assert.Equal(valid, SlugValidator.IsValidFormat(slug));
    }

    [Fact]
    public void SlotTime_RejectsATrailingNewline()
    {
        Assert.NotNull(AvailabilityService.SlotTimeError("10:00\n", 30));
        Assert.False(ImageUpload.IsCustomerPhotoUrl("/api/uploads/appointment-photos/" + new string('a', 32) + ".jpg\n"));
    }

    // ST-60
    [Fact]
    public void PendingCancellation_ReadsCompletedOnceTheVisitIsOver()
    {
        var past = AppClock.Now.Date.AddDays(-2);
        var future = AppClock.Now.Date.AddDays(2);
        Assert.Equal("COMPLETED", AppointmentStatusHelper.CustomerFacingStatus(AppointmentStatus.CONFIRMED, true, past, "10:00"));
        Assert.Equal("CANCELLATION_PENDING", AppointmentStatusHelper.CustomerFacingStatus(AppointmentStatus.CONFIRMED, true, future, "10:00"));
    }

    // ST-63: 2027-03-26 02:30 doesn't exist in Israel (clocks jump 02:00 -> 03:00).
    [Fact]
    public void AttendanceQuestion_StartInTheSpringGap_DoesNotThrow()
    {
        var appointment = new Appointment { Id = "a1", Date = new DateTime(2027, 3, 26), StartTime = "02:30", EndTime = "03:00" };
        var state = AttendanceQuestion.Park(null!, new WhatsAppConversationState { BusinessId = "b", Phone = "+15550000000" }, appointment, "EN");
        Assert.Equal(new DateTime(2027, 3, 26, 0, 30, 0), state.ExpiresAt);
    }

    // CHAT-63
    [Theory]
    [InlineData("\u200B", false)]
    [InlineData("\u200E\u200F", false)]
    [InlineData("\u2800", false)]
    [InlineData("\u202E", false)]
    [InlineData("\u0301", false)]
    [InlineData("hi", true)]
    [InlineData("שלום", true)]
    [InlineData("👍", true)]
    public void HasVisibleText(string text, bool visible)
    {
        Assert.Equal(visible, ChatFiles.HasVisibleText(text));
    }

    private static string Strip(string reply) =>
        (string)typeof(WhatsAppController).GetMethod("StripUnknownUrls", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [reply, new List<string>()])!;

    // AI-62
    [Theory]
    [InlineData("We are open Sunday.To book, reply 1.", "We are open Sunday.To book, reply 1.")]
    [InlineData("Hello.Shop is closed", "Hello.Shop is closed")]
    [InlineData("Ask the Friday.Co-owner", "Ask the Friday.Co-owner")]
    [InlineData("Visit evil.com now", "Visit  now")]
    [InlineData("Visit www.Evil.Com now", "Visit  now")]
    public void StripUnknownUrls_KeepsSentencesMissingASpace(string reply, string expected)
    {
        Assert.Equal(expected, Strip(reply));
    }
}
