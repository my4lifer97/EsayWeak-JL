using System.Security.Claims;
using System.Text.RegularExpressions;
using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BarberSaas.Api.Filters;

// Logs every authenticated write request (POST/PUT/PATCH/DELETE) against the acting barber or
// customer account, purely from claims every request already carries -- no existing controller
// needs to opt in or change. Platform-admin requests are skipped here; PlatformAdminController
// logs its own actions (e.g. impersonation) explicitly instead of being swept up generically.
// Deliberately records metadata only (no request/response bodies), so nothing sensitive ever
// lands in a log row.
public class ActivityLogFilter(AppDbContext db) : IAsyncResultFilter
{
    private static readonly HashSet<string> WriteMethods = ["POST", "PUT", "PATCH", "DELETE"];

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        await next();

        var request = context.HttpContext.Request;
        if (!WriteMethods.Contains(request.Method)) return;

        var user = context.HttpContext.User;
        var subject = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (subject is null) return;

        var type = user.FindFirst("type")?.Value;
        if (type == "platform_admin") return;

        var descriptor = context.ActionDescriptor as ControllerActionDescriptor;
        var action = descriptor is null ? request.Path.ToString() : $"{descriptor.ControllerName}.{descriptor.ActionName}";

        var log = new ActivityLog
        {
            Action = action,
            Description = Prettify(descriptor?.ActionName ?? request.Path.ToString()),
            Method = request.Method,
            Path = request.Path.ToString(),
            StatusCode = context.HttpContext.Response.StatusCode,
            IpAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString(),
        };

        if (type == "customer")
            log.CustomerAccountId = subject;
        else
            log.BarberId = subject;

        log.ImpersonatedByPlatformAdminId = user.FindFirst("impersonatedBy")?.Value;

        db.ActivityLogs.Add(log);
        await db.SaveChangesAsync();
    }

    private static string Prettify(string actionName) => Regex.Replace(actionName, "(?<!^)([A-Z])", " $1");
}
