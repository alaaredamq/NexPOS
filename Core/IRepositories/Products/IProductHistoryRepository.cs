using Core.Entities.Products;

namespace Core.IRepositories.Products;

public interface IProductHistoryRepository
{
    Task AddRangeAsync(IEnumerable<ProductHistory> entries);

    /// <summary>One page of a product's history, newest first, plus how many rows match.</summary>
    Task<(List<ProductHistory> Items, int Total)> GetPageAsync(int productId, IReadOnlyCollection<ProductHistoryType>? types, int skip, int take);

    /// <summary>Totals over the whole history of a product (not just the page).</summary>
    Task<ProductHistoryTotals> GetTotalsAsync(int productId);
}

/// <summary>Units sold, returned and added over a product's life.</summary>
public sealed record ProductHistoryTotals(decimal Sold, decimal Returned, decimal Added);
