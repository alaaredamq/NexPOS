using Core.Entities.Configuration;
using Core.IRepositories.Configuration;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Configuration;

public class AppSettingRepository : Repository<AppSetting>, IAppSettingRepository
{
    public AppSettingRepository(ErpDbContext dbContext, IServiceProvider serviceProvider)
        : base(dbContext, serviceProvider)
    {
    }

    public async Task<AppSetting?> GetByNameAsync(string name)
    {
        return await GetAllQueryable()
            .FirstOrDefaultAsync(a => a.Name == name);
    }

    public async Task<List<AppSetting>> GetByTypeAsync(string type)
    {
        return await GetAllQueryable()
            .Where(a => a.Type == type)
            .ToListAsync();
    }
}
