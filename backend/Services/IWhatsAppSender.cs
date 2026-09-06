using BarberSaas.Api.Models;

namespace BarberSaas.Api.Services;

public interface IWhatsAppSender
{
    Task SendAsync(Business business, string toPhone, string message);
}
