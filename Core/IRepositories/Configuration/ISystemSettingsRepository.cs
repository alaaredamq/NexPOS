using Core.Entities.Configuration;
using Core.IRepositories.Base;

namespace Core.IRepositories.Configuration
{
    public interface ISystemSettingsRepository : IRepository<SystemSettings>
    {
        Task<SystemSettings> GetByKeyAsync(string key);
        Task<IEnumerable<SystemSettings>> GetByCategoryAsync(string category);
        Task<IEnumerable<SystemSettings>> GetEditableSettingsAsync();
        Task<bool> KeyExistsAsync(string key);
        Task DeleteByKeyAsync(string key);
        Task UpdateMultipleAsync(IEnumerable<SystemSettings> settings);
    }
}
