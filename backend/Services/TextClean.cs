using System.Globalization;

namespace BarberSaas.Api.Services;

public static class TextClean
{
    // A person's name as typed by the owner: no line breaks, tabs or invisible/direction characters
    // (QA run 8: a zero-width-only name or an RTL override was saved), runs of spaces as one, trimmed.
    // The zero-width joiner/non-joiner stay: Persian and Arabic-script names and emoji need them.
    public static string Name(string name)
    {
        var kept = name.Select(c => char.IsControl(c) ? ' ' : c)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format || c is '‌' or '‍');
        var text = string.Join(' ', new string(kept.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        text = text.Trim('‌', '‍', ' ');
        // Only combining marks (a lone U+0301, U+FE0F) shows as nothing (QA run 9).
        return text.All(IsMarkOrJoiner) ? "" : text;
    }

    // Free text such as booking notes: line breaks kept, other control and invisible/direction
    // characters dropped, trimmed; blank (or only marks) is null (QA run 9: "   ", a zero-width
    // space and an RTL override were saved as notes).
    public static string? Note(string? note)
    {
        if (note is null) return null;
        var kept = note.Replace("\r\n", "\n")
            .Select(c => c == '\t' ? ' ' : c)
            .Where(c => c == '\n' || !char.IsControl(c))
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format || c is '‌' or '‍');
        var text = new string(kept.ToArray()).Trim();
        return text.Length == 0 || text.All(c => IsMarkOrJoiner(c) || char.IsWhiteSpace(c)) ? null : text;
    }

    private static bool IsMarkOrJoiner(char c) =>
        c is '‌' or '‍' || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark or UnicodeCategory.SpacingCombiningMark;
}
