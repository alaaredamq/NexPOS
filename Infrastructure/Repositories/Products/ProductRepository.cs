using Core.DTOs;
using Core.Entities.Products;
using Core.ICache;
using Core.IRepositories.Products;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Products;

public class ProductRepository : Repository<Product>, IProductRepository {
    
    public ProductRepository(ErpDbContext dbContext, IServiceProvider serviceProvider) : base(dbContext, serviceProvider) {
    }
    
    public new async Task<List<Product>> GetAllAsync()
    {
        return await GetAllQueryable()
            .Where(p => !p.IsDeleted) // Filter out soft-deleted products
            .Include(p => p.Category)
            .Include(p => p.Supplier)
            .Include(p => p.Currency)
            .ToListAsync();
    }

    public new async Task<List<Product>> GetAllAsync(System.Linq.Expressions.Expression<Func<Product, bool>>? predicate = null)
    {
        var query = GetAllQueryable()
            .Where(p => !p.IsDeleted) // Filter out soft-deleted products
            .Include(p => p.Category)
            .Include(p => p.Supplier)
            .Include(p => p.Currency).AsQueryable();

        if (predicate != null)
            query = query.Where(predicate);

        return await query.ToListAsync();
    }

    public new async Task<Product> GetByIdAsync(int id)
    {
        var product = await GetAllQueryable()
            .Where(p => !p.IsDeleted) // Filter out soft-deleted products
            .Include(p => p.Category)
            .Include(p => p.Supplier)
            .Include(p => p.Currency)
            .FirstOrDefaultAsync(p => p.Id == id);
        
        if (product == null)
            throw new ArgumentNullException($"Product with id {id} not found");
            
        return product;
    }
    
    /// <summary>
    /// Get product by ID including soft-deleted products (for admin operations)
    /// </summary>
    public async Task<Product?> GetByIdIncludingDeletedAsync(int id)
    {
        return await GetAllQueryable()
            .Include(p => p.Category)
            .Include(p => p.Supplier)
            .Include(p => p.Currency)
            .FirstOrDefaultAsync(p => p.Id == id);
    }
    
    public List<ProductDTO>? GetProducts()
    {
        return _CacheService.GetProducts();
    }

    public async Task<Product?> GetByBarcodeAsync(string barcode)
    {
        return await GetAllQueryable()
            .Where(p => !p.IsDeleted) // Filter out soft-deleted products
            .Include(p => p.Category)
            .Include(p => p.Supplier)
            .Include(p => p.Currency)
            .FirstOrDefaultAsync(p => p.Barcode == barcode);
    }

    public async Task<List<Product>> SearchAsync(string? term, int? categoryId, int? supplierId, bool onlyActive = true)
    {
        var query = GetAllQueryable()
            .Where(p => !p.IsDeleted) // Filter out soft-deleted products
            .Include(p => p.Category)
            .Include(p => p.Supplier)
            .Include(p => p.Currency).AsQueryable();
            
        if (onlyActive) query = query.Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(term))
        {
            query = query.Where(p => p.Name.Contains(term) || p.Barcode.Contains(term));
        }
        if (categoryId.HasValue) query = query.Where(p => p.CategoryId == categoryId);
        if (supplierId.HasValue) query = query.Where(p => p.SupplierId == supplierId);
        return await query.ToListAsync();
    }

    public async Task<List<Product>> GetActiveProductsAsync()
    {
        return await GetAllQueryable()
            .Where(p => p.IsActive && !p.IsDeleted) // Filter out soft-deleted products
            .Include(p => p.Category)
            .Include(p => p.Supplier)
            .Include(p => p.Currency)
            .ToListAsync();
    }

    public async Task<bool> TryDeductStockAsync(int productId, decimal quantity)
    {
        var rowsAffected = await _Entities
            .Where(p => p.Id == productId && !p.IsDeleted && p.StockQuantity >= quantity)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.StockQuantity, p => p.StockQuantity - quantity)
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow));

        return rowsAffected > 0;
    }

    public async Task<bool> ForceDeductStockAsync(int productId, decimal quantity)
    {
        var rowsAffected = await _Entities
            .Where(p => p.Id == productId && !p.IsDeleted)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.StockQuantity, p => p.StockQuantity >= quantity ? p.StockQuantity - quantity : 0)
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow));

        return rowsAffected > 0;
    }

}