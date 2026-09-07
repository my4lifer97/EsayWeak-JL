using System.Text;

namespace BarberSaas.Api.Services;

// Builds the login username for an approved business owner: first name + the first two letters of
// the family name, lowercased, with any non-letters dropped. Jamel + Marie -> "jamelma".
//
// The caller is responsible for uniqueness — GenerateUniqueAsync walks "jamelma", "jamelma1",
// "jamelma2", ... against a caller-supplied "already taken?" check (a DB lookup) until one is free.
// Names are validated as English-letters-only before they ever reach here (see
// BusinessOwnerRequestsController.Create), so the stripping below is just defence in depth.
public static class UsernameGenerator
{
    public static string BaseUsername(string firstName, string familyName)
    {
        var first = LettersOnly(firstName);
        var family = LettersOnly(familyName);
        var familyPrefix = family.Length >= 2 ? family[..2] : family;
        return (first + familyPrefix).ToLowerInvariant();
    }

    public static async Task<string> GenerateUniqueAsync(
        string firstName, string familyName, Func<string, Task<bool>> isTaken)
    {
        var root = BaseUsername(firstName, familyName);
        if (string.IsNullOrEmpty(root)) root = "owner";

        if (!await isTaken(root)) return root;
        for (var n = 1; ; n++)
        {
            var candidate = $"{root}{n}";
            if (!await isTaken(candidate)) return candidate;
        }
    }

    private static string LettersOnly(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetter(c)) sb.Append(c);
        return sb.ToString();
    }
}
