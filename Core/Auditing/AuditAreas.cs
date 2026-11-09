using System.Linq.Expressions;
using System.Reflection;
using Core.Entities.Logging;

namespace Core.Auditing;

/// <summary>
/// The part of the product an audit entry belongs to, for the viewer's "section" filter. Unlike
/// <see cref="AuditCategory"/> (what kind of event: a data change, a request, a sign-in ...), this is about
/// <i>where</i> in the app it happened: sales, the till, stock, accounts ...
/// </summary>
public enum AuditArea
{
    Sales = 0,
    Pos = 1,
    Inventory = 2,
    Customers = 3,
    Account = 4,
    Settings = 5,
    Reports = 6,
    System = 7,

    /// <summary>Everything not claimed above (page loads of the app shell, unclassified errors ...).</summary>
    Other = 8
}

/// <summary>
/// Decides which <see cref="AuditArea"/> a row is in. Nothing is stored on the row: the area is worked out from the
/// action, the record type and (for HTTP requests) the path, so entries written before this existed are sorted too.
/// <see cref="Classify"/> (used for display) and <see cref="Matches"/> (used for filtering in SQL) are built from the
/// same table, so a row is always listed under the area it is labelled with.
/// </summary>
public static class AuditAreas
{
    private sealed record Rules(string[] Actions, string[] Entities, string[] Paths);

    // Actions win over record types (a failed sale blocked by the licence is a sales event, not a settings one);
    // record types win over paths. Paths apply to request rows only and are matched without regard to case.
    private static readonly Dictionary<AuditArea, Rules> Table = new()
    {
        [AuditArea.Sales] = new(
            [AuditActions.SaleCompleted, AuditActions.SaleFailed, AuditActions.ReceiptFailed, AuditActions.ReceiptVoided,
             AuditActions.OrderCancelled, AuditActions.SaleCustomerChanged],
            ["Order", "Receipt", "ReceiptItem", "ReceiptPayment", "Discount", "PaymentMethod", "Currency", "ExchangeRate", "Tax", "TaxRate"],
            ["/api/v1/orders", "/api/v1/receipts", "/api/v1/discounts", "/api/v1/payment-methods", "/api/v1/currencies",
             "/api/v1/exchange-rates", "/api/v1/taxes", "/app/orders", "/app/receipts", "/app/discounts",
             "/Orders", "/Receipts", "/Discounts", "/PaymentMethods", "/Currency", "/ExchangeRates", "/Tax"]),

        [AuditArea.Pos] = new(
            [],
            ["Cart", "CartItem", "CartSession", "CartSessionItem", "CustomerCart", "CashDrawerSession", "CashDrop"],
            ["/api/v1/pos", "/api/v1/carts", "/api/v1/cash-drawer", "/app/pos", "/app/cash-drawer", "/POS", "/Carts", "/CashDrawer"]),

        [AuditArea.Inventory] = new(
            [AuditActions.StockAdjusted, AuditActions.ProductsImported, AuditActions.ProductsExported, AuditActions.PurchaseReceived, AuditActions.PurchaseCancelled],
            ["Product", "ProductCategory", "ProductPrice", "ProductHistory", "ProductUnit", "MainScreenProduct", "ProductDisplayOrder", "Supplier", "SupplierDebtTransaction", "Purchase", "PurchaseItem"],
            ["/api/v1/products", "/api/v1/categories", "/api/v1/inventory", "/api/v1/suppliers", "/api/v1/purchases", "/api/v1/product-display-order",
             "/app/products", "/app/categories", "/app/inventory", "/app/suppliers", "/app/purchases", "/app/product-display-order",
             "/Products", "/Categories", "/Inventory", "/Suppliers", "/Purchases", "/ProductDisplayOrder", "/MainScreenProducts"]),

        [AuditArea.Customers] = new(
            [],
            ["Customer", "CustomerDebtTransaction"],
            ["/api/v1/customers", "/app/customers", "/Customers"]),

        [AuditArea.Account] = new(
            [AuditActions.Login, AuditActions.LoginFailed, AuditActions.Logout, AuditActions.PinChanged,
             AuditActions.PinChangeFailed, AuditActions.PasswordChanged, AuditActions.AccessDenied],
            ["User", "Role", "Permission", "RolePermission", "UserPermission", "UserSession", AuditActions.AuthEntity, "Cookies"],
            ["/api/v1/account", "/api/v1/auth", "/api/v1/users", "/api/v1/roles", "/api/v1/session",
             "/app/account", "/app/users", "/app/roles", "/app/login", "/Account", "/Auth", "/Users"]),

        [AuditArea.Settings] = new(
            [AuditActions.RecoverySlipShown, AuditActions.RecoverySlipFailed, AuditActions.SealingConfirmed, AuditActions.SealKeyRestored],
            ["AppSetting", "CodeList", "CodeListValue", "StoreSettings", "SystemSettings", "UrlRoute", "License", "OnlineSealing"],
            ["/api/v1/settings", "/api/v1/code-lists", "/api/v1/license", "/api/v1/online", "/app/settings", "/app/license",
             "/Settings", "/AppSettings", "/CodeLists", "/License", "/Branding"]),

        [AuditArea.Reports] = new(
            [AuditActions.PdfGenerated, AuditActions.Print, AuditActions.PrintFailed],
            ["PrintJob"],
            ["/api/v1/reports", "/api/v1/dashboard", "/app/reports", "/app/dashboard", "/Reports", "/Dashboard"]),

        [AuditArea.System] = new(
            [AuditActions.AppStarted, AuditActions.AppStopping, AuditActions.MigrationsApplied, AuditActions.SeedData,
             AuditActions.BackupCreated, AuditActions.BackupFailed, AuditActions.BackupRestored, AuditActions.BackupRestoreFailed,
             AuditActions.BackupDeleted, AuditActions.BackupCleanup, AuditActions.AuditPurged, AuditActions.AuditRecovered,
             AuditActions.AuditExported],
            ["Backup", "BackupRecord", "BackupEntry", "BackupSchedule", "Application", "Database", "Audit", "Notification", "Alert"],
            ["/api/v1/backups", "/api/v1/backup-schedules", "/api/v1/audit-logs", "/api/v1/notifications",
             "/app/backup", "/app/audit-logs", "/app/notifications", "/Backup", "/AuditLogs", "/Notifications", "/Alerts", "/Health"])
    };

