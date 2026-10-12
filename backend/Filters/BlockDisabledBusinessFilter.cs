using BarberSaas.Api.Data;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace BarberSaas.Api.Filters;

// A business JWT lives 30 days, so checking the account only at login left two holes. This
// re-checks on every BusinessOnly action (one indexed lookup):
//   - Business.IsDisabled -> 403 with accountDisabled=true, which the frontend turns into a sign-out;
//   - the token's password stamp (JwtService.PasswordStamp) no longer matches -> 401, so a password
//     reset or change ends every other login. A token without the stamp (issued before it existed)
//     is treated the same: those owners just sign in once more.
// A platform admin's impersonation token (impersonatedBy claim) skips both on purpose, so support
// can still look inside a disabled account. Runs globally, like RequirePasswordChangeFilter.
public class BlockDisabledBusinessFilter(AppDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var isBusinessOnly = context.ActionDescriptor.EndpointMetadata
            .Any(m => m is AuthorizeAttribute { Policy: "BusinessOnly" });
        var businessId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (isBusinessOnly && businessId is not null && user.FindFirst("impersonatedBy") is null)
        {
            var account = await db.Businesses
                .Where(b => b.Id == businessId)
                .Select(b => new { b.IsDisabled, b.PasswordHash })
                .FirstOrDefaultAsync();

            if (account is { IsDisabled: true })
            {
                context.Result = new ObjectResult(new { error = "This account has been disabled. Contact support.", accountDisabled = true })
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                };
                return;
            }

            if (account is not null &&
                user.FindFirst(JwtService.PasswordStampClaim)?.Value != JwtService.PasswordStamp(account.PasswordHash))
            {
                context.Result = new UnauthorizedObjectResult(new { error = "Your password was changed. Please sign in again." });
                return;
            }
        }

        await next();
    }
}
