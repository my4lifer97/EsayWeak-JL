namespace BarberSaas.Api.Services;

// The terms and privacy policy a sign-up agreed to. Kept in step with LEGAL_UPDATED in
// frontend/src/lib/legalContent.ts: change both when the text changes. Stored from here, not from
// the client, so a sign-up can't claim a version it never saw.
public static class LegalTerms
{
    public const string Version = "2026-11-11";
    public const string NotAcceptedError = "Please agree to the Terms of Use and Privacy Policy";
}
