using System.Text.RegularExpressions;

namespace BarberSaas.Api.Services;

// Shared between AuthController.Register (self-service signup) and
// PlatformAdminController.ApproveBusinessOwnerRequest (admin-issued signup) so both paths reject
// the same reserved words and format, rather than maintaining two copies of this list.
public static class SlugValidator
{
    private static readonly string[] ReservedSlugs =
        // Every top-level frontend route (App.tsx) must be here, or that storefront can never load.
        ["admin", "api", "login", "register", "cron", "whatsapp", "_next", "favicon", "browse", "account",
         "platform-admin", "request-business-account", "wa-link"];

    public static bool IsValidFormat(string slug) =>
        !string.IsNullOrWhiteSpace(slug) && Regex.IsMatch(slug, @"^[a-z0-9-]+$") && slug.Length >= 3;

    public static bool IsReserved(string slug) => ReservedSlugs.Contains(slug);
}
