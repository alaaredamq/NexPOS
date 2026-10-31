namespace Web.Api.Infrastructure;

/// <summary>
/// CSRF defence for the versioned API. The sign-in cookie is <c>SameSite=Lax</c>, which already keeps it off cross-site
/// POSTs; this adds a second, independent gate: every non-GET <c>/api/v1/*</c> request must carry
/// <c>X-NexPOS-Client: web</c>. A custom header cannot be set by a cross-site HTML form, and a cross-site <c>fetch</c>
/// that sets it needs a CORS pre-flight this server does not approve for foreign origins. No antiforgery tokens are used.
/// <para>
/// Scoped to <c>api/v1</c> on purpose: the legacy <c>/api/*</c> controllers are called by the Razor pages' jQuery without
/// the header and keep working until cutover, when this check becomes the only gate (REACT_MIGRATION.md, Z.4).
/// </para>
/// </summary>
public sealed class ApiClientHeaderMiddleware
{
    private readonly RequestDelegate _next;

    public ApiClientHeaderMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        if (!ApiPaths.IsV1(context.Request) || !RequiresHeader(context.Request.Method) || HasHeader(context.Request))
            return _next(context);

        return ApiProblem.WriteAsync(context, StatusCodes.Status403Forbidden, ApiCodes.ClientHeaderRequired);
    }

    // OPTIONS is the CORS pre-flight; it carries no state change and must reach the CORS middleware.
    private static bool RequiresHeader(string method)
        => !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));

    private static bool HasHeader(HttpRequest request)
        => string.Equals(request.Headers[ApiPaths.ClientHeader].ToString(), ApiPaths.ClientHeaderValue, StringComparison.OrdinalIgnoreCase);
}
