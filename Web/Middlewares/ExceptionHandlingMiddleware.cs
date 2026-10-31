using System.Net;
using Web.Api.Infrastructure;

namespace Web.Middlewares;

/// <summary>
/// Global fallback for exceptions that escape controller actions.
/// AJAX/JSON requests get a clean {success:false, message} response (matching the shape
/// controllers already return from their own try/catch blocks); the exception detail is only
/// included in Development so production responses never leak stack traces or internals.
/// Non-AJAX requests are rethrown so the existing UseDeveloperExceptionPage / UseExceptionHandler
/// pipeline (configured in Program.cs) still renders the normal error page.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (ApiPaths.IsV1(context.Request))
        {
            await WriteApiProblemAsync(context, ex);
        }
        catch (Exception ex)
        {
            var isAjaxRequest = context.Request.Headers["X-Requested-With"] == "XMLHttpRequest"
                || context.Request.Headers.Accept.Any(a => a?.Contains("application/json") == true)
                || context.Request.Path.StartsWithSegments("/api");

            if (!isAjaxRequest)
            {
                // Let the standard ASP.NET Core exception handling (dev exception page /
                // UseExceptionHandler) deal with normal page requests.
                throw;
            }

            var controllerName = context.GetRouteValue("controller")?.ToString() ?? "Unknown";
            var actionName = context.GetRouteValue("action")?.ToString() ?? "Unknown";

            _logger.LogError(ex, "Unhandled exception in {Controller}/{Action}", controllerName, actionName);

            if (context.Response.HasStarted)
            {
                // Response body already partially written; nothing more we can safely do.
                return;
            }

            context.Response.Clear();
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var message = _environment.IsDevelopment()
                ? ex.Message
                : "An unexpected error occurred. Please try again or contact support.";

            await context.Response.WriteAsJsonAsync(new { success = false, message });
        }
    }

    /// <summary>The versioned API answers RFC 7807 problems with a stable <c>code</c> - never the legacy <c>{success,message}</c>.</summary>
    private async Task WriteApiProblemAsync(HttpContext context, Exception ex)
    {
        // The browser gave up (navigated away, aborted the fetch). Nothing is listening, and it is not an error.
        if (ex is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = 499;
            return;
        }

        var mapped = ApiExceptionMapper.Map(ex, _environment.IsDevelopment());
        if (mapped.Unexpected)
        {
            _logger.LogError(ex, "Unhandled exception in {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else
        {
            _logger.LogWarning("{Method} {Path} refused: {Code} ({Reason})", context.Request.Method, context.Request.Path, mapped.Code, ex.GetType().Name);
        }

        if (context.Response.HasStarted) return; // body partly written; nothing safe left to do

        context.Response.Clear();
        await ApiProblem.WriteAsync(context, mapped.Status, mapped.Code, mapped.Detail);
    }
}
