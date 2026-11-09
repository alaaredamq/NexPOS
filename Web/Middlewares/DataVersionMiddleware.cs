using Web.Api.Live;

namespace Web.Middlewares;

/// <summary>
/// Moves the <see cref="DataVersion"/> when a request that writes succeeded, so the other PCs of the shop notice (see that class).
/// The move happens just before the response starts, i.e. after the action finished and its changes are saved: a PC that sees the new
/// version and reloads can never read the old numbers. Reads (GET/HEAD), refused requests and failures change nothing and bump nothing.
/// Any write counts, not only sales: the screens decide what to reload, and an extra reload is harmless where a missed one is a stale stock number.
/// </summary>
public sealed class DataVersionMiddleware
{
    private readonly RequestDelegate _next;

    public DataVersionMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext http, DataVersion version)
    {
        var method = http.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)) return _next(http);

        http.Response.OnStarting(static state =>
        {
            var (context, data) = ((HttpContext, DataVersion))state;
            if (context.Response.StatusCode < 400) data.Bump();
            return Task.CompletedTask;
        }, (http, version));

        return _next(http);
    }
}
