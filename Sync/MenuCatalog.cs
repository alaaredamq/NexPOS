using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Sync;

/// <summary>One product as the online menu shows it. Deliberately small: no cost, no stock, no barcode, no supplier.</summary>
/// <param name="SectionKey">Where it goes on the menu: <c>s12</c> = the till's online-menu section 12, <c>c5</c> = product category 5
/// (used while the till has no online-menu sections).</param>
/// <param name="SectionOrder">The section's place on the menu (only meaningful when <see cref="MenuCatalogPush.Arranged"/>).</param>
/// <param name="ItemOrder">The product's place in its section.</param>
public sealed record MenuCatalogItem(
    int ProductId, string Name, string? Description, decimal Price, string? Currency,
    string SectionKey, string? SectionName, int SectionOrder, int ItemOrder);

/// <summary>The shop's whole sellable list, with its fingerprint. Sent to <c>POST api/sync/menu-catalog</c>.</summary>
/// <param name="Arranged">True when the till's online-menu screen decides the sections and their order; false when the list simply
/// follows the product categories and the order is left to the menu editor.</param>
public sealed record MenuCatalogPush(string Hash, IReadOnlyList<MenuCatalogItem> Items, bool Arranged = false);

/// <summary>
/// Publishes the shop's product list to its online menu, when (and only when) the owner linked this branch to one. Everything else the
/// till sends is sealed so the provider cannot read it; this list is sent readable on purpose, because its only use is a public web page.
/// It carries the active products' names, selling prices, currencies and where they go on the menu, nothing else.
/// <para>
/// Where they go: when the till has online-menu sections (the "Online Menu" screen), exactly the products those sections hold, in their
/// order. A product picked on its own wins over its category being picked. With no sections at all, every active product, grouped by category.
/// </para>
/// </summary>
public static class MenuCatalog
{
    public const int MaxItems = 5000;
    public const string Route = "api/sync/menu-catalog";

    private const string Columns = "p.Id, p.Name, p.Description, p.SellingPrice, c.Code, c.Notation";

    public static MenuCatalogPush Build(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var arranged = HasSections(connection);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = arranged
            ? $"""
               SELECT {Columns}, 's' || s.Id, s.Name, s.SortOrder
               FROM Products p
               LEFT JOIN Currencies c ON c.Id = p.CurrencyId
               LEFT JOIN OnlineMenuEntries ep ON ep.ProductId = p.Id
               LEFT JOIN OnlineMenuEntries ec ON ec.CategoryId = p.CategoryId AND ep.Id IS NULL
               JOIN OnlineMenuSections s ON s.Id = COALESCE(ep.SectionId, ec.SectionId)
               WHERE p.IsActive = 1 AND p.IsDeleted = 0
               ORDER BY s.SortOrder, s.Id, COALESCE(ep.SortOrder, ec.SortOrder), p.Name, p.Id
               LIMIT $max
               """
            : $"""
               SELECT {Columns}, 'c' || p.CategoryId, pc.Name, 0
               FROM Products p
               LEFT JOIN Currencies c ON c.Id = p.CurrencyId
               LEFT JOIN ProductCategories pc ON pc.Id = p.CategoryId
               WHERE p.IsActive = 1 AND p.IsDeleted = 0
               ORDER BY p.Id
               LIMIT $max
               """;
        cmd.Parameters.AddWithValue("$max", MaxItems);

        var items = new List<MenuCatalogItem>();
        var positions = new Dictionary<string, int>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var code = reader.IsDBNull(4) ? null : reader.GetString(4);
                var notation = reader.IsDBNull(5) ? null : reader.GetString(5);
                var key = reader.GetString(6);
                var position = positions.GetValueOrDefault(key);
                positions[key] = position + 1;
                items.Add(new MenuCatalogItem(
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
                    reader.IsDBNull(2) ? null : NullIfBlank(reader.GetString(2)),
                    ReadDecimal(reader, 3),
                    CurrencyLabel(code, notation),
                    key,
                    reader.IsDBNull(7) ? null : NullIfBlank(reader.GetString(7)),
                    reader.GetInt32(8),
                    position));
            }
        }
        return new MenuCatalogPush(Fingerprint(items, arranged), items, arranged);
    }

    /// <summary>A till whose database predates the online-menu screen simply has no sections.</summary>
    private static bool HasSections(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'OnlineMenuSections')";
        if (Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 0) return false;
        cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM OnlineMenuSections)";
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    /// <summary>A stable fingerprint of the list: same products, prices and places, same hash, on any PC.</summary>
    public static string Fingerprint(IReadOnlyList<MenuCatalogItem> items, bool arranged = false)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new { arranged, items }, SyncWire.Json);
        return Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant();
    }

    /// <summary>"$" for dollars, otherwise the shop's own notation, otherwise the code.</summary>
    public static string? CurrencyLabel(string? code, string? notation)
    {
        if (string.Equals(code, "USD", StringComparison.OrdinalIgnoreCase)) return "$";
        if (!string.IsNullOrWhiteSpace(notation)) return notation.Trim();
        return NullIfBlank(code);
    }

    /// <summary>
    /// Sends the list if it differs from what the cloud holds. Returns the hash the cloud now holds (unchanged when nothing was sent),
    /// or null when the send failed, so the caller tries again later.
    /// </summary>
    public static async Task<string?> PublishIfChangedAsync(HttpClient http, string connectionString, string? cloudHash, CancellationToken ct)
    {
        var catalog = Build(connectionString);
        if (catalog.Hash == cloudHash) return cloudHash;
        using var response = await http.PostAsJsonAsync(Route, catalog, SyncWire.Json, ct);
        return response.IsSuccessStatusCode ? catalog.Hash : null;
    }

    private static decimal ReadDecimal(SqliteDataReader reader, int i)
    {
        if (reader.IsDBNull(i)) return 0;
        var value = reader.GetValue(i);
        return value switch
        {
            string s => decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0,
            double d => (decimal)d,
            long l => l,
            _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
        };
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
