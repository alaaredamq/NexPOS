using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Localization;
using System.Globalization;
using System.Threading;

namespace Web.Controllers;

public abstract class BaseController : Controller
{
    protected string CurrentLanguage => HttpContext.Items["CurrentLanguage"]?.ToString() ?? "en";
    
    protected bool IsRtl => CurrentLanguage == "ar";

    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        // Culture is already applied by LanguageMiddleware + UseRequestLocalization before
        // the action runs; this method only needs the view-facing values below.
        var language = CurrentLanguage;
        var culture = new CultureInfo(language);

        // Set ViewBag properties for views
        ViewBag.Language = language;
        ViewBag.CurrentLanguage = language; // Used in views for URL generation
        ViewBag.IsRtl = IsRtl;
        ViewBag.CurrentCulture = culture.Name;
        
        // Set language-switched URLs for views
        ViewBag.EnglishUrl = GetSwitchedLanguageUrl("en");
        ViewBag.ArabicUrl = GetSwitchedLanguageUrl("ar");
        
        base.OnActionExecuting(context);
    }

    protected IActionResult RedirectToLocal(string? returnUrl)
    {
        if (Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        else
        {
            return RedirectToAction("Index", "Home");
        }
    }

    /// <summary>
    /// Generates a URL with the specified language in the route pattern /{lang}/{controller}/{action}
    /// </summary>
    protected string GetLanguageUrl(string language, string? controller = null, string? action = null, object? routeValues = null)
    {
        // Get current controller and action if not specified
        controller ??= ControllerContext.RouteData.Values["controller"]?.ToString() ?? "Home";
        action ??= ControllerContext.RouteData.Values["action"]?.ToString() ?? "Index";

        // Ensure language is valid
        if (language != "en" && language != "ar")
        {
            language = "en";
        }

        // Build route values with language
        var values = new Microsoft.AspNetCore.Routing.RouteValueDictionary(routeValues)
        {
            ["lang"] = language,
            ["controller"] = controller,
            ["action"] = action
        };

        // Generate URL using the localized route
        return Url.RouteUrl("localized", values) ?? $"/{language}/{controller}/{action}";
    }

    /// <summary>
    /// Generates a URL for the same page but with a different language
    /// </summary>
    protected string GetSwitchedLanguageUrl(string targetLanguage)
    {
        var controller = ControllerContext.RouteData.Values["controller"]?.ToString();
        var action = ControllerContext.RouteData.Values["action"]?.ToString();
        var routeValues = new Microsoft.AspNetCore.Routing.RouteValueDictionary(ControllerContext.RouteData.Values);
        
        // Remove language from route values if present
        routeValues.Remove("lang");
        
        // Copy query string parameters
        foreach (var query in Request.Query)
        {
            if (!routeValues.ContainsKey(query.Key))
            {
                routeValues[query.Key] = query.Value.ToString();
            }
        }

        return GetLanguageUrl(targetLanguage, controller, action, routeValues);
    }
}
