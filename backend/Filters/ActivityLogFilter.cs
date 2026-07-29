using System.Security.Claims;
using System.Text.RegularExpressions;
using BarberSaas.Api.Data;
using BarberSaas.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BarberSaas.Api.Filters;

// Lets an action attach a ready-made, human-readable sentence (naming the customer/service/
// appointment it actually acted on) for ActivityLogFilter to use instead of the generic
// Prettify(actionName) fallback. Opt-in per action -- call right before returning.
public static class ActivityDetailExtensions
{
    public static void SetActivityDetail(this ControllerBase controller, string detail) =>
        controller.HttpContext.Items["ActivityDetail"] = detail;

    // Avoids a dangling double space in a detail sentence when FamilyName is blank (common for
    // owner-created walk-in customers who only enter a first name).
    public static string FullName(string name, string familyName) =>
        string.IsNullOrWhiteSpace(familyName) ? name : $"{name} {familyName}";
}

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

        var detail = context.HttpContext.Items["ActivityDetail"] as string;

        var log = new ActivityLog
        {
            Action = action,
            Description = detail ?? Prettify(descriptor?.ActionName ?? request.Path.ToString()),
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
