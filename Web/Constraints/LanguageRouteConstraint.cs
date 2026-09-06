using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using System.Text.RegularExpressions;
using System;
using System.Linq;

namespace Web.Constraints;

public class LanguageRouteConstraint : IRouteConstraint
{
    private static readonly string[] SupportedLanguages = { "en", "ar" };

    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
    {
        if (values.TryGetValue(routeKey, out var value) && value is string language)
        {
            return SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase);
        }

        return false;
    }
}
