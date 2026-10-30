using BarberSaas.Api.Services;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

// QA run 9 fixes that come down to one helper.
public class QaRun9Tests
{
    // SEC-40
    [Theory]
    [InlineData("a​@b.com", false)]
    [InlineData("a‮@b.com", false)]
    [InlineData("a@-b.com", false)]
    [InlineData("a@b.c", false)]
    [InlineData("dana@mail.example.co.il", true)]
    public void EmailAddress_NoInvisibleCharacters_AndARealDomain(string email, bool valid)
    {
        Assert.Equal(valid, EmailAddress.IsValid(email));
    }

    // PUB-42
    [Theory]
    [InlineData("+9720000000", false)]
    [InlineData("0972501234574", false)]
    [InlineData("1234567", false)]
    [InlineData("050-123-4567", true)]
    [InlineData("03-1234567", true)]
    [InlineData("+1 555 901 0001", true)]
    public void Phone_IsraeliNumbersNeedARealLength(string phone, bool valid)
    {
        Assert.Equal(valid, PhoneNormalizer.IsValid(phone));
    }

    // OWN-40 / OWN-45
    [Theory]
    [InlineData("́", "")]
    [InlineData("️", "")]
    [InlineData("Dana", "Dana")]
    public void TextClean_Name_MarksOnlyIsEmpty(string input, string expected)
    {
        Assert.Equal(expected, TextClean.Name(input));
    }

    [Theory]
    [InlineData("   ", null)]
    [InlineData("​", null)]
    [InlineData("‮Hi", "Hi")]
    [InlineData(" line 1\r\nline 2 ", "line 1\nline 2")]
    public void TextClean_Note(string input, string? expected)
    {
        Assert.Equal(expected, TextClean.Note(input));
    }

    // CHAT-40 / CHAT-41
    [Theory]
    [InlineData("\U000E0061\U000E0062", "file")]
    [InlineData("a\U000F0000.pdf", "a.pdf")]
    [InlineData("‍.pdf", ".pdf")]
    [InlineData("a.pdf.‍", "a.pdf")]
    [InlineData("️", "file")]
    public void SafeFileName_CodePointsAndJoiners(string input, string expected)
    {
        Assert.Equal(expected, ChatFiles.SafeFileName(input));
    }

    [Fact]
    public void SafeFileName_ManyJoinersKeepTheExtension()
    {
        Assert.Equal("a.pdf", ChatFiles.SafeFileName(new string('‍', 130) + "a.pdf"));
    }
}
