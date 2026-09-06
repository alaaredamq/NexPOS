using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Threading.Tasks;
using System;

namespace Web.Handlers;

public class LanguageRouteHandler : IRouteHandler
{
    private readonly RequestDelegate _next;

    public LanguageRouteHandler(RequestDelegate next)
    {
        _next = next;
    }

    public RequestDelegate GetRequestHandler(HttpContext httpContext, RouteData routeData)
    {
        // Extract language from route data
        if (routeData.Values.TryGetValue("lang", out var langValue) && langValue is string language)
        {
            // Set language in context for middleware to use
            httpContext.Items["CurrentLanguage"] = language;
            httpContext.Items["LanguageFromRoute"] = true;
        }

        // Return the next handler
        return _next;
    }
}
