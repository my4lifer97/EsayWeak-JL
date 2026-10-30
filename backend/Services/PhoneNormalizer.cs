using System.Text.RegularExpressions;

namespace BarberSaas.Api.Services;

public static class PhoneNormalizer
{
    // The one stored format for every customer phone (Customers, CustomerAccounts, WhatsApp state,
    // OTPs, booking tokens, inquiries): E.164. Every phone that enters the system goes through here
    // before it's saved or matched, so "050-123-4567", "0501234567" and WhatsApp's "+972501234567"
    // are all the same customer. The business is Israel-based (Hebrew/Arabic UI, ILS pricing,
    // Cardcom), so a number with no country code is taken as a local Israeli number (leading 0
    // dropped in favor of +972); a "00" international prefix is read as "+". Existing rows were
    // rewritten to this format by the NormalizeStoredPhones migration -- keep the two in sync.
    public static string Normalize(string phone)
    {
        var trimmed = Digits.ToAscii(WithoutMarks(phone).Trim());
        var digits = Regex.Replace(trimmed, @"[^0-9]", "");
        if (digits.Length == 0) return "";
        // "(+1) 555...", "+972 050..." (the local 0 kept after the country code) and "972 50..."
        // with no plus all came out as a different number (QA run 7).
        string e164;
        if (trimmed.TrimStart('(', ' ').StartsWith('+')) e164 = $"+{digits}";
        else if (digits.StartsWith("00")) e164 = $"+{digits[2..]}";
        else if (digits.Length == 12 && digits.StartsWith("972")) e164 = $"+{digits}";
        // "972 050..." with no plus and the local 0 kept (QA run 8: stored as +972972050...).
        else if (digits.Length == 13 && digits.StartsWith("9720")) e164 = $"+{digits}";
        else return $"+972{digits.TrimStart('0')}";
        return e164.StartsWith("+9720") ? $"+972{e164[5..]}" : e164;
    }

    // Direction marks and other invisible format characters a copied number often carries
    // (U+202A..U+202E, U+200E/U+200F, U+2066..U+2069): "Please enter a valid phone number" (QA run 8).
    private static string WithoutMarks(string phone) =>
        new(phone.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).ToArray());

    // Kept for the send paths (SMS, WhatsApp), which must hand out E.164 even for a free-text
    // number that was never stored through Normalize (e.g. the owner's own phone in Settings).
    public static string ToE164(string phone) => Normalize(phone);

    // 7-15 digits, the E.164 range -- rejects letters-only input that would otherwise normalize
    // to an empty phone (and create an account with no number).
    public static bool IsValid(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return false;
        phone = Digits.ToAscii(WithoutMarks(phone));
        if (Regex.IsMatch(phone, @"[^0-9\s()+\-.]")) return false;
        var digits = Regex.Replace(phone, @"[^0-9]", "").Length;
        // No country code starts with 0: "+0000000000" was accepted.
        return digits is >= 7 and <= 15 && !Normalize(phone).StartsWith("+0");
    }
}
