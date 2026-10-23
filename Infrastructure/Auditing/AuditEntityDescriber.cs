using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Core.Auditing;
using Core.Entities.Base;
using Core.Entities.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Infrastructure.Auditing;

/// <summary>
/// Turns an EF change-tracker entry into audit data: which fields changed, their old/new values
/// (secrets redacted), a display label for the record and a one-line description. Pure logic with no
/// I/O apart from the optional "read the real row" fallback, so it is covered directly by unit tests.
/// </summary>
internal static class AuditEntityDescriber
{
    private static readonly string[] LabelProperties =
    [
        "Name", "Title", "Username", "ReceiptNumber", "OrderNumber", "Barcode", "SettingKey", "Key",
        "Code", "FileName", "ProductName", "Email"
    ];

    // Settings tables store "name -> value" rows, so a secret has an innocent-looking column name
    // ("SettingValue") and the secret-ness sits in the *key* ("SmtpPassword") or an IsEncrypted flag.
    private static readonly string[] KeyProperties = ["SettingKey", "Name", "Key"];
    private static readonly string[] ValueProperties = ["SettingValue", "Value"];

    // Bookkeeping that changes on every save and says nothing about what happened.
    private static readonly HashSet<string> NoiseProperties = new(StringComparer.Ordinal) { "UpdatedAt" };

    private static readonly ConcurrentDictionary<IProperty, PropertyPolicy> Policies = new();

    private readonly record struct PropertyPolicy(bool Ignore, bool Sensitive);

    /// <summary>What happened to one tracked entity in one SaveChanges call.</summary>
    internal sealed class PendingChange
    {
        public required EntityEntry Entry { get; init; }
        public required string EntityType { get; init; }
        public required string Action { get; set; }
        public int? EntityId { get; set; }
        public string? CompositeKey { get; set; }
        public string? Label { get; set; }
        public Dictionary<string, object?>? Old { get; set; }
        public Dictionary<string, object?>? New { get; set; }
        public List<string> Changed { get; } = [];
        public bool IsAdded => Action == AuditActions.Create;
    }

    /// <summary>Inspects one entry before SaveChanges; null when there is nothing worth recording.</summary>
    public static PendingChange? Inspect(EntityEntry entry)
    {
        if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            return null;

        var change = new PendingChange
        {
            Entry = entry,
            EntityType = entry.Metadata.ShortName(),
            Action = entry.State switch
            {
                EntityState.Added => AuditActions.Create,
                EntityState.Deleted => AuditActions.Delete,
                _ => AuditActions.Update
            }
        };

        if (entry.State == EntityState.Modified)
        {
            ComputeDiff(entry, change);
            if (change.Changed.Count == 0)
                return null; // a "touch" with nothing to report

            ClassifySoftDelete(change);
        }

        if (entry.State != EntityState.Added)
        {
            // Key and label are read now: after SaveChanges a deleted entity is detached.
            AssignKey(entry, change);
            change.Label = GetLabel(entry);
        }

        if (entry.State == EntityState.Deleted)
        {
            change.Old = ReadValues(entry, original: true, includeNulls: false);
        }

        return change;
    }

    /// <summary>Completes a change after SaveChanges: generated keys exist now and added rows can be read in full.</summary>
    public static AuditLog ToAuditLog(PendingChange change)
    {
        if (change.IsAdded)
        {
            AssignKey(change.Entry, change);
            change.Label = GetLabel(change.Entry);
            change.New = ReadValues(change.Entry, original: false, includeNulls: false);
        }

        return new AuditLog
        {
            Category = AuditCategory.Data,
            Action = change.Action,
            EntityType = change.EntityType,
            EntityId = change.EntityId,
            EntityLabel = AuditRedactor.Truncate(change.Label, 200),
            Description = Describe(change),
            ChangedFields = change.Changed.Count > 0 ? string.Join(", ", change.Changed) : null,
            OldValuesJson = ToJson(change.Old),
            NewValuesJson = ToJson(change.New),
            AdditionalInfoJson = change.CompositeKey is null
                ? null
                : AuditRedactor.SerializeDetails(new { key = change.CompositeKey })
        };
    }

    // ---- diff ---------------------------------------------------------------------------------------------------

