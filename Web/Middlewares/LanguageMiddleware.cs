using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Web.Api.Infrastructure;

namespace Web.Middlewares;

public class LanguageMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptions<RequestLocalizationOptions> _localizationOptions;

    public LanguageMiddleware(RequestDelegate next, IOptions<RequestLocalizationOptions> localizationOptions)
    {
        _next = next;
        _localizationOptions = localizationOptions;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // CRITICAL: Set culture BEFORE any view rendering happens
        // Extract language from URL path FIRST (e.g., /ar/Account/Login or /en/POS)
        var path = context.Request.Path.Value?.TrimStart('/');
        var lang = string.Empty;
        var languageFromRoute = false;

        // Priority 1: Check URL path first (most reliable)
        if (!string.IsNullOrEmpty(path))
        {
            var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            // First segment should be the language code
            if (pathSegments.Length > 0 && (pathSegments[0] == "ar" || pathSegments[0] == "en"))
            {
                lang = pathSegments[0];
                languageFromRoute = true;
            }
        }

        // Priority 2: Check route values (from route constraint) - this happens during routing
        if (string.IsNullOrEmpty(lang) && context.Request.RouteValues.TryGetValue("lang", out var routeLang))
        {
            var routeLangStr = routeLang?.ToString();
            if (routeLangStr == "ar" || routeLangStr == "en")
            {
                lang = routeLangStr;
                languageFromRoute = true;
            }
        }

        // The SPA keeps its language in the browser and sends it as Accept-Language (REACT_MIGRATION.md D8), so for
        // /api/v1 that header outranks the cookie the Razor pages left behind. The legacy /api/* controllers are called by the
        // Razor pages, whose Chromium always sends an Accept-Language of its own, so they keep the old order below. Without the header (curl) the old
        // order below applies too.
        var isApi = ApiPaths.IsV1(context.Request);
        if (string.IsNullOrEmpty(lang) && isApi)
        {
            lang = ApiLanguage.FromAcceptLanguage(context.Request);
        }

        // Priority 3: Check query parameter as fallback
        if (string.IsNullOrEmpty(lang))
        {
            var queryString = context.Request.Query;
            lang = queryString["lang"].FirstOrDefault();
        }

        // Priority 4: Check cookie (set by previous requests)
        if (string.IsNullOrEmpty(lang))
        {
            var cookieValue = context.Request.Cookies[CookieRequestCultureProvider.DefaultCookieName];
            if (!string.IsNullOrEmpty(cookieValue))
            {
                var requestCulture = CookieRequestCultureProvider.ParseCookieValue(cookieValue);
                if (requestCulture != null)
                {
                    var cultureValue = requestCulture.Cultures.FirstOrDefault();
                    if (cultureValue.HasValue)
                    {
                        var cultureStr = cultureValue.Value.ToString();
                        if (cultureStr == "ar" || cultureStr == "en")
                        {
                            lang = cultureStr;
                        }
                    }
                }
            }
        }

        // Priority 5: Check session for previously selected language
        if (string.IsNullOrEmpty(lang))
        {
            var sessionLang = context.Session.GetString("SelectedLanguage");
            if (!string.IsNullOrEmpty(sessionLang) && (sessionLang == "ar" || sessionLang == "en"))
            {
                lang = sessionLang;
            }
        }

        // Final fallback: Use current culture or default to English
        if (string.IsNullOrEmpty(lang))
        {
            var currentLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            lang = (currentLang == "ar" || currentLang == "en") ? currentLang : "en";
        }

        // Ensure we have a valid language
        if (lang != "ar" && lang != "en")
        {
            lang = "en";
        }

        // SET CULTURE IMMEDIATELY - This must happen before any view rendering
        SetCulture(context, lang, languageFromRoute);

        // Persist in session & cookie for future requests. Not for /api/v1: an API call is not a language choice, and
        // the SPA must not flip the language of the Razor pages (or start a session) by merely loading data.
        if (!isApi)
        {
            context.Session.SetString("SelectedLanguage", lang);
            context.Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(lang)),
                new CookieOptions
                {
                    Expires = DateTimeOffset.UtcNow.AddYears(1),
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = context.Request.IsHttps
                });
        }

        // Prevent caching for language-specific pages to ensure fresh content
        if (context.Request.Path.HasValue && !context.Request.Path.Value.StartsWith("/lib") && 
            !context.Request.Path.Value.StartsWith("/css") && !context.Request.Path.Value.StartsWith("/js") &&
            !context.Request.Path.Value.StartsWith("/Metronics") && !context.Request.Path.Value.StartsWith("/images") &&
            !context.Request.Path.Value.StartsWith("/_"))
        {
            // no-cache (not no-store): the renderer may keep the page for instant
            // back/forward, but must revalidate with the server before reusing it.
            context.Response.Headers["Cache-Control"] = "no-cache";
        }

        // Continue to next middleware
        await _next(context);
    }

    private void SetCulture(HttpContext context, string language, bool languageFromRoute)
    {
        var culture = new CultureInfo(language);
        var requestCulture = new RequestCulture(culture, culture);

        // Set the request culture feature - this is what IViewLocalizer and IStringLocalizer use
        context.Features.Set<IRequestCultureFeature>(new RequestCultureFeature(requestCulture, null));
        
        // Store in HttpContext.Items for easy access in controllers/views
        context.Items["CurrentLanguage"] = language;
        context.Items["LanguageFromRoute"] = languageFromRoute;

        // Set culture on the current async flow before view rendering. UseRequestLocalization
        // (which runs right after this middleware and reads the IRequestCultureFeature set
        // above) is what actually applies it for the rest of the pipeline; we set the thread
        // culture here only so code between the two middlewares sees the right value.
        // NOTE: never touch CultureInfo.DefaultThreadCurrentCulture / static CurrentCulture —
        // those are process-wide and would leak one request's language into others.
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}

