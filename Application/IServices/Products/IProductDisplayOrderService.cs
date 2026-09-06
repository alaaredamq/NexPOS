using Core.Entities.Products;

namespace Application.IServices.Products
{
    public interface IProductDisplayOrderService
    {
        Task<IEnumerable<ProductDisplayOrder>> GetAllAsync();
        Task<ProductDisplayOrder> GetByIdAsync(int id);
        Task<ProductDisplayOrder> CreateAsync(ProductDisplayOrder productDisplayOrder);
        Task<ProductDisplayOrder> UpdateAsync(ProductDisplayOrder productDisplayOrder);
        Task DeleteAsync(int id);
        Task<IEnumerable<ProductDisplayOrder>> GetActiveDisplayOrderAsync();
        Task UpdateDisplayOrderAsync(IEnumerable<ProductDisplayOrder> displayOrders);
    }
}
