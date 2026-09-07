using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BarberSaas.Api.Filters;

// Blocks every BusinessOnly action for an account with MustChangePassword=true (see
// JwtService.Generate's mustChangePassword claim) except whatever's marked
// [AllowWithPendingPasswordChange] -- currently only AuthController.ChangePassword. Customer and
// platform-admin tokens never carry this claim, so this is a no-op for them. Runs globally
// (registered in Program.cs) so no controller has to opt in.
public class RequirePasswordChangeFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var mustChangePassword = context.HttpContext.User.FindFirst("mustChangePassword")?.Value == "true";
        var allowed = context.ActionDescriptor.EndpointMetadata
            .Any(m => m is AllowWithPendingPasswordChangeAttribute);

        if (mustChangePassword && !allowed)
        {
            context.Result = new ObjectResult(new { error = "You must set a new password before continuing.", mustChangePassword = true })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
            return;
        }

        await next();
    }
}
