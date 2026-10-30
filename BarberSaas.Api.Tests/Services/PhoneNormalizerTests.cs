using BarberSaas.Api.Services;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

public class PhoneNormalizerTests
{
    [Theory]
    [InlineData("+1 (555) 000-1234", "+15550001234")]
    // No "+" = a local Israeli number (the business is Israel-based), same as ToE164.
    [InlineData("555-000-1234", "+9725550001234")]
    [InlineData("050-123-4567", "+972501234567")]
    [InlineData("0501234567", "+972501234567")]
    [InlineData("00972501234567", "+972501234567")]
    [InlineData("0044 20 7946 0000", "+442079460000")]
    [InlineData("+972 50-123-4567", "+972501234567")]
    [InlineData("+15550001234", "+15550001234")]
    [InlineData("  +1 555 000 1234  ", "+15550001234")]
    [InlineData("+972 050-123-4567", "+972501234567")]
    [InlineData("972501234567", "+972501234567")]
    [InlineData("(+1) 555 000 1234", "+15550001234")]
    [InlineData("\u0660\u0665\u0660\u0661\u0662\u0663\u0664\u0665\u0666\u0667", "+972501234567")]
    public void Normalize_ProducesExpectedResult(string input, string expected)
    {
        Assert.Equal(expected, PhoneNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_DifferentlyFormattedSameNumber_ProducesSameResult()
    {
        var a = PhoneNormalizer.Normalize("+1 (555) 000-1234");
        var b = PhoneNormalizer.Normalize("+15550001234");
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void Normalize_NoDigits_ReturnsEmpty(string input)
    {
        Assert.Equal("", PhoneNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("050-123-4567", true)]
    [InlineData("+972 (50) 123.4567", true)]
    [InlineData("+1234567", true)]          // 7 digits with a country code; "1234567" is a too-short Israeli number (QA run 9)
    [InlineData("123456", false)]           // too short
    [InlineData("1234567890123456", false)] // too long
    [InlineData("ASD", false)]
    [InlineData("050-ABC-4567", false)]
    [InlineData("+", false)]
    [InlineData("+0000000000", false)]
    [InlineData("\u0660\u0665\u0660\u0661\u0662\u0663\u0664\u0665\u0666\u0667", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_AcceptsOnlyRealLookingNumbers(string? input, bool expected)
    {
        Assert.Equal(expected, PhoneNormalizer.IsValid(input));
    }

    [Theory]
    [InlineData("0556652545", "+972556652545")]
    [InlineData("055-665-2545", "+972556652545")]
    [InlineData("+972556652545", "+972556652545")]
    [InlineData("+1 555 000 1234", "+15550001234")]
    public void ToE164_AddsIsraeliCountryCodeOnlyWhenMissing(string input, string expected)
    {
        Assert.Equal(expected, PhoneNormalizer.ToE164(input));
    }

    // The owner's phone is free text in Settings, often typed locally -- the bridge must still get
    // a real WhatsApp number (a local one used to hang the send for 100s).
    [Fact]
    public async Task BridgeWhatsAppSender_SendsInE164()
    {
        var bridge = new BarberSaas.Api.Tests.Fakes.FakeWhatsAppBridgeClient();
        var sender = new BridgeWhatsAppSender(bridge);

        await sender.SendAsync(new BarberSaas.Api.Models.Business { Id = "b1" }, "0556652545", "hi");

        Assert.Equal("+972556652545", Assert.Single(bridge.Sent).ToPhone);
    }
}
