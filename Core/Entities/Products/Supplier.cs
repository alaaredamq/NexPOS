using Core.Entities.Base;
using Core.Entities.Sales;

namespace Core.Entities.Products;

public class Supplier : ISSoftDeletable, IEntityBase<int>
{   
    public string Name { get; set; } = null!;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public double DiscountRate { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }
    public bool IsActive { get; set; }
    public int CurrencyId { get; set; }
    public decimal DebtToSupplier { get; set; } // Money I owe the supplier
    public decimal DebtFromSupplier { get; set; } // Money the supplier owes me
    public int? OrderNumber { get; set; }

    #region Navigation Properties

    public ICollection<Product> Products { get; set; } = new List<Product>();
    public Currency Currency { get; set; } = null!;

    #endregion

    #region Constructors

    public Supplier(string name, string? email = null, string? phone = null, string? address = null, string? city = null, string? country = null, string? postalCode = null)
    {
        Name = name;
        Email = email;
        Phone = phone;
        Address = address;
        City = city;
        Country = country;
        PostalCode = postalCode;
        IsActive = true;
    }

    #endregion
}