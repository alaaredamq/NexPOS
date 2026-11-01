using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Web.Helpers;

/// <summary>
/// Sends a browser that asks for the old Razor page of a screen the React app already has to the React page
/// (REACT_MIGRATION.md D7). The sidebar and the nav payload already link to the SPA; this covers every other way to land on the old
/// page - a link inside an older view (licence banner, profile buttons), a redirect after saving, a bookmark, the old top-bar menu -
/// so nobody is left on a screen that has been replaced. Only full-page GETs of a page's <c>Index</c> (and the account pages): the Razor
/// actions behind the buttons (details pages, exports, POSTs, AJAX) are untouched. <c>?legacy=1</c> opens the old page anyway.
/// Deleted together with the Razor pages at the cutover.
/// </summary>
public sealed class SpaRedirectFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsGet(request.Method)) return;
        if (request.Headers.XRequestedWith == "XMLHttpRequest" || request.Query.ContainsKey("legacy")) return;

        var controller = context.RouteData.Values["controller"]?.ToString();
        var action = context.RouteData.Values["action"]?.ToString();
        if (controller is null || action is null) return;

        var target = TargetFor(controller, action);
        if (target is null) return;

        context.Result = new RedirectResult(target + request.QueryString.Value, permanent: false);
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    /// <summary>The SPA path that replaces the Razor action, or null while the Razor page is still the real one.</summary>
    public static string? TargetFor(string controller, string action)
    {
        if (string.Equals(controller, "Account", StringComparison.OrdinalIgnoreCase))
        {
            // "ChangePassword" is the old name of the PIN screen (its title and fields are the PIN ones).
            if (string.Equals(action, "Profile", StringComparison.OrdinalIgnoreCase)) return "/app/account/profile";
            if (string.Equals(action, "ChangePin", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "ChangePassword", StringComparison.OrdinalIgnoreCase)) return "/app/account/change-pin";
            return null;
        }

        return string.Equals(action, "Index", StringComparison.OrdinalIgnoreCase) && SpaMigration.TryGetPath(controller, out var path) ? path : null;
    }
}
