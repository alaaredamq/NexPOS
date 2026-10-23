using Core.Auditing;
using Core.Entities.Logging;
using Infrastructure.Auditing;
using Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Auditing;

/// <summary>An IAuditTrail that just remembers what it was given (no queue, no database).</summary>
public sealed class CollectingAuditTrail : IAuditTrail
{
    private readonly object _gate = new();
    private readonly List<AuditLog> _entries = [];

    public IReadOnlyList<AuditLog> Entries
    {
        get { lock (_gate) return _entries.ToList(); }
    }

    public void Record(AuditLog entry)
    {
        lock (_gate) _entries.Add(entry);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }
}

/// <summary>A settable IAuditContext.</summary>
public sealed class FakeAuditContext : IAuditContext
{
    private int _suppressed;

    public AuditSnapshot Snapshot { get; set; } = new()
    {
        UserId = 7,
        Username = "alice",
        UserRole = "Cashier",
        IpAddress = "10.0.0.5",
        UserAgent = "TestAgent/1.0",
        CorrelationId = "REQ-1",
        HttpMethod = "POST",
        RequestPath = "/en/Test/Run",
        Source = "Web",
        MachineName = "TILL-01",
        AppVersion = "test-build"
    };

    public AuditSnapshot Capture() => Snapshot;

    public void SetActor(int userId, string username, string? role)
        => Snapshot = new AuditSnapshot
        {
            UserId = userId, Username = username, UserRole = role,
            IpAddress = Snapshot.IpAddress, UserAgent = Snapshot.UserAgent, CorrelationId = Snapshot.CorrelationId,
            HttpMethod = Snapshot.HttpMethod, RequestPath = Snapshot.RequestPath, Source = Snapshot.Source,
            MachineName = Snapshot.MachineName, AppVersion = Snapshot.AppVersion
        };

    public IDisposable BeginSystemScope(string source) => new Scope(() => { });

    public IDisposable SuppressDataChanges(string reason)
    {
        Interlocked.Increment(ref _suppressed);
        return new Scope(() => Interlocked.Decrement(ref _suppressed));
    }

    public bool DataChangesSuppressed => Volatile.Read(ref _suppressed) > 0;

    private sealed class Scope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}

/// <summary>
/// A real (SQLite in-memory) ErpDbContext with the three audit interceptors attached and a collecting trail,
/// so tests exercise the genuine EF pipeline.
/// </summary>
public sealed class AuditedDb : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AuditChangeTracker _tracker = new();

    public CollectingAuditTrail Trail { get; } = new();
    public FakeAuditContext Context { get; } = new();

    public AuditedDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // Build the schema (and the HasData seed) with a context that has no interceptors,
        // so the seed rows don't show up as audit entries.
        using var plain = new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>().UseSqlite(_connection).Options);
        plain.Database.EnsureCreated();
    }

    public ErpDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(
                new AuditSaveChangesInterceptor(Trail, Context, _tracker, NullLogger<AuditSaveChangesInterceptor>.Instance),
                new AuditTransactionInterceptor(Trail, _tracker, NullLogger<AuditTransactionInterceptor>.Instance),
                new AuditCommandInterceptor(Trail, Context, _tracker, NullLogger<AuditCommandInterceptor>.Instance))
            .Options;
        return new ErpDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
