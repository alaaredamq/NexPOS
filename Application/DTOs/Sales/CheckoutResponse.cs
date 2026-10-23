namespace Application.DTOs.Sales
{
    public class CheckoutResponse
    {
        public int OrderId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal ChangeAmount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public DateTime OrderDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ReceiptUrl { get; set; } = string.Empty;

        /// <summary>
        /// Whether the receipt reached the thermal printer. Null when the request didn't ask
        /// for printing (<see cref="CheckoutRequest.PrintReceipt"/> false).
        /// </summary>
        public bool? ReceiptPrinted { get; set; }
        public List<CheckoutItemResponse> Items { get; set; } = new List<CheckoutItemResponse>();
    }
    
    public class CheckoutItemResponse
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal TotalPrice { get; set; }
        
        /// <summary>
        /// Weight or volume amount for products sold by weight/volume (e.g., 0.5 for 500g or 0.5L).
        /// Null for products sold by piece.
        /// </summary>
        public decimal? WeightOrVolume { get; set; }
    }
}
