namespace BarberSaas.Api.Services;

public class DevOtpSender : IOtpSender
{
    public Task SendAsync(string phone, string code) => Task.CompletedTask;
}
