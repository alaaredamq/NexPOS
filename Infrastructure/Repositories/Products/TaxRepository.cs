using Core.Entities.Products;
using Core.IRepositories.Products;
using Core.Enum;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Products;

public class TaxRepository : Repository<TaxRate>, ITaxRepository
{
    public TaxRepository(ErpDbContext context, IServiceProvider serviceProvider) : base(context, serviceProvider)
    {
    }

    public async Task<TaxRate?> GetDefaultTaxAsync()
    {
        return await _Context.Set<TaxRate>()
            .Include(t => t.Category)
            .FirstOrDefaultAsync(t => t.IsDefault && t.IsActive && !t.IsDeleted);
    }

    public async Task<TaxRate?> GetActiveTaxAsync(TaxType type)
    {
        var now = DateTime.UtcNow;
        return await _Context.Set<TaxRate>()
            .Include(t => t.Category)
            .FirstOrDefaultAsync(t => t.Type == type && 
                                     t.IsActive && 
                                     !t.IsDeleted &&
                                     (!t.EffectiveFrom.HasValue || t.EffectiveFrom <= now) &&
                                     (!t.EffectiveTo.HasValue || t.EffectiveTo >= now));
    }

    public async Task<List<TaxRate>> GetActiveTaxesAsync()
    {
        var now = DateTime.UtcNow;
        return await _Context.Set<TaxRate>()
            .Include(t => t.Category)
            .Where(t => t.IsActive && 
                       !t.IsDeleted &&
                       (!t.EffectiveFrom.HasValue || t.EffectiveFrom <= now) &&
                       (!t.EffectiveTo.HasValue || t.EffectiveTo >= now))
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<List<TaxRate>> GetTaxesByTypeAsync(TaxType type)
    {
        return await _Context.Set<TaxRate>()
            .Include(t => t.Category)
            .Where(t => t.Type == type && !t.IsDeleted)
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<List<TaxRate>> GetTaxesByCategoryAsync(int categoryId)
    {
        var now = DateTime.UtcNow;
        return await _Context.Set<TaxRate>()
            .Include(t => t.Category)
            .Where(t => (t.CategoryId == categoryId || t.ApplyToAllProducts) &&
                       t.IsActive &&
                       !t.IsDeleted &&
                       (!t.EffectiveFrom.HasValue || t.EffectiveFrom <= now) &&
                       (!t.EffectiveTo.HasValue || t.EffectiveTo >= now))
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<List<TaxRate>> GetEffectiveTaxesAsync(DateTime date)
    {
        return await _Context.Set<TaxRate>()
            .Include(t => t.Category)
            .Where(t => t.IsActive &&
                       !t.IsDeleted &&
                       (!t.EffectiveFrom.HasValue || t.EffectiveFrom <= date) &&
                       (!t.EffectiveTo.HasValue || t.EffectiveTo >= date))
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<List<TaxRate>> GetTaxesByEffectiveDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        return await _Context.Set<TaxRate>()
            .Include(t => t.Category)
            .Where(t => !t.IsDeleted &&
                       ((t.EffectiveFrom.HasValue && t.EffectiveFrom <= endDate) || !t.EffectiveFrom.HasValue) &&
                       ((t.EffectiveTo.HasValue && t.EffectiveTo >= startDate) || !t.EffectiveTo.HasValue))
            .OrderBy(t => t.Name)
            .ToListAsync();
    }

    public async Task<bool> SetDefaultTaxAsync(int taxId)
    {
        // First, remove default flag from all taxes
        await _Context.Set<TaxRate>()
            .Where(t => t.IsDefault && !t.IsDeleted)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.IsDefault, false));

        // Set the new default tax
        var affected = await _Context.Set<TaxRate>()
            .Where(t => t.Id == taxId && !t.IsDeleted)
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.IsDefault, true));

        return affected > 0;
    }

    public async Task<bool> DeactivateTaxAsync(int taxId)
    {
        var affected = await _Context.Set<TaxRate>()
            .Where(t => t.Id == taxId && !t.IsDeleted)
            .ExecuteUpdateAsync(t => t
                .SetProperty(x => x.IsActive, false)
                .SetProperty(x => x.EffectiveTo, DateTime.UtcNow));

        return affected > 0;
    }

    public async Task<List<TaxRate>> SearchTaxesAsync(string searchTerm)
    {
        var term = searchTerm.ToLower();
        
        return await _Context.Set<TaxRate>()
            .Include(t => t.Category)
            .Where(t => !t.IsDeleted && (
                t.Name.ToLower().Contains(term) ||
                (t.Description != null && t.Description.ToLower().Contains(term)) ||
                (t.TaxCode != null && t.TaxCode.ToLower().Contains(term))
            ))
            .OrderBy(t => t.Name)
            .Take(50) // Limit results for performance
            .ToListAsync();
    }
}
