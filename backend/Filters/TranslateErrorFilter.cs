using System.Text.Json;
using BarberSaas.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BarberSaas.Api.Filters;

// Global. When the request has X-Lang: AR or HE, rewrites the `error` text of an error response
// ({ error = "...", ... }) through ApiErrorTranslations; the other fields are copied unchanged.
// "Always run" so it also covers results that other filters short-circuit with (input validation,
// disabled account, pending password change).
public class TranslateErrorFilter : IAsyncAlwaysRunResultFilter
{
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var lang = context.HttpContext.Request.Headers["X-Lang"].ToString().ToUpperInvariant();
        if (lang is "AR" or "HE"
            && context.Result is ObjectResult { Value: { } value, StatusCode: >= 400 } result
            && value.GetType().GetProperty("error") is { } errorProp
            && errorProp.PropertyType == typeof(string)
            && errorProp.GetValue(value) is string message)
        {
            var translated = ApiErrorTranslations.Translate(lang, message);
            if (translated != message)
            {
                var body = new Dictionary<string, object?>();
                foreach (var p in value.GetType().GetProperties())
                    body[JsonNamingPolicy.CamelCase.ConvertName(p.Name)] = p == errorProp ? translated : p.GetValue(value);
                result.Value = body;
            }
        }
        return next();
    }
}
