using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BarberSaas.Api.Filters;

// Checks the whatsapp-bridge's shared X-Bridge-Secret header as an authorization filter, so it runs
// before [ApiController]'s body validation: a caller without the secret used to get the request
// schema back in a 400 (QA run 2) instead of a plain 401.
[AttributeUsage(AttributeTargets.Method)]
public class RequireBridgeSecretAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var secret = config["WhatsAppBridge:Secret"];
        var header = context.HttpContext.Request.Headers["X-Bridge-Secret"].FirstOrDefault() ?? "";
        if (string.IsNullOrEmpty(secret)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header), Encoding.UTF8.GetBytes(secret)))
            context.Result = new UnauthorizedResult();
    }
}
