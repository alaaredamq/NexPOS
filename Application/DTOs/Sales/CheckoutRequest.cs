using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Sales
{
    public class CheckoutRequest
    {
        // CartId is optional - if not provided, items will be used to create a cart
        public int? CartId { get; set; }
        
        // Items to create cart from (used when CartId is not provided)
        public List<CheckoutItemRequest>? Items { get; set; }
        
        /// <summary>
        /// Customer ID. Use 0 or null for walk-in customers (no specific customer).
        /// Nullable so the client can send "customerId": null for walk-in without the
        /// whole request failing to bind (a null can't deserialize into a non-nullable int).
        /// </summary>
        public int? CustomerId { get; set; }
        
        public int UserId { get; set; } // User/Cashier processing the checkout
        
        public decimal TotalAmount { get; set; }
        
        public decimal? AmountPaid { get; set; } // How much customer actually paid (for change calculation)
        
        public int PaymentMethodId { get; set; }
        
        public decimal? DiscountAmount { get; set; }
        
        public string? DiscountCode { get; set; }
        
        public decimal? DiscountPercentage { get; set; }
        
        public string? Notes { get; set; }
        
        public int CurrencyId { get; set; } = 1; // Default to USD
        
        public bool PrintReceipt { get; set; } = true;
    }
    
    public class CheckoutItemRequest
    {
        public int ProductId { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        
        /// <summary>
        /// Weight or volume amount for products sold by weight/volume (e.g., 0.5 for 500g or 0.5L).
        /// Null for products sold by piece.
        /// </summary>
        public decimal? WeightOrVolume { get; set; }
    }
}
