using BarberSaas.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Filters;

// A business JWT lives 30 days, so checking Business.IsDisabled only at login left a disabled
// owner with full access until it expired. This re-checks on every BusinessOnly action (one
// indexed lookup) and 403s with accountDisabled=true, which the frontend turns into a sign-out.
// A platform admin's impersonation token (impersonatedBy claim) is let through on purpose, so
// support can still look inside a disabled account. Runs globally, like RequirePasswordChangeFilter.
public class BlockDisabledBusinessFilter(AppDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var isBusinessOnly = context.ActionDescriptor.EndpointMetadata
            .Any(m => m is AuthorizeAttribute { Policy: "BusinessOnly" });
        var businessId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (isBusinessOnly && businessId is not null && user.FindFirst("impersonatedBy") is null &&
            await db.Businesses.AnyAsync(b => b.Id == businessId && b.IsDisabled))
        {
            context.Result = new ObjectResult(new { error = "This account has been disabled. Contact support.", accountDisabled = true })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
            return;
        }

        await next();
    }
}
