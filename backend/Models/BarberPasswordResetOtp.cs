using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

public class BarberPasswordResetOtp
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Email { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; } = 0;
    public bool Consumed { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
