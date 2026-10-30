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
            && !e.EndsWith('.') && !e.Any(char.IsWhiteSpace) && !e.Contains("..")
            // The plain address only: MailAddress also takes "<a@b.com>", "a(x)@b.com" and
            // "\"a\"@b.com", which were stored as typed and slipped past the duplicate checks (QA run 8).
            && System.Net.Mail.MailAddress.TryCreate(e, out var parsed) && parsed.Address == e
            && e.All(c => c is not ('"' or '(' or ')' or '<' or '>' or ',' or ';' or '[' or ']' or '\\'))
            // Nothing invisible (a zero-width space or RTL override wrote one mailbox several ways),
            // and a real domain: no label starting or ending with "-", a top level of 2+ letters
            // ("a@-b.com", "a@b.c" passed; QA run 9).
            && !e.Any(c => char.IsControl(c) || System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format)
            // ASCII only: "a@Ｂ.com" and "a@é.com" wrote one mailbox several ways, and the mail we build
            // has no support for other addresses (QA run 10).
            && e.All(char.IsAscii)
            && ValidDomain(e[(at + 1)..]);
    }

    private static bool ValidDomain(string domain)
    {
        var labels = domain.Split('.');
        var top = labels[^1];
        return labels.All(l => l.Length is > 0 and <= 63 && l[0] != '-' && l[^1] != '-' && l.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            // A top level of letters, or an international one in its ASCII form ("xn--p1ai").
            && top.Length >= 2 && (top.All(char.IsAsciiLetter) || top.StartsWith("xn--", StringComparison.OrdinalIgnoreCase) && top.Length > 4);
    }
}
