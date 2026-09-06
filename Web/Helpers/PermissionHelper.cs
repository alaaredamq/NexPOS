using System.Reflection;
using System.Security.Claims;

namespace Web.Helpers;

/// <summary>
/// Helper class for managing permissions and roles
/// </summary>
public static class PermissionHelper
{
    /// <summary>
    /// Common system permissions
    /// </summary>
    public static class Permissions
    {
        // User Management
        public const string Users_View = "Users.View";
        public const string Users_Create = "Users.Create";
        public const string Users_Edit = "Users.Edit";
        public const string Users_Delete = "Users.Delete";
        public const string Users_Manage = "Users.Manage";

        // Product Management
        public const string Products_View = "Products.View";
        public const string Products_Create = "Products.Create";
        public const string Products_Edit = "Products.Edit";
        public const string Products_Delete = "Products.Delete";
        public const string Products_Manage = "Products.Manage";

        // Category Management
        public const string Categories_View = "Categories.View";
        public const string Categories_Create = "Categories.Create";
        public const string Categories_Edit = "Categories.Edit";
        public const string Categories_Delete = "Categories.Delete";
        public const string Categories_Manage = "Categories.Manage";

        // Customer Management
        public const string Customers_View = "Customers.View";
        public const string Customers_Create = "Customers.Create";
        public const string Customers_Edit = "Customers.Edit";
        public const string Customers_Delete = "Customers.Delete";
        public const string Customers_Manage = "Customers.Manage";

        // Supplier Management
        public const string Suppliers_View = "Suppliers.View";
        public const string Suppliers_Create = "Suppliers.Create";
        public const string Suppliers_Edit = "Suppliers.Edit";
        public const string Suppliers_Delete = "Suppliers.Delete";
        public const string Suppliers_Manage = "Suppliers.Manage";

        // Sales Management
        public const string Sales_View = "Sales.View";
        public const string Sales_Create = "Sales.Create";
        public const string Sales_Edit = "Sales.Edit";
        public const string Sales_Delete = "Sales.Delete";
        public const string Sales_Manage = "Sales.Manage";

        // Orders Management
        public const string Orders_View = "Orders.View";
        public const string Orders_Create = "Orders.Create";
        public const string Orders_Edit = "Orders.Edit";
        public const string Orders_Delete = "Orders.Delete";
        public const string Orders_Manage = "Orders.Manage";

        // Inventory Management
        public const string Inventory_View = "Inventory.View";
        public const string Inventory_Edit = "Inventory.Edit";
        public const string Inventory_Adjust = "Inventory.Adjust";
        public const string Inventory_Reports = "Inventory.Reports";
        public const string Inventory_Manage = "Inventory.Manage";

        // Reports
        public const string Reports_View = "Reports.View";
        public const string Reports_Export = "Reports.Export";
        public const string Reports_Manage = "Reports.Manage";

        // Settings
        public const string Settings_View = "Settings.View";
        public const string Settings_Edit = "Settings.Edit";
        public const string Settings_Manage = "Settings.Manage";

        // Dashboard
        public const string Dashboard_View = "Dashboard.View";
        public const string Dashboard_Manage = "Dashboard.Manage";

        // POS
        public const string POS_View = "POS.View";
        public const string POS_Manage = "POS.Manage";

        // POS
        public const string MainScreenProducts_Manage = "MainScreenProducts.Manage";

        // Backup Management
        public const string Backup_View = "Backup.View";
        public const string Backup_Create = "Backup.Create";
        public const string Backup_Restore = "Backup.Restore";
        public const string Backup_Manage = "Backup.Manage";

        // Notifications
        public const string Notifications_View = "Notifications.View";
        public const string Notifications_Manage = "Notifications.Manage";

        // Currency Management
        public const string Currency_View = "Currency.View";
        public const string Currency_Create = "Currency.Create";
        public const string Currency_Edit = "Currency.Edit";
        public const string Currency_Delete = "Currency.Delete";
        public const string Currency_Manage = "Currency.Manage";

        // Receipt Management
        public const string Receipts_View = "Receipts.View";
        public const string Receipts_Create = "Receipts.Create";
        public const string Receipts_Edit = "Receipts.Edit";
        public const string Receipts_Delete = "Receipts.Delete";
        public const string Receipts_Print = "Receipts.Print";

        // Tax Management
        public const string Tax_View = "Tax.View";
        public const string Tax_Create = "Tax.Create";
        public const string Tax_Edit = "Tax.Edit";
        public const string Tax_Delete = "Tax.Delete";
        public const string Tax_Manage = "Tax.Manage";

