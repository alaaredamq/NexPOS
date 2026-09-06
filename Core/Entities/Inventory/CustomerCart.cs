using Core.Entities.Base;
using Core.Entities.Users;

namespace Core.Entities.Inventory;

public class CustomerCart : ISSoftDeletable, IEntityBase<int>
{
    public int CustomerId { get; set; }
    public int CartId { get; set; }
    public bool IsOnHold { get; set; }

    public Customer Customer { get; set; } = null!;
    public Cart Cart { get; set; } = null!;

    public CustomerCart(int customerId, int cartId)
    {
        CustomerId = customerId;
        CartId = cartId;
        IsOnHold = true;
    }

    protected CustomerCart() { }
}


