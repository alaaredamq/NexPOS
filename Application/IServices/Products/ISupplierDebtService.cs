using Application.IServices.Base;
using Core.Entities.Products;

namespace Application.IServices.Products;

public interface ISupplierDebtService : IBaseService
{
    Task<List<SupplierDebtTransaction>> GetTransactionsAsync(int supplierId);

    /// <summary>
    /// Records a manual debt adjustment (staff editing what is owed to a supplier, or logging a
    /// payment). Positive amount increases what the business owes the supplier; negative decreases it.
    /// </summary>
    Task<SupplierDebtTransaction> AddManualTransactionAsync(int supplierId, int userId, decimal signedAmount, string? note);
}
