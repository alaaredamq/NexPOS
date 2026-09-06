namespace Core.Entities.Products;

/// <summary>
/// Defines the unit types for products (piece, kilo, litre, etc.)
/// </summary>
public static class ProductUnit
{
    public const string Piece = "piece";
    public const string Kilogram = "kg";
    public const string Liter = "liter";
    public const string Pound = "lb";
    public const string Gallon = "gallon";
    public const string Box = "box";
    public const string Pack = "pack";
    
    /// <summary>
    /// Gets all available unit types
    /// </summary>
    public static string[] GetAllUnits() => new[]
    {
        Piece,
        Kilogram,
        Liter,
        Pound,
        Gallon,
        Box,
        Pack
    };
    
    /// <summary>
    /// Checks if the unit is a weight-based unit (kg, lb)
    /// </summary>
    public static bool IsWeightUnit(string? unit) => 
        unit == Kilogram || unit == Pound;
    
    /// <summary>
    /// Checks if the unit is a volume-based unit (liter, gallon)
    /// </summary>
    public static bool IsVolumeUnit(string? unit) => 
        unit == Liter || unit == Gallon;
    
    /// <summary>
    /// Checks if the unit requires decimal quantities (weight/volume units)
    /// </summary>
    public static bool RequiresDecimalQuantity(string? unit) => 
        IsWeightUnit(unit) || IsVolumeUnit(unit);
    
    /// <summary>
    /// Validates if the provided unit is a valid unit type
    /// </summary>
    public static bool IsValidUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
            return true; // Unit is optional
        
        return GetAllUnits().Contains(unit, StringComparer.OrdinalIgnoreCase);
    }
}
