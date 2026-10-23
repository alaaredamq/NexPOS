using Application.IServices.Products;
using Core.Entities.Products;
using Core.Enum;
using Core.IRepositories.Products;

namespace Application.Services.Products;

public class SupplierDebtService : ISupplierDebtService
{
    private readonly ISupplierDebtTransactionRepository _transactionRepo;
    private readonly ISupplierRepository _supplierRepo;

    public SupplierDebtService(ISupplierDebtTransactionRepository transactionRepo, ISupplierRepository supplierRepo)
    {
        _transactionRepo = transactionRepo;
        _supplierRepo = supplierRepo;
    }

    public async Task<List<SupplierDebtTransaction>> GetTransactionsAsync(int supplierId)
        => await _transactionRepo.GetBySupplierIdAsync(supplierId);

    public async Task<SupplierDebtTransaction> AddManualTransactionAsync(int supplierId, int userId, decimal signedAmount, string? note)
    {
        var supplier = await _supplierRepo.GetByIdAsync(supplierId);

        var transaction = new SupplierDebtTransaction(supplierId, userId, signedAmount, SupplierDebtTransactionType.ManualAdjustment, note);
        await _transactionRepo.SaveAsync(transaction);

        supplier.Balance += signedAmount;
        await _supplierRepo.SaveAsync(supplier);

        return transaction;
    }
}
