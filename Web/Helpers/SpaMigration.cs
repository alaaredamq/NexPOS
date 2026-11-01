namespace Web.Helpers;

/// <summary>
/// Where each page lives in the React app (REACT_MIGRATION.md D7). Every nav page has a planned SPA path
/// (<see cref="PlannedPath"/>) that the SPA registers its route under; <see cref="Migrated"/> lists the pages
/// whose React version is finished - for those the Razor sidebar, the Ctrl+K palette and the session payload link
/// to the SPA instead of the Razor page. The Razor page itself keeps working until the cutover deletes it.
/// <para>
/// A module flips its page with one line in <see cref="Migrated"/> (keep it alphabetical to keep merges trivial):
/// <c>["Orders"] = "/app/orders",</c>
/// </para>
/// </summary>
public static class SpaMigration
{
    /// <summary>MVC controller name (as in <see cref="NavCatalog"/>) to SPA path. Case-insensitive.</summary>
    public static IReadOnlyDictionary<string, string> Migrated { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CashDrawer"] = "/app/cash-drawer",
            ["Dashboard"] = "/app/dashboard",
            ["Orders"] = "/app/orders",
            ["Products"] = "/app/products",
            ["Categories"] = "/app/categories",
            ["AuditLogs"] = "/app/audit-logs",
            ["Customers"] = "/app/customers",
            ["ProductDisplayOrder"] = "/app/product-display-order",
            ["Inventory"] = "/app/inventory",
            ["Notifications"] = "/app/notifications",
            ["Suppliers"] = "/app/suppliers",
            ["Receipts"] = "/app/receipts",
            ["Reports"] = "/app/reports",
            ["Settings"] = "/app/settings",
            ["Users"] = "/app/users",
        };

    public static bool IsMigrated(string controller) => Migrated.ContainsKey(controller);

    /// <summary>The SPA path of a migrated page, or false while the Razor page is still the live one.</summary>
    public static bool TryGetPath(string controller, out string path)
    {
        if (Migrated.TryGetValue(controller, out var found))
        {
            path = found;
            return true;
        }

        path = string.Empty;
        return false;
    }

    /// <summary>
    /// The path the SPA registers a page under, migrated or not (the shell shows a placeholder there until the module lands):
    /// the migrated path when there is one, otherwise <c>/app/</c> + the controller name in kebab-case
    /// (<c>CashDrawer</c> becomes <c>/app/cash-drawer</c>, <c>POS</c> becomes <c>/app/pos</c>).
    /// </summary>
    public static string PlannedPath(string controller)
        => TryGetPath(controller, out var path) ? path : "/app/" + Kebab(controller);

    /// <summary>The Razor page of a controller in the given language ("en" or "ar").</summary>
    public static string LegacyPath(string controller, string language) => $"/{language}/{controller}";

    /// <summary>Where a link to the page should go right now.</summary>
    public static string HrefFor(string controller, string language)
        => TryGetPath(controller, out var path) ? path : LegacyPath(controller, language);

    internal static string Kebab(string name)
    {
        var text = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            // A hyphen goes where lower case meets upper case ("CashDrawer"); an acronym ("POS") stays in one piece.
            if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1])) text.Append('-');
            text.Append(char.ToLowerInvariant(c));
        }

        return text.ToString();
    }
}
