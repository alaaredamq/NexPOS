using System.ComponentModel.DataAnnotations;

namespace Application.DTOs.Orders;

public class UpdateOrderStatusRequest
{
    [Required]
    public int OrderId { get; set; }
    
    [Required]
    [Range(0, 5, ErrorMessage = "Status must be between 0 and 5")]
    public int Status { get; set; }
}
