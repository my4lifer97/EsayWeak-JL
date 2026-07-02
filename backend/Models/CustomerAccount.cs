using System.ComponentModel.DataAnnotations;

namespace BarberSaas.Api.Models;

public class CustomerAccount
{
    [Key] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Phone { get; set; } = "";
    public string Name { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Customer> Profiles { get; set; } = [];
    public ICollection<Follow> Follows { get; set; } = [];
}
