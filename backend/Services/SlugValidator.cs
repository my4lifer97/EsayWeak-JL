using System.Text.RegularExpressions;

namespace BarberSaas.Api.Services;

// Shared between AuthController.Register (self-service signup) and
// PlatformAdminController.ApproveBusinessOwnerRequest (admin-issued signup) so both paths reject
// the same reserved words and format, rather than maintaining two copies of this list.
public static class SlugValidator
{
    private static readonly string[] ReservedSlugs =
        ["admin", "api", "login", "register", "cron", "whatsapp", "_next", "favicon", "browse", "account"];

    public static bool IsValidFormat(string slug) =>
        !string.IsNullOrWhiteSpace(slug) && Regex.IsMatch(slug, @"^[a-z0-9-]+$") && slug.Length >= 3;

    public static bool IsReserved(string slug) => ReservedSlugs.Contains(slug);
}
