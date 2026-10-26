class CartItem {
    constructor(Id, Name, Price, Quantity, Barcode, CategoryId, CurrencyId = null, OriginalPrice = null, ConvertedPrice = null, CurrencyNotation = null, ProductUnit = null, SelectedUnit = null, WeightOrVolume = null, DisplayValue = null, DisplayUnit = null){
        try {
            this.Id = Id;
            this.Name = Name || 'Unknown Product';
            this.Price = ConvertedPrice || Price || 0; // Use converted price if available, otherwise original
            this.OriginalPrice = OriginalPrice || Price || 0; // Store original price
            this.Quantity = Quantity || 1;
            this.Barcode = Barcode || '';
            this.CategoryId = CategoryId || 0;
            this.CurrencyId = CurrencyId; // Product's original currency
            this.ConvertedPrice = ConvertedPrice || Price || 0; // Price in default currency
            this.CurrencyNotation = CurrencyNotation || 'LBP'; // Default notation
            this.ProductUnit = ProductUnit || 'piece'; // Product's base unit
            this.SelectedUnit = SelectedUnit || ProductUnit || 'piece'; // Unit used for this cart item
            this.WeightOrVolume = WeightOrVolume; // Weight/volume in base unit (kg or L) for price calculation
            this.DisplayValue = DisplayValue; // Original entered value for display (e.g., 300 for 300g)
            this.DisplayUnit = DisplayUnit; // Original entered unit for display (e.g., 'g')
            
            // Ensure units are strings
            if (this.ProductUnit) this.ProductUnit = String(this.ProductUnit).toLowerCase();
            if (this.SelectedUnit) this.SelectedUnit = String(this.SelectedUnit).toLowerCase();
            if (this.DisplayUnit) this.DisplayUnit = String(this.DisplayUnit).toLowerCase();
            
            console.log(`CartItem created: ${this.Name}, Quantity: ${this.Quantity}, ProductUnit: ${this.ProductUnit}, SelectedUnit: ${this.SelectedUnit}, WeightOrVolume: ${this.WeightOrVolume}, DisplayValue: ${this.DisplayValue}, DisplayUnit: ${this.DisplayUnit}`);
        } catch (error) {
            console.error('Error creating CartItem:', error);
            throw error;
        }
    }
    
    get Total(){
        // If WeightOrVolume is set, use it directly for price calculation
        // WeightOrVolume is stored in the product's base unit (kg or L)
        if (this.WeightOrVolume !== null && this.WeightOrVolume !== undefined) {
            return this.ConvertedPrice * this.WeightOrVolume;
        }
        
        // Legacy: If unit conversion is needed, convert quantity to product's base unit first
        try {
            if (typeof UnitConverter !== 'undefined' && 
                this.ProductUnit && 
                this.SelectedUnit && 
                this.ProductUnit !== 'piece' &&
                this.ProductUnit !== this.SelectedUnit &&
                UnitConverter.requiresConversion && 
                UnitConverter.requiresConversion(this.ProductUnit)) {
                const convertedQuantity = UnitConverter.convertToProductUnit(this.Quantity, this.SelectedUnit, this.ProductUnit);
                return this.ConvertedPrice * convertedQuantity;
            }
        } catch (error) {
            console.error('Error converting unit in Total calculation:', error);
        }
        return this.ConvertedPrice * this.Quantity;
    }
    
    get TotalOriginal(){
        return this.OriginalPrice * this.Quantity;
    }
    
    set Total(value){
        throw new Error("Total is a read-only property.");
    }
    
    toString(){
        let quantityDisplay;
        if (this.DisplayValue !== null && this.DisplayValue !== undefined && this.DisplayUnit) {
            // Show original entered value (e.g., "300 g")
            const unitFormat = typeof UnitConverter !== 'undefined' && UnitConverter.formatUnit 
                ? UnitConverter.formatUnit(this.DisplayUnit) 
                : this.DisplayUnit;
            quantityDisplay = `${this.DisplayValue} ${unitFormat}`;
        } else if (this.WeightOrVolume !== null && this.WeightOrVolume !== undefined) {
            // Fallback: show weight/volume in base unit
            const unitFormat = typeof UnitConverter !== 'undefined' && UnitConverter.formatUnit 
                ? UnitConverter.formatUnit(this.ProductUnit) 
                : this.ProductUnit;
            quantityDisplay = `${this.WeightOrVolume} ${unitFormat}`;
        } else {
            const unitDisplay = this.SelectedUnit && this.SelectedUnit !== 'piece' 
                ? ` ${UnitConverter?.formatUnit(this.SelectedUnit) || this.SelectedUnit}` 
                : '';
            quantityDisplay = `${this.Quantity}${unitDisplay}`;
        }
        return `${this.Name} (${quantityDisplay}) - ${this.CurrencyNotation} ${this.Total.toLocaleString()}`;
    }
    
    toJSON(){
        return {
            Id: this.Id,
            Name: this.Name,
            Price: this.ConvertedPrice,
            OriginalPrice: this.OriginalPrice,
            Quantity: this.Quantity,
            Barcode: this.Barcode,
            CategoryId: this.CategoryId,
            CurrencyId: this.CurrencyId,
            ConvertedPrice: this.ConvertedPrice,
            CurrencyNotation: this.CurrencyNotation,
            ProductUnit: this.ProductUnit,
            SelectedUnit: this.SelectedUnit,
            WeightOrVolume: this.WeightOrVolume,
            DisplayValue: this.DisplayValue,
            DisplayUnit: this.DisplayUnit,
            Total: this.Total
        };
    }
    
    static fromJSON(json){
        return new CartItem(
            json.Id, 
            json.Name, 
            json.Price, 
            json.Quantity, 
            json.Barcode, 
            json.CategoryId,
            json.CurrencyId,
            json.OriginalPrice,
            json.ConvertedPrice,
            json.CurrencyNotation,
            json.ProductUnit,
            json.SelectedUnit,
            json.WeightOrVolume,
            json.DisplayValue,
            json.DisplayUnit
        );
    }
    
    createHtmlElement(index) {
        // Format the weight/volume display
        let quantityDisplay = this.Quantity;
        let unitDisplay = '';
        
        if (this.DisplayValue !== null && this.DisplayValue !== undefined && this.DisplayUnit) {
            // Show the original entered value (e.g., "300" with "g" unit)
            const unitFormat = typeof UnitConverter !== 'undefined' && UnitConverter.formatUnit 
                ? UnitConverter.formatUnit(this.DisplayUnit) 
                : this.DisplayUnit;
            quantityDisplay = this.DisplayValue;
            unitDisplay = unitFormat;
        } else if (this.WeightOrVolume !== null && this.WeightOrVolume !== undefined) {
            // Fallback: show the converted weight/volume in base unit
            const unitFormat = typeof UnitConverter !== 'undefined' && UnitConverter.formatUnit 
                ? UnitConverter.formatUnit(this.ProductUnit) 
                : this.ProductUnit;
            quantityDisplay = this.WeightOrVolume;
            unitDisplay = unitFormat;
        } else if (this.SelectedUnit && this.SelectedUnit !== 'piece') {
            unitDisplay = typeof UnitConverter !== 'undefined' && UnitConverter.formatUnit 
                ? UnitConverter.formatUnit(this.SelectedUnit) 
                : this.SelectedUnit;
        }
        
        return `
            <tr>
                <td class="align-middle">
                    <div class="cart-product-info">
                        <div class="cart-product-name">${this.Name}</div>
                    </div>
                </td>
                <td class="align-middle text-center">
                    <div class="quantity-controls">
                        <button class="quantity-btn qty-decrease" type="button">-</button>
                        <input type="number" class="quantity-input" value="${quantityDisplay}" readonly />
                        <button class="quantity-btn qty-increase" type="button">+</button>
                    </div>
                    ${unitDisplay ? `<small class="text-muted d-block mt-1">${unitDisplay}</small>` : ''}
                </td>
                <td class="align-middle text-end">
                    <div class="price-display">${this.CurrencyNotation} ${this.Total.toLocaleString('en-US', {minimumFractionDigits: Math.abs(this.Total) >= 1000 ? 0 : 2, maximumFractionDigits: 2})}</div>
                </td>
                <td class="align-middle text-center">
                    <button class="cart-action-btn btn-remove remove-item" type="button" title="Remove">
                        <i class="fas fa-trash"></i>
                    </button>
                </td>
            </tr>
        `;
    }
}
    
    