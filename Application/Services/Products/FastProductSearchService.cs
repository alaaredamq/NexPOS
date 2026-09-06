using Application.IServices.Products;
using Core.DTOs;
using Core.IRepositories.Products;
using Core.ICache;
using Microsoft.Extensions.Logging;

namespace Application.Services.Products
{
    /// <summary>
    /// Fast product search service with caching for POS operations
    /// Uses the existing CacheService for memory and distributed caching
    /// </summary>
    public class FastProductSearchService : IFastProductSearchService
    {
        private readonly IProductRepository _productRepository;
        private readonly ICacheService _cacheService;
        private readonly ILogger<FastProductSearchService> _logger;
        
        private const string PRODUCTS_CACHE_KEY = "pos_products_cache";
        private const string BARCODE_INDEX_KEY = "pos_barcode_index";
        private const string NAME_INDEX_KEY = "pos_name_index";
        private const int CACHE_DURATION_MINUTES = 60; // 1 hour
        
        private static readonly SemaphoreSlim _cacheSemaphore = new(1, 1);

        public FastProductSearchService(
            IProductRepository productRepository,
            ICacheService cacheService,
            ILogger<FastProductSearchService> logger)
        {
            _productRepository = productRepository;
            _cacheService = cacheService;
            _logger = logger;
        }

        public bool IsCacheInitialized => _cacheService.Exists(PRODUCTS_CACHE_KEY);

        public async Task<ProductDTO?> SearchByBarcodeAsync(string barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode))
                
                return null;

            // Trim whitespace from barcode
            
            barcode = barcode.Trim();

            await EnsureCacheInitializedAsync();

            try
            {
                var barcodeIndex = await _cacheService.GetBarcodeIndexAsync();
                if (barcodeIndex != null && barcodeIndex.TryGetValue(barcode.ToUpperInvariant(), out int productId))
                {
                    return await GetByIdAsync(productId);
                }
                
                // Fallback: Query database directly if cache lookup fails
                _logger.LogDebug("Barcode not found in cache index, trying database lookup: {Barcode}", barcode);
                var products = await _productRepository.GetActiveProductsAsync();
                var product = products.FirstOrDefault(p => 
                    !string.IsNullOrWhiteSpace(p.Barcode) && 
                    p.Barcode.Trim().Equals(barcode, StringComparison.OrdinalIgnoreCase));
                
                if (product != null)
                {
                    _logger.LogInformation("Found product by barcode in database (cache miss): {Barcode}, ProductId: {ProductId}", barcode, product.Id);
                    // Refresh cache to include this product
                    await RefreshCacheAsync();
                    return MapToDTO(product);
                }
                
                _logger.LogDebug("Barcode not found in database: {Barcode}", barcode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching product by barcode: {Barcode}", barcode);
            }

            return null;
        }

        public async Task<IEnumerable<ProductDTO>> SearchByNameAsync(string searchTerm, int limit = 10)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return Enumerable.Empty<ProductDTO>();

            await EnsureCacheInitializedAsync();

            try
            {
                var results = new List<ProductDTO>();
                var searchLower = searchTerm.ToLowerInvariant();

                var products = await _cacheService.GetPOSProductsAsync();
                if (products != null)
                {
                    foreach (var product in products)
                    {
                        if (product.Name.ToLowerInvariant().Contains(searchLower))
                        {
                            results.Add(product);
                            if (results.Count >= limit)
                                break;
                        }
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching products by name: {SearchTerm}", searchTerm);
                return Enumerable.Empty<ProductDTO>();
            }
        }


        public async Task<ProductDTO?> GetByIdAsync(int productId)
        {
            await EnsureCacheInitializedAsync();

            try
            {
                var products = await _cacheService.GetPOSProductsAsync();
                if (products != null)
                {
                    return products.FirstOrDefault(p => p.Id == productId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting product by ID: {ProductId}", productId);
            }

            return null;
        }

        public async Task RefreshCacheAsync()
        {
            await _cacheSemaphore.WaitAsync();
            try
            {
                _logger.LogInformation("Refreshing product cache...");
                
                // Load all active products
                var products = await _productRepository.GetActiveProductsAsync();
                var productDtos = products.Select(MapToDTO).ToList();

                // Create indexes for fast lookup
                var barcodeIndex = new Dictionary<string, int>();

                foreach (var product in productDtos)
                {
                    // Index by barcode (trim whitespace)
                    if (!string.IsNullOrWhiteSpace(product.Barcode))
                    {
                        var trimmedBarcode = product.Barcode.Trim();
                        if (!string.IsNullOrEmpty(trimmedBarcode))
                        {
                            barcodeIndex[trimmedBarcode.ToUpperInvariant()] = product.Id;
                        }
                    }
                }

                // Cache using CacheService dedicated POS methods (both memory and distributed cache)
                await _cacheService.SetPOSProductsAsync(productDtos, CACHE_DURATION_MINUTES);
                await _cacheService.SetBarcodeIndexAsync(barcodeIndex, CACHE_DURATION_MINUTES);

                _logger.LogInformation("Product cache refreshed with {Count} products", productDtos.Count);
            }
            finally
            {
                _cacheSemaphore.Release();
            }
        }

        public async Task<IEnumerable<ProductDTO>> GetAllActiveProductsAsync()
        {
            await EnsureCacheInitializedAsync();

            var products = await _cacheService.GetPOSProductsAsync();
            return products ?? Enumerable.Empty<ProductDTO>();
        }

        public async Task<object> GetCacheStatsAsync()
        {
            await EnsureCacheInitializedAsync();

            var stats = new
            {
                IsInitialized = IsCacheInitialized,
                ProductCount = 0,
                BarcodeIndexCount = 0,
                LastRefresh = DateTime.UtcNow
            };

            var products = await _cacheService.GetPOSProductsAsync();
            if (products != null)
            {
                var barcodeIndex = await _cacheService.GetBarcodeIndexAsync();
                var barcodeCount = barcodeIndex?.Count ?? 0;

                return new
                {
                    IsInitialized = true,
                    ProductCount = products.Count,
                    BarcodeIndexCount = barcodeCount,
                    LastRefresh = DateTime.UtcNow
                };
            }

            return stats;
        }

        public async Task ClearCacheAsync()
        {
            try
            {
                await _cacheService.ClearPOSCachesAsync();
                _logger.LogInformation("POS product caches cleared successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing POS product caches");
                throw;
            }
        }

        private async Task EnsureCacheInitializedAsync()
        {
            if (!IsCacheInitialized)
            {
                await RefreshCacheAsync();
            }
        }

        private ProductDTO MapToDTO(Core.Entities.Products.Product product)
        {
            return new ProductDTO
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                Barcode = product.Barcode,
                SellingPrice = product.SellingPrice,
                CostPrice = product.CostPrice,
                StockQuantity = product.StockQuantity,
                MinimumStock = product.MinimumStock,
                CategoryId = product.CategoryId,
                Category = product.Category,
                SupplierId = product.SupplierId,
                Supplier = product.Supplier,
                CurrencyId = product.CurrencyId,
                Currency = product.Currency,
                BrandId = product.BrandId,
                IsActive = product.IsActive,
                IsPack = product.IsPack,
                PackSize = product.PackSize,
                SingleProductId = product.SingleProductId,
                CreatedAt = product.CreatedAt,
                UpdatedAt = product.UpdatedAt,
                // Apply supplier discount rate
                SupplierDiscountRate = product.Supplier?.DiscountRate ?? 0
            };
        }
    }       
}
