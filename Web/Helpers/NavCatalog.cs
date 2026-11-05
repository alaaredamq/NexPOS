namespace Web.Helpers;

/// <summary>
/// One page a user can navigate to. <see cref="Permissions"/> is any-of; empty means every signed-in user.
/// <see cref="AlsoActiveOn"/> lists extra controllers that should light this entry up in the rail.
/// </summary>
public sealed record NavPage(
    string Controller,
    string LabelKey,
    string Icon,
    NavSection Section,
    string[] Permissions,
    string[]? AlsoActiveOn = null)
{
    public string Action => "Index";

    public bool IsActiveFor(string? controller) =>
        string.Equals(controller, Controller, StringComparison.OrdinalIgnoreCase)
        || (AlsoActiveOn?.Contains(controller, StringComparer.OrdinalIgnoreCase) ?? false);
}

public enum NavSection { Sales, Catalog, Insights, Admin }

/// <summary>
/// Every page in the app, in rail order. The sidebar and the Ctrl+K jump-to palette both render from this
/// list, filtered to what the signed-in user's role grants (the grants themselves are the Permission rows
/// kept in step by RoleAccessService), so a user can never be offered a page that would bounce them to
/// "access denied". Adding a page means adding it here and putting the same permission on its controller.
/// </summary>
public static class NavCatalog
{
    public static IReadOnlyList<NavSection> Sections { get; } =
        new[] { NavSection.Sales, NavSection.Catalog, NavSection.Insights, NavSection.Admin };

    public static string SectionLabelKey(NavSection section) => section switch
    {
        NavSection.Sales => "NavSectionSales",
        NavSection.Catalog => "NavSectionCatalog",
        NavSection.Insights => "NavSectionInsights",
        _ => "NavSectionAdmin",
    };

    private static string[] P(params string[] permissions) => permissions;

    public static IReadOnlyList<NavPage> All { get; } = new[]
    {
        // Sales
        new NavPage("POS", "PointOfSale", "fa-cash-register", NavSection.Sales, P(), new[] { "Home" }),
        new NavPage("Dashboard", "Dashboard", "fa-tachometer-alt", NavSection.Sales, P(PermissionHelper.Permissions.Dashboard_View)),
        new NavPage("Orders", "Orders", "fa-shopping-cart", NavSection.Sales, P(PermissionHelper.Permissions.Orders_View)),
        new NavPage("CashDrawer", "CashDrawer", "fa-vault", NavSection.Sales, P(PermissionHelper.Permissions.Sales_View)),
        new NavPage("Receipts", "Receipts", "fa-receipt", NavSection.Sales, P(PermissionHelper.Permissions.Sales_View)),

        // Catalog & people
        new NavPage("Products", "Products", "fa-box", NavSection.Catalog, P(PermissionHelper.Permissions.Products_View)),
        new NavPage("Categories", "Categories", "fa-tags", NavSection.Catalog, P(PermissionHelper.Permissions.Categories_View)),
        new NavPage("ProductDisplayOrder", "ProductDisplayOrder", "fa-sort-amount-down", NavSection.Catalog, P(PermissionHelper.Permissions.MainScreenProducts_Manage)),
        new NavPage("Inventory", "Inventory", "fa-warehouse", NavSection.Catalog, P(PermissionHelper.Permissions.Inventory_View)),
        new NavPage("Suppliers", "Suppliers", "fa-truck", NavSection.Catalog, P(PermissionHelper.Permissions.Suppliers_View)),
        new NavPage("Purchases", "Purchases", "fa-file-invoice", NavSection.Catalog, P(PermissionHelper.Permissions.Purchases_View)),
        new NavPage("Customers", "Customers", "fa-users", NavSection.Catalog, P(PermissionHelper.Permissions.Customers_View)),

        // Insights
        new NavPage("Reports", "Reports", "fa-chart-bar", NavSection.Insights, P(PermissionHelper.Permissions.Reports_View)),
        new NavPage("Notifications", "Notifications", "fa-bell", NavSection.Insights, P()),
        new NavPage("Backup", "Backup", "fa-database", NavSection.Insights, P(PermissionHelper.Permissions.Backup_View)),

        // Administration
        new NavPage("Users", "Users", "fa-user-cog", NavSection.Admin, P(PermissionHelper.Permissions.Users_View)),
        new NavPage("Currency", "Currency", "fa-coins", NavSection.Admin, P(PermissionHelper.Permissions.Currency_View)),
        new NavPage("Tax", "Tax", "fa-percentage", NavSection.Admin, P(PermissionHelper.Permissions.Tax_View)),
        new NavPage("Settings", "Settings", "fa-cog", NavSection.Admin, P(PermissionHelper.Permissions.Settings_Manage)),
        new NavPage("License", "License_Title", "fa-key", NavSection.Admin, P(PermissionHelper.Permissions.License_Manage)),
        new NavPage("AuditLogs", "AuditLog", "fa-clipboard-list", NavSection.Admin, P(PermissionHelper.Permissions.AuditLogs_View)),
    };

    /// <summary>The pages this user may open, in rail order.</summary>
    public static List<NavPage> VisibleTo(System.Security.Claims.ClaimsPrincipal user) =>
        All.Where(p => p.Permissions.Length == 0 || PermissionHelper.HasAnyPermission(user, p.Permissions)).ToList();
}
