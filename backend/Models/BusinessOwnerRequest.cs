using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

public enum BusinessOwnerRequestStatus { Pending, Approved, Rejected }

// A prospective business owner's request for an account, submitted publicly and reviewed by a
// platform admin -- see BusinessOwnerRequestsController (submission) and
// PlatformAdminController's approve/reject actions. Approving creates a real Business with a
// system-generated temp password; rejecting just records the decision and leaves the requester
// free to submit a fresh request later (only a second *concurrent* Pending request is blocked).
public class BusinessOwnerRequest
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BusinessName { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? BusinessTypeId { get; set; }
    public BusinessOwnerRequestStatus Status { get; set; } = BusinessOwnerRequestStatus.Pending;
    public string? RejectionNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedByPlatformAdminId { get; set; }
    // Set on approval -- links back to the Business this request produced.
    public string? CreatedBusinessId { get; set; }

    public BusinessTypeDefinition? BusinessType { get; set; }
    public PlatformAdmin? ReviewedByPlatformAdmin { get; set; }
    public Business? CreatedBusiness { get; set; }
}
