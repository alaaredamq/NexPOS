using Core.Entities.Base;
using Core.Entities.Sales;
using Core.Enum;

namespace Core.Entities.Users;

/// <summary>
/// One entry in a customer's debt ledger. Amount is signed: positive increases what the
/// customer owes the business, negative increases what the business owes the customer (or
/// records a payment). Customer.Balance is a running total kept in sync with this ledger so
/// it can be displayed without summing every transaction.
/// </summary>
public class CustomerDebtTransaction : BaseEntity, IEntityBase<int>
{
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    public decimal Amount { get; set; }
    public CustomerDebtTransactionType Type { get; set; }
    public string? Note { get; set; }

    public CustomerDebtTransaction(int customerId, int userId, decimal amount, CustomerDebtTransactionType type, string? note = null, int? orderId = null)
    {
        if (amount == 0)
            throw new ArgumentException("Amount cannot be zero.", nameof(amount));

        CustomerId = customerId;
        UserId = userId;
        Amount = amount;
        Type = type;
        Note = note;
        OrderId = orderId;
    }

    // Parameterless constructor for EF Core
    private CustomerDebtTransaction() { }
}