public static class ApiLanguage
{
    /// <summary>
    /// The supported language ("en" / "ar") the request prefers, by <c>Accept-Language</c> quality, or "" when it names
    /// none of them. "ar-LB" counts as "ar".
    /// </summary>
    public static string FromAcceptLanguage(HttpRequest request)
    {
        var preferred = request.GetTypedHeaders().AcceptLanguage
            .Where(l => l.Quality is null or > 0)
            .OrderByDescending(l => l.Quality ?? 1.0)
            .Select(l => l.Value.Value?.Split('-')[0].ToLowerInvariant());

        return preferred.FirstOrDefault(l => l is "ar" or "en") ?? string.Empty;
    }
}

public static class LanguageMiddlewareExtensions
{
    public static IApplicationBuilder UseLanguageMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<LanguageMiddleware>();
    }
}

/// <summary>
/// Bridges <see cref="LanguageMiddleware"/> (which resolves the language from the URL path,
/// query, cookie, session, …) to <c>UseRequestLocalization</c>.
///
/// Without this, <c>RequestLocalizationMiddleware</c> runs its default providers
/// (QueryString → Cookie → Accept-Language) and overwrites the culture our middleware just
/// set: on the FIRST request after a language switch the <c>.AspNetCore.Culture</c> cookie
/// still holds the previous language, so <c>/ar/…</c> would render in English and only pick
/// up Arabic on the next request. Reading <c>HttpContext.Items["CurrentLanguage"]</c> — set
/// by <see cref="LanguageMiddleware"/> earlier in the pipeline — makes the switch take effect
/// immediately. Registered at index 0 so it wins over the default providers.
/// </summary>
public sealed class CurrentLanguageItemRequestCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var lang = httpContext.Items["CurrentLanguage"] as string;

        if (lang != "ar" && lang != "en")
        {
            return NullProviderCultureResult;
        }

        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(lang, lang));
    }
}
