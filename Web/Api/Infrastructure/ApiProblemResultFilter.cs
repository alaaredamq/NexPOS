using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;

namespace Web.Api.Infrastructure;

/// <summary>
/// Makes every <see cref="ProblemDetails"/> that leaves an <c>api/v1</c> action carry the API's fields - a <c>code</c>,
/// the <c>traceId</c>, a localized title - even when the action just wrote <c>return NotFound()</c>. Registered
/// globally; does nothing for the legacy controllers and the Razor pages.
/// </summary>
public sealed class ApiProblemResultFilter : IResultFilter, IOrderedFilter
{
    // Runs after the controller-level [Produces("application/json")], which would otherwise reset the media type of a problem to plain JSON.
    public int Order => int.MaxValue;

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (!ApiPaths.IsV1(context.HttpContext.Request) || context.Result is not ObjectResult { Value: ProblemDetails problem } result)
            return;

        var status = problem.Status ?? result.StatusCode ?? StatusCodes.Status500InternalServerError;
        var code = ApiProblem.CodeOf(problem);
        if (code.Length == 0) code = ApiCodes.ForStatus(status);

        // MVC fills the title with the English reason phrase ("Not Found"); replace that - and only that, an action's
        // own title stays - with the localized message for the code.
        if (problem.Title is null || problem.Title == ReasonPhrases.GetReasonPhrase(status)
            || problem.Title == "One or more validation errors occurred.")
        {
            problem.Title = ApiProblem.Localize(context.HttpContext, ApiCodes.MessageKey(code, status));
        }

        problem.Instance ??= context.HttpContext.Request.Path.Value;
        ApiProblem.Stamp(context.HttpContext, problem, code);
        result.ContentTypes.Clear();
        result.ContentTypes.Add(ApiProblem.ContentType);
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
