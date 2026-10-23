using Core.Auditing;
using Core.Entities.Logging;
using Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Auditing;

/// <summary>The writer: enrichment, batching, shutdown draining and the "database is broken" fallback.</summary>
public sealed class AuditTrailTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nexpos-audit-tests", Guid.NewGuid().ToString("N"));
    private readonly List<(CancellationTokenSource Cts, Task Run)> _runs = [];

    public AuditTrailTests() => Directory.CreateDirectory(_dir);

    private string Connection => $"Data Source={Path.Combine(_dir, "audit.db")}";
    private string FallbackDir => Path.Combine(_dir, "fallback");

    private AuditTrail NewTrail(string? connection = null, IAuditContext? context = null)
        => new(new AuditStoreOptions(
                connection ?? Connection, FallbackDir,
                RetryDelays: [TimeSpan.FromMilliseconds(10)],
                CoalesceWindow: TimeSpan.FromMilliseconds(5)),
            context ?? new FakeAuditContext(),
            NullLogger<AuditTrail>.Instance);

    private (CancellationTokenSource Cts, Task Run) StartWriter(AuditTrail trail)
    {
        var cts = new CancellationTokenSource();
        var run = Task.Run(() => trail.RunAsync(cts.Token));
        _runs.Add((cts, run));
        return (cts, run);
    }

    private async Task<List<AuditLog>> ReadAllAsync()
    {
        await using var db = new AuditDbContext(AuditDatabase.CreateOptions(Connection));
        return await db.AuditLogs.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
    }

    public void Dispose()
    {
        foreach (var (cts, run) in _runs)
        {
            cts.Cancel();
            try { run.Wait(TimeSpan.FromSeconds(5)); } catch { /* shutting down */ }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* temp files */ }
    }

    [Fact]
    public async Task Record_FillsWhoAndWhereFromTheContext_AndWritesToTheStore()
    {
        var trail = NewTrail();
        await trail.InitializeAsync();
        StartWriter(trail);

        trail.Record(new AuditLog
        {
            Category = AuditCategory.Business,
            Action = "SaleCompleted",
            EntityType = "Order",
            EntityId = 42,
            Description = "Sale #42 completed"
        });
        await trail.FlushAsync();

        var row = Assert.Single(await ReadAllAsync());
        Assert.Equal(7, row.UserId);
        Assert.Equal("alice", row.Username);
        Assert.Equal("Cashier", row.UserRole);
        Assert.Equal("10.0.0.5", row.IpAddress);
        Assert.Equal("TestAgent/1.0", row.UserAgent);
        Assert.Equal("REQ-1", row.CorrelationId);
        Assert.Equal("TILL-01", row.MachineName);
        Assert.Equal("test-build", row.AppVersion);
        Assert.Equal("Web", row.Source);
        Assert.Equal("POST", row.HttpMethod);
        Assert.Equal("/en/Test/Run", row.RequestPath);
        Assert.Equal(AuditCategory.Business, row.Category);
        Assert.Equal(AuditOutcome.Success, row.Outcome);
        Assert.True((DateTime.UtcNow - row.CreatedAt).TotalMinutes < 1);
    }

    [Fact]
    public async Task Record_KeepsValuesTheCallerSetExplicitly()
    {
        var trail = NewTrail();
        await trail.InitializeAsync();
        StartWriter(trail);

        // A failed sign-in: nobody is authenticated, the *attempted* name is what matters.
        trail.Record(new AuditLog
        {
            Category = AuditCategory.Security,
            Action = "LoginFailed",
            EntityType = "User",
            Username = "mallory",
            IpAddress = "203.0.113.9",
            Source = "Web"
        });
        await trail.FlushAsync();

        var row = Assert.Single(await ReadAllAsync());
        Assert.Equal("mallory", row.Username);
        Assert.Null(row.UserId);
        Assert.Equal("203.0.113.9", row.IpAddress);
    }

    [Fact]
    public async Task ManyEntries_AreAllWritten_InOrder()
    {
        var trail = NewTrail();
        await trail.InitializeAsync();
        StartWriter(trail);

        for (var i = 0; i < 1200; i++)
        {
            trail.Record(new AuditLog { Category = AuditCategory.System, Action = "Tick", EntityType = "T", Description = i.ToString() });
        }

        await trail.FlushAsync();

        var rows = await ReadAllAsync();
        Assert.Equal(1200, rows.Count);
        Assert.Equal(Enumerable.Range(0, 1200).Select(i => i.ToString()), rows.Select(r => r.Description));
    }

    [Fact]
    public async Task CleanShutdown_DrainsWhatIsStillQueued()
    {
        var trail = NewTrail();
        await trail.InitializeAsync();
        var (cts, run) = StartWriter(trail);

        for (var i = 0; i < 50; i++)
        {
            trail.Record(new AuditLog { Category = AuditCategory.System, Action = "Last", EntityType = "T", Description = i.ToString() });
        }

        cts.Cancel(); // no flush: simply stop the application
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(50, (await ReadAllAsync()).Count);
    }

    [Fact]
    public async Task Flush_IsServedAtOnce_NotAfterTheBatchingDelay()
    {
        // A request that changed data waits for its audit row to reach the disk (the desktop shell kills the
        // server without a graceful shutdown). That wait must not include the usual batching delay.
        var trail = new AuditTrail(
            new AuditStoreOptions(Connection, FallbackDir, RetryDelays: [], CoalesceWindow: TimeSpan.FromSeconds(8)),
            new FakeAuditContext(), NullLogger<AuditTrail>.Instance);
        await trail.InitializeAsync();
        StartWriter(trail);

        trail.Record(new AuditLog { Category = AuditCategory.Business, Action = "SaleCompleted", EntityType = "Order" });

        var clock = System.Diagnostics.Stopwatch.StartNew();
        await trail.FlushAsync().WaitAsync(TimeSpan.FromSeconds(6));
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"the flush took {clock.Elapsed.TotalMilliseconds:F0} ms - it waited for the batching delay");
        Assert.Single(await ReadAllAsync());
    }

    [Fact]
    public async Task Record_NeverThrows_EvenWhenTheContextDoes()
    {
        var trail = NewTrail(context: new ThrowingAuditContext());

        var exception = Record.Exception(() =>
            trail.Record(new AuditLog { Category = AuditCategory.System, Action = "X", EntityType = "T" }));

        Assert.Null(exception);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task UnwritableStore_SpillsToTheFallbackFolder_AndALaterStartReplaysIt()
    {
        // A read-only connection to a file that doesn't exist can never be written.
        var broken = NewTrail($"Data Source={Path.Combine(_dir, "missing-folder", "audit.db")};Mode=ReadOnly");
        await broken.InitializeAsync(); // must not throw: auditing problems can't stop the application
        StartWriter(broken);

        broken.Record(new AuditLog { Category = AuditCategory.Business, Action = "SaleCompleted", EntityType = "Order", Description = "must not be lost" });
        await broken.FlushAsync();

        var parked = Assert.Single(Directory.GetFiles(FallbackDir, "audit-*.jsonl"));
        Assert.Contains("must not be lost", await File.ReadAllTextAsync(parked));

        // Next start, healthy store.
        var healthy = NewTrail();
        await healthy.InitializeAsync();
        StartWriter(healthy);
        await healthy.FlushAsync();

        Assert.Empty(Directory.GetFiles(FallbackDir, "audit-*.jsonl")); // consumed
        var rows = await ReadAllAsync();
        Assert.Contains(rows, r => r.Description == "must not be lost" && r.Action == "SaleCompleted");
        Assert.Contains(rows, r => r.Action == AuditActions.AuditRecovered);
    }

    [Fact]
    public async Task EnsureCreated_IsIdempotent_AndKeepsExistingRows()
    {
        var trail = NewTrail();
        await trail.InitializeAsync();
        StartWriter(trail);
        trail.Record(new AuditLog { Category = AuditCategory.System, Action = "Keep", EntityType = "T" });
        await trail.FlushAsync();

        AuditDatabase.EnsureCreated(Connection);
        AuditDatabase.EnsureCreated(Connection);

        Assert.Single(await ReadAllAsync());
    }

    private sealed class ThrowingAuditContext : IAuditContext
    {
        public AuditSnapshot Capture() => throw new InvalidOperationException("no http context");
        public void SetActor(int userId, string username, string? role) { }
        public IDisposable BeginSystemScope(string source) => throw new NotSupportedException();
        public IDisposable SuppressDataChanges(string reason) => throw new NotSupportedException();
        public bool DataChangesSuppressed => false;
    }
}