        // Discount Management
        public const string Discounts_View = "Discounts.View";
        public const string Discounts_Manage = "Discounts.Manage";

        // Payment Method Management
        public const string PaymentMethods_View = "PaymentMethods.View";
        public const string PaymentMethods_Manage = "PaymentMethods.Manage";

        // Alerts
        public const string Alerts_View = "Alerts.Read";

        // Audit Logs
        public const string AuditLogs_View = "AuditLogs.Read";

        /// <summary>
        /// Every permission constant declared above. Used to grant SuperAdmin everything
        /// without needing to keep a second list in sync as permissions are added.
        /// </summary>
        public static string[] All => typeof(Permissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToArray();
    }

    /// <summary>
    /// Common system roles
    /// </summary>
    public static class Roles
    {
        public const string SuperAdmin = "SuperAdmin";
        public const string Admin = "Admin";
        public const string Manager = "Manager";
        public const string Cashier = "Cashier";
        public const string Employee = "Employee";
        public const string Viewer = "Viewer";
    }

    /// <summary>
    /// Role to permissions mapping
    /// </summary>
    public static readonly Dictionary<string, string[]> RolePermissions = new()
    {
        // SuperAdmin bypasses the maintained per-role lists below and always gets every
        // permission that exists, so it can never be locked out by an omission there.
        [Roles.SuperAdmin] = Permissions.All,
        [Roles.Admin] = new[]
        {
            // All permissions
            Permissions.Users_View,
            Permissions.Users_Delete,
            Permissions.Orders_View,
            Permissions.Dashboard_View,
            Permissions.POS_View,
            Permissions.Products_View,
            Permissions.Categories_View,
            Permissions.Customers_View,
            Permissions.Inventory_View,
            Permissions.Users_Manage,
            Permissions.Products_Manage,
            Permissions.Categories_Manage,
            Permissions.Customers_Manage,
            Permissions.Suppliers_Manage,
            Permissions.Sales_Manage,
            Permissions.Orders_Manage,
            Permissions.Inventory_Edit,
            Permissions.Inventory_Adjust,
            Permissions.Inventory_Reports,
            Permissions.Inventory_Manage,
            Permissions.Reports_Manage,
            Permissions.Settings_Manage,
            Permissions.Dashboard_Manage,
            Permissions.POS_Manage,
            Permissions.MainScreenProducts_Manage,
            Permissions.Customers_Create,
            Permissions.Categories_Edit,
            Permissions.Customers_Edit,
            Permissions.Inventory_Edit,
            Permissions.Orders_Edit,
            Permissions.Products_Edit,
            Permissions.Products_Create,
            Permissions.Categories_Create,
            Permissions.Orders_Create,
            Permissions.Sales_Create,
            Permissions.Suppliers_Create,
            Permissions.Users_Create,
            Permissions.Sales_Edit,
            Permissions.Settings_Edit,
            Permissions.Suppliers_Edit,
            Permissions.Suppliers_View,
            Permissions.Users_Edit,
            Permissions.Reports_Export,
            Permissions.Reports_View,
            Permissions.Backup_View,
            Permissions.Backup_Create,
            Permissions.Backup_Restore,
            Permissions.Backup_Manage,
            Permissions.Notifications_View,
            Permissions.Notifications_Manage,
            Permissions.Currency_View,
            Permissions.Currency_Create,
            Permissions.Currency_Edit,
            Permissions.Currency_Delete,
            Permissions.Currency_Manage,
            Permissions.Receipts_View,
            Permissions.Receipts_Create,
            Permissions.Receipts_Edit,
            Permissions.Receipts_Delete,
            Permissions.Receipts_Print,
            Permissions.Tax_View,
            Permissions.Tax_Create,
            Permissions.Tax_Edit,
            Permissions.Tax_Delete,
            Permissions.Tax_Manage,
            Permissions.Sales_View,
            Permissions.Sales_Create,
            Permissions.Sales_Delete,
            Permissions.Sales_Edit,
            Permissions.Sales_Manage,
            Permissions.Discounts_View,
            Permissions.Discounts_Manage,
            Permissions.PaymentMethods_View,
            Permissions.PaymentMethods_Manage,
            Permissions.Alerts_View,
            Permissions.AuditLogs_View,
        },
        [Roles.Manager] = new[]
        {
            // Management permissions (no user management)
            Permissions.Products_Manage,
            Permissions.Categories_Manage,
            Permissions.Customers_Manage,
            Permissions.Suppliers_Manage,
            Permissions.Sales_Manage,
            Permissions.Orders_Manage,
            Permissions.Inventory_View,
            Permissions.Inventory_Edit,
            Permissions.Inventory_Adjust,
            Permissions.Inventory_Reports,
            Permissions.Reports_View,
            Permissions.Reports_Export,
            Permissions.Settings_View,
            Permissions.Dashboard_Manage,
            Permissions.POS_Manage,
            Permissions.MainScreenProducts_Manage,
            Permissions.Discounts_View,
            Permissions.Discounts_Manage,
            Permissions.PaymentMethods_View,
            Permissions.PaymentMethods_Manage,
            Permissions.Alerts_View,
            Permissions.Currency_View,
            Permissions.Tax_View,
        },
        [Roles.Cashier] = new[]
        {
            // POS and basic operations
            Permissions.Products_View,
            Permissions.Categories_View,
            Permissions.Customers_View,
            Permissions.Customers_Create,
            Permissions.Sales_Manage,
            Permissions.Orders_View,
            Permissions.Dashboard_View,
            Permissions.POS_Manage
        },
        [Roles.Employee] = new[]
        {
            // Basic viewing permissions
            Permissions.Products_View,
            Permissions.Categories_View,
            Permissions.Customers_View,
            Permissions.Dashboard_View,
            Permissions.POS_View
        },
        [Roles.Viewer] = new[]
        {
            // Read-only permissions
            Permissions.Products_View,
            Permissions.Categories_View,
            Permissions.Customers_View,
            Permissions.Dashboard_View,
            Permissions.Reports_View
        }
    };

