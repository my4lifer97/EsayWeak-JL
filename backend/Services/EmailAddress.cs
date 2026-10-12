namespace BarberSaas.Api.Services;

// Owner emails are matched case-insensitively: a phone keyboard's capital letter used to break
// login, and Name@x.com / name@x.com could register as two accounts. New rows are stored
// normalized; lookups compare Email.ToLower() so older mixed-case rows still match.
public static class EmailAddress
{
    public static string Normalize(string? email) => (email ?? "").Trim().ToLowerInvariant();
}
