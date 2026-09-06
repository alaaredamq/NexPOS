using Infrastructure.Data;
using Core.Entities.Configuration;
using Core.IRepositories.Configuration;
using Infrastructure.Repositories.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Configuration;

public class CodeListValueRepository : BaseRepository<CodeListValue, int>, ICodeListValueRepository
{
    public CodeListValueRepository(ErpDbContext context, IServiceProvider serviceProvider) : base(context, serviceProvider) { }

    public async Task<IEnumerable<CodeListValue>> GetByCodeListIdAsync(int codeListId)
    {
        return await Table
            .Include(clv => clv.CodeList)
            .Where(clv => clv.CodeListId == codeListId)
            .OrderBy(clv => clv.DisplayOrder)
            .ToListAsync();
    }

    public async Task<IEnumerable<CodeListValue>> GetActiveByCodeListIdAsync(int codeListId)
    {
        return await Table
            .Include(clv => clv.CodeList)
            .Where(clv => clv.CodeListId == codeListId && clv.IsActive)
            .OrderBy(clv => clv.DisplayOrder)
            .ToListAsync();
    }

    public async Task<IEnumerable<CodeListValue>> GetByCodeListKeyAsync(string key)
    {
        return await Table
            .Include(clv => clv.CodeList)
            .Where(clv => clv.CodeList.Key == key)
            .OrderBy(clv => clv.DisplayOrder)
            .ToListAsync();
    }

    public async Task<IEnumerable<CodeListValue>> GetActiveByCodeListKeyAsync(string key)
    {
        return await Table
            .Include(clv => clv.CodeList)
            .Where(clv => clv.CodeList.Key == key && clv.IsActive)
            .OrderBy(clv => clv.DisplayOrder)
            .ToListAsync();
    }
}
