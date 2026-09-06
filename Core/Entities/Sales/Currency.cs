using Core.Entities.Base;

namespace Core.Entities.Sales;

public class Currency : BaseEntity, IEntityBase<int> {
    public string Name { get; set; }
    public float RateToUSD { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public string Code { get; set; }
    public string Notation { get; set; }
    
    public Currency(string name, float rateToUSD, bool isDefault, bool isActive, string code, string notation) {
        Name = name;
        RateToUSD = rateToUSD;
        IsDefault = isDefault;
        IsActive = isActive;
        Code = code;
        Notation = notation;
    }
    
    // override default of class
    public override string ToString() {
        return $"{Name} ({Code}) - Rate to USD: {RateToUSD}, Notation: {Notation}, Default: {IsDefault}, Active: {IsActive}";
    }
}