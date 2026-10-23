using System.Data.Common;
using System.Text.RegularExpressions;
using Core.Auditing;
using Core.Entities.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Auditing;

/// <summary>
/// Safety net for writes that skip the change tracker: <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> (the stock
/// deduction at checkout is one) and raw SQL. Any INSERT/UPDATE/DELETE that EF issues outside of
/// SaveChanges is recorded here, so no data change can slip past the trail - including ones a
/// developer adds next year without knowing the audit exists.
/// </summary>
public sealed partial class AuditCommandInterceptor : DbCommandInterceptor
{
    // Tables whose bound parameter values (PIN hashes, session tokens) must not be written down.
    private static readonly HashSet<string> SensitiveTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "Users", "UserSessions", "UserPermissions"
    };

    private readonly IAuditTrail _trail;
    private readonly IAuditContext _context;
    private readonly AuditChangeTracker _tracker;
    private readonly ILogger<AuditCommandInterceptor> _logger;

    public AuditCommandInterceptor(
        IAuditTrail trail, IAuditContext context, AuditChangeTracker tracker, ILogger<AuditCommandInterceptor> logger)
    {
        _trail = trail;
        _context = context;
        _tracker = tracker;
        _logger = logger;
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Observe(command, eventData, result, error: null);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Observe(command, eventData, result, error: null);
        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
        => Observe(command, eventData, rows: null, error: eventData.Exception);

    public override Task CommandFailedAsync(
        DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Observe(command, eventData, rows: null, error: eventData.Exception);
        return Task.CompletedTask;
    }

    private void Observe(DbCommand command, CommandEndEventData eventData, int? rows, Exception? error)
    {
        try
        {
            var context = eventData.Context;
            if (context is null || _context.DataChangesSuppressed) return;
            if (_tracker.TryGet(context, out var state) && state.InSave) return; // SaveChanges' own statements: already audited

            var sql = command.CommandText;
            if (!TryParse(sql, out var verb, out var table)) return;
            if (table.Equals("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase)) return;

            var action = verb switch
            {
                "INSERT" => AuditActions.BulkInsert,
                "UPDATE" => AuditActions.BulkUpdate,
                _ => AuditActions.BulkDelete
            };

            // "Products" (the table) -> "Product" (the entity), so a record's history lines up with its bulk updates.
            var entityName = context.Model.GetEntityTypes()
                .FirstOrDefault(t => string.Equals(t.GetTableName(), table, StringComparison.OrdinalIgnoreCase))
                ?.ShortName() ?? table;

            var entityId = FindTargetId(command, sql);
            var columns = verb == "UPDATE" ? ChangedColumns(sql) : [];
            var failed = error is not null;
            var summary = failed
                ? $"{verb} on {table} failed: {AuditRedactor.Truncate(error!.Message, 300)}"
                : $"{verb} on {table}{(columns.Count > 0 ? $" [{string.Join(", ", columns)}]" : "")}: {rows} row(s) affected";

            var row = new AuditLog
            {
                Category = AuditCategory.Data,
                Action = action,
                EntityType = entityName,
                EntityId = entityId,
                Description = summary,
                ChangedFields = columns.Count > 0 ? string.Join(", ", columns) : null,
                Outcome = failed ? AuditOutcome.Failure : AuditOutcome.Success,
                Severity = failed ? AuditLogSeverity.Error : AuditLogSeverity.Info,
                DurationMs = (int)eventData.Duration.TotalMilliseconds,
                AdditionalInfoJson = AuditRedactor.SerializeDetails(new
                {
                    rowsAffected = rows,
                    sql = AuditRedactor.Truncate(sql, 1500),
                    parameters = Parameters(command, table)
                })
            };

            _tracker.Publish(context, _trail, [row]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit: could not record a bulk write");
        }
    }

    private static bool TryParse(string sql, out string verb, out string table)
    {
        verb = table = string.Empty;
        var match = StatementPattern().Match(sql);
        if (!match.Success) return false;

        verb = match.Groups["verb"].Value.ToUpperInvariant() switch
        {
            var v when v.StartsWith("INSERT", StringComparison.Ordinal) => "INSERT",
            var v when v.StartsWith("UPDATE", StringComparison.Ordinal) => "UPDATE",
            _ => "DELETE"
        };
        table = match.Groups["table"].Value;
        return true;
    }

    /// <summary>The columns an UPDATE assigns, e.g. SET "StockQuantity" = ..., "UpdatedAt" = ... -> [StockQuantity, UpdatedAt].</summary>
    private static List<string> ChangedColumns(string sql)
    {
        var set = SetClausePattern().Match(sql);
        if (!set.Success) return [];

        return AssignmentPattern().Matches(set.Groups["set"].Value)
            .Select(m => m.Groups["column"].Value)
            .Distinct()
            .ToList();
    }

    /// <summary>For "... WHERE p.Id = @id" statements, the id of the single targeted row.</summary>
    private static int? FindTargetId(DbCommand command, string sql)
    {
        var matches = IdComparisonPattern().Matches(sql);
        if (matches.Count != 1) return null;

        var name = matches[0].Groups["param"].Value;
        foreach (DbParameter parameter in command.Parameters)
        {
            if (!string.Equals(parameter.ParameterName.TrimStart('@', '$', ':'), name.TrimStart('@', '$', ':'),
                    StringComparison.OrdinalIgnoreCase))
                continue;

            return parameter.Value switch
            {
                int i => i,
                long l when l is > 0 and <= int.MaxValue => (int)l,
                _ => null
            };
        }

        return null;
    }

    private static Dictionary<string, object?> Parameters(DbCommand command, string table)
    {
        var redact = SensitiveTables.Contains(table);
        var result = new Dictionary<string, object?>();
        foreach (DbParameter parameter in command.Parameters)
        {
            result[parameter.ParameterName] = redact || AuditRedactor.IsSensitiveName(parameter.ParameterName)
                ? AuditRedactor.Redacted
                : AuditEntityDescriber.FormatValue(parameter.Value is DBNull ? null : parameter.Value);
        }

        return result;
    }

    [GeneratedRegex(
        """^\s*(?<verb>INSERT(?:\s+OR\s+\w+)?\s+INTO|UPDATE|DELETE\s+FROM)\s+["`\[]?(?<table>\w+)["`\]]?""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StatementPattern();

    [GeneratedRegex(@"\bSET\s+(?<set>.*?)(?:\s+WHERE\b|\s+FROM\b|\s+RETURNING\b|$)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex SetClausePattern();

    [GeneratedRegex("""(?:^|,)\s*"(?<column>\w+)"\s*=""", RegexOptions.CultureInvariant)]
    private static partial Regex AssignmentPattern();

    [GeneratedRegex("""\."Id"\s*=\s*(?<param>[@$:]\w+)|(?<![.\w])"Id"\s*=\s*(?<param>[@$:]\w+)""", RegexOptions.CultureInvariant)]
    private static partial Regex IdComparisonPattern();
}
