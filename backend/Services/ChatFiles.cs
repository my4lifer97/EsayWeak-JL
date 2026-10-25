namespace BarberSaas.Api.Services;

// What the owner may send from the Chats page: photos (sent as WhatsApp images) and common
// documents. Each file's first bytes must match its extension, like ImageUpload, so a renamed
// file of another kind is refused.
public static class ChatFiles
{
    public const long MaxBytes = 10 * 1024 * 1024;
    // The whole multipart body (caption and phone included).
    public const long MaxRequestBytes = MaxBytes + 64 * 1024;
    public const string Error = "Send a photo (JPG, PNG, WEBP) or a PDF, Word or Excel file up to 10MB.";

    private static readonly Dictionary<string, string> AllowedTypes = new()
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png", [".webp"] = "image/webp",
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    // The content type to store and send it with, or null if it isn't allowed. Decided by the
    // extension and the file's own bytes, not the browser's Content-Type (which varies for Office files).
    public static async Task<string?> ValidateAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0 || file.Length > MaxBytes) return null;
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedTypes.TryGetValue(ext, out var contentType)) return null;

        var head = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false);
        var h = head.AsSpan(0, read);
        var ok = ext switch
        {
            ".pdf" => h.StartsWith("%PDF"u8),
            // Office Open XML files are zip archives.
            ".docx" or ".xlsx" => h.StartsWith((byte[])[0x50, 0x4B, 0x03, 0x04]),
            // Older Office files are OLE compound documents.
            ".doc" or ".xls" => h.StartsWith((byte[])[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]),
            _ => ImageUpload.HasSignature(h, contentType),
        };
        return ok ? contentType : null;
    }

    // Just the name (no path), without characters that break a download header, at most 120 long.
    public static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c) && c is not ('"' or '/' or ':' or '*' or '?' or '<' or '>' or '|')).ToArray()).Trim();
        if (name.Length == 0) name = "file";
        if (name.Length <= 120) return name;
        var ext = Path.GetExtension(name);
        return name[..(120 - ext.Length)] + ext;
    }
}
