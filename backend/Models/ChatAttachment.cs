using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

// A photo or file the owner sent from the Chats page (ChatMessage.AttachmentId). Kept in the
// database, not on disk: the backend container's files don't survive a deploy, and the owner
// should still see what they sent.
public class ChatAttachment
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BusinessId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Data { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
}
