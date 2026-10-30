using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BarberSaas.Api.Filters;

// A bare NotFound() went out as an empty ProblemDetails with no `error`, which the frontend and
// TranslateErrorFilter read (QA run 6). Gives it the same body every other 404 here has.
public class NotFoundBodyFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is NotFoundResult)
            context.Result = new NotFoundObjectResult(new { error = "Not found" });
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
