using Application.DTOs.Settings;
using Core.Entities.Configuration;

namespace Application.IServices.Settings
{
    public interface ISystemSettingsService
    {
        // Basic CRUD operations
        Task<IEnumerable<SystemSettings>> GetAllAsync();
        Task<SystemSettings> GetByIdAsync(int id);
        Task<SystemSettings> GetByKeyAsync(string key);
        Task<SystemSettings> CreateAsync(SystemSettings entity);
        Task<SystemSettings> UpdateAsync(SystemSettings entity);
        Task DeleteAsync(int id);
        Task DeleteByKeyAsync(string key);

        // Bulk operations
        Task UpdateMultipleAsync(IEnumerable<SystemSettings> settings);
        Task<BulkSettingUpdateRequest> UpdateBulkAsync(BulkSettingUpdateRequest request);

        // Category operations
        Task<IEnumerable<SettingCategoryDTO>> GetSettingsByCategoryAsync();
        Task<IEnumerable<SystemSettings>> GetSettingsByCategoryAsync(string category);

        // Application settings
        Task<AppSettingsDTO> GetAppSettingsAsync();
        Task<AppSettingsDTO> UpdateAppSettingsAsync(AppSettingsDTO appSettings);

        // Individual setting operations
        Task<string> GetSettingValueAsync(string key, string defaultValue = "");
        Task<T> GetSettingValueAsync<T>(string key, T defaultValue = default!);
        Task SetSettingValueAsync(string key, string value);
        Task SetSettingValueAsync<T>(string key, T value);

        // System initialization
        Task InitializeDefaultSettingsAsync();
        Task<bool> IsFirstRunAsync();
        Task ResetToDefaultsAsync();

        // Validation
        Task<bool> ValidateSettingAsync(string key, string value);
        Task<List<string>> ValidateSettingsAsync(Dictionary<string, string> settings);

        // Export/Import
        Task<string> ExportSettingsAsync();
        Task<bool> ImportSettingsAsync(string jsonData);
    }
}
