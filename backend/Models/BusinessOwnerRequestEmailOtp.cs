using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

// Proves the prospective owner actually controls the email address they typed into the public
// "request a business account" form, before that request ever reaches the platform admin --
// mirrors BusinessEmailOtp's shape/lifecycle exactly (6-digit code, bcrypt hash, single business
// rule enforced at verification time: unconsumed, unexpired, under the attempt cap).
public class BusinessOwnerRequestEmailOtp
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Email { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; } = 0;
    public bool Consumed { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