    /// <summary>
    /// Check if the current user has a specific permission
    /// </summary>
    public static bool HasPermission(ClaimsPrincipal user, string permission)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return false;

        var permissions = user.FindAll("Permission").Select(c => c.Value).ToList();
        return permissions.Contains(permission);
    }

    /// <summary>
    /// Check if the current user has any of the specified permissions
    /// </summary>
    public static bool HasAnyPermission(ClaimsPrincipal user, params string[] permissions)
    {
        if (user?.Identity?.IsAuthenticated != true || permissions.Length == 0)
            return false;

        var userPermissions = user.FindAll("Permission").Select(c => c.Value).ToList();
        return permissions.Any(permission => userPermissions.Contains(permission));
    }

    /// <summary>
    /// Check if the current user has all of the specified permissions
    /// </summary>
    public static bool HasAllPermissions(ClaimsPrincipal user, params string[] permissions)
    {
        if (user?.Identity?.IsAuthenticated != true || permissions.Length == 0)
            return false;

        var userPermissions = user.FindAll("Permission").Select(c => c.Value).ToList();
        return permissions.All(permission => userPermissions.Contains(permission));
    }

    /// <summary>
    /// Check if the current user has a specific role
    /// </summary>
    public static bool HasRole(ClaimsPrincipal user, string role)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return false;

        var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        return roles.Contains(role);
    }

    /// <summary>
    /// Check if the current user has any of the specified roles
    /// </summary>
    public static bool HasAnyRole(ClaimsPrincipal user, params string[] roles)
    {
        if (user?.Identity?.IsAuthenticated != true || roles.Length == 0)
            return false;

        var userRoles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        return roles.Any(role => userRoles.Contains(role));
    }

    /// <summary>
    /// Check if the current user has all of the specified roles
    /// </summary>
    public static bool HasAllRoles(ClaimsPrincipal user, params string[] roles)
    {
        if (user?.Identity?.IsAuthenticated != true || roles.Length == 0)
            return false;

        var userRoles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        return roles.All(role => userRoles.Contains(role));
    }

    /// <summary>
    /// Get all permissions for a role
    /// </summary>
    public static string[] GetPermissionsForRole(string role)
    {
        return RolePermissions.TryGetValue(role, out var permissions) ? permissions : Array.Empty<string>();
    }

    /// <summary>
    /// Get all roles that have a specific permission
    /// </summary>
    public static string[] GetRolesWithPermission(string permission)
    {
        return RolePermissions
            .Where(kvp => kvp.Value.Contains(permission))
            .Select(kvp => kvp.Key)
            .ToArray();
    }

    /// <summary>
    /// Check if a role has a specific permission
    /// </summary>
    public static bool RoleHasPermission(string role, string permission)
    {
        return RolePermissions.TryGetValue(role, out var permissions) && permissions.Contains(permission);
    }
}
