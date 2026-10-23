using System.Globalization;
using System.Text;
using Core.Entities.Logging;

namespace Application.Services.Logging;

/// <summary>Writes audit rows as an Excel-friendly CSV (UTF-8 with BOM, one row per entry).</summary>
public static class AuditLogCsv
{
    private static readonly string[] Header =
    [
        "Id", "TimeUtc", "TimeLocal", "User", "Role", "Category", "Action", "Entity", "EntityId", "Label",
        "Description", "Outcome", "Severity", "ChangedFields", "OldValues", "NewValues", "Details",
        "IpAddress", "Machine", "Version", "RequestId", "Method", "Path", "Status", "DurationMs", "Source"
    ];

    public static async Task<int> WriteAsync(
        IAsyncEnumerable<AuditLog> rows, Stream output, CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true)
        {
            NewLine = "\r\n"
        };

        await writer.WriteLineAsync(string.Join(',', Header));

        var count = 0;
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            await writer.WriteLineAsync(ToLine(row));
            count++;
        }

        await writer.FlushAsync(cancellationToken);
        return count;
    }

    internal static string ToLine(AuditLog row)
    {
        var utc = DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc);

        return string.Join(',', new[]
        {
            Cell(row.Id.ToString(CultureInfo.InvariantCulture)),
            Cell(utc.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
            Cell(utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)),
            Cell(row.Username),
            Cell(row.UserRole),
            Cell(row.Category.ToString()),
            Cell(row.Action),
            Cell(row.EntityType),
            Cell(row.EntityId?.ToString(CultureInfo.InvariantCulture)),
            Cell(row.EntityLabel),
            Cell(row.Description),
            Cell(row.Outcome.ToString()),
            Cell(row.Severity.ToString()),
            Cell(row.ChangedFields),
            Cell(row.OldValuesJson),
            Cell(row.NewValuesJson),
            Cell(row.AdditionalInfoJson),
            Cell(row.IpAddress),
            Cell(row.MachineName),
            Cell(row.AppVersion),
            Cell(row.CorrelationId),
            Cell(row.HttpMethod),
            Cell(row.RequestPath),
            Cell(row.StatusCode?.ToString(CultureInfo.InvariantCulture)),
            Cell(row.DurationMs?.ToString(CultureInfo.InvariantCulture)),
            Cell(row.Source)
        });
    }

    /// <summary>
    /// Quotes a value for CSV and defuses spreadsheet formulas. Product names, labels and descriptions are
    /// typed by users, so a cell like <c>=HYPERLINK(...)</c> would otherwise run when support opens the export in Excel.
    /// </summary>
    internal static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        var needsQuotes = value.AsSpan().IndexOfAny(",\"\r\n") >= 0;
        return needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
