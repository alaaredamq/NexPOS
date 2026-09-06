using Core.Entities.Products;
using Core.IRepositories.Products;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Products;

public class SupplierRepository : Repository<Supplier>, ISupplierRepository
{
    public SupplierRepository(ErpDbContext dbContext, IServiceProvider serviceProvider)
        : base(dbContext, serviceProvider)
    {
    }

    public new async Task<List<Supplier>> GetAllAsync()
    {
        return await GetAllQueryable()
            .Include(s => s.Currency)
            .Include(s => s.Products)
            .ToListAsync();
    }

    public new async Task<Supplier> GetByIdAsync(int id)
    {
        var supplier = await GetAllQueryable()
            .Include(s => s.Currency)
            .Include(s => s.Products)
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);

        if (supplier == null)
            throw new ArgumentNullException($"Supplier with id {id} not found");

        return supplier;
    }
}


