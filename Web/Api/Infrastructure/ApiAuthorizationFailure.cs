using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Web.Api.Infrastructure;

/// <summary>
/// What <c>[RequirePermission]</c>, <c>[RequireRole]</c> and the cookie handler answer when a request is refused.
/// A page request is sent to the login / access-denied page as it always was; an <c>/api/*</c> request gets
/// 401 / 403 <see cref="ProblemDetails"/> JSON, because a fetch() that follows a redirect to an HTML login page
/// cannot tell what happened.
/// </summary>
public static class ApiAuthorizationFailure
{
    public static IActionResult Unauthenticated(AuthorizationFilterContext context)
    {
        if (ApiPaths.IsApi(context.HttpContext.Request))
            return Problem(context.HttpContext, StatusCodes.Status401Unauthorized, ApiCodes.Unauthenticated);

        return new RedirectToActionResult("Login", "Account", new
        {
            returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString
        });
    }

    public static IActionResult Denied(AuthorizationFilterContext context)
        => ApiPaths.IsApi(context.HttpContext.Request)
            ? Problem(context.HttpContext, StatusCodes.Status403Forbidden, ApiCodes.Forbidden)
            : new RedirectToActionResult("AccessDenied", "Account", null);

    private static ObjectResult Problem(HttpContext http, int status, string code)
        => new(ApiProblem.Create(http, status, code))
        {
            StatusCode = status,
            ContentTypes = { ApiProblem.ContentType },
        };

    /// <summary>
    /// Cookie-handler challenge for <c>[Authorize]</c>: <c>/api/*</c> gets JSON, anything else keeps whatever
    /// the handler would have done (redirect to the login page, the 401 an XHR gets, ...).
    /// </summary>
    public static Func<RedirectContext<CookieAuthenticationOptions>, Task> CookieChallenge(
        Func<RedirectContext<CookieAuthenticationOptions>, Task> original, int status, string code)
        => context => ApiPaths.IsApi(context.Request)
            ? ApiProblem.WriteAsync(context.HttpContext, status, code)
            : original(context);
}
