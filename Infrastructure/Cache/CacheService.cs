using Common.Helpers;
using Common.Mapper;
using Infrastructure.Data;
using Core.DTOs;
using Core.ICache;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Infrastructure.Cache;

/// <summary>
/// In-process memory cache. This is a single-instance desktop application, so there is no
/// distributed cache — every entry lives in <see cref="IMemoryCache"/> only (no per-operation
/// JSON serialization). The async methods are kept for API compatibility with existing callers.
/// </summary>
public class CacheService : ICacheService
{
    private readonly IMemoryCache _memoryCache;
    private readonly IDbContextFactory<ErpDbContext> _contextFactory;

    // Lets Clear() drop every entry at once without disposing the shared IMemoryCache.
    private CancellationTokenSource _resetCts = new();

    public CacheService(IMemoryCache memoryCache, IDbContextFactory<ErpDbContext> contextFactory)
    {
        _memoryCache = memoryCache;
        _contextFactory = contextFactory;
    }

    private MemoryCacheEntryOptions EntryOptions(int minutes) =>
        new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(minutes)
        }.AddExpirationToken(new CancellationChangeToken(_resetCts.Token));

    public T? CreateOrGet<T>(string key, Func<T> create, int cacheTimeInMinutes = 60)
    {
        if (_memoryCache.TryGetValue(key, out T? data))
        {
            return data;
        }

        var result = create();
        _memoryCache.Set(key, result, EntryOptions(cacheTimeInMinutes));
        return result;
    }

    public T? Get<T>(string key) => _memoryCache.Get<T>(key);

    public void Set<T>(string key, T data, int cacheTimeInMinutes = 60) =>
        _memoryCache.Set(key, data, EntryOptions(cacheTimeInMinutes));

    public void Remove(string key) => _memoryCache.Remove(key);

    public void Clear()
    {
        var previous = Interlocked.Exchange(ref _resetCts, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
    }

    public bool Exists(string key) => _memoryCache.TryGetValue(key, out _);

    public async Task<T?> CreateOrGetAsync<T>(string key, Func<Task<T>> create, int cacheTimeInMinutes = 60)
    {
        if (_memoryCache.TryGetValue(key, out T? data))
        {
            return data;
        }

        var result = await create();
        if (result != null)
        {
            _memoryCache.Set(key, result, EntryOptions(cacheTimeInMinutes));
        }

        return result;
    }

    public Task<T?> GetAsync<T>(string key) => Task.FromResult(_memoryCache.Get<T>(key));

    public Task SetAsync<T>(string key, T data, int cacheTimeInMinutes = 60)
    {
        _memoryCache.Set(key, data, EntryOptions(cacheTimeInMinutes));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _memoryCache.Remove(key);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string key) => Task.FromResult(_memoryCache.TryGetValue(key, out _));

    #region Products

    public List<ProductDTO>? GetProducts()
    {
        if (_memoryCache.TryGetValue("ProductsDtos", out List<ProductDTO>? cachedProducts))
        {
            return cachedProducts;
        }

        try
        {
            using var context = _contextFactory.CreateDbContext();
            var dtos = EntityMapper.Map<List<ProductDTO>>(context.Products.AsNoTracking().ToList());

            if (dtos.IsSuccessful && !dtos.Result.IsNullOrEmptyOrDefault())
            {
                return CreateOrGet("ProductsDtos", () => dtos.Result!);
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    #endregion

    #region POS Fast Search

    public Task<List<ProductDTO>?> GetPOSProductsAsync() =>
        GetAsync<List<ProductDTO>>("pos_products_cache");

    public Task SetPOSProductsAsync(List<ProductDTO> products, int cacheTimeInMinutes = 60) =>
        SetAsync("pos_products_cache", products, cacheTimeInMinutes);

    public Task<Dictionary<string, int>?> GetBarcodeIndexAsync() =>
        GetAsync<Dictionary<string, int>>("pos_barcode_index");

    public Task SetBarcodeIndexAsync(Dictionary<string, int> barcodeIndex, int cacheTimeInMinutes = 60) =>
        SetAsync("pos_barcode_index", barcodeIndex, cacheTimeInMinutes);

    public async Task ClearPOSCachesAsync()
    {
        await RemoveAsync("pos_products_cache");
        await RemoveAsync("pos_barcode_index");
    }

    #endregion
}
