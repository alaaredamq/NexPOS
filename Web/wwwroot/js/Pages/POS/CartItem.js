class CartItem {
    constructor(Id, Name, Price, Quantity, Barcode, CategoryId, CurrencyId = null, OriginalPrice = null, ConvertedPrice = null, CurrencyNotation = null, ProductUnit = null, SelectedUnit = null, WeightOrVolume = null, DisplayValue = null, DisplayUnit = null, CanChangePrice = false){
        try {
            // Price choice. By default a line sells at the product's own selling price. The cashier can pick one of
            // the product's extra prices (PriceTierId + PriceLabel) or type a custom one (IsCustomPrice). Either way
            // ConvertedPrice always holds the unit price now in force, expressed in PriceCurrencyId.
            // Manual and pack lines have no stored prices to choose from, so CanChangePrice stays false for them.
            this.CanChangePrice = !!CanChangePrice;
            this.PriceTierId = null;
            this.PriceLabel = null;
            this.IsCustomPrice = false;
            this.PriceCurrencyId = null;
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
            CanChangePrice: this.CanChangePrice,
            PriceTierId: this.PriceTierId,
            PriceLabel: this.PriceLabel,
            IsCustomPrice: this.IsCustomPrice,
            PriceCurrencyId: this.PriceCurrencyId,
            Total: this.Total
        };
    }

    /**
     * Makes this line sell at a different unit price. `price` is in `currencyId` (the currency the cart shows).
     * Pass `tierId` + `label` for one of the product's extra prices, `isCustom: true` for a typed price, or
     * neither to go back to the product's default selling price.
     */
    setPrice({ price, currencyId, tierId = null, label = null, isCustom = false }) {
        this.Price = price;
        this.ConvertedPrice = price;
        this.PriceCurrencyId = currencyId || this.PriceCurrencyId;
        this.PriceTierId = tierId;
        this.PriceLabel = label;
        this.IsCustomPrice = isCustom;
    }

    /**
     * What to send the server so it can price this line itself. The server never takes the browser's price:
     * it takes a stored price id, or a custom price that it re-checks against the purchase price.
     */
    priceSelection(displayCurrencyId) {
        if (this.IsCustomPrice) {
            return { customPrice: this.ConvertedPrice, customPriceCurrencyId: this.PriceCurrencyId || displayCurrencyId };
        }
        if (this.PriceTierId) {
            return { priceTierId: this.PriceTierId };
        }
        return {};
    }

    static escapeHtml(value) {
        return String(value ?? '').replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch]));
    }

    static formatMoney(value) {
        return Number(value).toLocaleString('en-US', {
            minimumFractionDigits: Math.abs(value) >= 1000 ? 0 : 2,
            maximumFractionDigits: 2
        });
    }
    
    static fromJSON(json){
        const item = new CartItem(
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
            json.DisplayUnit,
            json.CanChangePrice
        );
        item.PriceTierId = json.PriceTierId ?? null;
        item.PriceLabel = json.PriceLabel ?? null;
        item.IsCustomPrice = !!json.IsCustomPrice;
        item.PriceCurrencyId = json.PriceCurrencyId ?? null;
        return item;
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
        
        const tr = (key, fallback, params) => (window.i18n && window.i18n.isLoaded)
            ? window.i18n.tf(key, params || {}, fallback)
            : Object.keys(params || {}).reduce((text, k) => text.replace(`{${k}}`, params[k]), fallback);
        const esc = CartItem.escapeHtml;
        const name = esc(this.Name);
        const notation = esc(this.CurrencyNotation);
        const totalText = CartItem.formatMoney(this.Total);

        // Small line under the name: what one unit costs, for lines where that isn't the same as the line total.
        let each = '';
        if (this.WeightOrVolume !== null && this.WeightOrVolume !== undefined) {
            const unitLabel = typeof UnitConverter !== 'undefined' && UnitConverter.formatUnit
                ? UnitConverter.formatUnit(this.ProductUnit) : this.ProductUnit;
            each = `<span class="cr-each">${esc(tr('pos.pricePerUnit', '{price} / {unit}', { price: CartItem.formatMoney(this.ConvertedPrice), unit: unitLabel }))}</span>`;
        } else if (this.Quantity > 1) {
            each = `<span class="cr-each">${esc(tr('pos.priceEach', '{price} each', { price: CartItem.formatMoney(this.ConvertedPrice) }))}</span>`;
        }

        // The price is a button when the cashier may pick another price for this line.
        let price;
        if (this.CanChangePrice) {
            const kind = this.IsCustomPrice ? 'custom' : (this.PriceTierId ? 'tier' : 'default');
            const label = esc(this.IsCustomPrice
                ? tr('pos.priceCustom', 'Custom')
                : (this.PriceLabel || tr('pos.priceRetail', 'Retail')));
            const aria = esc(tr('pos.priceButtonLabel', '{amount}, {label} price. Change the price of {name}', {
                amount: `${this.CurrencyNotation} ${totalText}`,
                label: this.IsCustomPrice ? tr('pos.priceCustom', 'Custom') : (this.PriceLabel || tr('pos.priceRetail', 'Retail')),
                name: this.Name
            }));
            price = `
                <button type="button" class="price-btn" data-kind="${kind}" aria-haspopup="dialog" aria-expanded="false" aria-label="${aria}">
                    <span class="pb-total fit"><span class="cur">${notation}</span> ${totalText}</span>
                    <span class="pb-tier">${kind === 'custom' ? '<i class="fas fa-pen pb-icon" aria-hidden="true"></i>' : ''}<span class="pb-name">${label}</span><i class="fas fa-chevron-down pb-icon" aria-hidden="true"></i></span>
                </button>`;
        } else {
            price = `<div class="cr-price-text fit"><span class="cur">${notation}</span> ${totalText}</div>`;
        }

        return `
            <div class="cart-row">
                <div class="cr-name">${name}${each}</div>
                <div class="cr-qty">
                    <div class="cr-stepper">
                        <button class="qty-decrease" type="button" aria-label="${esc(tr('pos.decreaseQuantity', 'Decrease quantity of {name}', { name: this.Name }))}"><i class="fas fa-minus" aria-hidden="true"></i></button>
                        <output>${quantityDisplay}</output>
                        <button class="qty-increase" type="button" aria-label="${esc(tr('pos.increaseQuantity', 'Increase quantity of {name}', { name: this.Name }))}"><i class="fas fa-plus" aria-hidden="true"></i></button>
                    </div>
                    ${unitDisplay ? `<small>${esc(unitDisplay)}</small>` : ''}
                </div>
                <div class="cr-price">${price}</div>
                <button class="cr-del remove-item" type="button" aria-label="${esc(tr('pos.removeItem', 'Remove {name}', { name: this.Name }))}">
                    <i class="fas fa-trash" aria-hidden="true"></i>
                </button>
            </div>
        `;
    }
}
    
    