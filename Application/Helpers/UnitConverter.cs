namespace Application.Helpers;

/// <summary>
/// Unit Converter Helper for C#
/// Handles conversions between different units (kg, g, mg, liter, ml, etc.)
/// </summary>
public static class UnitConverter
{
    // Weight units (base: kg)
    private static readonly Dictionary<string, decimal> WeightMultipliers = new(StringComparer.OrdinalIgnoreCase)
    {
        { "kg", 1m },
        { "kilogram", 1m },
        { "kilo", 1m },
        { "g", 0.001m },
        { "gram", 0.001m },
        { "mg", 0.000001m },
        { "milligram", 0.000001m },
        { "lb", 0.453592m },
        { "pound", 0.453592m },
        { "oz", 0.0283495m },
        { "ounce", 0.0283495m }
    };

    // Volume units (base: liter)
    private static readonly Dictionary<string, decimal> VolumeMultipliers = new(StringComparer.OrdinalIgnoreCase)
    {
        { "liter", 1m },
        { "l", 1m },
        { "litre", 1m },
        { "ml", 0.001m },
        { "milliliter", 0.001m },
        { "gallon", 3.78541m },
        { "floz", 0.0295735m }
    };

    /// <summary>
    /// Check if unit is a weight unit
    /// </summary>
    public static bool IsWeightUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return false;
        return WeightMultipliers.ContainsKey(unit);
    }

    /// <summary>
    /// Check if unit is a volume unit
    /// </summary>
    public static bool IsVolumeUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return false;
        return VolumeMultipliers.ContainsKey(unit);
    }

    /// <summary>
    /// Check if unit requires conversion (weight or volume)
    /// </summary>
    public static bool RequiresConversion(string? unit)
    {
        return IsWeightUnit(unit) || IsVolumeUnit(unit);
    }

    /// <summary>
    /// Convert quantity from one unit to another
    /// </summary>
    public static decimal Convert(decimal quantity, string fromUnit, string toUnit)
    {
        if (string.IsNullOrWhiteSpace(fromUnit) || string.IsNullOrWhiteSpace(toUnit) ||
            string.Equals(fromUnit, toUnit, StringComparison.OrdinalIgnoreCase))
        {
            return quantity;
        }

        var from = fromUnit.ToLower();
        var to = toUnit.ToLower();

        // Weight conversions
        if (IsWeightUnit(from) && IsWeightUnit(to))
        {
            var fromMultiplier = WeightMultipliers.GetValueOrDefault(from, 1m);
            var toMultiplier = WeightMultipliers.GetValueOrDefault(to, 1m);
            return (quantity * fromMultiplier) / toMultiplier;
        }

        // Volume conversions
        if (IsVolumeUnit(from) && IsVolumeUnit(to))
        {
            var fromMultiplier = VolumeMultipliers.GetValueOrDefault(from, 1m);
            var toMultiplier = VolumeMultipliers.GetValueOrDefault(to, 1m);
            return (quantity * fromMultiplier) / toMultiplier;
        }

        // Can't convert between different unit types
        return quantity;
    }

    /// <summary>
    /// Convert quantity to product's base unit
    /// </summary>
    public static decimal ConvertToProductUnit(decimal quantity, string fromUnit, string productUnit)
    {
        if (!RequiresConversion(productUnit))
        {
            return quantity; // No conversion needed for count units
        }

        if (string.Equals(fromUnit, productUnit, StringComparison.OrdinalIgnoreCase))
        {
            return quantity; // Same unit, no conversion
        }

        return Convert(quantity, fromUnit, productUnit);
    }

    /// <summary>
    /// Format unit for display
    /// </summary>
    public static string FormatUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return "piece";
        
        var normalized = unit.ToLower();
        
        // Weight units
        if (normalized == "kg") return "kg";
        if (normalized == "g" || normalized == "gram") return "g";
        if (normalized == "mg" || normalized == "milligram") return "mg";
        if (normalized == "lb" || normalized == "pound") return "lb";
        if (normalized == "oz" || normalized == "ounce") return "oz";
        
        // Volume units
        if (normalized == "liter" || normalized == "l" || normalized == "litre") return "L";
        if (normalized == "ml" || normalized == "milliliter") return "mL";
        if (normalized == "gallon") return "gal";
        if (normalized == "floz") return "fl oz";
        
        // Count units
        return normalized;
    }
}
