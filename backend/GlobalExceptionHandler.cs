using Microsoft.AspNetCore.Diagnostics;

namespace BarberSaas.Api;

// Catches anything a controller doesn't handle itself so a bug never leaks a raw stack
// trace / bare 500 to the client — every response keeps the same { error: "..." } shape
// the rest of the API already uses, and the real exception always gets logged.
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception processing {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/json";

        var message = env.IsDevelopment() ? exception.Message : "An unexpected error occurred.";
        await httpContext.Response.WriteAsJsonAsync(new { error = message }, cancellationToken);

        return true;
    }
}
