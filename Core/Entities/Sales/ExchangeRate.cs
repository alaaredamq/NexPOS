using Core.Entities.Base;

namespace Core.Entities.Sales;

public class ExchangeRate : ISSoftDeletable, IEntityBase<int>
{
    public int FromCurrencyId { get; set; }
    public int ToCurrencyId { get; set; }
    public decimal Rate { get; set; }
    public int? SupplierId { get; set; }

    #region Navigation Properties

    public Currency FromCurrency { get; set; } = null!;
    public Currency ToCurrency { get; set; } = null!;
    public Products.Supplier? Supplier { get; set; }

    #endregion

    #region Constructors

    public ExchangeRate(int fromCurrencyId, int toCurrencyId, decimal rate, int? supplierId = null)
    {
        FromCurrencyId = fromCurrencyId;
        ToCurrencyId = toCurrencyId;
        Rate = rate;
        SupplierId = supplierId;
    }

    protected ExchangeRate() { }

    #endregion
}


