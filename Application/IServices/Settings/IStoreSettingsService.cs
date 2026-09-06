using Application.DTOs.Settings;
using Core.Entities.Configuration;

namespace Application.IServices.Settings;

/// <summary>
/// Service interface for managing store settings.
/// Store settings contain information displayed on receipts like store name, address, etc.
/// </summary>
public interface IStoreSettingsService
{
    /// <summary>
    /// Gets the current store settings
    /// </summary>
    Task<StoreSettingsDTO> GetStoreSettingsAsync();
    
    /// <summary>
    /// Gets the raw store settings entity
    /// </summary>
    Task<StoreSettings> GetStoreSettingsEntityAsync();
    
    /// <summary>
    /// Updates the store settings
    /// </summary>
    Task<StoreSettingsDTO> UpdateStoreSettingsAsync(UpdateStoreSettingsRequest request, int? userId = null);
    
    /// <summary>
    /// Resets store settings to defaults
    /// </summary>
    Task<StoreSettingsDTO> ResetToDefaultsAsync();
    
    /// <summary>
    /// Validates store settings
    /// </summary>
    Task<List<string>> ValidateSettingsAsync(UpdateStoreSettingsRequest request);
}
