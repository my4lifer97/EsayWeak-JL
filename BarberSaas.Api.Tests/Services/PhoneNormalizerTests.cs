using BarberSaas.Api.Services;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

public class PhoneNormalizerTests
{
    [Theory]
    [InlineData("+1 (555) 000-1234", "+15550001234")]
    [InlineData("555-000-1234", "5550001234")]
    [InlineData("+972 50-123-4567", "+972501234567")]
    [InlineData("+15550001234", "+15550001234")]
    [InlineData("  +1 555 000 1234  ", "+15550001234")]
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
