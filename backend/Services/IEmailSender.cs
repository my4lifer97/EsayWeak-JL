namespace BarberSaas.Api.Services;

public interface IEmailSender
{
    Task SendAsync(string email, string subject, string body);
}
