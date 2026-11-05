using Application.DTOs.Products;
using Application.IServices.Products;
using Core.Entities.Products;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Products;

public class ProductPriceService : IProductPriceService
{
    private readonly ErpDbContext _db;

    public ProductPriceService(ErpDbContext db)
    {
        _db = db;
    }

    public async Task<ProductPriceOptions?> GetOptionsAsync(int productId, int displayCurrencyId)
    {
        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted);
        if (product == null) return null;

        var ids = new[] { product.CurrencyId, displayCurrencyId };
        var currencies = await _db.Currencies.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync();
        var from = currencies.First(c => c.Id == product.CurrencyId);
        var to = currencies.FirstOrDefault(c => c.Id == displayCurrencyId) ?? from;

        var tiers = await GetTiersAsync(productId);

        var options = new List<PriceOption> { new(null, string.Empty, PriceRules.ConvertViaUsd(product.SellingPrice, from, to)) };
        options.AddRange(tiers.Select(t => new PriceOption(t.Id, t.Name, PriceRules.ConvertViaUsd(t.Price, from, to))));

        return new ProductPriceOptions(
            product.Id,
            to.Id,
            PriceRules.MinimumUnitPrice(product, from, to),
            product.CostPrice.HasValue,
            options);
    }

    public async Task<List<ProductPrice>> GetTiersAsync(int productId)
    {
        return await _db.ProductPrices.AsNoTracking()
            .Where(t => t.ProductId == productId)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .ToListAsync();
    }

    public async Task<Dictionary<int, List<ProductPrice>>> GetAllTiersAsync()
    {
        var all = await _db.ProductPrices.AsNoTracking()
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .ToListAsync();
        return all.GroupBy(t => t.ProductId).ToDictionary(g => g.Key, g => g.ToList());
    }

    public Task ReplaceTiersAsync(int productId, IEnumerable<ProductPriceInput> tiers)
        => ReplaceTiersAsync(new Dictionary<int, List<ProductPriceInput>> { [productId] = tiers.ToList() });

    public async Task ReplaceTiersAsync(IReadOnlyDictionary<int, List<ProductPriceInput>> tiersByProduct)
    {
        var ids = tiersByProduct.Keys.ToList();
        var existing = (await _db.ProductPrices.Where(t => ids.Contains(t.ProductId)).ToListAsync())
            .ToLookup(t => t.ProductId);

        foreach (var (productId, tiers) in tiersByProduct)
            Stage(productId, existing[productId], tiers);

        await _db.SaveChangesAsync();
    }

    private void Stage(int productId, IEnumerable<ProductPrice> existing, IEnumerable<ProductPriceInput> tiers)
    {
        _db.ProductPrices.RemoveRange(existing);

        var order = 0;
        foreach (var tier in tiers)
        {
            var name = tier.Name?.Trim();
            if (string.IsNullOrEmpty(name) || tier.Price <= 0) continue;
            _db.ProductPrices.Add(new ProductPrice
            {
                ProductId = productId,
                Name = name.Length > 50 ? name[..50] : name,
                Price = tier.Price,
                SortOrder = order++
            });
        }
    }
}
