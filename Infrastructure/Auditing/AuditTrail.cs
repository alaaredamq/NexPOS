using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Core.Auditing;
using Core.Entities.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Auditing;

/// <summary>
/// Collects audit entries from every thread into a queue and writes them to the audit database in
/// batches from one background loop (<see cref="RunAsync"/>). The caller never waits for the disk and
/// can never be hurt by a problem with it:
/// <list type="bullet">
/// <item>a failed write is retried, then the batch is saved to a fallback file that is replayed on the next start;</item>
/// <item>a full queue spills to the same fallback file instead of blocking or dropping;</item>
/// <item>a clean shutdown drains the queue before stopping.</item>
/// </list>
/// </summary>
public sealed class AuditTrail : IAuditTrail
{
    private const int QueueCapacity = 50_000;
    private const int MaxBatch = 500;

    // Lets a burst (one request produces a request row plus several data rows) land in one transaction.
    private static readonly TimeSpan DefaultCoalesceWindow = TimeSpan.FromMilliseconds(75);
    private static readonly TimeSpan[] DefaultRetryDelays =
        [TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];

    private static readonly JsonSerializerOptions FileJson = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Channel<Envelope> _queue = Channel.CreateBounded<Envelope>(
        new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly AuditStoreOptions _store;
    private readonly IAuditContext _context;
    private readonly ILogger<AuditTrail> _logger;
    private readonly DbContextOptions<AuditDbContext> _dbOptions;
    private readonly TimeSpan[] _retryDelays;
    private readonly TimeSpan _coalesceWindow;

    // Raised by FlushAsync so the writer skips its batching delay when somebody is waiting for the write.
    private TaskCompletionSource _urgent = NewSignal();

    private readonly record struct Envelope(AuditLog? Entry, TaskCompletionSource? Flush);

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public AuditTrail(AuditStoreOptions store, IAuditContext context, ILogger<AuditTrail> logger)
    {
        _store = store;
        _context = context;
        _logger = logger;
        _dbOptions = AuditDatabase.CreateOptions(store.ConnectionString);
        _retryDelays = store.RetryDelays ?? DefaultRetryDelays;
        _coalesceWindow = store.CoalesceWindow ?? DefaultCoalesceWindow;
    }

    // ---- IAuditTrail --------------------------------------------------------------------------------------------

    public void Record(AuditLog entry)
    {
        try
        {
            Enrich(entry);

            if (!_queue.Writer.TryWrite(new Envelope(entry, null)))
            {
                // Queue full (the writer is stuck) or already shut down: park the entry on disk
                // rather than blocking the caller or losing it.
                WriteFallback([entry]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit: could not queue an entry");
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var done = NewSignal();
        try
        {
            await _queue.Writer.WriteAsync(new Envelope(null, done), cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return; // already shut down and drained
        }

        // Somebody is waiting: write now rather than after the usual batching delay.
        Volatile.Read(ref _urgent).TrySetResult();

        await done.Task.WaitAsync(cancellationToken);
    }

    // ---- lifecycle ----------------------------------------------------------------------------------------------

    /// <summary>Creates the database if needed and recovers entries parked in the fallback folder.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            AuditDatabase.EnsureCreated(_store.ConnectionString);
        }
        catch (Exception ex)
        {
            // Not fatal: entries queue up and spill to the fallback folder until the store is healthy.
            _logger.LogError(ex, "Audit: the audit database could not be prepared");
            return;
        }

        await ReplayFallbackAsync();
    }

    /// <summary>The writer loop. Runs until <paramref name="stoppingToken"/> is cancelled, then drains the queue.</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AuditLog>(MaxBatch);
        var flushes = new List<TaskCompletionSource>();

        try
        {
            while (await _queue.Reader.WaitToReadAsync(stoppingToken))
            {
                await CoalesceAsync(stoppingToken);
                Drain(batch, flushes);
                await WriteBatchAsync(batch);
                CompleteFlushes(flushes);
            }
        }
        catch (OperationCanceledException)
        {
            // stopping - fall through to the final drain
        }

        _queue.Writer.TryComplete();
        while (true)
        {
            Drain(batch, flushes);
            if (batch.Count == 0 && flushes.Count == 0) break;
            await WriteBatchAsync(batch);
            CompleteFlushes(flushes);
        }
    }

    // ---- internals ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Waits a moment so a burst (a request's rows arrive within a few milliseconds of each other) lands in one
    /// transaction - but not when a caller is waiting on <see cref="FlushAsync"/>, which is then served at once.
    /// </summary>
    private async Task CoalesceAsync(CancellationToken stoppingToken)
    {
        var urgent = Volatile.Read(ref _urgent);
        if (!urgent.Task.IsCompleted)
        {
            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            await Task.WhenAny(Task.Delay(_coalesceWindow, delayCts.Token), urgent.Task);
            delayCts.Cancel(); // don't leave the timer running when the urgent signal won
        }

        Volatile.Write(ref _urgent, NewSignal());
    }

    private void Drain(List<AuditLog> batch, List<TaskCompletionSource> flushes)
    {
        while (batch.Count < MaxBatch && _queue.Reader.TryRead(out var envelope))
        {
            if (envelope.Entry is not null) batch.Add(envelope.Entry);
            if (envelope.Flush is not null) flushes.Add(envelope.Flush);
        }
    }

    private static void CompleteFlushes(List<TaskCompletionSource> flushes)
    {
        foreach (var flush in flushes) flush.TrySetResult();
        flushes.Clear();
    }

    private async Task WriteBatchAsync(List<AuditLog> batch)
    {
        if (batch.Count == 0) return;

        Exception? last = null;
        for (var attempt = 0; attempt <= _retryDelays.Length; attempt++)
        {
            try
            {
                await InsertAsync(batch);
                batch.Clear();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt < _retryDelays.Length)
                {
                    // Deliberately not cancellable: finishing the write matters more than a prompt stop.
                    await Task.Delay(_retryDelays[attempt]);
                }
            }
        }

        _logger.LogError(last, "Audit: could not write {Count} entries to the audit database; they were saved to {Folder} and will be replayed on the next start",
            batch.Count, _store.FallbackDirectory);
        WriteFallback(batch);
        batch.Clear();
    }

    private async Task InsertAsync(IReadOnlyCollection<AuditLog> entries)
    {
        await using var db = new AuditDbContext(_dbOptions);
        db.ChangeTracker.AutoDetectChangesEnabled = false;

        foreach (var entry in entries)
        {
            entry.Id = 0; // a retried batch must not carry ids from a failed attempt
        }

        db.AuditLogs.AddRange(entries);
        await db.SaveChangesAsync();
    }

    private void WriteFallback(IEnumerable<AuditLog> entries)
    {
        var list = entries.ToList();
        try
        {
            Directory.CreateDirectory(_store.FallbackDirectory);
            var file = Path.Combine(_store.FallbackDirectory, $"audit-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.jsonl");
            File.WriteAllLines(file, list.Select(e => JsonSerializer.Serialize(e, FileJson)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit: the fallback file could not be written - {Count} entries are lost", list.Count);
        }
    }

    private async Task ReplayFallbackAsync()
    {
        if (!Directory.Exists(_store.FallbackDirectory)) return;

        var recovered = 0;
        foreach (var file in Directory.EnumerateFiles(_store.FallbackDirectory, "audit-*.jsonl").Order())
        {
            try
            {
                var entries = File.ReadLines(file)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => JsonSerializer.Deserialize<AuditLog>(line, FileJson))
                    .OfType<AuditLog>()
                    .ToList();

                await InsertAsync(entries);
                File.Delete(file);
                recovered += entries.Count;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit: could not replay {File}; it is kept and will be retried on the next start", file);
            }
        }

        if (recovered > 0)
        {
            Record(new AuditLog
            {
                Category = AuditCategory.System,
                Action = AuditActions.AuditRecovered,
                EntityType = "Audit",
                Description = $"Recovered {recovered} audit entries that could not be written earlier",
                Severity = AuditLogSeverity.Warning
            });
        }
    }

    private void Enrich(AuditLog entry)
    {
        var snapshot = _context.Capture();

        if (entry.UserId is null && string.IsNullOrEmpty(entry.Username))
        {
            entry.UserId = snapshot.UserId;
            entry.Username = snapshot.Username;
            entry.UserRole ??= snapshot.UserRole;
        }
        else if (entry.UserId is not null && entry.UserId == snapshot.UserId)
        {
            entry.Username ??= snapshot.Username;
            entry.UserRole ??= snapshot.UserRole;
        }

        if (string.IsNullOrEmpty(entry.IpAddress)) entry.IpAddress = snapshot.IpAddress;
        if (string.IsNullOrEmpty(entry.UserAgent)) entry.UserAgent = snapshot.UserAgent;
        entry.MachineName ??= snapshot.MachineName;
        entry.AppVersion ??= snapshot.AppVersion;
        entry.CorrelationId ??= snapshot.CorrelationId;
        entry.Source ??= snapshot.Source;
        entry.HttpMethod ??= snapshot.HttpMethod;
        entry.RequestPath ??= snapshot.RequestPath;

        // The columns have sensible limits; keep every row inside them whatever the caller passed.
        entry.Action = AuditRedactor.Truncate(entry.Action, 120) ?? string.Empty;
        entry.EntityType = AuditRedactor.Truncate(entry.EntityType, 100) ?? string.Empty;
        entry.EntityLabel = AuditRedactor.Truncate(entry.EntityLabel, 200);
        entry.Username = AuditRedactor.Truncate(entry.Username, 100);
        entry.Description = AuditRedactor.Truncate(entry.Description, 2000);
        entry.ChangedFields = AuditRedactor.Truncate(entry.ChangedFields, 1000);
        entry.UserAgent = AuditRedactor.Truncate(entry.UserAgent, 300) ?? string.Empty;
        entry.RequestPath = AuditRedactor.Truncate(entry.RequestPath, 500);
    }
}
