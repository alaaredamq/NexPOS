using Core.DTOs;
using Core.Entities.Products;

namespace Core.ICache;

public interface ICacheService {
    T? CreateOrGet<T>(string key, Func<T> create, int cacheTimeInMinutes = 60);
    T? Get<T>(string key);
    void Set<T>(string key, T data, int cacheTimeInMinutes = 60);
    void Remove(string key);
    void Clear();
    bool Exists(string key);

    // Async methods
    Task<T?> CreateOrGetAsync<T>(string key, Func<Task<T>> create, int cacheTimeInMinutes = 60);
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T data, int cacheTimeInMinutes = 60);
    Task RemoveAsync(string key);
    Task<bool> ExistsAsync(string key);

    #region Products

    List<ProductDTO>? GetProducts();

    #endregion

    #region POS Fast Search

    /// <summary>
    /// Get cached products for POS operations
    /// </summary>
    Task<List<ProductDTO>?> GetPOSProductsAsync();

    /// <summary>
    /// Cache products for POS operations
    /// </summary>
    Task SetPOSProductsAsync(List<ProductDTO> products, int cacheTimeInMinutes = 60);

    /// <summary>
    /// Get barcode index for fast POS lookups
    /// </summary>
    Task<Dictionary<string, int>?> GetBarcodeIndexAsync();

    /// <summary>
    /// Cache barcode index for fast POS lookups
    /// </summary>
    Task SetBarcodeIndexAsync(Dictionary<string, int> barcodeIndex, int cacheTimeInMinutes = 60);

    /// <summary>
    /// Clear all POS-related caches
    /// </summary>
    Task ClearPOSCachesAsync();

    #endregion
}