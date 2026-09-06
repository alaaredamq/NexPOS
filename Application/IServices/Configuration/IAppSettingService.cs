using Application.IServices.Base;
using Core.Entities.Configuration;

namespace Application.IServices.Configuration;

public interface IAppSettingService : IBaseService
{
    Task<List<AppSetting>> GetAllAsync();
    Task<AppSetting?> GetByNameAsync(string name);
    Task<List<AppSetting>> GetByTypeAsync(string type);
    Task<AppSetting> GetByIdAsync(int id);
    Task<AppSetting> CreateAsync(AppSetting appSetting);
    Task UpdateAsync(int id, AppSetting appSetting);
    Task UpdateByNameAsync(string name, string value);
    Task DeleteAsync(int id);
    Task<T?> GetTypedValueAsync<T>(string name);
}
