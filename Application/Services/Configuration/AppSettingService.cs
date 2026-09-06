using Application.IServices.Configuration;
using Core.Entities.Configuration;
using Core.IRepositories.Configuration;

namespace Application.Services.Configuration;

public class AppSettingService : IAppSettingService
{
    private readonly IAppSettingRepository _repo;

    public AppSettingService(IAppSettingRepository repo)
    {
        _repo = repo;
    }

    public async Task<List<AppSetting>> GetAllAsync() => await _repo.GetAllAsync();
    public async Task<AppSetting?> GetByNameAsync(string name) => await _repo.GetByNameAsync(name);
    public async Task<List<AppSetting>> GetByTypeAsync(string type) => await _repo.GetByTypeAsync(type);
    public async Task<AppSetting> GetByIdAsync(int id) => await _repo.GetByIdAsync(id);
    public async Task<AppSetting> CreateAsync(AppSetting appSetting) => await _repo.SaveAsync(appSetting);
    
    public async Task UpdateAsync(int id, AppSetting appSetting)
    {
        var existing = await _repo.GetByIdAsync(id);
        existing.Name = appSetting.Name;
        existing.Value = appSetting.Value;
        existing.Description = appSetting.Description;
        existing.Type = appSetting.Type;
        await _repo.SaveAsync(existing);
    }
    
    public async Task UpdateByNameAsync(string name, string value)
    {
        var existing = await _repo.GetByNameAsync(name);
        if (existing != null)
        {
            existing.Value = value;
            await _repo.SaveAsync(existing);
        }
    }
    
    public async Task DeleteAsync(int id)
    {
        var existing = await _repo.GetByIdAsync(id);
        await _repo.DeleteAsync(existing);
    }
    
    public async Task<T?> GetTypedValueAsync<T>(string name)
    {
        var setting = await _repo.GetByNameAsync(name);
        if (setting == null) return default;
        
        var typedValue = setting.GetTypedValue();
        if (typedValue is T directValue) return directValue;
        
        return (T)Convert.ChangeType(typedValue, typeof(T));
    }
}
