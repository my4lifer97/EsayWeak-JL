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
}
