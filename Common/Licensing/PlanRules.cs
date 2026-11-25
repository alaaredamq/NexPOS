namespace Common.Licensing;

/// <summary>A part of the app that only the Pro plan includes. The name is also the stable id the API and the SPA use.</summary>
public enum ProFeature
{
    /// <summary>Stock levels, adjustments, low stock, stock value.</summary>
    Inventory,
    Suppliers,
    Purchases,
    AuditLog,
    /// <summary>Tills beyond the store server (the key's seat count).</summary>
    ExtraTills,
    /// <summary>More than <see cref="PlanRules.BasicMaxActiveUsers"/> active users.</summary>
    MoreUsers,
    /// <summary>The till sends payment requests to Whish itself (online payments). Taking Whish by hand is in every plan.</summary>
    OnlinePayments
}

/// <summary>
/// What each plan includes. One place, so the server's gates, the menu and the user limit can never disagree.
/// Data is never deleted because of the plan: on Basic the Pro pages are locked, and what they hold comes back on upgrade.
/// </summary>
public static class PlanRules
{
    /// <summary>Basic covers the owner plus two cashiers.</summary>
    public const int BasicMaxActiveUsers = 3;

    /// <summary>Basic always runs on a single PC, whatever seat count a key carries.</summary>
    public const int BasicSeats = 1;

    public static bool Includes(LicensePlan plan, ProFeature feature) => plan == LicensePlan.Pro;

    /// <summary>Null means no limit.</summary>
    public static int? MaxActiveUsers(LicensePlan plan) => plan == LicensePlan.Pro ? null : BasicMaxActiveUsers;

    public static int EffectiveSeats(LicensePlan plan, int seats) => plan == LicensePlan.Pro ? seats : BasicSeats;
}
