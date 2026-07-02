namespace BarberSaas.Api.Services;

public interface IOtpSender
{
    Task SendAsync(string phone, string code);
}
