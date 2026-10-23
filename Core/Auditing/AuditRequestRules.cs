using Core.Entities.Logging;

namespace Core.Auditing;

/// <summary>
/// The decisions the request-audit middleware makes, as pure functions (no HttpContext) so they can
/// be tested directly.
/// </summary>
public static class AuditRequestRules
{
    private static readonly string[] StaticExtensions =
    [
        ".js", ".css", ".map", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".webp",
        ".woff", ".woff2", ".ttf", ".eot", ".otf"
    ];

    /// <summary>
    /// Should this request be left out of the trail? True for static assets and for the configured
    /// noisy prefixes. The language segment ("/en/...", "/ar/...") is ignored, because the same
    /// endpoint is reached both with and without it.
    /// </summary>
    public static bool IsExcluded(string? path, IEnumerable<string> excludedPrefixes)
    {
        if (string.IsNullOrEmpty(path)) return false;

        var normalized = StripLanguage(path);

        foreach (var extension in StaticExtensions)
        {
            if (normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return true;
        }

        foreach (var prefix in excludedPrefixes)
        {
            if (!string.IsNullOrWhiteSpace(prefix) &&
                normalized.StartsWith(StripLanguage(prefix), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>"/en/POS/Index" -> "/POS/Index"; "/ar" -> "/"; paths without a language segment are unchanged.</summary>
    public static string StripLanguage(string path)
    {
        foreach (var language in new[] { "/en", "/ar" })
        {
            if (path.Equals(language, StringComparison.OrdinalIgnoreCase)) return "/";
            if (path.StartsWith(language + "/", StringComparison.OrdinalIgnoreCase)) return path[language.Length..];
        }

        return path;
    }

    public static AuditOutcome OutcomeFor(int statusCode, bool threw)
    {
        if (threw) return AuditOutcome.Failure;
        return statusCode switch
        {
            401 or 403 => AuditOutcome.Denied,
            >= 400 => AuditOutcome.Failure,
            _ => AuditOutcome.Success
        };
    }

    public static AuditLogSeverity SeverityFor(int statusCode, bool threw)
    {
        if (threw || statusCode >= 500) return AuditLogSeverity.Error;
        return statusCode >= 400 ? AuditLogSeverity.Warning : AuditLogSeverity.Info;
    }

    /// <summary>"POST POS/Checkout" for MVC endpoints, "GET /some/path" for anything else.</summary>
    public static string ActionName(string method, string? controller, string? action, string? path)
        => !string.IsNullOrEmpty(controller) && !string.IsNullOrEmpty(action)
            ? $"{method} {controller}/{action}"
            : $"{method} {path}";
}
