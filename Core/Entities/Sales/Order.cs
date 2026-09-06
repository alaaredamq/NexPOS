using Common.Helpers;
using Core.Entities.Base;
using Core.Entities.Inventory;
using Core.Entities.Users;
using Core.Enum;

namespace Core.Entities.Sales;

public class Order: ISSoftDeletable, IEntityBase<int>
{
    /// <summary>
    /// Customer ID. Null for walk-in/anonymous customers.
    /// </summary>
    public int? CustomerId { get; set; }
    public int CartId { get; set; }
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public int Status { get; set; } = (int)OrderStatus.Pending;
    public int CurrencyId { get; set; }
    public int? CashDrawerSessionId { get; set; }

    #region Navigation Properties

    public Cart Cart { get; set; } = null!;
    public Customer? Customer { get; set; }
    public Currency Currency { get; set; } = null!;
    public CashDrawerSession? CashDrawerSession { get; set; }

    #endregion

    #region Constructors

    public Order(int? customerId, int cartId, decimal totalAmount, int? status, int? currencyId)
    {
        CustomerId = customerId;
        CartId = cartId;
        OrderDate = DateTime.UtcNow;
        TotalAmount = totalAmount;
        CreatedAt = DateTime.UtcNow;
        Status = status.IsNullOrEmpty() ? (int)OrderStatus.Pending : status.GetValueOrDefault();
        CurrencyId = currencyId.IsNullOrEmpty() ? 1 : currencyId.GetValueOrDefault();
    }

    public Order(int? customerId, int cartId, decimal totalAmount)
    {
        CustomerId = customerId;
        CartId = cartId;
        OrderDate = DateTime.UtcNow;
        TotalAmount = totalAmount;
        CreatedAt = DateTime.UtcNow;
    }

    #endregion
}