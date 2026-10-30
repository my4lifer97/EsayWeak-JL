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
        return text.Trim('‌', '‍', ' ');
    }
}
