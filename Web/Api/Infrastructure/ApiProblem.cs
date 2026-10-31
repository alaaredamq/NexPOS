using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Web.Api.Infrastructure;

/// <summary>
/// Builds the one error shape of the API: RFC 7807 <see cref="ProblemDetails"/> plus
/// <c>code</c> (stable machine string), <c>traceId</c> (same value as the <c>X-Request-Id</c> header and the audit rows)
/// and, for validation failures, <c>errors{field:[message]}</c>. Used by controllers, filters and middleware alike,
/// so a 401 from the cookie handler looks exactly like a 409 from an action.
/// </summary>
public static class ApiProblem
{
    public const string ContentType = "application/problem+json";

    public static ProblemDetails Create(HttpContext http, int status, string? code = null, string? detail = null)
    {
        code ??= ApiCodes.ForStatus(status);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = Localize(http, ApiCodes.MessageKey(code, status)),
            Detail = detail,
            Instance = http.Request.Path.Value,
        };
        Stamp(http, problem, code);
        return problem;
    }

    public static HttpValidationProblemDetails Validation(HttpContext http, IDictionary<string, string[]> errors, string? detail = null)
    {
        var problem = new HttpValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = Localize(http, ApiCodes.MessageKey(ApiCodes.ValidationFailed, StatusCodes.Status400BadRequest)),
            Detail = detail,
            Instance = http.Request.Path.Value,
        };
        Stamp(http, problem, ApiCodes.ValidationFailed);
        return problem;
    }

    /// <summary>Adds <c>code</c> and <c>traceId</c> (and a localized title) to a problem someone else built, without overwriting a code already set.</summary>
    public static void Stamp(HttpContext http, ProblemDetails problem, string? code = null)
    {
        problem.Extensions["code"] = problem.Extensions.TryGetValue("code", out var existing) && existing is string s && s.Length > 0
            ? s
            : code ?? ApiCodes.ForStatus(problem.Status ?? StatusCodes.Status500InternalServerError);
        problem.Extensions["traceId"] = http.TraceIdentifier;
    }

    public static string CodeOf(ProblemDetails problem)
        => problem.Extensions.TryGetValue("code", out var code) && code is string s ? s : string.Empty;

    /// <summary>Writes the problem as the response. For code that runs outside MVC (middleware, cookie events).</summary>
    public static Task WriteAsync(HttpContext http, ProblemDetails problem)
    {
        http.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return http.Response.WriteAsJsonAsync(problem, problem.GetType(), options: null, contentType: ContentType);
    }

    public static Task WriteAsync(HttpContext http, int status, string? code = null, string? detail = null)
        => WriteAsync(http, Create(http, status, code, detail));

    /// <summary>
    /// Looks a message up in the language of the request. Middleware that sits outside the localization middleware
    /// (exception handling, status pages) has lost the request culture again by the time it answers, so the
    /// culture the request resolved is applied explicitly for the lookup.
    /// </summary>
    public static string Localize(HttpContext http, string key)
    {
        var localizer = http.RequestServices.GetService<IStringLocalizer<SharedResource>>();
        if (localizer is null) return key;

        var culture = http.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture;
        if (culture is null || Equals(culture, CultureInfo.CurrentUICulture)) return localizer[key].Value;

        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture;
        try
        {
            return localizer[key].Value;
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
