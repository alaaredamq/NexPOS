using Core.Entities.Configuration;
using Core.IRepositories.Configuration;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Configuration
{
    public class SystemSettingsRepository : Repository<SystemSettings>, ISystemSettingsRepository
    {
        public SystemSettingsRepository(ErpDbContext context, IServiceProvider serviceProvider) 
            : base(context, serviceProvider)
        {
        }

        public async Task<SystemSettings> GetByKeyAsync(string key)
        {
            return await _Entities
                .FirstOrDefaultAsync(s => s.SettingKey == key) ?? new SystemSettings();
        }

        public async Task<IEnumerable<SystemSettings>> GetByCategoryAsync(string category)
        {
            return await _Entities
                .Where(s => s.Category == category)
                .OrderBy(s => s.SettingKey)
                .ToListAsync();
        }

        public async Task<IEnumerable<SystemSettings>> GetEditableSettingsAsync()
        {
            return await _Entities
                .Where(s => s.IsEditable)
                .OrderBy(s => s.Category)
                .ThenBy(s => s.SettingKey)
                .ToListAsync();
        }

        public async Task<bool> KeyExistsAsync(string key)
        {
            return await _Entities
                .AnyAsync(s => s.SettingKey == key);
        }

        public async Task DeleteByKeyAsync(string key)
        {
            var setting = await _Entities.FirstOrDefaultAsync(s => s.SettingKey == key);
            if (setting != null)
            {
                _Entities.Remove(setting);
                await SaveChangesAsync();
            }
        }

        public async Task UpdateMultipleAsync(IEnumerable<SystemSettings> settings)
        {
            foreach (var setting in settings)
            {
                setting.UpdatedAt = DateTime.UtcNow;
                _Entities.Update(setting);
            }
            await SaveChangesAsync();
        }
    }
}
