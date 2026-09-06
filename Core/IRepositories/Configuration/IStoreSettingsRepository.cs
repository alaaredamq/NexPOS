using Core.Entities.Configuration;
using Core.IRepositories.Base;

namespace Core.IRepositories.Configuration;

/// <summary>
/// Repository interface for StoreSettings entity.
/// StoreSettings is a singleton entity - only one record should exist.
/// </summary>
public interface IStoreSettingsRepository : IRepository<StoreSettings>
{
    /// <summary>
    /// Gets the store settings. Creates default settings if none exist.
    /// </summary>
    Task<StoreSettings> GetStoreSettingsAsync();
    
    /// <summary>
    /// Updates the store settings
    /// </summary>
    Task<StoreSettings> UpdateStoreSettingsAsync(StoreSettings settings);
    
    /// <summary>
    /// Checks if store settings exist
    /// </summary>
    Task<bool> ExistsAsync();
}
