using Application.IServices.Products;
using Core.Entities.Products;
using Core.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Products
{
    public class ProductDisplayOrderService : IProductDisplayOrderService
    {
        private readonly IProductDisplayOrderRepository _repository;

        public ProductDisplayOrderService(IProductDisplayOrderRepository repository)
        {
            _repository = repository;
        }

        public async Task<IEnumerable<ProductDisplayOrder>> GetAllAsync()
        {
            return await _repository.GetAllAsync();
        }

        public async Task<ProductDisplayOrder> GetByIdAsync(int id)
        {
            return await _repository.GetByIdAsync(id);
        }

        public async Task<ProductDisplayOrder> CreateAsync(ProductDisplayOrder productDisplayOrder)
        {
            productDisplayOrder.CreatedAt = DateTime.UtcNow;
            productDisplayOrder.UpdatedAt = DateTime.UtcNow;
            return await _repository.SaveAsync(productDisplayOrder);
        }

        public async Task<ProductDisplayOrder> UpdateAsync(ProductDisplayOrder productDisplayOrder)
        {
            productDisplayOrder.UpdatedAt = DateTime.UtcNow;
            return await _repository.SaveAsync(productDisplayOrder);
        }

        public async Task DeleteAsync(int id)
        {
            await _repository.DeleteAsync(id);
        }

        public async Task<IEnumerable<ProductDisplayOrder>> GetActiveDisplayOrderAsync()
        {
            return await _repository.GetActiveDisplayOrderAsync();
        }

        public async Task UpdateDisplayOrderAsync(IEnumerable<ProductDisplayOrder> displayOrders)
        {
            await _repository.UpdateDisplayOrderAsync(displayOrders);
        }
    }
}
