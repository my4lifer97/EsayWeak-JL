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
        var trimmed = phone.Trim();
        var digits = Regex.Replace(trimmed, @"[^\d]", "");
        if (digits.Length == 0) return "";
        if (trimmed.StartsWith('+')) return $"+{digits}";
        if (digits.StartsWith("00")) return $"+{digits[2..]}";
        return $"+972{digits.TrimStart('0')}";
    }

    // Kept for the send paths (SMS, WhatsApp), which must hand out E.164 even for a free-text
    // number that was never stored through Normalize (e.g. the owner's own phone in Settings).
    public static string ToE164(string phone) => Normalize(phone);

    // 7-15 digits, the E.164 range -- rejects letters-only input that would otherwise normalize
    // to an empty phone (and create an account with no number).
    public static bool IsValid(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone) || Regex.IsMatch(phone, @"[^\d\s()+\-.]")) return false;
        var digits = Regex.Replace(phone, @"[^\d]", "").Length;
        return digits is >= 7 and <= 15;
    }
}
