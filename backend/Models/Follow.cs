using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

public class Follow
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CustomerAccountId { get; set; } = "";
    public string BusinessId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public CustomerAccount CustomerAccount { get; set; } = null!;
    public Business Business { get; set; } = null!;
}
