using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BarberSaas.Api.Filters;

// Shared input checks for every action, so a bad value gets a clear 400 instead of an exception:
//   - dates: a `date`/`startDate`/`endDate` query argument, or a Date/StartDate/EndDate property on
//     a request body, must be an exact yyyy-MM-dd. Endpoints used to call DateTime.Parse on them
//     and answer "2026-02-30" or "tomorrow" with a 500. Empty is left to the endpoint (some are
//     optional);
//   - length caps: Notes up to 2000 characters and any *Name up to 200, so nobody can store a
//     100,000-character booking note or customer name.
// Runs globally, like RequirePasswordChangeFilter.
public class ValidateInputFilter : IActionFilter
{
    public const int MaxNotesLength = 2000;
    public const int MaxNameLength = 200;

    private static readonly HashSet<string> DateNames = new(StringComparer.OrdinalIgnoreCase) { "date", "startDate", "endDate" };

    public void OnActionExecuting(ActionExecutingContext context)
    {
        foreach (var (name, value) in context.ActionArguments)
        {
            var error = value switch
            {
                string s => Check(name, s),
                // Only our own request DTOs -- not IFormFile, CancellationToken and the like.
                not null when value.GetType().Assembly == typeof(ValidateInputFilter).Assembly => CheckBody(value),
                _ => null,
            };
            if (error is not null)
            {
                context.Result = new BadRequestObjectResult(new { error });
                return;
            }
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    private static string? CheckBody(object body)
    {
        foreach (var prop in body.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(string) || prop.GetIndexParameters().Length > 0) continue;
            if (prop.GetCustomAttribute<SkipLengthCheckAttribute>() is not null) continue;
            if (Check(prop.Name, (string?)prop.GetValue(body)) is { } error) return error;
        }
        return null;
    }

    // Longer free text an owner or a platform admin types (QA run 6: no limit at all).
    private static readonly HashSet<string> LongTextNames = new(StringComparer.OrdinalIgnoreCase) { "note", "businessDescription", "systemNeeds" };
    public const int MaxLongTextLength = 2000;
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 20000;
    public const int MaxPhoneLength = 50;
    public const int MaxEmailLength = 254;

    private static string? Check(string name, string? value)
    {
        if (value is null) return null;
        // An empty "date" went on to DateTime.Parse and a 500 (QA run 6). StartDate/EndDate may be
        // sent empty to mean "none".
        if (value.Length == 0)
            return name.Equals("date", StringComparison.OrdinalIgnoreCase) ? "Invalid date. Use the format YYYY-MM-DD." : null;
        if (DateNames.Contains(name) && !IsDate(value))
            return "Invalid date. Use the format YYYY-MM-DD.";
        // bcrypt only uses the first 72 bytes, so a longer password was cut without a word (QA run 6).
        if (name.Contains("password", StringComparison.OrdinalIgnoreCase) && System.Text.Encoding.UTF8.GetByteCount(value) > 72)
            return "Passwords can be at most 72 characters.";
        if (LongTextNames.Contains(name) && value.Length > MaxLongTextLength)
            return $"This text can be at most {MaxLongTextLength} characters.";
        if (name.Equals("subject", StringComparison.OrdinalIgnoreCase) && value.Length > MaxSubjectLength)
            return $"The subject can be at most {MaxSubjectLength} characters.";
        if (name.Equals("body", StringComparison.OrdinalIgnoreCase) && value.Length > MaxBodyLength)
            return $"The message can be at most {MaxBodyLength} characters.";
        if (name.Equals("notes", StringComparison.OrdinalIgnoreCase) && value.Length > MaxNotesLength)
            return $"Notes can be at most {MaxNotesLength} characters.";
        if (name.EndsWith("name", StringComparison.OrdinalIgnoreCase) && value.Length > MaxNameLength)
            return $"Names can be at most {MaxNameLength} characters.";
        // The owner-request form's email and phone had no cap at all (QA run 7).
        if (name.EndsWith("phone", StringComparison.OrdinalIgnoreCase) && value.Length > MaxPhoneLength)
            return "Please enter a valid phone number";
        if (name.EndsWith("email", StringComparison.OrdinalIgnoreCase) && value.Length > MaxEmailLength)
            return "Invalid email";
        return null;
    }

    private static bool IsDate(string value) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
        && d.Year is >= 2000 and <= 2100;
}

// Opts a DTO property out of ValidateInputFilter's checks, for a value the action shortens itself
// instead (WhatsApp profile names: rejecting one meant the customer got no reply).
[AttributeUsage(AttributeTargets.Property)]
public sealed class SkipLengthCheckAttribute : Attribute;
