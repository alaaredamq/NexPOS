namespace Application.DTOs.Sales
{
    public class HoldCartRequest
    {
        public int? CustomerId { get; set; }
        public string? Notes { get; set; }
        public decimal? DiscountAmount { get; set; }
        public string? DiscountCode { get; set; }
        public decimal? DiscountPercentage { get; set; }
        public List<HoldCartItemRequest> Items { get; set; } = new();
    }

    public class HoldCartItemRequest
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal TotalPrice => Price * Quantity;
        
        /// <summary>
        /// Weight or volume amount for products sold by weight/volume (e.g., 0.5 for 500g or 0.5L).
        /// Null for products sold by piece.
        /// </summary>
        public decimal? WeightOrVolume { get; set; }
    }
}