    /// <summary>The areas a user can pick, in the order the viewer lists them (<see cref="AuditArea.Other"/> last).</summary>
    public static IReadOnlyList<AuditArea> All { get; } = System.Enum.GetValues<AuditArea>();

    /// <summary>The area of one entry, as <see cref="Matches"/> would sort it.</summary>
    public static AuditArea Classify(string? action, string? entityType, string? requestPath)
    {
        foreach (var (area, rules) in Table)
        {
            if (Has(rules.Actions, action)) return area;
        }

        foreach (var (area, rules) in Table)
        {
            if (Has(rules.Entities, entityType)) return area;
        }

        if (string.Equals(entityType, AuditActions.HttpEntity, StringComparison.OrdinalIgnoreCase) && requestPath is not null)
        {
            var path = AuditRequestRules.StripLanguage(requestPath);
            foreach (var (area, rules) in Table)
            {
                if (rules.Paths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return area;
            }
        }

        return AuditArea.Other;
    }

    /// <summary>The same test as <see cref="Classify"/>, as a condition the database can run.</summary>
    public static Expression<Func<AuditLog, bool>> Matches(AuditArea area)
    {
        var row = Expression.Parameter(typeof(AuditLog), "x");
        var body = area == AuditArea.Other
            ? Expression.Not(Table.Keys.Select(a => Condition(a, row)).Aggregate(Expression.OrElse))
            : Condition(area, row);
        return Expression.Lambda<Func<AuditLog, bool>>(body, row);
    }

    private static bool Has(string[] values, string? value)
        => value is not null && values.Contains(value, StringComparer.OrdinalIgnoreCase);

    // ---- the SQL form ------------------------------------------------------------------------------------------

    private static readonly MethodInfo ContainsMethod = typeof(Enumerable).GetMethods()
        .Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
        .MakeGenericMethod(typeof(string));

    private static readonly MethodInfo StartsWithMethod = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;

    private static Expression Condition(AuditArea area, ParameterExpression row)
    {
        var rules = Table[area];
        var action = Expression.Property(row, nameof(AuditLog.Action));
        var entityType = Expression.Property(row, nameof(AuditLog.EntityType));
        var path = Expression.Property(row, nameof(AuditLog.RequestPath));

        Expression In(string[] values, Expression column) => Expression.Call(ContainsMethod, Expression.Constant(values), column);

        // The action decides first. Failing that, the record type or the request path decides - but only when the
        // action does not already belong to another area.
        var otherActions = Table.Where(t => t.Key != area).SelectMany(t => t.Value.Actions).ToArray();

        // Request rows: the path, with or without a "/en" / "/ar" language segment.
        var isRequest = Expression.Equal(entityType, Expression.Constant(AuditActions.HttpEntity));
        var onPath = rules.Paths
            .SelectMany(p => new[] { p, "/en" + p, "/ar" + p })
            .Select(p => (Expression)Expression.Call(path, StartsWithMethod, Expression.Constant(p)))
            .Aggregate(Expression.OrElse);

        Expression byRecordOrPath = Expression.AndAlso(
            Expression.Not(In(otherActions, action)),
            Expression.OrElse(In(rules.Entities, entityType), Expression.AndAlso(isRequest, onPath)));

        return rules.Actions.Length > 0 ? Expression.OrElse(In(rules.Actions, action), byRecordOrPath) : byRecordOrPath;
    }
}
