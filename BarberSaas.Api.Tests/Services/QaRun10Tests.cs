using BarberSaas.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

// QA run 10 fixes that come down to one helper.
public class QaRun10Tests
{
    // SEC-50 / OWN-57
    [Theory]
    [InlineData("a@Ｂ.com", false)]
    [InlineData("a@é.com", false)]
    [InlineData("é@b.com", false)]
    [InlineData("a@b.cöm", false)]
    [InlineData("a@exa_mple.com", false)]
    [InlineData("a@b.xn--p1ai", true)]
    [InlineData("a@xn--mnchen-3ya.de", true)]
    [InlineData("a@b.xn--", false)]
    [InlineData("dana.c+1@mail.example.co.il", true)]
    public void EmailAddress_AsciiOnly_AndPunycodeTopLevels(string email, bool valid)
    {
        Assert.Equal(valid, EmailAddress.IsValid(email));
    }

    // CHAT-50 / CHAT-51
    [Theory]
    [InlineData("x ", "x")]
    [InlineData(" x", "x")]
    [InlineData("x ", "x")]
    [InlineData("a.́", "a")]
    [InlineData("a\U000E0100", "a\U000E0100")]
    [InlineData("\U000E0100", "file")]
    [InlineData("שָׁלוֹם.pdf", "שָׁלוֹם.pdf")]
    public void SafeFileName_SpacesAndMarks(string input, string expected)
    {
        Assert.Equal(expected, ChatFiles.SafeFileName(input));
    }

    // CHAT-52
    [Theory]
    [InlineData(' ')]
    [InlineData('.')]
    public void SafeFileName_CutDoesNotEndOnASpaceOrDot(char at)
    {
        var name = ChatFiles.SafeFileName(new string('a', 115) + at + new string('b', 50));
        Assert.False(name.EndsWith(' ') || name.EndsWith('.'), name);

        var withExtension = ChatFiles.SafeFileName(new string('a', 115) + at + new string('b', 50) + ".pdf");
        Assert.EndsWith(".pdf", withExtension);
        Assert.DoesNotContain(at + ".pdf", withExtension);
    }

    private static LoginThrottle Throttle() => new(new ConfigurationBuilder().Build());

    // SEC-51
    [Fact]
    public void LoginThrottle_AnAddressThatSignedInBefore_SkipsTheAccountWideLimit()
    {
        var throttle = Throttle();
        var now = DateTime.UtcNow;
        throttle.RecordSuccess("owner", "dana@example.com", "1.1.1.1");

        for (var i = 0; i < 20; i++) throttle.RecordFailure("owner", "dana@example.com", $"9.9.9.{i}", now);

        Assert.Null(throttle.RetryAfter("owner", "dana@example.com", "1.1.1.1", now));
        Assert.NotNull(throttle.RetryAfter("owner", "dana@example.com", "2.2.2.2", now));
    }

    [Fact]
    public void LoginThrottle_AnAddressThatSignedInBefore_KeepsItsOwnLimit()
    {
        var throttle = Throttle();
        var now = DateTime.UtcNow;
        throttle.RecordSuccess("owner", "dana@example.com", "1.1.1.1");

        for (var i = 0; i < 5; i++) throttle.RecordFailure("owner", "dana@example.com", "1.1.1.1", now);

        Assert.NotNull(throttle.RetryAfter("owner", "dana@example.com", "1.1.1.1", now));
    }
}
