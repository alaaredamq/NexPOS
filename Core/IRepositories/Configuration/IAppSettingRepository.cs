using Core.Entities.Configuration;
using Core.IRepositories.Base;

namespace Core.IRepositories.Configuration;

public interface IAppSettingRepository : IRepository<AppSetting>
{
    Task<AppSetting?> GetByNameAsync(string name);
    Task<List<AppSetting>> GetByTypeAsync(string type);
}
