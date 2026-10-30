namespace BarberSaas.Api.Services;

// Owner emails are matched case-insensitively: a phone keyboard's capital letter used to break
// login, and Name@x.com / name@x.com could register as two accounts. New rows are stored
// normalized; lookups compare Email.ToLower() so older mixed-case rows still match.
public static class EmailAddress
{
    public static string Normalize(string? email) => (email ?? "").Trim().ToLowerInvariant();

    // Shape check only (one @, a dot in the domain, no spaces) -- delivery is the real test. The
    // RFC 5321 lengths too (64 before the @, 254 in all): a 300-character address was accepted for
    // chat alerts (QA run 5).
    public static bool IsValid(string? email)
    {
        var e = (email ?? "").Trim();
        var at = e.IndexOf('@');
        return at > 0 && at <= 64 && e.Length <= 254 && at == e.LastIndexOf('@') && e.IndexOf('.', at) > at + 1
            && !e.EndsWith('.') && !e.Any(char.IsWhiteSpace)
            && System.Net.Mail.MailAddress.TryCreate(e, out _);
    }
}
