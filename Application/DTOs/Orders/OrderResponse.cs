using Core.Discounts;
using Core.Entities.Sales;
using Core.Enum;

namespace Application.DTOs.Orders;

public class OrderResponse
{
    public int Id { get; set; }
    /// <summary>
    /// Customer ID. Null for walk-in/anonymous customers.
    /// </summary>
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = null!;
    public int CartId { get; set; }
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; }
    public string StatusName => Status.ToString();
    public int CurrencyId { get; set; }
    public string CurrencyCode { get; set; } = null!;
    public string CurrencyNotation { get; set; } = null!;
    public int ItemCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Cart details for adjustment calculation
    public decimal CartSubtotal { get; set; }
    /// <summary>The discount as money off, whether it was entered as an amount or as a percentage.</summary>
    public decimal CartDiscountAmount { get; set; }
    /// <summary>The discount as a percentage of <see cref="CartSubtotal"/>, whether it was entered as an amount or as a percentage.</summary>
    public decimal CartDiscountPercentage { get; set; }

    public static OrderResponse FromEntity(Order order)
    {
        var cartSubtotal = order.Cart?.CartItems?.Sum(ci => ci.TotalPrice) ?? 0;
        var discount = DiscountBreakdown.From(cartSubtotal, order.Cart?.DiscountAmount, order.Cart?.DiscountPercentage);

        return new OrderResponse
        {
            Id = order.Id,
            CustomerId = order.CustomerId,
            CustomerName = order.Customer != null 
                ? $"{order.Customer.FirstName} {order.Customer.LastName}".Trim() 
                : "Walk-in Customer",
            CartId = order.CartId,
            OrderDate = order.OrderDate,
            TotalAmount = order.TotalAmount,
            Status = (OrderStatus)order.Status,
            CurrencyId = order.CurrencyId,
            CurrencyCode = order.Currency?.Code ?? "USD",
            CurrencyNotation = order.Currency?.Notation ?? "$",
            ItemCount = order.Cart?.CartItems?.Count ?? 0,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt,
            CartSubtotal = cartSubtotal,
            CartDiscountAmount = discount.Amount,
            CartDiscountPercentage = discount.Percentage
        };
    }
}
