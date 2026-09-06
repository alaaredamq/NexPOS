using Infrastructure.Data;
using Core.Entities.Configuration;
using Core.IRepositories.Configuration;
using Infrastructure.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Configuration;

public class CodeListRepository : BaseRepository<CodeList, int>, ICodeListRepository
{
    public CodeListRepository(ErpDbContext context, IServiceProvider serviceProvider) : base(context, serviceProvider) { }

    public async Task<CodeList?> GetByKeyAsync(string key)
    {
        return await Table
            .Include(cl => cl.Values)
            .FirstOrDefaultAsync(cl => cl.Key == key);
    }

    public async Task<IEnumerable<CodeList>> GetActiveAsync()
    {
        return await Table
            .Include(cl => cl.Values.Where(v => v.IsActive))
            .Where(cl => cl.IsActive)
            .OrderBy(cl => cl.DisplayOrder)
            .ToListAsync();
    }

    public async Task<IEnumerable<CodeListValue>> GetValuesByCodeListKeyAsync(string key)
    {
        return await Table
            .Where(cl => cl.Key == key)
            .SelectMany(cl => cl.Values)
            .Include(clv => clv.CodeList)
            .OrderBy(clv => clv.DisplayOrder)
            .ToListAsync();
    }

    public async Task<IEnumerable<CodeListValue>> GetActiveValuesByCodeListKeyAsync(string key)
    {
        return await Table
            .Where(cl => cl.Key == key)
            .SelectMany(cl => cl.Values.Where(v => v.IsActive))
            .Include(clv => clv.CodeList)
            .OrderBy(clv => clv.DisplayOrder)
            .ToListAsync();
    }
}
