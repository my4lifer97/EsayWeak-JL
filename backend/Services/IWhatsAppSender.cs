using BarberSaas.Api.Models;

namespace BarberSaas.Api.Services;

public interface IWhatsAppSender
{
    Task SendAsync(Barber barber, string toPhone, string message);
}
