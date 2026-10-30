using BarberSaas.Api.Models;
using BarberSaas.Api.Services;
using Xunit;

namespace BarberSaas.Api.Tests.Services;

// QA run 5: a cut in the middle of an emoji left a lone surrogate, which Postgres refuses (500).
public class TextCutTests
{
    private static bool HasLoneSurrogate(string s) =>
        s.Length > 0 && (char.IsHighSurrogate(s[^1]) || s.Where((c, i) => char.IsLowSurrogate(c) && (i == 0 || !char.IsHighSurrogate(s[i - 1]))).Any());

    [Fact]
    public void Cut_NeverSplitsAnEmoji()
    {
        var text = new string('a', 9) + "😀";

        Assert.Equal(new string('a', 9), TextCut.Cut(text, 10));
        Assert.Equal(text, TextCut.Cut(text, 11));
    }

    [Fact]
    public void ChatMessage_CutAtTheLimit_KeepsWholeEmoji()
    {
        var text = new string('a', ChatLog.MaxTextChars - 1) + "😀😀";

        var message = ChatLog.NewMessage("b", "+1", ChatSender.Customer, text);

        Assert.False(HasLoneSurrogate(message.Text));
        Assert.True(message.Text.Length <= ChatLog.MaxTextChars);
    }

    [Theory]
    [InlineData(115)]
    [InlineData(116)]
    public void SafeFileName_CutAtTheLimit_KeepsWholeEmojiAndTheExtension(int padding)
    {
        var name = ChatFiles.SafeFileName(new string('a', padding) + "😀😀😀😀😀.pdf");

        Assert.False(HasLoneSurrogate(name));
        Assert.EndsWith(".pdf", name);
        Assert.True(name.Length <= 120);
    }

    [Fact]
    public void SafeFileName_AVeryLongExtension_DoesNotThrow()
    {
        var name = ChatFiles.SafeFileName("a." + new string('x', 200));

        Assert.True(name.Length <= 120);
    }
}
