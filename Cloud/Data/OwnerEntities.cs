namespace Cloud.Data;

/// <summary>
/// A shop owner (or a person the owner trusts) who signs in to the phone app. Belongs to exactly one client and can only
/// ever reach that client's shop. Separate from <see cref="AdminUser"/>, which is you.
/// </summary>
public class OwnerUser
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    /// <summary>Lower-case; the sign-in name. Unique across all clients.</summary>
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSignInAt { get; set; }

    /// <summary>What this login may do (see <see cref="OwnerPermissions"/>). Logins made before roles existed keep everything.</summary>
    public int Permissions { get; set; } = (int)OwnerPermissions.All;

    public OwnerPermissions Can => (OwnerPermissions)Permissions;
}

/// <summary>
/// What a phone login may see and do. The cloud enforces it: a table the login may not read is never handed out, and a kind of change
/// it may not make is never queued. (The phone app also hides what a login cannot use, but that is only a courtesy.)
/// Limits work per table, because every row is one encrypted blob: <see cref="Stock"/> includes product cost prices, so a login that can
/// read stock can in principle read costs. <see cref="Financials"/> hides them in the app and opens the cash drawer.
/// </summary>
[Flags]
public enum OwnerPermissions
{
    None = 0,

    /// <summary>Receipts and what was sold (Today, Sales, Reports without profit).</summary>
    Sales = 1,

    /// <summary>Products, categories and stock levels.</summary>
    Stock = 2,

    /// <summary>Customers, what they owe, and suppliers.</summary>
    Debts = 4,

    /// <summary>Add products, customers and suppliers.</summary>
    Add = 8,

    /// <summary>Edit products and adjust stock.</summary>
    Edit = 16,

    /// <summary>Profit, cost prices, margins and the cash drawer. Only the app hides costs; the cash drawer's tables are withheld.</summary>
    Financials = 32,

    All = Sales | Stock | Debts | Add | Edit | Financials,
}

/// <summary>The three ready-made roles the portal offers.</summary>
public static class OwnerRoles
{
    public const string Owner = "owner";
    public const string Manager = "manager";
    public const string Staff = "staff";

    public static OwnerPermissions For(string? role) => role?.ToLowerInvariant() switch
    {
        Manager => OwnerPermissions.Sales | OwnerPermissions.Stock | OwnerPermissions.Debts | OwnerPermissions.Add | OwnerPermissions.Edit,
        Staff => OwnerPermissions.Sales | OwnerPermissions.Stock,
        _ => OwnerPermissions.All,
    };

    /// <summary>The preset these permissions match, or "custom".</summary>
    public static string NameOf(OwnerPermissions p) =>
        p == For(Owner) ? Owner : p == For(Manager) ? Manager : p == For(Staff) ? Staff : "custom";

    /// <summary>The tables a login with these permissions may read. Currencies and payment methods are only names, so everyone gets them.</summary>
    public static bool MayRead(OwnerPermissions p, string table) => table switch
    {
        "Currencies" or "PaymentMethods" => true,
        "Receipts" or "ReceiptItems" => p.HasFlag(OwnerPermissions.Sales),
        "Products" or "ProductCategories" => p.HasFlag(OwnerPermissions.Stock),
        "Customers" or "CustomerDebtTransactions" or "Suppliers" => p.HasFlag(OwnerPermissions.Debts),
        "CashDrawerSession" or "CashDrop" or "Orders" or "Users" => p.HasFlag(OwnerPermissions.Financials),
        _ => p == OwnerPermissions.All, // anything else: owner only
    };

    /// <summary>The permission each kind of change needs.</summary>
    public static OwnerPermissions NeededFor(string commandType) => commandType switch
    {
        "customer.create" or "supplier.create" or "product.create" => OwnerPermissions.Add,
        "product.update" or "stock.adjust" => OwnerPermissions.Edit,
        _ => OwnerPermissions.All,
    };
}

/// <summary>One signed-in phone. Only the token's hash is stored, so a leaked control database cannot be used to sign in.</summary>
public class OwnerSession
{
    public int Id { get; set; }
    public int OwnerUserId { get; set; }
    public OwnerUser OwnerUser { get; set; } = null!;
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public enum CommandStatus
{
    /// <summary>Waiting for the shop PC to ask.</summary>
    Queued = 0,
    /// <summary>Handed to the shop PC; no answer yet.</summary>
    Delivered = 1,
    Applied = 2,
    /// <summary>The shop PC refused it (see the result code). Final.</summary>
    Rejected = 3,
    /// <summary>The owner withdrew it before the shop PC took it.</summary>
    Discarded = 4,
}

/// <summary>
/// One change the owner asked for from the phone, waiting for (or finished with) the shop PC. The cloud never edits the
/// shop's data itself: the shop PC is the single source of truth, so there is nothing to reconcile.
/// </summary>
public class SiteCommand
{
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    public Site Site { get; set; } = null!;
    /// <summary>For example <c>customer.create</c>.</summary>
    public string Type { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    /// <summary>Chosen by the phone. Sending the same key twice for one shop returns the first command, so a retry on bad mobile data cannot add something twice.</summary>
    public string IdempotencyKey { get; set; } = "";
    public int OwnerUserId { get; set; }
    public string RequestedBy { get; set; } = "";
    public CommandStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ResultCode { get; set; }
    public string? ResultMessage { get; set; }
    public string? ResultDataJson { get; set; }
}
