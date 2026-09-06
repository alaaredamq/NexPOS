namespace Application.DTOs.Sales
{
    public class CalculateTotalRequest
    {
        public int CartId { get; set; }
        public decimal? DiscountAmount { get; set; }
        public string? DiscountCode { get; set; }
        public decimal? DiscountPercentage { get; set; }
    }
}
