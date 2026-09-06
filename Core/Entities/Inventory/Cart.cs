using Core.Entities.Base;
using Core.Entities.Users;

namespace Core.Entities.Inventory;

public class Cart: BaseEntity, IEntityBase<int>
{
    /// <summary>
    /// Customer ID. Null for walk-in/anonymous customers.
    /// </summary>
    public int? CustomerId { get; set; }
    public bool IsActive { get; set; }
    public bool IsDeleted { get; set; }
    public decimal? DiscountAmount { get; set; }
    public string? DiscountCode { get; set; }
    public decimal? DiscountPercentage { get; set; }

    #region Navigation Properties

    public Customer? Customer { get; set; }
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();

    #endregion

    #region Constructors

    public Cart(int? customerId)
    {
        CustomerId = customerId;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    #endregion
}