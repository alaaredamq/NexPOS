using Core.Entities.Base;
using Core.Entities.Users;
using Core.Enum;

namespace Core.Entities.Products;

/// <summary>
/// One entry in a supplier's debt ledger. Amount is signed: positive increases what the
/// business owes the supplier (e.g. goods taken on credit), negative decreases it (a payment
/// to the supplier, or the supplier owing the business). Supplier.Balance is a running total
/// kept in sync with this ledger so it can be displayed without summing every transaction.
/// </summary>
public class SupplierDebtTransaction : BaseEntity, IEntityBase<int>
{
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public decimal Amount { get; set; }
    public SupplierDebtTransactionType Type { get; set; }
    public string? Note { get; set; }

    public SupplierDebtTransaction(int supplierId, int userId, decimal amount, SupplierDebtTransactionType type, string? note = null)
    {
        if (amount == 0)
            throw new ArgumentException("Amount cannot be zero.", nameof(amount));

        SupplierId = supplierId;
        UserId = userId;
        Amount = amount;
        Type = type;
        Note = note;
    }

    // Parameterless constructor for EF Core
    private SupplierDebtTransaction() { }
}
