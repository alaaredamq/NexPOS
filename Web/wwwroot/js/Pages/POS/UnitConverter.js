/**
 * Unit Converter Utility
 * Handles conversions between different units (kg, g, mg, liter, ml, etc.)
 */
class UnitConverter {
    // Weight units (base: kg)
    static WEIGHT_UNITS = {
        'kg': { name: 'Kilogram', multiplier: 1, base: 'kg' },
        'g': { name: 'Gram', multiplier: 0.001, base: 'kg' },
        'mg': { name: 'Milligram', multiplier: 0.000001, base: 'kg' },
        'lb': { name: 'Pound', multiplier: 0.453592, base: 'kg' },
        'oz': { name: 'Ounce', multiplier: 0.0283495, base: 'kg' }
    };

    // Volume units (base: liter)
    static VOLUME_UNITS = {
        'liter': { name: 'Liter', multiplier: 1, base: 'liter' },
        'l': { name: 'Liter', multiplier: 1, base: 'liter' },
        'ml': { name: 'Milliliter', multiplier: 0.001, base: 'liter' },
        'gallon': { name: 'Gallon', multiplier: 3.78541, base: 'liter' },
        'floz': { name: 'Fluid Ounce', multiplier: 0.0295735, base: 'liter' }
    };

    // Count units (no conversion needed)
    static COUNT_UNITS = {
        'piece': { name: 'Piece', multiplier: 1, base: 'piece' },
        'box': { name: 'Box', multiplier: 1, base: 'box' },
        'pack': { name: 'Pack', multiplier: 1, base: 'pack' }
    };

    /**
     * Get all available units for a product unit type
     */
    static getAvailableUnits(productUnit) {
        if (!productUnit) return [this.COUNT_UNITS.piece];

        const normalizedUnit = productUnit.toLowerCase();
        
        // Check weight units
        if (this.isWeightUnit(normalizedUnit)) {
            return Object.keys(this.WEIGHT_UNITS).map(key => ({
                value: key,
                label: this.WEIGHT_UNITS[key].name,
                base: 'kg'
            }));
        }
        
        // Check volume units
        if (this.isVolumeUnit(normalizedUnit)) {
            return Object.keys(this.VOLUME_UNITS).map(key => ({
                value: key,
                label: this.VOLUME_UNITS[key].name,
                base: 'liter'
            }));
        }
        
        // Default to count units
        return [this.COUNT_UNITS.piece];
    }

    /**
     * Check if unit is a weight unit
     */
    static isWeightUnit(unit) {
        if (!unit) return false;
        const normalized = unit.toLowerCase();
        return normalized === 'kg' || normalized === 'kilogram' || 
               normalized === 'g' || normalized === 'gram' ||
               normalized === 'mg' || normalized === 'milligram' ||
               normalized === 'lb' || normalized === 'pound' ||
               normalized === 'oz' || normalized === 'ounce';
    }

    /**
     * Check if unit is a volume unit
     */
    static isVolumeUnit(unit) {
        if (!unit) return false;
        const normalized = unit.toLowerCase();
        return normalized === 'liter' || normalized === 'l' || normalized === 'litre' ||
               normalized === 'ml' || normalized === 'milliliter' ||
               normalized === 'gallon' || normalized === 'floz';
    }

    /**
     * Check if unit requires conversion (weight or volume)
     */
    static requiresConversion(unit) {
        return this.isWeightUnit(unit) || this.isVolumeUnit(unit);
    }

    /**
     * Convert quantity from one unit to another
     * @param {number} quantity - The quantity to convert
     * @param {string} fromUnit - Source unit
     * @param {string} toUnit - Target unit
     * @returns {number} Converted quantity
     */
    static convert(quantity, fromUnit, toUnit) {
        if (!fromUnit || !toUnit || fromUnit.toLowerCase() === toUnit.toLowerCase()) {
            return quantity;
        }

        const from = fromUnit.toLowerCase();
        const to = toUnit.toLowerCase();

        // Weight conversions
        if (this.isWeightUnit(from) && this.isWeightUnit(to)) {
            // Convert to base (kg), then to target
            const fromMultiplier = this.WEIGHT_UNITS[from]?.multiplier || 1;
            const toMultiplier = this.WEIGHT_UNITS[to]?.multiplier || 1;
            return (quantity * fromMultiplier) / toMultiplier;
        }

        // Volume conversions
        if (this.isVolumeUnit(from) && this.isVolumeUnit(to)) {
            // Convert to base (liter), then to target
            const fromMultiplier = this.VOLUME_UNITS[from]?.multiplier || 
                                 (from === 'liter' || from === 'l' ? 1 : 0.001);
            const toMultiplier = this.VOLUME_UNITS[to]?.multiplier || 
                               (to === 'liter' || to === 'l' ? 1 : 0.001);
            return (quantity * fromMultiplier) / toMultiplier;
        }

        // Can't convert between different unit types
        console.warn(`Cannot convert from ${from} to ${to} - different unit types`);
        return quantity;
    }

    /**
     * Convert quantity to product's base unit
     * @param {number} quantity - The quantity to convert
     * @param {string} fromUnit - Source unit (e.g., 'mg', 'g')
     * @param {string} productUnit - Product's unit (e.g., 'kg')
     * @returns {number} Quantity in product's base unit
     */
    static convertToProductUnit(quantity, fromUnit, productUnit) {
        if (!productUnit || !this.requiresConversion(productUnit)) {
            return quantity; // No conversion needed for count units
        }

        const normalizedProductUnit = productUnit.toLowerCase();
        const normalizedFromUnit = fromUnit.toLowerCase();

        // If same unit, no conversion
        if (normalizedFromUnit === normalizedProductUnit) {
            return quantity;
        }

        return this.convert(quantity, fromUnit, productUnit);
    }

    /**
     * Format unit display name
     */
    static formatUnit(unit) {
        if (!unit) return 'piece';
        
        const normalized = unit.toLowerCase();
        
        // Weight units
        if (normalized === 'kg') return 'kg';
        if (normalized === 'g' || normalized === 'gram') return 'g';
        if (normalized === 'mg' || normalized === 'milligram') return 'mg';
        if (normalized === 'lb' || normalized === 'pound') return 'lb';
        if (normalized === 'oz' || normalized === 'ounce') return 'oz';
        
        // Volume units
        if (normalized === 'liter' || normalized === 'l' || normalized === 'litre') return 'L';
        if (normalized === 'ml' || normalized === 'milliliter') return 'mL';
        if (normalized === 'gallon') return 'gal';
        if (normalized === 'floz') return 'fl oz';
        
        // Count units
        return normalized;
    }
}
