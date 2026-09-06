namespace Core.ICache;

/// <summary>
/// Central registry of memory-cache keys and TTLs. Keep every cached read here so the
/// matching eviction on write is easy to find.
/// </summary>
public static class CacheKeys
{
    /// <summary>TTL for slow-changing reference/lookup lists.</summary>
    public const int LookupTtlMinutes = 30;

    /// <summary>TTL for dashboard aggregates (short — data is time-sensitive).</summary>
    public const int DashboardTtlMinutes = 2;

    public const string Categories = "lookup:product-categories";
    public const string PaymentMethods = "lookup:payment-methods";
    public const string SalesTaxes = "lookup:sales-taxes";
    public const string ProductTaxes = "lookup:product-taxes";
    public const string Discounts = "lookup:discounts";
    public const string Suppliers = "lookup:suppliers";
    public const string SystemSettings = "lookup:system-settings";
    public const string StoreSettings = "lookup:store-settings";

    public static string Dashboard(string section, string? arg = null) =>
        arg is null ? $"dashboard:{section}" : $"dashboard:{section}:{arg}";
}
