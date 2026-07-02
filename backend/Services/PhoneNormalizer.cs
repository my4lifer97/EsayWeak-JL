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
}
