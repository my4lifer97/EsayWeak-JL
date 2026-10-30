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
         "platform-admin", "admin-platform", "request-business-account", "wa-link",
         // API prefixes and words a storefront shouldn't take (QA run 6).
         "customer", "customers", "businesses", "business-types", "auth", "uploads", "health", "static", "assets", "support", "help",
         "reviews", "business-owner-requests"];

    // Hyphens only between letters/digits: "a--b" and "shop-" were accepted (QA run 7).
    public static bool IsValidFormat(string slug) =>
        !string.IsNullOrWhiteSpace(slug) && Regex.IsMatch(slug, @"^[a-z0-9]+(-[a-z0-9]+)*$") && slug.Length is >= 3 and <= MaxLength;

    // A 300-character slug registered (QA run 4); it goes into every link we send.
    public const int MaxLength = 60;

    public static bool IsReserved(string slug) => ReservedSlugs.Contains(slug);
}