    private static void ComputeDiff(EntityEntry entry, PendingChange change)
    {
        var old = new Dictionary<string, object?>();
        var current = new Dictionary<string, object?>();
        var redactValues = ShouldRedactValueProperty(entry);

        foreach (var property in entry.Properties)
        {
            var policy = PolicyFor(property.Metadata);
            if (policy.Ignore || property.Metadata.IsPrimaryKey() || NoiseProperties.Contains(property.Metadata.Name))
                continue;

            if (ValuesEqual(property.OriginalValue, property.CurrentValue))
                continue;

            Record(change, old, current, property.Metadata, property.OriginalValue, property.CurrentValue,
                policy.Sensitive || (redactValues && IsValueProperty(property.Metadata.Name)));
        }

        if (change.Changed.Count == 0)
        {
            // Entities saved through Update(detached) arrive with original == current for every column,
            // so the diff above is empty even though the caller changed things. Read the stored row and
            // compare against that, so the trail still shows what really changed.
            DiffAgainstDatabase(entry, change, old, current, redactValues);
        }

        if (change.Changed.Count > 0)
        {
            change.Old = old;
            change.New = current;
        }
    }

    private static void DiffAgainstDatabase(
        EntityEntry entry, PendingChange change, Dictionary<string, object?> old,
        Dictionary<string, object?> current, bool redactValues)
    {
        PropertyValues? stored;
        try
        {
            stored = entry.GetDatabaseValues();
        }
        catch (Exception)
        {
            return; // the row can't be read (locked, gone) - nothing trustworthy to compare with
        }

        if (stored is null) return;

        foreach (var property in entry.Properties)
        {
            var policy = PolicyFor(property.Metadata);
            if (policy.Ignore || property.Metadata.IsPrimaryKey() || NoiseProperties.Contains(property.Metadata.Name))
                continue;

            var before = stored[property.Metadata];
            if (ValuesEqual(before, property.CurrentValue))
                continue;

            Record(change, old, current, property.Metadata, before, property.CurrentValue,
                policy.Sensitive || (redactValues && IsValueProperty(property.Metadata.Name)));
        }
    }

    private static void Record(
        PendingChange change, Dictionary<string, object?> old, Dictionary<string, object?> current,
        IProperty property, object? before, object? after, bool redact)
    {
        change.Changed.Add(property.Name);
        old[property.Name] = redact ? AuditRedactor.Redacted : FormatValue(before);
        current[property.Name] = redact ? AuditRedactor.Redacted : FormatValue(after);
    }

    private static void ClassifySoftDelete(PendingChange change)
    {
        if (change.Entry.Entity is not ISSoftDeletable) return;
        if (change.Old is null || change.New is null) return;
        if (!change.Old.TryGetValue(nameof(ISSoftDeletable.IsDeleted), out var before) ||
            !change.New.TryGetValue(nameof(ISSoftDeletable.IsDeleted), out var after))
            return;

        if (before is false && after is true) change.Action = AuditActions.SoftDelete;
        else if (before is true && after is false) change.Action = AuditActions.Restore;
    }

    // ---- full-row snapshots -------------------------------------------------------------------------------------

    private static Dictionary<string, object?> ReadValues(EntityEntry entry, bool original, bool includeNulls)
    {
        var values = new Dictionary<string, object?>();
        var redactValues = ShouldRedactValueProperty(entry);

        foreach (var property in entry.Properties)
        {
            var policy = PolicyFor(property.Metadata);
            if (policy.Ignore) continue;

            var raw = original ? property.OriginalValue : property.CurrentValue;
            if (raw is null && !includeNulls) continue;

            var sensitive = policy.Sensitive || (redactValues && IsValueProperty(property.Metadata.Name));
            values[property.Metadata.Name] = sensitive && raw is not null ? AuditRedactor.Redacted : FormatValue(raw);
        }

        return values;
    }

    // ---- keys & labels ------------------------------------------------------------------------------------------

    private static void AssignKey(EntityEntry entry, PendingChange change)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return;

        if (key.Properties.Count == 1 && key.Properties[0].ClrType == typeof(int))
        {
            var id = entry.Property(key.Properties[0].Name).CurrentValue;
            change.EntityId = id is int value && value != 0 ? value : null;
            return;
        }

