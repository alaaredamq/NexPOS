using Core.Auditing;
using Core.Entities.Logging;
using Core.IRepositories.Logging;
using Infrastructure.Auditing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Services.Logging;

/// <summary>
/// Runs the audit writer for the life of the application: prepares the audit database, writes queued
/// entries in the background and, on a clean shutdown, drains the queue so nothing in flight is lost.
/// Also applies the (opt-in) retention policy once a day.
/// </summary>
public sealed class AuditWriterHostedService : BackgroundService
{
    private static readonly TimeSpan FirstPurgeDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(24);

    private readonly AuditTrail _trail;
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<AuditOptions> _options;
    private readonly ILogger<AuditWriterHostedService> _logger;

    public AuditWriterHostedService(
        AuditTrail trail, IServiceScopeFactory scopes, IOptions<AuditOptions> options,
        ILogger<AuditWriterHostedService> logger)
    {
        _trail = trail;
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await _trail.InitializeAsync();
        await base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(_trail.RunAsync(stoppingToken), RunRetentionAsync(stoppingToken));

    private async Task RunRetentionAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstPurgeDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await PurgeAsync(stoppingToken);
                await Task.Delay(PurgeInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // stopping
        }
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        var days = _options.Value.RetentionDays;
        if (days <= 0) return; // keep everything (the default)

        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-days);
            using var scope = _scopes.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
            var removed = await repository.PurgeOlderThanAsync(cutoff, cancellationToken);

            if (removed > 0)
            {
                _trail.Record(new AuditLog
                {
                    Category = AuditCategory.System,
                    Action = AuditActions.AuditPurged,
                    EntityType = "Audit",
                    Description = $"Retention policy ({days} days): removed {removed} audit entries older than {cutoff:yyyy-MM-dd}",
                    Severity = AuditLogSeverity.Warning,
                    Source = "System:AuditRetention"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit retention purge failed");
        }
    }
}
