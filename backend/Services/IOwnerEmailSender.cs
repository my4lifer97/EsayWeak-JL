namespace BarberSaas.Api.Services;

// Separate from IEmailSender (which handles system emails -- verification codes, forgot-password
// -- via whichever provider is configured, see Program.cs's precedence chain). This one is
// dedicated to PlatformAdminController's admin-composed "email the business owner" feature
// (credentials, chatbot links, etc.), sent through the platform admin's own Gmail account via the
// Gmail API rather than a third-party relay -- same reasoning as self-hosting WhatsApp via Baileys
// instead of a paid Business API: the Gmail API is a plain HTTPS call (unlike SMTP, which Railway
// blocks below the Pro plan), authenticated as a real personal account instead of a service the
// recipient has never heard of.
public interface IOwnerEmailSender
{
    Task SendAsync(string toEmail, string subject, string body);
}
