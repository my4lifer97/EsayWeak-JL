using System.Globalization;

namespace BarberSaas.Api.Services;

// .NET's \d matches every script's digits (Arabic-Indic, Devanagari, Thai, fullwidth...), but
// int.Parse only reads 0-9: "२" passed the chatbot's number regex and then threw a 500, and
// "٠٥٠..." was stored as a phone as typed (QA run 7). Text is turned into ASCII digits first,
// and patterns that feed int.Parse use [0-9], never \d.
public static class Digits
{
    public static string ToAscii(string text)
    {
        if (!text.Any(c => c > 127 && char.IsDigit(c))) return text;
        return new string(text.Select(c => c > 127 && char.IsDigit(c) ? (char)('0' + CharUnicodeInfo.GetDecimalDigitValue(c)) : c).ToArray());
    }
}
