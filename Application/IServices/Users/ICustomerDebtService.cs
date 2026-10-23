using Application.IServices.Base;
using Core.Entities.Users;

namespace Application.IServices.Users;

public interface ICustomerDebtService : IBaseService
{
    Task<List<CustomerDebtTransaction>> GetTransactionsAsync(int customerId);

    /// <summary>
    /// Records a manual debt adjustment (staff editing what a customer owes, or logging a
    /// payment). Positive amount increases what the customer owes; negative decreases it.
    /// </summary>
    Task<CustomerDebtTransaction> AddManualTransactionAsync(int customerId, int userId, decimal signedAmount, string? note);

    /// <summary>
    /// Records a POS sale placed on the customer's account. Amount must be positive - a sale
    /// always increases what the customer owes.
    /// </summary>
    Task<CustomerDebtTransaction> AddSaleTransactionAsync(int customerId, int userId, int orderId, decimal amount);
}