        change.CompositeKey = string.Join("/", key.Properties.Select(p => $"{p.Name}={entry.Property(p.Name).CurrentValue}"));
    }

    internal static string? GetLabel(EntityEntry entry)
    {
        foreach (var name in LabelProperties)
        {
            var value = ReadString(entry, name);
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }

        var first = ReadString(entry, "FirstName");
        var last = ReadString(entry, "LastName");
        var full = $"{first} {last}".Trim();
        return full.Length > 0 ? full : null;
    }

    private static string? ReadString(EntityEntry entry, string propertyName)
    {
        if (entry.Metadata.FindProperty(propertyName) is not { ClrType: var type } || type != typeof(string))
            return null;

        return entry.Property(propertyName).CurrentValue as string;
    }

    // ---- text ---------------------------------------------------------------------------------------------------

    internal static string Describe(PendingChange change)
    {
        var who = change.Label is { Length: > 0 }
            ? $"'{AuditRedactor.Truncate(change.Label, 80)}'{IdSuffix(change)}"
            : IdSuffix(change).Trim();
        var subject = $"{change.EntityType} {who}".TrimEnd();

        return change.Action switch
        {
            AuditActions.Create => $"Created {subject}",
            AuditActions.Delete => $"Deleted {subject}",
            AuditActions.SoftDelete => $"Deleted (soft) {subject}",
            AuditActions.Restore => $"Restored {subject}",
            _ => $"Updated {subject}: {SummariseChanges(change)}"
        };
    }

    private static string IdSuffix(PendingChange change)
        => change.EntityId is { } id ? $" (#{id})" : change.CompositeKey is { } key ? $" ({key})" : string.Empty;

    private static string SummariseChanges(PendingChange change)
    {
        const int shown = 4;
        var parts = change.Changed.Take(shown).Select(name =>
        {
            var before = change.Old?.GetValueOrDefault(name);
            var after = change.New?.GetValueOrDefault(name);
            return $"{name} {Short(before)} → {Short(after)}";
        });

        var text = string.Join("; ", parts);
        return change.Changed.Count > shown ? $"{text}; +{change.Changed.Count - shown} more" : text;
    }

    private static string Short(object? value)
        => value is null ? "∅" : AuditRedactor.Truncate(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), 40) ?? "∅";

    // ---- values & policy ----------------------------------------------------------------------------------------

    internal static object? FormatValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                return AuditRedactor.Truncate(text);
            case bool or byte or sbyte or short or ushort or int or uint or long or ulong or decimal:
                return value;
            case float f:
                return float.IsFinite(f) ? f : f.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case double d:
                return double.IsFinite(d) ? d : d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case DateTime dt:
                // No zone designator, on purpose: SQLite hands every date back as "unspecified kind" while a value just
                // assigned in code is tagged UTC, so marking only the latter made a diff look like "X → Y Z".
                // The application stores its timestamps in UTC, so that is what these values are.
                return dt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
            case DateTimeOffset dto:
                return dto.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            case DateOnly date:
                return date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            case TimeOnly time:
                return time.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            case TimeSpan span:
                return span.ToString("c", System.Globalization.CultureInfo.InvariantCulture);
            case Guid guid:
                return guid.ToString();
            case Enum e:
                return e.ToString();
            case byte[] bytes:
                return $"[binary {bytes.Length} bytes]";
            default:
                return AuditRedactor.Truncate(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is byte[] x && b is byte[] y) return x.AsSpan().SequenceEqual(y);
        return Equals(a, b);
    }

    private static PropertyPolicy PolicyFor(IProperty property)
        => Policies.GetOrAdd(property, static p =>
        {
            var info = p.PropertyInfo;
            var ignore = info?.GetCustomAttribute<AuditIgnoreAttribute>() is not null;
            var sensitive = info?.GetCustomAttribute<AuditSensitiveAttribute>() is not null ||
                            AuditRedactor.IsSensitiveName(p.Name);
            return new PropertyPolicy(ignore, sensitive);
        });

    /// <summary>True when this row is a settings entry whose *value* must be hidden (secret-looking key, or marked encrypted).</summary>
    private static bool ShouldRedactValueProperty(EntityEntry entry)
    {
        if (!ValueProperties.Any(name => entry.Metadata.FindProperty(name) is not null))
            return false;

        if (entry.Metadata.FindProperty("IsEncrypted") is not null &&
            entry.Property("IsEncrypted").CurrentValue is true)
            return true;

        foreach (var name in KeyProperties)
        {
            if (entry.Metadata.FindProperty(name) is { ClrType: var type } && type == typeof(string) &&
                AuditRedactor.IsSensitiveName(entry.Property(name).CurrentValue as string))
                return true;
        }

        return false;
    }

    private static bool IsValueProperty(string name) => ValueProperties.Contains(name);

    private static string? ToJson(Dictionary<string, object?>? values)
        => values is null || values.Count == 0 ? null : AuditRedactor.Cap(JsonSerializer.Serialize(values));
}
