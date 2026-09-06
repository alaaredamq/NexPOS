using Core.DTOs;

namespace Application.IServices.Products
{
    /// <summary>
    /// Fast product search service with caching for POS operations
    /// </summary>
    public interface IFastProductSearchService
    {
        /// <summary>
        /// Search product by barcode (fastest method)
        /// </summary>
        Task<ProductDTO?> SearchByBarcodeAsync(string barcode);

        /// <summary>
        /// Search products by name or partial name
        /// </summary>
        Task<IEnumerable<ProductDTO>> SearchByNameAsync(string searchTerm, int limit = 10);


        /// <summary>
        /// Get product by ID (from cache if available)
        /// </summary>
        Task<ProductDTO?> GetByIdAsync(int productId);

        /// <summary>
        /// Refresh the product cache (call when products are modified)
        /// </summary>
        Task RefreshCacheAsync();

        /// <summary>
        /// Get all active products for initial load
        /// </summary>
        Task<IEnumerable<ProductDTO>> GetAllActiveProductsAsync();

        /// <summary>
        /// Check if cache is initialized
        /// </summary>
        bool IsCacheInitialized { get; }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        Task<object> GetCacheStatsAsync();

        /// <summary>
        /// Clear all POS-related caches (call when products are modified)
        /// </summary>
        Task ClearCacheAsync();
    }
}
