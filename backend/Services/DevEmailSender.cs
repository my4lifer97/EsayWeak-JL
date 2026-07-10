namespace BarberSaas.Api.Services;

public class DevEmailSender : IEmailSender
{
    public Task SendAsync(string email, string subject, string body) => Task.CompletedTask;
}
