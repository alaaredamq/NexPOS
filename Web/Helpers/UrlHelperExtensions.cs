using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Web.Helpers;

public static class UrlHelperExtensions
{
    /// <summary>
    /// Generates a URL with language in the route pattern /{lang}/{controller}/{action}
    /// </summary>
    public static string ActionWithLanguage(this IUrlHelper urlHelper, string action, string controller, string language, object? routeValues = null)
    {
        // Ensure language is valid
        if (language != "en" && language != "ar")
        {
            language = "en";
        }

        // Build route values with language - ensure lang, controller, and action override any values from routeValues
        var values = new Microsoft.AspNetCore.Routing.RouteValueDictionary(routeValues);
        values["lang"] = language;
        values["controller"] = controller;
        values["action"] = action;

        // Generate URL using the localized route
        return urlHelper.RouteUrl("localized", values) ?? $"/{language}/{controller}/{action}";
    }

    /// <summary>
    /// Generates a URL for the same page but with a different language
    /// </summary>
    public static string ActionWithCurrentLanguage(this IUrlHelper urlHelper, string action, string controller, object? routeValues = null)
    {
        // Get current language from route or default to "en"
        var currentLang = urlHelper.ActionContext.RouteData.Values["lang"]?.ToString() 
                          ?? urlHelper.ActionContext.HttpContext.Items["CurrentLanguage"]?.ToString()
                          ?? "en";

        return urlHelper.ActionWithLanguage(action, controller, currentLang, routeValues);
    }
}

