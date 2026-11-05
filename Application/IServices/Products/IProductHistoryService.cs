using Core.Entities.Products;
using Core.IRepositories.Products;

namespace Application.IServices.Products;

/// <summary>Extra facts about a product save, so its history line says what really happened.</summary>
/// <param name="StockType">How a changed stock figure is described when it was adjusted by hand (added / removed); null means it was typed in (<see cref="ProductHistoryType.StockSet"/>).</param>
/// <param name="Reason">The reason the person gave for a stock adjustment.</param>
/// <param name="FromImport">The save came from an import file.</param>
public sealed record ProductChangeContext(ProductHistoryType? StockType = null, string? Reason = null, bool FromImport = false);

/// <summary>
/// Writes and reads the history of a product: everything that was done to it. Recording never fails the
/// operation being recorded - a history line that can't be written is logged and dropped.
/// </summary>
public interface IProductHistoryService
{
    /// <summary>The product was added. <paramref name="product"/> must already have its id.</summary>
    Task RecordCreatedAsync(Product product, bool fromImport = false);

    /// <summary>One import file's worth of history in a single write: the products it created and the ones it changed.</summary>
    Task RecordImportAsync(IReadOnlyList<Product> created, IReadOnlyList<(ProductSnapshot Before, Product After)> changed);

    /// <summary>The product was deleted.</summary>
    Task RecordDeletedAsync(int productId);

    /// <summary>
    /// Compares <paramref name="before"/> (read before saving) with <paramref name="after"/> and records one line for each
    /// thing that changed: stock, selling price, purchase price, minimum stock, and one line listing any other fields.
    /// </summary>
    Task RecordChangesAsync(ProductSnapshot before, Product after, ProductChangeContext? context = null);

    /// <summary>
    /// Units moved by a sale, a return or a purchase. The stock figures are read from the database, so call it after the
    /// stock was updated.
    /// </summary>
    /// <param name="change">Signed: negative takes units off the shelf.</param>
    /// <param name="stockBefore">Pass it when known (a sale past the available stock does not subtract cleanly).</param>
    /// <param name="stockAfter">Pass it when the stock was read at the time (several lines of one sale can hit the same product); otherwise it is read now.</param>
    Task RecordMovementAsync(int productId, ProductHistoryType type, decimal change, int? orderId = null, string? reason = null,
        int? purchaseId = null, decimal? stockBefore = null, decimal? stockAfter = null);

    Task<ProductHistoryPage> GetPageAsync(int productId, IReadOnlyCollection<ProductHistoryType>? types, int page, int pageSize);
}

public sealed record ProductHistoryPage(List<ProductHistory> Items, int Total, ProductHistoryTotals Totals);
