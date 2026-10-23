using System.Net;
using System.Security.Claims;
using Core.Auditing;

namespace Web.Services;

/// <summary>
/// Answers "who is doing this, and from where" for the audit trail: the signed-in user of the current
/// HTTP request, or a named system actor for background work and start-up (no HttpContext there).
/// Registered as a singleton - everything it reads is ambient (HttpContext / AsyncLocal).
/// </summary>
public sealed class HttpAuditContext : IAuditContext
{
    private const string ActorItemKey = "Audit.Actor";

    private static readonly AsyncLocal<string?> SystemSource = new();
    private static readonly AsyncLocal<int> Suppressed = new();

    private readonly IHttpContextAccessor _accessor;
    private readonly string _appVersion = $"build {BuildInfo.BuildDate}";

    private sealed record ActorOverride(int UserId, string Username, string? Role);

    public HttpAuditContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public bool DataChangesSuppressed => Suppressed.Value > 0;

    public AuditSnapshot Capture()
    {
        try
        {
            var http = _accessor.HttpContext;
            if (http is null || SystemSource.Value is not null)
                return SystemSnapshot();

            int? userId = null;
            string? username = null;
            string? role = null;

            if (http.Items.TryGetValue(ActorItemKey, out var overridden) && overridden is ActorOverride actor)
            {
                (userId, username, role) = (actor.UserId, actor.Username, actor.Role);
            }
            else if (http.User.Identity?.IsAuthenticated == true)
            {
                userId = int.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0 ? id : null;
                username = http.User.Identity.Name ?? http.User.FindFirstValue(ClaimTypes.Name);
                role = http.User.FindFirstValue(ClaimTypes.Role) ?? http.User.FindFirstValue("Role");
            }

            return new AuditSnapshot
            {
                UserId = userId,
                Username = username,
                UserRole = role,
                IpAddress = FormatAddress(http.Connection.RemoteIpAddress),
                UserAgent = http.Request.Headers.UserAgent.ToString(),
                CorrelationId = http.TraceIdentifier,
                HttpMethod = http.Request.Method,
                RequestPath = http.Request.Path.Value,
                Source = "Web",
                MachineName = Environment.MachineName,
                AppVersion = _appVersion
            };
        }
        catch (Exception)
        {
            // The request object can already be recycled (work that outlives the request). Never let that fail an audit.
            return SystemSnapshot();
        }
    }

    public void SetActor(int userId, string username, string? role)
    {
        var http = _accessor.HttpContext;
        if (http is not null)
        {
            http.Items[ActorItemKey] = new ActorOverride(userId, username, role);
        }
    }

    public IDisposable BeginSystemScope(string source)
    {
        var previous = SystemSource.Value;
        SystemSource.Value = source;
        return new Restore(() => SystemSource.Value = previous);
    }

    public IDisposable SuppressDataChanges(string reason)
    {
        Suppressed.Value++;
        return new Restore(() => Suppressed.Value--);
    }

    private AuditSnapshot SystemSnapshot() => new()
    {
        Username = "System",
        Source = SystemSource.Value is { } source ? $"System:{source}" : "System",
        MachineName = Environment.MachineName,
        AppVersion = _appVersion
    };

    private static string FormatAddress(IPAddress? address)
    {
        if (address is null) return string.Empty;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return IPAddress.IsLoopback(address) ? "127.0.0.1" : address.ToString();
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0) undo();
        }
    }
}
