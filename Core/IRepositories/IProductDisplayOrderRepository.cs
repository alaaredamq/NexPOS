using Core.Entities.Products;
using Core.IRepositories.Base;

namespace Core.IRepositories
{
    public interface IProductDisplayOrderRepository : IRepository<ProductDisplayOrder>
    {
        Task<IEnumerable<ProductDisplayOrder>> GetActiveDisplayOrderAsync();
        Task UpdateDisplayOrderAsync(IEnumerable<ProductDisplayOrder> displayOrders);
    }
}
