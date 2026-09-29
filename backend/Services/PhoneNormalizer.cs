using System.Text.RegularExpressions;

namespace BarberSaas.Api.Services;

public static class PhoneNormalizer
{
    public static string Normalize(string phone)
    {
        var trimmed = phone.Trim();
        var hasPlus = trimmed.StartsWith('+');
        var digits = Regex.Replace(trimmed, @"[^\d]", "");
        return hasPlus ? $"+{digits}" : digits;
    }

    // Normalize (used for every stored phone, so it can't change without breaking existing
    // phone-matching) keeps a "+" only if one was typed -- a bare local number like "0501234567"
    // comes through with no country code. Anything sent out (SMS, WhatsApp) needs E.164. The
    // business is Israel-based (Hebrew/Arabic UI, ILS pricing, Cardcom), so a number with no
    // country code is taken as a local Israeli number (leading 0 dropped in favor of +972).
    public static string ToE164(string phone)
    {
        var normalized = Normalize(phone);
        return normalized.StartsWith('+') ? normalized : $"+972{normalized.TrimStart('0')}";
    }
}
