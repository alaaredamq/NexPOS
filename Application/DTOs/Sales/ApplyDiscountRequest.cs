namespace Application.DTOs.Sales
{
    public class ApplyDiscountRequest
    {
        public string SessionId { get; set; } = string.Empty;
        public decimal? DiscountAmount { get; set; }
        public string? DiscountCode { get; set; }
        public decimal? DiscountPercentage { get; set; }
    }
}
