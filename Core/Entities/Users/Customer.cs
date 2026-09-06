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
    public decimal Dept { get; set; }

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

    #region Methods

    public void AddDept(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be greater than zero.", nameof(amount));

        Dept += amount;
    }

    #endregion
}