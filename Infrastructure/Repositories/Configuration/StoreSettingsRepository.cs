using Core.Entities.Configuration;
using Core.IRepositories.Configuration;
using Infrastructure.Data;
using Infrastructure.Repositories.Classes.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories.Configuration;

/// <summary>
/// Repository for StoreSettings entity.
/// StoreSettings is a singleton entity - only one record should exist.
/// </summary>
public class StoreSettingsRepository : Repository<StoreSettings>, IStoreSettingsRepository
{
    public StoreSettingsRepository(ErpDbContext context, IServiceProvider serviceProvider) 
        : base(context, serviceProvider)
    {
    }

    /// <summary>
    /// Gets the store settings. Creates default settings if none exist.
    /// </summary>
    public async Task<StoreSettings> GetStoreSettingsAsync()
    {
        var settings = await _Entities.FirstOrDefaultAsync();
        
        if (settings == null)
        {
            // Create default settings if none exist
            settings = StoreSettings.CreateDefault();
            await _Entities.AddAsync(settings);
            await SaveChangesAsync();
        }
        
        return settings;
    }

    /// <summary>
    /// Updates the store settings
    /// </summary>
    public async Task<StoreSettings> UpdateStoreSettingsAsync(StoreSettings settings)
    {
        settings.LastModifiedAt = DateTime.UtcNow;
        settings.UpdatedAt = DateTime.UtcNow;
        
        _Entities.Update(settings);
        await SaveChangesAsync();
        
        return settings;
    }

    /// <summary>
    /// Checks if store settings exist
    /// </summary>
    public async Task<bool> ExistsAsync()
    {
        return await _Entities.AnyAsync();
    }
}
