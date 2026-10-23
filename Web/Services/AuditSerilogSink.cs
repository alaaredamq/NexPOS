using Core.Auditing;
using Core.Entities.Logging;
using Serilog.Core;
using Serilog.Events;

namespace Web.Services;

/// <summary>
/// Copies every warning and error the application logs into the audit trail, stamped with the user and
/// request that were active when it happened. Much of this codebase catches an exception, logs it and
/// answers HTTP 200 <c>{success:false}</c>; to the request log that looks like a success, so without
/// this the failure would only exist in a text file nobody can tie to a person or a click.
/// </summary>
public sealed class AuditSerilogSink : ILogEventSink
{
    // Anything the audit machinery itself logs must not loop back into the audit machinery.
    private static readonly string[] OwnSources =
    [
        "Infrastructure.Auditing", "Application.Services.Logging", "Web.Middlewares.AuditRequestMiddleware",
        "Web.Services.AuditSerilogSink"
    ];

    // Property noise that Serilog/ASP.NET attach to every event.
    private static readonly HashSet<string> SkippedProperties = new(StringComparer.Ordinal)
    {
        "SourceContext", "RequestId", "RequestPath", "ConnectionId", "ActionId", "ActionName", "EventId",
        "SpanId", "TraceId", "ParentId", "ThreadId"
    };

    [ThreadStatic] private static bool _emitting;

    private readonly IServiceProvider _services;
    private IAuditTrail? _trail;

    // The logger is configured before the host (and so the trail) exists, hence the lazy lookup.
    public AuditSerilogSink(IServiceProvider services)
    {
        _services = services;
    }

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Warning || _emitting) return;

        var source = SourceOf(logEvent);
        if (OwnSources.Any(own => source.StartsWith(own, StringComparison.Ordinal))) return;

        // Framework warnings are chatter, not something a user did; framework *errors* are kept.
        if (logEvent.Level == LogEventLevel.Warning && IsFramework(source)) return;

        _emitting = true;
        try
        {
            _trail ??= (IAuditTrail?)_services.GetService(typeof(IAuditTrail));
            _trail?.Record(ToEntry(logEvent, source));
        }
        catch (Exception)
        {
            // A logging sink must never throw into the code that logged.
        }
        finally
        {
            _emitting = false;
        }
    }

    private static AuditLog ToEntry(LogEvent logEvent, string source)
    {
        var message = logEvent.RenderMessage();
        var exception = logEvent.Exception;
        var description = exception is null
            ? message
            : $"{message} – {exception.GetType().Name}: {exception.Message}";

        var isError = logEvent.Level >= LogEventLevel.Error;

        return new AuditLog
        {
            CreatedAt = logEvent.Timestamp.UtcDateTime,
            Category = AuditCategory.Diagnostic,
            Action = logEvent.Level switch
            {
                LogEventLevel.Warning => AuditActions.Warning,
                LogEventLevel.Error => AuditActions.Error,
                _ => AuditActions.Critical
            },
            EntityType = ShortName(source),
            Description = AuditRedactor.Truncate(description, 1000),
            Outcome = isError ? AuditOutcome.Failure : AuditOutcome.Success,
            Severity = logEvent.Level switch
            {
                LogEventLevel.Warning => AuditLogSeverity.Warning,
                LogEventLevel.Error => AuditLogSeverity.Error,
                _ => AuditLogSeverity.Critical
            },
            AdditionalInfoJson = AuditRedactor.SerializeDetails(new
            {
                logger = source,
                messageTemplate = logEvent.MessageTemplate.Text,
                exception = exception is null
                    ? null
                    : new
                    {
                        type = exception.GetType().FullName,
                        message = AuditRedactor.Truncate(exception.Message, 1000),
                        stackTrace = AuditRedactor.Truncate(exception.ToString(), 6000)
                    },
                properties = logEvent.Properties
                    .Where(p => !SkippedProperties.Contains(p.Key))
                    .Take(12)
                    .ToDictionary(p => p.Key, p => AuditRedactor.Truncate(p.Value.ToString().Trim('"'), 200))
            })
        };
    }

    private static string SourceOf(LogEvent logEvent)
        => logEvent.Properties.TryGetValue("SourceContext", out var value)
            ? value.ToString().Trim('"')
            : string.Empty;

    private static bool IsFramework(string source)
        => source.StartsWith("Microsoft.", StringComparison.Ordinal) ||
           source.StartsWith("System.", StringComparison.Ordinal) ||
           source.StartsWith("Serilog", StringComparison.Ordinal);

    private static string ShortName(string source)
    {
        if (string.IsNullOrEmpty(source)) return "Application";
        var name = source[(source.LastIndexOf('.') + 1)..];
        return name.Length > 0 ? name : source;
    }
}
