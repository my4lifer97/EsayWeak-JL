using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BarberSaas.Api.Filters;

// A bare NotFound() went out as an empty ProblemDetails with no `error`, which the frontend and
// TranslateErrorFilter read (QA run 6). Gives it the same body every other 404 here has. Ordered
// before [ApiController]'s ClientErrorResultFilter (-2000), which otherwise turns the NotFound()
// into that ProblemDetails first (QA run 7: the fix didn't take). The order goes on the
// registration in Program.cs: a filter added by type takes its order from there.
public class NotFoundBodyFilter : IAlwaysRunResultFilter
{
    public const int FilterOrder = -3000;

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is NotFoundResult)
            context.Result = new NotFoundObjectResult(new { error = "Not found" });
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
