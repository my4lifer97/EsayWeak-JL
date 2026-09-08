using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

// A customer's rating (1-5) + optional comment for a business. Gated on the customer having a
// completed appointment there (Status == CONFIRMED with its end time in the past -- see
// AppointmentStatusHelper; there is no stored COMPLETED). One review per (CustomerAccount,
// Business), editable in place -- see the unique index in AppDbContext.
public class Review
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string BusinessId { get; set; } = "";
    public string CustomerAccountId { get; set; } = "";
    // The customer's most-recent completed appointment at the time the review was created --
    // informational ("verified visit"), not kept in sync afterwards. Restrict on delete to match
    // Appointment's own FK conventions (appointments are never hard-deleted anyway).
    public string AppointmentId { get; set; } = "";

    public int Rating { get; set; }
    public string? Comment { get; set; }

    // The business owner's single public response. Null until they reply.
    public string? OwnerReply { get; set; }
    public DateTime? OwnerRepliedAt { get; set; }

    // Platform-admin moderation. Hidden reviews are excluded from the public list and from the
    // business's RatingCount/RatingAverage, but stay visible to the owner and platform admin.
    public bool IsHidden { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Business Business { get; set; } = null!;
    public CustomerAccount CustomerAccount { get; set; } = null!;
    public Appointment Appointment { get; set; } = null!;
}
