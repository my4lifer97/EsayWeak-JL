namespace BarberSaas.Api.Services;

public static class TextCut
{
    // At most `max` characters, never splitting an emoji (surrogate pair): Npgsql refuses to save a
    // lone surrogate, so a message, name or file name cut there failed with a 500 (QA runs 2 and 5).
    public static string Cut(string text, int max)
    {
        if (text.Length <= max) return text;
        return char.IsHighSurrogate(text[max - 1]) ? text[..(max - 1)] : text[..max];
    }
}
