using Application.Services.Base;
using Application.IServices.Products;
using Core.Entities.Products;
using Core.IRepositories.Products;

namespace Application.Services.Products;

public class MainScreenProductService : IMainScreenProductService
{
    private readonly IMainScreenProductRepository _repository;

    public MainScreenProductService(IMainScreenProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<MainScreenProduct>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<MainScreenProduct?> GetByIdAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<MainScreenProduct> CreateAsync(MainScreenProduct mainScreenProduct)
    {
        return await _repository.SaveAsync(mainScreenProduct);
    }

    public async Task<MainScreenProduct> UpdateAsync(MainScreenProduct mainScreenProduct)
    {
        return await _repository.SaveAsync(mainScreenProduct);
    }

    public async Task DeleteAsync(int id)
    {
        var mainScreenProduct = await _repository.GetByIdAsync(id);
        if (mainScreenProduct != null)
        {
            await _repository.DeleteAsync(mainScreenProduct);
        }
    }

    public async Task<IEnumerable<MainScreenProduct>> GetPinnedProductsAsync()
    {
        return await _repository.GetPinnedProductsAsync();
    }

    public async Task<IEnumerable<MainScreenProduct>> GetMainScreenProductsAsync()
    {
        return await _repository.GetMainScreenProductsAsync();
    }

    public async Task<MainScreenProduct?> GetByProductIdAsync(int productId)
    {
        return await _repository.GetByProductIdAsync(productId);
    }

    public async Task PinProductAsync(int productId, int displayOrder)
    {
        await _repository.PinProductAsync(productId, displayOrder);
    }

    public async Task UnpinProductAsync(int productId)
    {
        await _repository.UnpinProductAsync(productId);
    }

    public async Task UpdateDisplayOrderAsync(int productId, int newOrder)
    {
        await _repository.UpdateDisplayOrderAsync(productId, newOrder);
    }

    public async Task<IEnumerable<Product>> SearchProductsForMainScreenAsync(string searchTerm, int limit = 20)
    {
        return await _repository.SearchProductsForMainScreenAsync(searchTerm, limit);
    }

    public async Task ReorderProductsAsync(List<int> productIds)
    {
        for (int i = 0; i < productIds.Count; i++)
        {
            await _repository.UpdateDisplayOrderAsync(productIds[i], i + 1);
        }
    }
}
