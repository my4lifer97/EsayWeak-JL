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

        httpContext.Response.ContentType = "application/json";
        // Written here directly, so TranslateErrorFilter (an MVC filter) never sees these.
        var lang = httpContext.Request.Headers["X-Lang"].ToString().ToUpperInvariant();

        // A known outage gets its own status and a message written for the user, in every env.
        if (exception is Services.WhatsAppBridgeUnavailableException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await httpContext.Response.WriteAsJsonAsync(new { error = exception.Message }, cancellationToken);
            return true;
        }

        // A double-submitted delete or apply: the row the request loaded was changed or removed by
        // the other one before this one saved. It was a 500 with EF's message (QA run 8).
        // The same race seen as a unique (23505) or foreign-key (23503) violation: two first-time
        // preset applies both inserting a day's hours, a preset edit racing its delete (QA run 9).
        if (exception is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException
            || exception is Microsoft.EntityFrameworkCore.DbUpdateException && (FindPostgresError(exception, "23505") || FindPostgresError(exception, "23503")))
        {
            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            await httpContext.Response.WriteAsJsonAsync(new { error = Services.ApiErrorTranslations.Translate(lang, "This was just changed. Please refresh and try again.") }, cancellationToken);
            return true;
        }

        // Postgres refuses a NUL character in text (SQLSTATE 22021); any endpoint that gets one
        // in user input answers with a 400 instead of a 500.
        if (FindPostgresError(exception, "22021"))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(new { error = Services.ApiErrorTranslations.Translate(lang, "The text contains characters that aren't allowed.") }, cancellationToken);
            return true;
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var message = env.IsDevelopment() ? exception.Message : Services.ApiErrorTranslations.Translate(lang, "An unexpected error occurred.");
        await httpContext.Response.WriteAsJsonAsync(new { error = message }, cancellationToken);

        return true;
    }

    private static bool FindPostgresError(Exception? exception, string sqlState)
    {
        for (var e = exception; e is not null; e = e.InnerException)
            if (e is Npgsql.PostgresException pg && pg.SqlState == sqlState) return true;
        return false;
    }
}
