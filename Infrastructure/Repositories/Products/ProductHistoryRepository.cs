using Core.Entities.Products;
using Core.IRepositories.Products;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Products;

public class ProductHistoryRepository : IProductHistoryRepository
{
    private readonly ErpDbContext _db;

    public ProductHistoryRepository(ErpDbContext db)
    {
        _db = db;
    }

    public async Task AddRangeAsync(IEnumerable<ProductHistory> entries)
    {
        var list = entries.ToList();
        if (list.Count == 0) return;

        _db.ProductHistories.AddRange(list);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            // The caller drops a line it can't write; leaving it tracked would make the *next* save (a sale's) retry it and fail too.
            foreach (var entry in list) _db.Entry(entry).State = EntityState.Detached;
            throw;
        }
    }

    public async Task<(List<ProductHistory> Items, int Total)> GetPageAsync(
        int productId, IReadOnlyCollection<ProductHistoryType>? types, int skip, int take)
    {
        var query = _db.ProductHistories.AsNoTracking().Where(h => h.ProductId == productId);
        if (types is { Count: > 0 }) query = query.Where(h => types.Contains(h.Type));

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(h => h.CreatedAt)
            .ThenByDescending(h => h.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        return (items, total);
    }

    public async Task<ProductHistoryTotals> GetTotalsAsync(int productId)
    {
        // SQLite can't SUM a decimal in SQL, so the (small) per-product set is summed here.
        var rows = await _db.ProductHistories.AsNoTracking()
            .Where(h => h.ProductId == productId && h.QuantityChange != null
                && (h.Type == ProductHistoryType.Sold || h.Type == ProductHistoryType.SaleReturned || h.Type == ProductHistoryType.StockAdded || h.Type == ProductHistoryType.Received))
            .Select(h => new { h.Type, Quantity = h.QuantityChange!.Value })
            .ToListAsync();

        return new ProductHistoryTotals(
            Sold: -rows.Where(r => r.Type == ProductHistoryType.Sold).Sum(r => r.Quantity),
            Returned: rows.Where(r => r.Type == ProductHistoryType.SaleReturned).Sum(r => r.Quantity),
            Added: rows.Where(r => r.Type is ProductHistoryType.StockAdded or ProductHistoryType.Received).Sum(r => r.Quantity));
    }
}
