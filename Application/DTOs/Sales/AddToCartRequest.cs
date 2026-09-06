namespace Application.DTOs.Sales
{
    public class AddToCartRequest
    {
        public string SessionId { get; set; } = string.Empty;
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        
        /// <summary>
        /// Weight or volume amount for products sold by weight/volume (e.g., 0.5 for 500g or 0.5L).
        /// Null for products sold by piece.
        /// </summary>
        public decimal? WeightOrVolume { get; set; }
    }
}
