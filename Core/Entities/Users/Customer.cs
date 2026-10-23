using Core.Entities.Base;
using Core.Entities.Inventory;
using Core.Entities.Sales;

namespace Core.Entities.Users;

public class Customer: ISSoftDeletable, IEntityBase<int>
{
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Running debt balance, kept in sync with <see cref="DebtTransactions"/>. Positive means
    /// the customer owes the business; negative means the business owes the customer.
    /// </summary>
    public decimal Balance { get; set; }

    #region Address

    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }

    #endregion
    public bool IsActive { get; set; }

    #region Navigation Properties

    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Cart> Carts { get; set; } = new List<Cart>();
    public ICollection<CustomerCart> CustomerCarts { get; set; } = new List<CustomerCart>();
    public ICollection<CustomerDebtTransaction> DebtTransactions { get; set; } = new List<CustomerDebtTransaction>();

    #endregion

    #region Constructors

    public Customer(string firstName, string lastName, string email, string? phoneNumber = null, string? address = null, string? city = null, string? country = null, string? postalCode = null)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        PhoneNumber = phoneNumber;
        Address = address;
        City = city;
        Country = country;
        PostalCode = postalCode;
        IsActive = true;
    }

    #endregion

}