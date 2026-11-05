using Application.IServices.Base;
using Core.Entities.Purchasing;
using Core.Enum;

namespace Application.IServices.Purchasing;

/// <param name="UpdateCostPrice">Also make this unit cost the product's cost price (converted to the product's currency).</param>
public sealed record PurchaseLineInput(int ProductId, decimal Quantity, decimal UnitCost, bool UpdateCostPrice);

/// <param name="PurchaseDay">The local day the goods arrived (time of day is ignored).</param>
/// <param name="DiscountPercent">Taken off the lines' subtotal, 0 to 100.</param>
/// <param name="AmountPaid">Paid to the supplier now, in the supplier's currency. The rest goes on the supplier's account.</param>
/// <param name="PayFromDrawer">The payment came out of the cash in the paying user's open drawer: it is taken off the drawer's expected cash (as a cash drop, converted to the shop's main currency).</param>
public sealed record NewPurchase(
    int SupplierId,
    DateTime PurchaseDay,
    string? SupplierReference,
    string? Notes,
    decimal DiscountPercent,
    decimal AmountPaid,
    IReadOnlyList<PurchaseLineInput> Lines,
    bool PayFromDrawer = false);

/// <summary>Filter shared by the list, the tiles and the CSV. Dates are UTC bounds (see <c>AppTime.LocalDayRangeToUtc</c>).</summary>
public sealed record PurchaseFilter(
    string? Search = null,
    int? SupplierId = null,
    PurchaseStatus? Status = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);

public sealed record PurchaseListRow(
    int Id,
    DateTime PurchaseDate,
    int SupplierId,
    string SupplierName,
    string? SupplierReference,
    string CurrencyNotation,
    int ItemCount,
    decimal Total,
    decimal AmountPaid,
    PurchaseStatus Status);

/// <summary>Totals of the non-cancelled purchases in a filter, per currency (amounts in different currencies are never added together).</summary>
public sealed record PurchaseCurrencyTotal(int CurrencyId, string CurrencyNotation, decimal Total, decimal Paid);

public sealed record PurchaseSummary(int Count, int CancelledCount, IReadOnlyList<PurchaseCurrencyTotal> Totals);

/// <summary>A purchase can't be saved or cancelled; <see cref="Code"/> says why so the API can answer with the right text.</summary>
public sealed class PurchaseRuleException : InvalidOperationException
{
    public const string NoLines = "no_lines";
    public const string DuplicateProduct = "duplicate_product";
    public const string UnknownProduct = "unknown_product";
    public const string UnknownSupplier = "unknown_supplier";
    public const string InactiveSupplier = "inactive_supplier";
    public const string FutureDate = "future_date";
    public const string PaidTooMuch = "paid_exceeds_total";
    public const string BadAmount = "bad_amount";
    public const string AlreadyCancelled = "already_cancelled";
    public const string StockSold = "stock_already_sold";
    public const string DrawerNotOpen = "drawer_not_open";

    public PurchaseRuleException(string code, string message, string? subject = null) : base(message)
    {
        Code = code;
        Subject = subject;
    }

    public string Code { get; }

    /// <summary>The product name (or number) the rule is about, for the message.</summary>
    public string? Subject { get; }
}

/// <summary>
/// Goods bought from a supplier. Saving a purchase adds every line to its product's stock and puts what was not paid
/// on the supplier's balance, all in one transaction; cancelling takes both back.
/// </summary>
public interface IPurchaseService : IBaseService
{
    Task<Purchase> CreateAsync(NewPurchase request, int userId);

    /// <summary>Takes the stock back and removes the purchase from the supplier's balance. Throws <see cref="PurchaseRuleException"/> when it was cancelled already or some of the stock has been sold since.</summary>
    Task<Purchase> CancelAsync(int purchaseId, int userId, string? reason);

    /// <summary>With items, supplier, currency and the people involved; null when there is no such purchase.</summary>
    Task<Purchase?> GetAsync(int purchaseId);

    Task<(List<PurchaseListRow> Rows, int Total)> SearchAsync(PurchaseFilter filter, string? sort, bool descending, int skip, int take);

    Task<PurchaseSummary> SummaryAsync(PurchaseFilter filter);

    /// <summary>The purchases of a filter with their items, for the CSV.</summary>
    Task<List<Purchase>> ExportAsync(PurchaseFilter filter);

    /// <summary>The user's display name for the people shown on a purchase.</summary>
    Task<Dictionary<int, string>> UserNamesAsync(IEnumerable<int> userIds);
}
