namespace Core.Auditing;

/// <summary>
/// Who is doing something and where from, captured at the moment an audit row is created.
/// </summary>
public sealed class AuditSnapshot
{
    public int? UserId { get; init; }
    public string? Username { get; init; }
    public string? UserRole { get; init; }
    public string IpAddress { get; init; } = string.Empty;
    public string UserAgent { get; init; } = string.Empty;
    /// <summary>Request id shared by every row one HTTP request produces.</summary>
    public string? CorrelationId { get; init; }
    public string? HttpMethod { get; init; }
    public string? RequestPath { get; init; }
    /// <summary>"Web", or "System:&lt;job&gt;" for background work.</summary>
    public string Source { get; init; } = "System";
    public string MachineName { get; init; } = Environment.MachineName;
    public string AppVersion { get; init; } = string.Empty;
}

/// <summary>
/// The ambient "who/where" of the work currently running: the signed-in user of an HTTP request,
/// or a named system actor for background work and start-up. Implemented by the web layer; the
/// audit interceptors (Infrastructure) only see this abstraction.
/// </summary>
public interface IAuditContext
{
    /// <summary>Snapshot of the current actor and request. Safe to call from any thread.</summary>
    AuditSnapshot Capture();

    /// <summary>
    /// Attributes the rest of the current request to this user. Used by the sign-in flow, where the
    /// database writes (last-login, session row) happen before the auth cookie exists.
    /// </summary>
    void SetActor(int userId, string username, string? role);

    /// <summary>Runs the current async flow as a named system actor (background jobs, start-up).</summary>
    IDisposable BeginSystemScope(string source);

    /// <summary>
    /// Stops per-row change auditing inside the scope (schema migrations, bulk demo seeding), where
    /// the individual rows are not events anyone would investigate. Callers record one summary
    /// event instead. Not for use on user-driven work.
    /// </summary>
    IDisposable SuppressDataChanges(string reason);

    bool DataChangesSuppressed { get; }
}
