using Application.IServices.Users;
using Core.Entities.Users;
using Core.Enum;
using Core.IRepositories.Users;

namespace Application.Services.Users;

public class CustomerDebtService : ICustomerDebtService
{
    private readonly ICustomerDebtTransactionRepository _transactionRepo;
    private readonly ICustomerRepository _customerRepo;

    public CustomerDebtService(ICustomerDebtTransactionRepository transactionRepo, ICustomerRepository customerRepo)
    {
        _transactionRepo = transactionRepo;
        _customerRepo = customerRepo;
    }

    public async Task<List<CustomerDebtTransaction>> GetTransactionsAsync(int customerId)
        => await _transactionRepo.GetByCustomerIdAsync(customerId);

    public async Task<CustomerDebtTransaction> AddManualTransactionAsync(int customerId, int userId, decimal signedAmount, string? note)
        => await AddTransactionAsync(customerId, userId, signedAmount, CustomerDebtTransactionType.ManualAdjustment, note, orderId: null);

    public async Task<CustomerDebtTransaction> AddSaleTransactionAsync(int customerId, int userId, int orderId, decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Sale amount must be greater than zero.", nameof(amount));

        return await AddTransactionAsync(customerId, userId, amount, CustomerDebtTransactionType.Sale, note: null, orderId);
    }

    public async Task<decimal> ReverseSaleAsync(int orderId, int userId, string? note = null)
    {
        var open = await FindOpenSaleAsync(orderId);
        if (open == null)
            return 0m;

        await AddTransactionAsync(open.Value.HolderId, userId, -open.Value.Amount, CustomerDebtTransactionType.SaleReversal, note, orderId);
        return open.Value.Amount;
    }

    public async Task<decimal> MoveSaleAsync(int orderId, int newCustomerId, int userId, string? note = null)
    {
        var open = await FindOpenSaleAsync(orderId);
        if (open == null || open.Value.HolderId == newCustomerId)
            return 0m;

        await AddTransactionAsync(open.Value.HolderId, userId, -open.Value.Amount, CustomerDebtTransactionType.SaleTransfer, note, orderId);
        await AddTransactionAsync(newCustomerId, userId, open.Value.Amount, CustomerDebtTransactionType.SaleTransfer, note, orderId);
        return open.Value.Amount;
    }

    /// <summary>The order's debt that is still on somebody's account: who holds it (the sale's customer, or whoever it was last moved to) and how much. Null when it is not a debt sale or was reversed.</summary>
    private async Task<(int HolderId, decimal Amount)?> FindOpenSaleAsync(int orderId)
    {
        var entries = (await _transactionRepo.GetByOrderIdAsync(orderId)).OrderBy(t => t.Id).ToList();
        var sale = entries.FirstOrDefault(t => t.Type == CustomerDebtTransactionType.Sale);
        if (sale == null || entries.Any(t => t.Type == CustomerDebtTransactionType.SaleReversal))
            return null;

        var holder = entries.LastOrDefault(t => t.Type == CustomerDebtTransactionType.SaleTransfer && t.Amount > 0)?.CustomerId ?? sale.CustomerId;
        return (holder, sale.Amount);
    }

    private async Task<CustomerDebtTransaction> AddTransactionAsync(int customerId, int userId, decimal signedAmount, CustomerDebtTransactionType type, string? note, int? orderId)
    {
        var customer = await _customerRepo.GetByIdAsync(customerId);

        var transaction = new CustomerDebtTransaction(customerId, userId, signedAmount, type, note, orderId);
        await _transactionRepo.SaveAsync(transaction);

        customer.Balance += signedAmount;
        await _customerRepo.SaveAsync(customer);

        return transaction;
    }
}
