using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Sync;

public sealed record SyncTable(string Name, string KeyColumn, IReadOnlyList<string> Columns);

/// <summary>
/// Which tables and columns take part in the sync, derived from the EF model so a new entity is picked up
/// automatically. The cloud validates every incoming table and column name against the same list, so a
/// batch can never touch anything the till would not have sent.
/// </summary>
public sealed class SyncSchema
{
    /// <summary>
    /// Local-only state: live sessions and carts, backups (pointless in a mirror), notifications/alerts, routes,
    /// and settings tables that hold machine configuration (printer names, encrypted values).
    /// </summary>
    public static readonly IReadOnlySet<string> ExcludedTables = new HashSet<string>(StringComparer.Ordinal)
    {
        "UserSessions", "Backups", "BackupRecords", "BackupSchedules",
        "Notifications", "Alerts", "UrlRoutes",
        "Carts", "CartItems", "CartSessions", "CartSessionItems", "CustomerCarts",
        "SystemSettings", "AppSettings",
        // Registered till PCs and their token hashes: a shop's access control, never copied off the shop's network.
        "TillDevices",
    };

    /// <summary>Credentials never leave the shop. The mirror gets an empty placeholder where the column is NOT NULL.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> ExcludedColumns = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Users"] = ["PasswordHash", "PinHash"],
    };

    private readonly Dictionary<string, SyncTable> _tables;

    private SyncSchema(Dictionary<string, SyncTable> tables, IReadOnlyList<string> skipped)
    {
        _tables = tables;
        Skipped = skipped;
    }

    public IReadOnlyCollection<SyncTable> Tables => _tables.Values;

    /// <summary>Mapped tables left out because their key is not a single int (they cannot be addressed by id).</summary>
    public IReadOnlyList<string> Skipped { get; }

    public bool TryGetTable(string name, out SyncTable table) => _tables.TryGetValue(name, out table!);

    public static SyncSchema FromModel(IModel model)
    {
        var tables = new Dictionary<string, SyncTable>(StringComparer.Ordinal);
        var skipped = new List<string>();

        foreach (var entityType in model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (tableName is null || tables.ContainsKey(tableName) || ExcludedTables.Contains(tableName)) continue;

            var store = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
            var key = entityType.FindPrimaryKey();
            if (key is null || key.Properties.Count != 1 || key.Properties[0].ClrType != typeof(int))
            {
                skipped.Add(tableName);
                continue;
            }

            var keyColumn = key.Properties[0].GetColumnName(store)!;
            ExcludedColumns.TryGetValue(tableName, out var hidden);
            var columns = entityType.GetProperties()
                .Select(p => p.GetColumnName(store))
                .Where(c => c is not null && (hidden is null || !hidden.Contains(c)))
                .Select(c => c!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            tables[tableName] = new SyncTable(tableName, keyColumn, columns);
        }

        return new SyncSchema(tables, skipped);
    }

    /// <summary>Quotes an identifier for SQLite. Names come from the EF model (trusted), but quoting keeps keywords safe.</summary>
    public static string Q(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
