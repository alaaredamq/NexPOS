using Application.DTOs.Sales;

namespace Application.IServices.Sales
{
    public interface IPOSCheckoutService
    {
        /// <summary>
        /// Process checkout for POS system
        /// </summary>
        Task<CheckoutResponse> ProcessCheckoutAsync(CheckoutRequest request);
        
        /// <summary>
        /// Calculate total with discounts and taxes
        /// </summary>
        Task<decimal> CalculateTotalAsync(int cartId, decimal? discountAmount = null, string? discountCode = null, decimal? discountPercentage = null);
        
        /// <summary>
        /// Validate cart before checkout
        /// </summary>
        Task<bool> ValidateCartAsync(int cartId);
        
        /// <summary>
        /// Get available payment methods
        /// </summary>
        Task<List<PaymentMethodDto>> GetPaymentMethodsAsync();
        
        /// <summary>
        /// Process payment
        /// </summary>
        Task<PaymentResult> ProcessPaymentAsync(decimal amount, int paymentMethodId, string? reference = null);
        
        /// <summary>
        /// Get available discounts
        /// </summary>
        Task<List<Core.Entities.Sales.Discount>> GetDiscountsAsync();
    }
    
    public class PaymentMethodDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public bool RequiresChange { get; set; } = false;
    }
    
    public class PaymentResult
    {
        public bool Success { get; set; }
        public string TransactionId { get; set; } = string.Empty;
        public decimal PaidAmount { get; set; }
        public decimal ChangeAmount { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
