using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

public class CustomerAccount
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Phone { get; set; } = "";
    public string Name { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    // "HE"/"AR"/"EN", detected from the last WhatsApp message this phone sent to any business
    // (WhatsAppController.DetectLanguage) -- outbound messages we send on our own initiative, like
    // the waitlist "slot opened up" one, go out in it. Null until they've written something with
    // a language signal; callers then fall back to the business's language.
    public string? LastMessageLanguage { get; set; }

    public ICollection<Customer> Profiles { get; set; } = [];
    public ICollection<Follow> Follows { get; set; } = [];
    public ICollection<WaitlistEntry> WaitlistEntries { get; set; } = [];
    public ICollection<Review> Reviews { get; set; } = [];
}
