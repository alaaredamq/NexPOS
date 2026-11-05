using System.Diagnostics;
using System.Text;
using Core.Auditing;
using Core.Entities.Logging;
using Microsoft.Extensions.Options;

namespace Web.Middlewares;

/// <summary>
/// Writes one audit row for every request that reaches the application: who, when, from where, which
/// page/endpoint, with which (redacted) parameters, what status came back and how long it took.
/// Sits just after the static files, outside everything that can fail or refuse a request, so
/// denied, blocked, redirected and crashed requests are recorded as faithfully as successful ones.
/// The rows of the data changes the request caused carry the same request id.
/// </summary>
public sealed class AuditRequestMiddleware
{
    private const long MaxBufferedBody = 1_048_576;

    /// <summary>A stuck disk must never hang a request: after this the entry is left to the background writer.</summary>
    private static readonly TimeSpan DurabilityWait = TimeSpan.FromSeconds(2);

    private readonly RequestDelegate _next;
    private readonly IAuditTrail _trail;
    private readonly IOptionsMonitor<AuditOptions> _options;
    private readonly ILogger<AuditRequestMiddleware> _logger;

    public AuditRequestMiddleware(
        RequestDelegate next, IAuditTrail trail, IOptionsMonitor<AuditOptions> options, ILogger<AuditRequestMiddleware> logger)
    {
        _next = next;
        _trail = trail;
        _options = options;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext http)
    {
        var options = _options.CurrentValue;
        var path = http.Request.Path.Value ?? "/";

        if (HttpMethods.IsOptions(http.Request.Method) || AuditRequestRules.IsExcluded(path, options.ExcludedPaths))
        {
            await _next(http);
            return;
        }

        // Lets a user (or support, reading the browser's network tab) quote the id that ties a screen to the audit rows.
        http.Response.OnStarting(static state =>
        {
            var context = (HttpContext)state;
            context.Response.Headers["X-Request-Id"] = context.TraceIdentifier;
            return Task.CompletedTask;
        }, http);

        var startedAt = DateTime.UtcNow;
        var clock = Stopwatch.StartNew();
        var bufferBody = options.CaptureRequestBody && ShouldBufferBody(http.Request);
        if (bufferBody) http.Request.EnableBuffering();

        Exception? failure = null;
        try
        {
            await _next(http);
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            clock.Stop();
            await RecordAsync(http, options, startedAt, (int)clock.ElapsedMilliseconds, failure, bufferBody);
        }
    }

    private async Task RecordAsync(
        HttpContext http, AuditOptions options, DateTime startedAt, int elapsedMs, Exception? failure, bool bodyWasBuffered)
    {
        try
        {
            var method = http.Request.Method;
            var path = http.Request.Path.Value ?? "/";
            var controller = http.Request.RouteValues["controller"]?.ToString();
            var action = http.Request.RouteValues["action"]?.ToString();
            var status = failure is not null ? StatusCodes.Status500InternalServerError : http.Response.StatusCode;

            // Opening a page or loading a list is the bulk of the traffic and says nothing worth keeping.
            if (!AuditRequestRules.ShouldRecord(method, status, failure is not null, options.LogReadRequests)) return;

            var description = $"{method} {path} → {status} ({elapsedMs} ms)";
            if (failure is not null) description += $" – {failure.GetType().Name}";

            _trail.Record(new AuditLog
            {
                CreatedAt = startedAt,
                Category = AuditCategory.Request,
                Action = AuditRequestRules.ActionName(method, controller, action, path),
                EntityType = AuditActions.HttpEntity,
                Description = description,
                Outcome = AuditRequestRules.OutcomeFor(status, failure is not null),
                Severity = AuditRequestRules.SeverityFor(status, failure is not null),
                HttpMethod = method,
                RequestPath = path,
                StatusCode = status,
                DurationMs = elapsedMs,
                AdditionalInfoJson = AuditRedactor.SerializeDetails(await DetailsAsync(http, options, failure, bodyWasBuffered))
            });

            // The desktop shell ends the server abruptly (no graceful shutdown), which would lose whatever is still
            // queued. So for anything that changed state - or failed - wait until it is on disk. The browser already
            // has its response by now, and reads (page views, lookups) keep the cheap batched path.
            if (MustBeDurable(method, status))
            {
                await WaitUntilWrittenAsync();
            }
        }
        catch (Exception ex)
        {
            // Auditing must never turn a good response into an error.
            _logger.LogWarning(ex, "Audit: could not record request {Path}", http.Request.Path);
        }
    }

    private static bool MustBeDurable(string method, int status)
        => !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method)) || status >= StatusCodes.Status400BadRequest;

    private async Task WaitUntilWrittenAsync()
    {
        using var timeout = new CancellationTokenSource(DurabilityWait);
        try
        {
            await _trail.FlushAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Audit: an entry was not on disk after {Seconds}s; the background writer will finish it", DurabilityWait.TotalSeconds);
        }
    }

    private static async Task<object> DetailsAsync(HttpContext http, AuditOptions options, Exception? failure, bool bodyWasBuffered)
    {
        var request = http.Request;
        var location = http.Response.Headers.Location.ToString();
        var referer = request.Headers.Referer.ToString();

        return new
        {
            route = request.RouteValues.Count == 0
                ? null
                : request.RouteValues.ToDictionary(r => r.Key, r => r.Value?.ToString()),
            query = request.QueryString.HasValue
                ? AuditRedactor.RedactPairs(request.Query.Select(q => new KeyValuePair<string, string?>(q.Key, q.Value.ToString())))
                : null,
            form = ReadForm(request),
            body = bodyWasBuffered ? await ReadBodyAsync(request, options.MaxBodyBytes) : null,
            redirectTo = string.IsNullOrEmpty(location) ? null : location,
            referer = string.IsNullOrEmpty(referer) ? null : RefererPath(referer),
            exception = failure is null ? null : new { type = failure.GetType().FullName, message = AuditRedactor.Truncate(failure.Message, 500) }
        };
    }

    private static bool ShouldBufferBody(HttpRequest request)
        => (HttpMethods.IsPost(request.Method) || HttpMethods.IsPut(request.Method) ||
            HttpMethods.IsPatch(request.Method) || HttpMethods.IsDelete(request.Method))
           && request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true
           && (request.ContentLength is null || request.ContentLength <= MaxBufferedBody);

    private static Dictionary<string, string?>? ReadForm(HttpRequest request)
    {
        if (!request.HasFormContentType ||
            request.ContentType?.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) != true)
            return null; // multipart uploads (images) are not worth buffering

        try
        {
            return AuditRedactor.RedactPairs(request.Form.Select(f => new KeyValuePair<string, string?>(f.Key, f.Value.ToString())));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<object?> ReadBodyAsync(HttpRequest request, int maxBytes)
    {
        try
        {
            if (!request.Body.CanSeek) return null;

            var length = request.ContentLength ?? request.Body.Length;
            if (length > maxBytes) return new { notCaptured = $"{length} bytes (limit {maxBytes})" };

            request.Body.Position = 0;
            using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var text = await reader.ReadToEndAsync();
            request.Body.Position = 0;

            return AuditRedactor.RedactJson(text) is { } redacted
                ? System.Text.Json.Nodes.JsonNode.Parse(redacted)
                : new { notCaptured = "not valid JSON" };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? RefererPath(string referer)
        => Uri.TryCreate(referer, UriKind.Absolute, out var uri) ? uri.AbsolutePath : null;
}
