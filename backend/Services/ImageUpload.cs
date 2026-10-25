namespace BarberSaas.Api.Services;

// Shared check for every image upload (logo, item gallery, customer reference photo). The
// extension and the browser-sent Content-Type are both just claims -- QA uploaded an HTML file
// renamed to .png and it was accepted -- so the file's first bytes must also match that format.
public static class ImageUpload
{
    public const long MaxBytes = 5 * 1024 * 1024;
    // For [RequestSizeLimit]: the whole multipart body, so the file itself can be up to MaxBytes
    // (a 5,242,830-byte image was refused at exactly MaxBytes, QA run 3).
    public const long MaxRequestBytes = MaxBytes + 64 * 1024;
    public const string Error = "Please upload a JPG, PNG, or WEBP image up to 5MB.";

    private static readonly Dictionary<string, string> AllowedTypes = new()
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp",
    };

    // Returns the lowercase extension to save the file under, or null if it isn't a real JPG/PNG/WEBP.
    // Exactly what POST /{slug}/appointments/photo returns. A prefix check let
    // "/api/uploads/appointment-photos/../../secret" through (QA run 3).
    public static bool IsCustomerPhotoUrl(string? url) =>
        url is not null && System.Text.RegularExpressions.Regex.IsMatch(url, @"^/api/uploads/appointment-photos/[0-9a-f]{32}\.(jpg|jpeg|png|webp)$");

    public static async Task<string?> ValidateAsync(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (file.Length == 0 || file.Length > MaxBytes
            || !AllowedTypes.TryGetValue(ext, out var expectedContentType)
            || file.ContentType != expectedContentType)
            return null;

        var head = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false);
        return HasSignature(head.AsSpan(0, read), expectedContentType) ? ext : null;
    }

    internal static bool HasSignature(ReadOnlySpan<byte> h, string contentType) => contentType switch
    {
        "image/jpeg" => h.StartsWith((byte[])[0xFF, 0xD8, 0xFF]),
        "image/png" => h.StartsWith((byte[])[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        // "RIFF" <4-byte size> "WEBP"
        "image/webp" => h.Length >= 12 && h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8),
        _ => false,
    };
}
