using Core.Entities.Base;
using Core.Entities.Users;

namespace Core.Entities.Inventory;

public class CartSession : BaseEntity, IEntityBase<int>
{
    public string SessionId { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal? DiscountAmount { get; set; }
    public string? DiscountCode { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public string? Notes { get; set; }

    #region Navigation Properties

    public Customer? Customer { get; set; }
    public ICollection<CartSessionItem> CartItems { get; set; } = new List<CartSessionItem>();

    #endregion

    #region Constructors

    public CartSession(string sessionId, int? customerId = null)
    {
        SessionId = sessionId;
        CustomerId = customerId;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    protected CartSession() { }

    #endregion
}
