namespace Sync;

/// <summary>
/// Which synced tables the cloud may read. Everything the cloud does not strictly need is sealed: the shop PC encrypts
/// it with a key only the shop (and the owner's linked phones) hold, so the provider cannot read it.
/// See docs/online/PRIVACY-KEYS.md.
/// <para>
/// A table is sealed unless it is in <see cref="Readable"/>, so a table added later is private by default. The
/// <see cref="Sealed"/> list repeats that choice explicitly on purpose: a test fails until every table of the model is
/// in exactly one of the two lists, which forces a decision when someone adds a table.
/// </para>
/// </summary>
public static class SealPolicy
{
    /// <summary>The cloud reads these: enough for the owner to change prices from a phone later.</summary>
    public static readonly IReadOnlySet<string> Readable = new HashSet<string>(StringComparer.Ordinal)
    {
        "Products", "ProductCategories", "ProductPrices", "MainScreenProducts", "ProductDisplayOrders",
        "Currencies", "ExchangeRates", "Taxes", "TaxRates", "Discounts", "PaymentMethods",
        "CodeLists", "CodeListValues",
    };

    /// <summary>Sealed on purpose. Unlisted tables are sealed too (see the class remarks).</summary>
    public static readonly IReadOnlySet<string> Sealed = new HashSet<string>(StringComparer.Ordinal)
    {
        "Customers", "CustomerDebtTransactions", "Suppliers", "SupplierDebtTransactions",
        "Purchases", "PurchaseItems", "Orders", "Receipts", "ReceiptItems", "ReceiptPayments",
        "CashDrawerSession", "CashDrop",
        "ProductHistories", "StoreSettings",
        "Users", "Roles", "Permissions", "RolePermissions", "UserPermissions",
    };

    /// <summary>Columns sealed inside a readable table. They travel in the sealed part of the change, never in its values.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> SealedColumns = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Products"] = ["CostPrice"], // shows the shop's margin
    };

    /// <summary>The whole row is sealed: nothing of it travels in the clear.</summary>
    public static bool IsFullySealed(string table) => !Readable.Contains(table);

    /// <summary>Some of the row is sealed: a fully sealed table, or a readable one with sealed columns.</summary>
    public static bool HasSealedPart(string table) => IsFullySealed(table) || SealedColumns.ContainsKey(table);

    public static IReadOnlyList<string> SealedColumnsOf(string table)
        => SealedColumns.TryGetValue(table, out var columns) ? columns : [];

    /// <summary>
    /// Splits a row into what travels in the clear and what is sealed. A fully sealed table sends no values at all.
    /// </summary>
    public static (Dictionary<string, object?>? Open, Dictionary<string, object?>? Secret) Split(string table, Dictionary<string, object?> row)
    {
        if (IsFullySealed(table)) return (null, row);
        var hidden = SealedColumnsOf(table);
        if (hidden.Count == 0) return (row, null);

        var open = new Dictionary<string, object?>(row.Count, StringComparer.Ordinal);
        var secret = new Dictionary<string, object?>(hidden.Count, StringComparer.Ordinal);
        foreach (var (name, value) in row)
            (hidden.Contains(name) ? secret : open)[name] = value;
        return (open, secret);
    }
}
