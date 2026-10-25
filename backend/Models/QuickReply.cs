using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

// A ready message the owner saved for the Chats page (⚡ Ready messages, "My messages"), put into
// the message box with one click. Written in whatever language the owner typed it in.
public class QuickReply
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BusinessId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
}
