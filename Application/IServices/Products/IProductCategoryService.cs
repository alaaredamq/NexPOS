using Application.IServices.Base;
using Core.Entities.Products;

namespace Application.IServices.Products;

public interface IProductCategoryService : IBaseService
{
    Task<List<ProductCategory>> GetAllAsync();
    Task<ProductCategory> GetByIdAsync(int id);
    Task<ProductCategory> CreateAsync(ProductCategory category);
    Task UpdateAsync(int id, ProductCategory category);
    Task DeleteAsync(int id);
}


