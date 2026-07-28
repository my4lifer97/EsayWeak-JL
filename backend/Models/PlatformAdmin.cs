using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

public class PlatformAdmin
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
