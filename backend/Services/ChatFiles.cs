using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.EntityFrameworkCore;

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

    // A customer's photo, video, voice note, sticker or file, sent with their message by the bridge
    // (kept for the Chats page while their chat is open). The bridge sends bigger ones by name only.
    public const long MaxInboundBytes = 16 * 1024 * 1024;

    // How much of customers' files is stored, per rolling 24 hours (QA run 5: anyone could open a
    // chat and send unlimited 16MB files into the shared database). Past it, the owner sees the
    // file's name and opens it on the phone, like a file too big to download.
    public const int MaxInboundFilesPerCustomerPerDay = 20;
    public const long MaxInboundBytesPerCustomerPerDay = 50L * 1024 * 1024;
    public const long MaxInboundBytesPerBusinessPerDay = 500L * 1024 * 1024;

    // Whether one more customer file of `size` bytes (0 when not known yet) may be stored.
    public static async Task<bool> InboundQuotaAllowsAsync(AppDbContext db, string businessId, string customerPhone, long size)
    {
        var since = DateTime.UtcNow.AddDays(-1);
        var customer = await db.ChatMessages
            .Where(m => m.BusinessId == businessId && m.CustomerPhone == customerPhone && m.Sender == ChatSender.Customer
                && m.AttachmentId != null && m.CreatedAt > since)
            .Select(m => (long)m.Attachment!.Data.Length)
            .ToListAsync();
        if (customer.Count >= MaxInboundFilesPerCustomerPerDay || customer.Sum() + size > MaxInboundBytesPerCustomerPerDay) return false;
        var business = await db.ChatAttachments
            .Where(a => a.BusinessId == businessId && a.CreatedAt > since)
            .SumAsync(a => (long)a.Data.Length);
        return business + size <= MaxInboundBytesPerBusinessPerDay;
    }

    // What a customer's file may be stored as, with the extension for a file WhatsApp sent unnamed
    // (photos, voice notes). Anything else is stored as a plain download, so a customer's file can't
    // open as a page (HTML, SVG) on the dashboard.
    private static readonly Dictionary<string, string> InboundTypes = new()
    {
        ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["image/webp"] = ".webp", ["image/gif"] = ".gif",
        ["video/mp4"] = ".mp4", ["video/3gpp"] = ".3gp", ["video/quicktime"] = ".mov",
        ["audio/ogg"] = ".ogg", ["audio/mpeg"] = ".mp3", ["audio/mp4"] = ".m4a", ["audio/aac"] = ".aac", ["audio/amr"] = ".amr",
        ["application/pdf"] = ".pdf",
        ["application/msword"] = ".doc",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
        ["application/vnd.ms-excel"] = ".xls",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx",
    };

    // "audio/ogg; codecs=opus" -> "audio/ogg"; an unknown type -> application/octet-stream.
    public static string InboundContentType(string? mimetype)
    {
        var type = (mimetype ?? "").Split(';')[0].Trim().ToLowerInvariant();
        return InboundTypes.ContainsKey(type) ? type : "application/octet-stream";
    }

    // The customer's own file name, or one made up from the kind of message ("photo.jpg", "voice.ogg").
    public static string InboundFileName(string? fileName, string? kind, string contentType)
    {
        if (!string.IsNullOrWhiteSpace(fileName)) return SafeFileName(fileName);
        var name = kind switch { "image" => "photo", "video" or "ptv" => "video", "audio" => "voice", "sticker" => "sticker", _ => "file" };
        return name + InboundTypes.GetValueOrDefault(contentType, "");
    }

    // Just the name (no path), without characters that break a download header, at most 120 long.
    public static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c) && c is not ('"' or '/' or ':' or '*' or '?' or '<' or '>' or '|')).ToArray()).Trim();
        if (name.Length == 0) name = "file";
        if (name.Length <= 120) return name;
        // A long "extension" ("a.<200 characters>") is just part of the name.
        var ext = Path.GetExtension(name);
        if (ext.Length > 20) ext = "";
        return TextCut.Cut(name[..^ext.Length], 120 - ext.Length) + ext;
    }
}
