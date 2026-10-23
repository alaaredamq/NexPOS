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
