class POS {
    constructor() {
        console.log('🏗️ POS constructor starting...');
        
        // State variables
        this.cart = [];
        this.total = 0;
        this.defaultCurrency = null;
        this.currencies = [];
        this.exchangeRate = 1500; // LBP to USD (fallback)
        this.currentDiscount = null; // Store current discount
        this.isUserInteracting = false; // Flag to prevent refocus during user interactions
        this.pendingQuantity = null; // Store quantity prefix for next scan (e.g., "5*" sets this to 5)

        // DOM elements
        this.elements = {
            cartContainer: $("#cart-items"),
            cartTableBody: $("#cart-table-body"),
            cartTableContainer: $("#cart-table-container"),
            emptyCart: $("#empty-cart"),
            totalDisplay: $("#total-lbp"),
            totalUsdDisplay: $("#total-usd"),
            cartCount: $("#cart-count"),
            barcodeInput: $("#barcode-input"),
            addProductBtn: $("#add-product-btn"),
            checkoutButton: $("#checkoutButton"),
            clearCartButton: $("#clearCartButton"),
            holdButton: $("#holdButton"),
            addToCart: $(".add-to-cart-btn"),
            retrieveHeldCartBtn: $("#retrieve-held-cart-btn"),
            addManualItemBtn: $("#add-manual-item-btn"),
            discountBtn: $("#discount-btn"),
            priceCheckBtn: $("#price-check-btn"),
        };

        // Initialize the POS system
        this.init();
    }

    // ===== INITIALIZATION =====
    async init() {
        await this.loadDefaultCurrency();
        await this.loadCurrencies();
        
        // Refresh cache on init to ensure currency data is loaded
        await this.ensureCacheHasCurrency();
        
        this.bindEvents();
        this.updateCartDisplay();
        this.barcodeInputHandler();
        this.setupBarcodeAutoFocus();
    }
    
    // ===== BARCODE AUTO-FOCUS =====
    setupBarcodeAutoFocus() {
        // Focus barcode input on page load (with small delay to ensure DOM is ready)
        setTimeout(() => {
            this.focusBarcodeInput();
        }, 100);
        
        // Also focus when DOM is fully ready
        $(document).ready(() => {
            this.focusBarcodeInput();
        });
        
        // Refocus when window gains focus (user returns to tab)
        $(window).on('focus', () => {
            this.focusBarcodeInput();
        });
        
        // Refocus after a short delay when clicking anywhere on the page
        // (except when clicking on interactive elements)
        $(document).on('click', (e) => {
            const $target = $(e.target);
            
            // Don't refocus if clicking on:
            // - The barcode input or its container
            // - Product search inputs
            // - Category filter buttons
            // - Dropdowns (Bootstrap and Select2)
            // - Any input, select, textarea, or button elements
            // - Modals or modal backdrops
            // - Buttons that open modals
            // - Product cards (to add to cart)
            const shouldRefocus = $target.closest(
                '#barcode-input, ' +
                '.input-group, ' +
                '#product-search, ' +
                '#product-search-mobile, ' +
                '.ui-autocomplete, ' +
                '.ui-menu-item, ' +
                '.dropdown, ' +
                '.dropdown-toggle, ' +
                '.dropdown-menu, ' +
                '.select2-container, ' +
                '.select2-dropdown, ' +
                'input, ' +
                'select, ' +
                'textarea, ' +
                'button, ' +
                '.swal2-container, ' +
                '.modal, ' +
                '[data-bs-toggle="modal"], ' +
                '[data-bs-toggle="dropdown"], ' +
                '.add-to-cart-btn, ' +
                '.product-card'
            ).length === 0;
            
            if (shouldRefocus) {
                // Small delay to allow other click handlers to run first
                setTimeout(() => {
                    // Double-check that no input/select/textarea is focused
                    const activeElement = document.activeElement;
                    const isInputFocused = activeElement && (
                        activeElement.tagName === 'INPUT' ||
                        activeElement.tagName === 'SELECT' ||
                        activeElement.tagName === 'TEXTAREA' ||
                        activeElement.isContentEditable ||
                        activeElement.closest('.select2-container') ||
                        activeElement.closest('.dropdown-menu')
                    );
                    
                    if (!isInputFocused) {
                        this.focusBarcodeInput();
                    }
                }, 100);
            }
        });
        
        // Refocus after Bootstrap modals close
        $(document).on('hidden.bs.modal', () => {
            setTimeout(() => {
                this.focusBarcodeInput();
            }, 100);
        });
        
        // Refocus after SweetAlert2 closes
        const self = this;
        
        // Intercept Swal.fire to add refocus after modal closes
        const originalSwalFire = Swal.fire;
        Swal.fire = function(...args) {
            const result = originalSwalFire.apply(Swal, args);
            // If result is a promise (SweetAlert2), add refocus on close
            if (result && typeof result.then === 'function') {
                result.then((value) => {
                    // Wait a bit for the modal to fully close
                    setTimeout(() => {
                        self.focusBarcodeInput();
                    }, 150);
                    return value;
                }).catch((error) => {
                    setTimeout(() => {
                        self.focusBarcodeInput();
                    }, 150);
                    throw error;
                });
            }
            return result;
        };
        
        // Also handle Swal.close directly
        const originalSwalClose = Swal.close;
        Swal.close = function(...args) {
            const result = originalSwalClose.apply(Swal, args);
            setTimeout(() => {
                self.focusBarcodeInput();
            }, 150);
            return result;
        };
        
        // Listen for SweetAlert2 DOM removal (when modal is fully closed)
        const observer = new MutationObserver((mutations) => {
            mutations.forEach((mutation) => {
                mutation.removedNodes.forEach((node) => {
                    if (node.nodeType === 1 && node.classList && node.classList.contains('swal2-container')) {
                        setTimeout(() => {
                            self.focusBarcodeInput();
                        }, 100);
                    }
                });
            });
        });
        
        // Observe the body for SweetAlert2 container removal
        observer.observe(document.body, {
            childList: true,
            subtree: true
        });
        
        // Also listen for clicks on SweetAlert2 buttons as backup
        document.addEventListener('click', (e) => {
            if (e.target.closest('.swal2-close, .swal2-confirm, .swal2-cancel, .swal2-deny')) {
                setTimeout(() => {
                    // Only refocus if no other input is focused
                    const activeElement = document.activeElement;
                    const isInputFocused = activeElement && (
                        activeElement.tagName === 'INPUT' ||
                        activeElement.tagName === 'SELECT' ||
                        activeElement.tagName === 'TEXTAREA' ||
                        activeElement.isContentEditable
                    );
                    if (!isInputFocused) {
                        self.focusBarcodeInput();
                    }
                }, 200);
            }
        });
        
        // Track when user is actively interacting with other elements
        $(document).on('focus', '#product-search, #product-search-mobile, select, .select2-search__field, input, textarea', () => {
            self.isUserInteracting = true;
            // Reset flag after a delay
            setTimeout(() => {
                self.isUserInteracting = false;
            }, 500);
        });
        
        // Track when dropdowns are shown/hidden
        $(document).on('show.bs.dropdown', () => {
            self.isUserInteracting = true;
        });
        
        $(document).on('hidden.bs.dropdown', () => {
            setTimeout(() => {
                self.isUserInteracting = false;
                // Refocus barcode after dropdown closes
                self.focusBarcodeInput();
            }, 100);
        });
        
        // Track when Select2 is opened/closed
        $(document).on('select2:open', () => {
            self.isUserInteracting = true;
        });
        
        $(document).on('select2:close', () => {
            setTimeout(() => {
                self.isUserInteracting = false;
                // Refocus barcode after Select2 closes
                self.focusBarcodeInput();
            }, 100);
        });
        
        // Track keyboard input in search fields
        $(document).on('keydown keypress input', '#product-search, #product-search-mobile', () => {
            self.isUserInteracting = true;
            // Reset after user stops typing
            clearTimeout(self.searchInteractionTimeout);
            self.searchInteractionTimeout = setTimeout(() => {
                self.isUserInteracting = false;
            }, 1000);
        });
    }
    
    focusBarcodeInput() {
        // Don't refocus if user is actively interacting with other elements
        if (this.isUserInteracting) {
            return;
        }
        
        // Check if any input/select/textarea is currently focused
        const activeElement = document.activeElement;
        const isInputFocused = activeElement && (
            activeElement.tagName === 'INPUT' ||
            activeElement.tagName === 'SELECT' ||
            activeElement.tagName === 'TEXTAREA' ||
            activeElement.isContentEditable ||
            activeElement.closest('.select2-container') ||
            activeElement.closest('.dropdown-menu') ||
            activeElement.closest('.swal2-container')
        );
        
        // Don't refocus if another input is focused
        if (isInputFocused) {
            return;
        }
        
        // Only focus if the input is visible and not disabled
        if (this.elements.barcodeInput.length && 
            this.elements.barcodeInput.is(':visible') && 
            !this.elements.barcodeInput.is(':disabled')) {
            this.elements.barcodeInput.focus();
        }
    }
    
    async ensureCacheHasCurrency() {
        try {
            // Check if this is first load or cache needs refresh
            const cacheRefreshed = sessionStorage.getItem('pos_cache_refreshed');
            
            if (!cacheRefreshed) {
                console.log('🔄 First load - refreshing product cache with currency data...');
                const response = await $.ajax({
                    url: '/POS/RefreshCache',
                    method: 'POST'
                });
                
                if (response.success) {
                    console.log('✅ Product cache initialized with currency data');
                    sessionStorage.setItem('pos_cache_refreshed', 'true');
                    // Reload page to get fresh product data
                    window.location.reload();
                }
            } else {
                console.log('✅ Cache already refreshed this session');
            }
        } catch (error) {
            console.error('❌ Error initializing cache:', error);
        }
    }

    async loadDefaultCurrency() {
        try {
            const response = await $.ajax({
                url: '/POS/GetDefaultCurrency',
                method: 'GET'
            });

            if (response.success) {
                this.defaultCurrency = response.data;
                console.log('✅ Default currency loaded:', this.defaultCurrency);
                console.log(`   ID: ${this.defaultCurrency.id}, Code: ${this.defaultCurrency.code}, Notation: ${this.defaultCurrency.notation}, RateToUSD: ${this.defaultCurrency.rateToUSD}`);
            } else {
                console.error('❌ Failed to load default currency:', response.message);
                toastr.error(window.i18n.t('pos.failedToLoadDefaultCurrency', 'Failed to load default currency'));
            }
        } catch (error) {
            console.error('❌ Error loading default currency:', error);
            toastr.error(window.i18n.t('pos.errorLoadingDefaultCurrency', 'Error loading default currency'));
        }
    }

    async loadCurrencies() {
        try {
            const response = await $.ajax({
                url: '/POS/GetCurrencies',
                method: 'GET'
            });

            if (response.success) {
                this.currencies = response.data;
                console.log('✅ Currencies loaded:', this.currencies);
                this.currencies.forEach(c => {
                    console.log(`   ${c.code}: ID=${c.id}, Notation=${c.notation}, RateToUSD=${c.rateToUSD}, IsDefault=${c.isDefault}`);
                });
            } else {
                console.error('❌ Failed to load currencies:', response.message);
            }
        } catch (error) {
            console.error('❌ Error loading currencies:', error);
        }
    }

    async convertPrice(amount, fromCurrencyId, toCurrencyId) {
        try {
            console.log(`💱 Calling ConvertPrice API: ${amount} from ${fromCurrencyId} to ${toCurrencyId}`);
            
            const response = await $.ajax({
                url: '/POS/ConvertPrice',
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify({
                    amount: amount,
                    fromCurrencyId: fromCurrencyId,
                    toCurrencyId: toCurrencyId
                })
            });

            console.log('ConvertPrice API Response:', response);

            if (response.success) {
                console.log(`✅ Conversion successful: ${amount} → ${response.data.convertedAmount}`);
                return response.data.convertedAmount;
            } else {
                console.error('❌ Failed to convert price:', response.message);
                return amount; // Return original amount if conversion fails
            }
        } catch (error) {
            console.error('❌ Error converting price:', error);
            return amount; // Return original amount if conversion fails
        }
    }

    bindEvents() {
        // Cart action buttons
        this.elements.checkoutButton.on("click", () => this.checkout());
        this.elements.clearCartButton.on("click", () => this.clearCart());
        this.elements.holdButton.on("click", () => this.hold());
        
        
        // Quick action buttons
        this.elements.retrieveHeldCartBtn.on("click", () => this.retrieveHeldCart());
        this.elements.addManualItemBtn.on("click", () => this.addManualItem());
        this.elements.discountBtn.on("click", () => this.applyDiscount());
        this.elements.priceCheckBtn.on("click", () => this.priceCheck());
        
        // Add to cart from product grid
        $(document).on("click", ".add-to-cart-btn", async (e) => {
            // Don't handle if clicking on the "Add Product" button
            if ($(e.target).closest('.add-product-btn').length > 0) {
                return;
            }
            
            e.preventDefault();
            e.stopPropagation();
            
            // Check barcode input for quantity prefix (e.g., "2*" or "5x")
            // This handles the case where user types quantity prefix and clicks on product without pressing Enter
            let clickQuantity = 1;
            const barcodeInputVal = this.elements.barcodeInput.val()?.trim() || '';
            const quantityPrefixMatch = barcodeInputVal.match(/^(\d+)\s*[*xX]\s*$/);
            if (quantityPrefixMatch) {
                const qty = parseInt(quantityPrefixMatch[1], 10);
                if (qty > 0 && qty <= 999) {
                    clickQuantity = qty;
                    console.log(`📦 Quantity prefix from input: ${qty}`);
                    // Clear the barcode input after using the quantity
                    this.elements.barcodeInput.val('');
                    this.elements.barcodeInput.removeClass('quantity-prefix-mode');
                }
            } else if (this.pendingQuantity) {
                // Fallback to pending quantity if set via Enter key
                clickQuantity = this.pendingQuantity;
                console.log(`📦 Using pending quantity: ${this.pendingQuantity}`);
                this.pendingQuantity = null;
            }
            
            const $card = $(e.currentTarget);
            
            const id = $card.data("product-id");
            const name = $card.data("product-name");
            const price = parseFloat($card.data("product-price"));
            const sku = $card.data("product-sku");
            const category = $card.data("category");
            const currencyId = parseInt($card.data("currency-id")) || this.defaultCurrency?.id;
            const productCurrencyNotation = $card.data("currency-notation") || 'LBP';
            const productUnit = ($card.data("product-unit") || 'piece').toLowerCase();
            const isPack = $card.data("is-pack") === true || $card.data("is-pack") === "true";
            const packSize = parseInt($card.data("pack-size")) || 0;
            const singleProductId = parseInt($card.data("single-product-id")) || 0;
            
            // If product is a pack, show selector for single product
            if (isPack) {
                await this.handlePackProduct(id, name, price, sku, category, currencyId, productCurrencyNotation, packSize, singleProductId);
                return;
            }
            
            console.log(`=== ADDING PRODUCT TO CART ===`);
            console.log(`Product: ${name}`);
            console.log(`Original Price: ${price}`);
            console.log(`Product Unit: ${productUnit}`);
            console.log(`Product Currency ID: ${currencyId}`);
            console.log(`Product Currency Notation: ${productCurrencyNotation}`);
            console.log(`Default Currency ID: ${this.defaultCurrency?.id}`);
            console.log(`Default Currency Notation: ${this.defaultCurrency?.notation}`);
            
            // Convert price to default currency if needed
            let convertedPrice = price;
            let displayNotation = this.defaultCurrency?.notation || 'LBP';
            
            if (this.defaultCurrency && currencyId && currencyId !== this.defaultCurrency.id) {
                console.log(`🔄 CONVERTING: ${price} from currency ${currencyId} to ${this.defaultCurrency.id}`);
                convertedPrice = await this.convertPrice(price, currencyId, this.defaultCurrency.id);
                console.log(`✅ Converted price: ${convertedPrice}`);
                console.log(`Will display as: ${displayNotation} ${convertedPrice}`);
            } else {
                console.log(`ℹ️ No conversion needed - same currency (or default not loaded)`);
                console.log(`Will display as: ${displayNotation} ${convertedPrice}`);
            }
            
            // Use the quantity from prefix (either from input field or pendingQuantity)
            let quantity = clickQuantity;
            let unit = productUnit || 'piece';
            let weightOrVolume = null; // Store the actual weight/volume in base unit
            let displayValue = null; // Original entered value (e.g., 300 for 300g)
            let displayUnit = null; // Original entered unit (e.g., 'g')
            
            // Show modal for weight/volume products to enter the amount
            try {
                if (typeof UnitConverter !== 'undefined' && 
                    UnitConverter.requiresConversion &&
                    productUnit && 
                    productUnit !== 'piece' && 
                    productUnit !== '' &&
                    UnitConverter.requiresConversion(productUnit)) {
                    // Show quantity input modal with unit selection
                    console.log('Product requires weight/volume input, showing modal');
                    const quantityResult = await this.showQuantityInputModal(name, productUnit, convertedPrice, displayNotation);
                    if (!quantityResult) {
                        console.log('User cancelled quantity modal');
                        return; // User cancelled or modal failed
                    }
                    quantity = quantityResult.quantity;
                    unit = quantityResult.unit || productUnit;
                    weightOrVolume = quantityResult.weightOrVolume; // Weight/volume in base unit (kg or L)
                    displayValue = quantityResult.displayValue; // Original entered value for display
                    displayUnit = quantityResult.displayUnit; // Original entered unit for display
                } else {
                    console.log('No unit conversion needed, using default quantity');
                }
            } catch (error) {
                console.error('Error in unit conversion check:', error);
                // Fallback to default quantity if anything fails
                quantity = 1;
                unit = productUnit || 'piece';
            }
            console.log('Quantity/WeightOrVolume:', { quantity, unit, weightOrVolume, displayValue, displayUnit });
            
            console.log('Creating cart item:', { id, name, quantity, unit, productUnit, weightOrVolume, displayValue, displayUnit });
            
            try {
                const newItem = new CartItem(
                    id, 
                    name, 
                    price, 
                    quantity, 
                    sku, 
                    category, 
                    currencyId, 
                    price, 
                    convertedPrice,
                    displayNotation,  // Cart ALWAYS shows in default currency
                    productUnit || 'piece',  // Product's base unit
                    unit,              // Selected/entered unit
                    weightOrVolume,    // Weight or volume in base unit (e.g., 0.3 for 300g in kg)
                    displayValue,      // Original entered value for display (e.g., 300)
                    displayUnit        // Original entered unit for display (e.g., 'g')
                );
                console.log('CartItem created successfully:', newItem);
                console.log(`=== END ADD TO CART ===\n`);
                this.addToCart(newItem);
                console.log('Item added to cart, cart length:', this.cart.length);
                
                // Show confirmation if multiple items were added (via quantity prefix)
                if (quantity > 1) {
                    toastr.success(window.i18n.t('pos.addedMultipleItems', `Added ${quantity}x ${name}`).replace('${quantity}', quantity).replace('${name}', name));
                }
            } catch (error) {
                console.error('Error creating CartItem:', error);
                toastr.error('Error adding product to cart: ' + error.message);
            }
            
            // Visual feedback - simple border flash
            $card.css('border-color', '#20c997');
            setTimeout(() => $card.css('border-color', ''), 200);
        });
        
        console.log('🔧 About to setup autocomplete...');
        // Autocomplete search functionality
        this.setupProductAutocomplete();
        
        console.log('✅ POS constructor completed');
    }

    // ===== PRODUCT SEARCH AUTocomplete =====
    setupProductAutocomplete() {
        const self = this;
        
        console.log('🔧 Setting up simple autocomplete...');
        
        // Check if jQuery UI is available
        if (typeof $.fn.autocomplete === 'undefined') {
            console.error('❌ jQuery UI autocomplete not available!');
            return;
        }
        
        // Safely destroy existing instances
        $("#product-search, #product-search-mobile").each(function() {
            if ($(this).hasClass('ui-autocomplete-input')) {
                $(this).autocomplete('destroy');
            }
        });
        
        // Test if elements exist
        console.log('🔍 Search elements found:', $("#product-search").length, $("#product-search-mobile").length);
        
        // Simple autocomplete setup
        $("#product-search, #product-search-mobile").autocomplete({
            minLength: 2,
            delay: 300,
            // Don't append to body, let it position naturally
            source: function(request, response) {
                console.log('🔍 Searching for:', request.term);
                
                $.ajax({
                    url: `/POS/SearchProduct?searchTerm=${encodeURIComponent(request.term)}`,
                    method: 'GET',
                    success: function(result) {
                        console.log('📦 Search result:', result);
                        
                        if (result.success && result.data) {
                            const products = Array.isArray(result.data) ? result.data : [result.data];
                            console.log('📦 Products found:', products.length);
                            
                            if (products.length === 0) {
                                response([{
                                    label: 'No products found for "' + request.term + '"',
                                    value: request.term, // Keep the search term
                                    noResult: true
                                }]);
                                return;
                            }
                            
                            const suggestions = products.map(p => ({
                                label: `${p.name || p.Name} (${p.barcode || p.Barcode || 'No barcode'})`,
                                value: p.name || p.Name,
                                product: p
                            }));
                            
                            console.log('📋 Suggestions:', suggestions);
                            response(suggestions);
                        } else {
                            console.log('❌ No data or not successful');
                            response([{
                                label: 'No products found for "' + request.term + '"',
                                value: request.term, // Keep the search term
                                noResult: true
                            }]);
                        }
                    },
                    error: function(xhr, status, error) {
                        console.error('❌ Search error:', status, error);
                        response([{
                            label: 'No products found for "' + request.term + '"',
                            value: request.term, // Keep the search term
                            noResult: true
                        }]);
                    }
                });
            },
            select: async function(event, ui) {
                event.preventDefault();
                console.log('🎯 Product selected:', ui.item);
                
                // Don't select if it's a "no result" item
                if (ui.item.noResult) {
                    return false;
                }
                
                if (!ui.item.product) {
                    console.error('❌ No product data in selection');
                    return false;
                }
                
                const product = ui.item.product;
                console.log('📦 Product data:', product);
                
                const productId = product.id || product.Id;
                const productName = product.name || product.Name;
                const productPrice = product.price || product.sellingPrice || product.SellingPrice || 0;
                const productBarcode = product.barcode || product.Barcode || '';
                const productUnit = product.unit || product.Unit || 'piece';
                
                console.log('🛒 Adding to cart:', { productId, productName, productPrice, productBarcode, productUnit });
                
                try {
                    // Handle unit conversion if needed
                    let quantity = 1;
                    let unit = (productUnit || 'piece').toLowerCase();
                    
                    if (typeof UnitConverter !== 'undefined' && 
                        productUnit && 
                        productUnit !== 'piece' && 
                        UnitConverter.requiresConversion(productUnit)) {
                        // Show quantity input modal
                        try {
                            const quantityResult = await self.showQuantityInputModal(productName, productUnit);
                            if (!quantityResult) {
                                return false; // User cancelled
                            }
                            quantity = quantityResult.quantity;
                            unit = quantityResult.unit;
                        } catch (error) {
                            console.error('Error showing quantity modal:', error);
                            // Fallback to default
                            quantity = 1;
                            unit = productUnit;
                        }
                    }
                    
                    // Convert price to default currency if needed
                    const currencyId = product.currencyId || product.CurrencyId || self.defaultCurrency?.id;
                    let convertedPrice = productPrice;
                    if (self.defaultCurrency && currencyId && currencyId !== self.defaultCurrency.id) {
                        convertedPrice = await self.convertPrice(productPrice, currencyId, self.defaultCurrency.id);
                    }
                    
                    // Create cart item
                    const newItem = new CartItem(
                        productId,
                        productName,
                        productPrice,
                        quantity,
                        productBarcode,
                        product.categoryId || product.CategoryId,
                        currencyId,
                        productPrice,
                        convertedPrice,
                        self.defaultCurrency?.notation || 'LBP',
                        productUnit,  // Product's base unit
                        unit          // Selected unit
                    );
                    
                    self.addToCart(newItem);
                    console.log('✅ Product added to cart successfully');
                    
                    // Show success message
                    if (typeof toastr !== 'undefined') {
                        toastr.success(`${productName} added to cart!`);
                    }
                    
                } catch (error) {
                    console.error('❌ Error adding to cart:', error);
                }
                
                // Only clear input and close if product was successfully added
                $(this).val('');
                $(this).autocomplete('close');
                
                return false;
            },
            focus: function(event, ui) {
                // Skip no-result items when navigating with keyboard
                if (ui.item && ui.item.noResult) {
                    return false;
                }
                // Prevent input value from changing on focus
                return false;
            },
            open: function(event, ui) {
                const widget = $(this).autocomplete('widget');
                const input = $(this);
                const inputOffset = input.offset();
                
                // Position dropdown properly relative to input
                widget.css({
                    'z-index': 10000,
                    'position': 'absolute',
                    'top': inputOffset.top + input.outerHeight() + 2,
                    'left': inputOffset.left,
                    'width': Math.max(input.outerWidth(), 280),
                    'display': 'block',
                    'visibility': 'visible',
                    'opacity': 1
                });
            },
            close: function(event, ui) {
                const widget = $(this).autocomplete('widget');
                // Properly hide and reset the widget
                widget.hide().css({
                    'display': 'none',
                    'visibility': 'hidden',
                    'opacity': '0'
                });
                
                // Don't clear input value on close - let user keep their search
                // $(this).val(''); // Removed this line
            }
        });
        
        // Custom rendering for autocomplete items
        $("#product-search, #product-search-mobile").each(function() {
            const autocompleteInstance = $(this).autocomplete("instance");
            if (autocompleteInstance) {
                autocompleteInstance._renderItem = function(ul, item) {
                    if (item.noResult) {
                        return $("<li>")
                            .addClass('no-results')
                            .css('pointer-events', 'none')
                            .append(`<div class="ui-menu-item-wrapper" style="pointer-events: none;">${item.label}</div>`)
                            .appendTo(ul);
                    }
                    
                    return $("<li>")
                        .append(`<div class="ui-menu-item-wrapper">${item.label}</div>`)
                        .appendTo(ul);
                };
            }
        });
        
        // Add click outside to close functionality
        $(document).on('click.autocomplete', function(e) {
            const $target = $(e.target);
            const isAutocomplete = $target.closest('.ui-autocomplete').length > 0;
            const isSearchInput = $target.closest('.search-input, #product-search, #product-search-mobile').length > 0;
            
            if (!isAutocomplete && !isSearchInput) {
                $("#product-search, #product-search-mobile").each(function() {
                    const widget = $(this).autocomplete('widget');
                    if (widget.is(':visible') || widget.css('opacity') !== '0') {
                        $(this).autocomplete('close');
                        // Force hide if still visible
                        widget.hide().css({
                            'display': 'none',
                            'visibility': 'hidden',
                            'opacity': '0'
                        });
                    }
                });
            }
        });
        
        // Close on escape key
        $("#product-search, #product-search-mobile").on('keydown', function(e) {
            if (e.keyCode === 27) { // Escape key
                $(this).autocomplete('close');
            }
        });
        
        // Force hide any existing autocomplete widgets that might be stuck
        $('.ui-autocomplete').hide();
        
        console.log('✅ Autocomplete initialized successfully');
    }

    // ===== CART OPERATIONS =====
    addToCart(item) {
        try {
            console.log('addToCart called with item:', item);
            if (!item || !item.Id) {
                console.error('Invalid item passed to addToCart:', item);
                toastr.error('Invalid product data');
                return;
            }
            
            const existingItem = this.cart.find(ci => ci.Id === item.Id);
            if (existingItem) {
                // For weight/volume products, accumulate WeightOrVolume and DisplayValue
                if (item.WeightOrVolume !== null && item.WeightOrVolume !== undefined) {
                    existingItem.WeightOrVolume = (existingItem.WeightOrVolume || 0) + item.WeightOrVolume;
                    // Also accumulate display value if units match
                    if (existingItem.DisplayUnit === item.DisplayUnit && item.DisplayValue) {
                        existingItem.DisplayValue = (existingItem.DisplayValue || 0) + item.DisplayValue;
                    } else {
                        // If units don't match, convert DisplayValue to base unit display
                        existingItem.DisplayValue = existingItem.WeightOrVolume;
                        existingItem.DisplayUnit = existingItem.ProductUnit;
                    }
                    console.log('Updated weight/volume item, new WeightOrVolume:', existingItem.WeightOrVolume, 'DisplayValue:', existingItem.DisplayValue);
                } else {
                    // For piece products, accumulate quantity
                    existingItem.Quantity += item.Quantity;
                    console.log('Updated existing item, new quantity:', existingItem.Quantity);
                }
            } else {
                this.cart.push(item);
                console.log('Added new item to cart, cart length:', this.cart.length);
            }
            this.updateCartDisplay();
            // Refocus barcode input after adding to cart
            this.focusBarcodeInput();
        } catch (error) {
            console.error('Error in addToCart:', error);
            toastr.error('Error adding item to cart: ' + error.message);
        }
    }

    removeFromCart(index) {
        this.cart.splice(index, 1);
        this.updateCartDisplay();
        // Refocus barcode input after removing item
        this.focusBarcodeInput();
    }

    updateQuantity(index, delta) {
        const item = this.cart[index];
        if (!item) return;
        
        item.Quantity += delta;
        if (item.Quantity <= 0) {
            this.cart.splice(index, 1);
        }
        this.updateCartDisplay();
        // Refocus barcode input after updating quantity
        this.focusBarcodeInput();
    }

    clearCart() {
        Swal.fire({
            title: window.i18n.t('pos.areYouSure', 'Are you sure?'),
            text: window.i18n.t('pos.clearAllItems', 'This will clear all items from the cart.'),
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: window.i18n.t('pos.yesClearIt', 'Yes, clear it!'),
            customClass: {
                confirmButton: 'btn btn-primary',
                cancelButton: 'btn btn-danger'
            }
        }).then((result) => {
            if (result.isConfirmed) {
                this.cart = [];
                this.currentDiscount = null; // Clear discount
                this.updateCartDisplay();
                toastr.success(window.i18n.t('pos.cartCleared', 'Cart cleared'));
                // Refocus barcode input after clearing cart
                this.focusBarcodeInput();
            } else {
                // Refocus even if cancelled
                this.focusBarcodeInput();
            }
        });
    }

    async checkout() {
        if (this.cart.length === 0) {
            toastr.warning(window.i18n.t('pos.cartIsEmpty', 'Cart is empty!'));
            return;
        }

        try {
            // Show checkout modal with payment method selection
            const result = await Swal.fire({
                title: window.i18n.t('pos.completeCheckout', 'Complete Checkout'),
                html: `
                    <div class="text-start">
                        <div class="mb-3">
                            <label class="form-label fw-bold">${window.i18n.t('pos.customerOptional', 'Customer (Optional)')}</label>
                            <select id="checkout-customer-select" class="form-select">
                                <option value="">${window.i18n.t('pos.loadingCustomers', 'Loading customers...')}</option>
                            </select>
                        </div>
                        <div class="mb-3">
                            <label class="form-label fw-bold">${window.i18n.t('pos.paymentMethodRequired', 'Payment Method *')}</label>
                            <select id="checkout-payment-method" class="form-select" required>
                                <option value="">${window.i18n.t('pos.selectPaymentMethod', 'Select payment method...')}</option>
                            </select>
                        </div>
                        <div class="mb-3">
                            <label class="form-label fw-bold">${window.i18n.t('pos.totalAmountEditable', 'Total Amount (Editable)')}</label>
                            <div class="input-group">
                                <span class="input-group-text">${this.defaultCurrency?.notation || 'LBP'}</span>
                                <input type="number" id="checkout-total-amount" class="form-control fw-bold" value="${this.calculateTotal().toFixed(2)}" step="0.01" min="0">
                            </div>
                            <small class="text-muted">${window.i18n.t('pos.original', 'Original')}: ${this.defaultCurrency?.notation || 'LBP'} ${this.calculateTotal().toLocaleString('en-US', {minimumFractionDigits: 2, maximumFractionDigits: 2})}</small>
                        </div>
                        <div class="mb-3">
                            <label class="form-label">${window.i18n.t('pos.notesOptional', 'Notes (Optional)')}</label>
                            <textarea id="checkout-notes" class="form-control" rows="2" placeholder="${window.i18n.t('pos.addNotes', 'Add notes...')}"></textarea>
                        </div>
                    </div>
                `,
                showCancelButton: true,
                confirmButtonText: '<i class="fas fa-check me-2"></i>' + window.i18n.t('pos.completeCheckout', 'Complete Checkout'),
                cancelButtonText: window.i18n.t('common.cancel', 'Cancel'),
                customClass: {
                    confirmButton: 'btn btn-success btn-lg',
                    cancelButton: 'btn btn-secondary'
                },
                width: '500px',
                didOpen: async () => {
                    // Load customers
                    try {
                        const customersResponse = await $.ajax({
                            url: '/POS/GetCustomers',
                            method: 'GET'
                        });

                        const customerSelect = $('#checkout-customer-select');
                        customerSelect.empty(); // Clear the loading placeholder

                        // Walk-in customer (no specific customer) is the default option
                        customerSelect.append(`<option value="0" selected>${window.i18n.t('pos.walkInCustomer', 'Walk-in Customer')}</option>`);

                        if (customersResponse.success && customersResponse.data.length > 0) {
                            customersResponse.data.forEach((customer) => {
                                customerSelect.append(`<option value="${customer.id}">${customer.name}</option>`);
                            });
                        }
                    } catch (error) {
                        console.error('Error loading customers:', error);
                        $('#checkout-customer-select').html(`<option value="0" selected>${window.i18n.t('pos.walkInCustomer', 'Walk-in Customer')}</option>`);
                    }

                    // Load payment methods
                    try {
                        const paymentResponse = await $.ajax({
                            url: '/POS/GetPaymentMethods',
                            method: 'GET'
                        });

                        if (paymentResponse.success && paymentResponse.data.length > 0) {
                            const paymentSelect = $('#checkout-payment-method');
                            paymentResponse.data.forEach(pm => {
                                paymentSelect.append(`<option value="${pm.id}" ${pm.isDefault ? 'selected' : ''}>${pm.name}</option>`);
                            });
                        }
                    } catch (error) {
                        console.error('Error loading payment methods:', error);
                        toastr.error(window.i18n.t('pos.failedToLoadPaymentMethods', 'Failed to load payment methods'));
                    }
                },
                preConfirm: () => {
                    const paymentMethodId = $('#checkout-payment-method').val();
                    if (!paymentMethodId) {
                        Swal.showValidationMessage(window.i18n.t('pos.pleaseSelectPaymentMethod', 'Please select a payment method'));
                        return false;
                    }
                    
                    const totalAmount = parseFloat($('#checkout-total-amount').val());
                    if (isNaN(totalAmount) || totalAmount < 0) {
                        Swal.showValidationMessage(window.i18n.t('pos.pleaseEnterValidAmount', 'Please enter a valid total amount'));
                        return false;
                    }
                    
                    const customerVal = $('#checkout-customer-select').val();
                    // "0" (or empty) means walk-in customer -> no specific customer
                    const customerId = customerVal && customerVal !== '0' ? parseInt(customerVal) : null;
                    return {
                        customerId: customerId,
                        paymentMethodId: parseInt(paymentMethodId),
                        totalAmount: totalAmount,
                        notes: $('#checkout-notes').val()
                    };
                }
            });

            if (result.isConfirmed) {
                // Process checkout immediately - no loading popup for speed
                
                // First, create a cart in the backend (keep it active for checkout)
                const cartResponse = await $.ajax({
                    url: '/POS/HoldCart',
                    method: 'POST',
                    contentType: 'application/json',
                    data: JSON.stringify({
                        customerId: result.value.customerId,
                        items: this.cart.map(item => ({
                            productId: item.Id,
                            quantity: item.Quantity,
                            price: item.Price,
                            weightOrVolume: item.WeightOrVolume // Weight/volume for non-piece products
                        })),
                        // currentDiscount is always { type: 'percentage'|'amount', value, name? } -
                        // it has no .amount/.code/.percentage properties, so those always read as
                        // null here previously and every discount silently vanished at checkout.
                        discountAmount: this.currentDiscount?.type === 'amount' ? this.currentDiscount.value : null,
                        discountCode: this.currentDiscount?.name || null,
                        discountPercentage: this.currentDiscount?.type === 'percentage' ? this.currentDiscount.value : null,
                        notes: result.value.notes,
                        isForCheckout: true  // Keep cart active for checkout
                    })
                });

                if (!cartResponse.success) {
                    throw new Error('Failed to create cart');
                }

                const cartId = cartResponse.data.cartId;

                // Now process the checkout
                const checkoutData = {
                    cartId: cartId,
                    customerId: result.value.customerId,
                    totalAmount: result.value.totalAmount, // Use the edited total amount
                    paymentMethodId: result.value.paymentMethodId,
                    discountAmount: this.currentDiscount?.type === 'amount' ? this.currentDiscount.value : null,
                    discountCode: this.currentDiscount?.name || null,
                    discountPercentage: this.currentDiscount?.type === 'percentage' ? this.currentDiscount.value : null,
                    notes: result.value.notes,
                    currencyId: this.defaultCurrency?.id || 1,
                    printReceipt: false
                };
                console.log('🛒 Checkout request data:', checkoutData);
                console.log('🛒 Checkout JSON:', JSON.stringify(checkoutData));
                
                const checkoutResponse = await $.ajax({
                    url: '/POS/Checkout',
                    method: 'POST',
                    contentType: 'application/json',
                    data: JSON.stringify(checkoutData)
                });

                if (checkoutResponse.success) {
                    const orderData = checkoutResponse.data;
                    
                    // Clear the cart
                    this.cart = [];
                    this.currentDiscount = null;
                    this.updateCartDisplay();

                    // Refresh product grid to show updated stock quantities
                    await this.refreshProductGrid();
                    
                    // Print receipt using direct ESC/POS commands - no HTML involved
                    try {
                        // Fix case sensitivity - C# returns OrderId (uppercase), JS expects orderId (lowercase)
                        const orderId = orderData.OrderId || orderData.orderId;
                        console.log('🖨️ Attempting direct thermal print for order:', orderId);
                        
                        if (!orderId) {
                            throw new Error('Order ID not found in checkout response');
                        }
                        
                        var formData = new FormData();
                        formData.append("orderId", orderId)
                        
                        const printResponse = await $.ajax({
                            url: '/Orders/PrintReceiptForCheckout',
                            method: 'POST',
                            contentType: false,
                            processData: false,
                            data: formData
                        });

                        console.log('🖨️ Direct print response:', printResponse);

                        if (printResponse.success) {
                            console.log('✅ Direct thermal receipt printed successfully');
                            const orderNumber = orderData.OrderNumber || orderData.orderNumber || orderId;
                            toastr.success(`${window.i18n.t('pos.saleComplete', 'Sale Complete!')} ${window.i18n.t('pos.receipt', 'Receipt')} #${orderNumber} - Printed via Direct ESC/POS`, window.i18n.t('common.success', 'Success'), {
                                timeOut: 4000,
                                closeButton: true,
                                progressBar: true
                            });
                        } else {
                            console.warn('⚠️ Direct thermal printing failed:', printResponse.message);
                            const warningOrderNumber = orderData.OrderNumber || orderData.orderNumber || orderId;
                            toastr.warning(`${window.i18n.t('pos.saleComplete', 'Sale Complete!')} ${window.i18n.t('pos.receipt', 'Receipt')} #${warningOrderNumber} - ${printResponse.message}`, window.i18n.t('common.warning', 'Warning'), {
                                timeOut: 5000,
                                closeButton: true,
                                progressBar: true
                            });
                            
                            // Fallback: Open receipt in browser for manual printing
                            if (printResponse.receiptUrl) {
                                window.open(printResponse.receiptUrl, '_blank');
                            }
                        }
                    } catch (printError) {
                        console.error('❌ Error with direct thermal printing:', printError);
                        const errorOrderNumber = orderData.OrderNumber || orderData.orderNumber || orderId || 'Unknown';
                        toastr.error(`${window.i18n.t('pos.saleComplete', 'Sale Complete!')} ${window.i18n.t('pos.receipt', 'Receipt')} #${errorOrderNumber} - ${window.i18n.t('pos.printError', 'Print Error')}`, window.i18n.t('common.error', 'Error'), {
                            timeOut: 5000,
                            closeButton: true,
                            progressBar: true
                        });
                        
                        // Fallback: Open receipt in browser for manual printing
                        const fallbackOrderId = orderData.OrderId || orderData.orderId;
                        if (fallbackOrderId) {
                            window.open(`/Orders/Receipt/${fallbackOrderId}`, '_blank');
                        }
                    }
                    
                    // Refocus barcode input after successful checkout
                    this.focusBarcodeInput();
                } else {
                    throw new Error(checkoutResponse.message || 'Checkout failed');
                }
            } else {
                // Checkout was cancelled - refocus barcode input
                this.focusBarcodeInput();
            }
        } catch (error) {
            console.error('Checkout error:', error);
            Swal.fire({
                icon: 'error',
                title: window.i18n.t('pos.checkoutFailed', 'Checkout Failed'),
                text: error.message || window.i18n.t('pos.errorOccurred', 'An error occurred during checkout'),
                confirmButtonText: window.i18n.t('common.ok', 'OK'),
                customClass: {
                    confirmButton: 'btn btn-danger'
                }
            }).then(() => {
                // Refocus after error dialog closes
                this.focusBarcodeInput();
            });
        }
    }
    
    searchProductByBarcode(barcode) {
        // Trim barcode to ensure no whitespace issues
        barcode = (barcode || '').trim();
        
        if (!barcode) {
            return Promise.resolve(null);
        }
        
        return new Promise((resolve, reject) => {
            $.ajax({
                url: `/POS/SearchProduct?searchTerm=${encodeURIComponent(barcode)}`,
                method: 'GET',
                success: function(response) {
                    if (response.success && response.data) {
                        // Handle both single product and array results
                        const productData = Array.isArray(response.data) ? response.data[0] : response.data;
                        resolve(productData);
                    } else {
                        resolve(null);
                    }
                },
                error: function(err) {
                    console.error('Error searching product by barcode:', err);
                    reject(err);
                }
            });
        });
    }
    
    /**
     * Validate if input is a valid barcode (not a QR code or other unwanted format)
     * @param {string} input - The scanned input
     * @returns {{isValid: boolean, reason: string}} - Validation result
     */
    validateBarcodeInput(input) {
        if (!input || input.length === 0) {
            return { isValid: false, reason: 'empty' };
        }

        // QR codes often contain URLs
        if (/^https?:\/\//i.test(input) || /^www\./i.test(input)) {
            console.log('🚫 Rejected: URL detected (likely QR code)');
            return { isValid: false, reason: 'url' };
        }

        // QR codes may contain email addresses
        if (/^mailto:/i.test(input) || /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/.test(input)) {
            console.log('🚫 Rejected: Email detected (likely QR code)');
            return { isValid: false, reason: 'email' };
        }

        // QR codes may contain phone numbers with tel: prefix
        if (/^tel:/i.test(input) || /^sms:/i.test(input)) {
            console.log('🚫 Rejected: Phone/SMS link detected (likely QR code)');
            return { isValid: false, reason: 'phone' };
        }

        // QR codes may contain vCard data
        if (/^BEGIN:VCARD/i.test(input) || /^MECARD:/i.test(input)) {
            console.log('🚫 Rejected: vCard detected (likely QR code)');
            return { isValid: false, reason: 'vcard' };
        }

        // QR codes may contain WiFi configuration
        if (/^WIFI:/i.test(input)) {
            console.log('🚫 Rejected: WiFi config detected (likely QR code)');
            return { isValid: false, reason: 'wifi' };
        }

        // QR codes may contain JSON data
        if (/^\s*[\[{]/.test(input) && /[\]}]\s*$/.test(input)) {
            try {
                JSON.parse(input);
                console.log('🚫 Rejected: JSON data detected (likely QR code)');
                return { isValid: false, reason: 'json' };
            } catch (e) {
                // Not valid JSON, continue checking
            }
        }

        // QR codes may contain geo coordinates
        if (/^geo:/i.test(input)) {
            console.log('🚫 Rejected: Geo location detected (likely QR code)');
            return { isValid: false, reason: 'geo' };
        }

        // QR codes for payments (various formats)
        if (/^bitcoin:/i.test(input) || /^ethereum:/i.test(input) || /^upi:/i.test(input)) {
            console.log('🚫 Rejected: Crypto/payment link detected (likely QR code)');
            return { isValid: false, reason: 'payment' };
        }

        // QR codes often have very long content (barcodes are typically 8-20 chars)
        // Allow up to 50 chars for some longer barcode formats (Code 128, etc.)
        if (input.length > 50) {
            console.log(`🚫 Rejected: Input too long (${input.length} chars, likely QR code)`);
            return { isValid: false, reason: 'too_long' };
        }

        // QR codes may contain multiple lines
        if (input.includes('\n') || input.includes('\r')) {
            console.log('🚫 Rejected: Multi-line input detected (likely QR code)');
            return { isValid: false, reason: 'multiline' };
        }

        // QR codes may contain special characters that barcodes don't typically have
        // Allow: alphanumeric, dash, underscore, dot, space (for some barcode types)
        // Disallow: @, #, $, %, &, etc. in unusual patterns
        if (/[<>{}|\\^~`\[\]]/.test(input)) {
            console.log('🚫 Rejected: Special characters detected (likely QR code)');
            return { isValid: false, reason: 'special_chars' };
        }

        // Valid barcode formats typically match these patterns:
        // - UPC-A: 12 digits
        // - UPC-E: 8 digits  
        // - EAN-13: 13 digits
        // - EAN-8: 8 digits
        // - Code 39: alphanumeric with some special chars
        // - Code 128: alphanumeric
        // - ITF-14: 14 digits
        // Most product barcodes are numeric or alphanumeric without spaces
        
        console.log(`✅ Barcode validated: ${input}`);
        return { isValid: true, reason: 'ok' };
    }

    /**
     * Parse barcode input for quantity prefix
     * Supports formats: "5*123456789" or "5x123456789" (add 5 of barcode 123456789)
     * If no prefix, returns quantity of 1
     * @param {string} input - The raw barcode input
     * @returns {{quantity: number, barcode: string}} - Parsed quantity and barcode
     */
    parseQuantityPrefix(input) {
        if (!input) return { quantity: 1, barcode: '' };
        
        // Match patterns like "5*barcode", "5xbarcode", "5 * barcode", "5 x barcode"
        // Also supports "5*" at the start (user types quantity first, then scans)
        const match = input.match(/^(\d+)\s*[*xX]\s*(.+)$/);
        
        if (match) {
            const quantity = parseInt(match[1], 10);
            const barcode = match[2].trim();
            
            // Validate quantity (must be positive and reasonable)
            if (quantity > 0 && quantity <= 999) {
                console.log(`📦 Quantity prefix detected: ${quantity} x ${barcode}`);
                return { quantity, barcode };
            }
        }
        
        // No valid prefix found, treat entire input as barcode with quantity 1
        return { quantity: 1, barcode: input.trim() };
    }

    async barcodeLookup(input) {
        try {
            // Parse quantity prefix from input (e.g., "5*123456789" = 5 items)
            const { quantity, barcode } = this.parseQuantityPrefix(input);
            
            if (!barcode) {
                // If only quantity prefix was entered (e.g., "5*"), store it for next scan
                if (quantity > 1) {
                    this.pendingQuantity = quantity;
                    toastr.info(window.i18n.t('pos.quantitySetTo', `Quantity set to ${quantity}. Scan the product now.`).replace('${quantity}', quantity));
                    this.elements.barcodeInput.val('');
                    this.focusBarcodeInput();
                }
                return;
            }
            
            // Validate barcode (reject QR codes and other unwanted formats)
            const validation = this.validateBarcodeInput(barcode);
            if (!validation.isValid) {
                this.pendingQuantity = null; // Reset pending quantity on invalid input
                this.elements.barcodeInput.val('');
                
                // Show user-friendly error message based on rejection reason
                let errorMsg = window.i18n.t('pos.qrCodeDetected', 'QR code detected. Please scan the product barcode instead.');
                if (validation.reason === 'too_long') {
                    errorMsg = window.i18n.t('pos.inputTooLong', 'Input too long. Please scan the product barcode, not the QR code.');
                } else if (validation.reason === 'url') {
                    errorMsg = window.i18n.t('pos.urlDetected', 'URL detected. Please scan the product barcode, not the QR code.');
                }
                
                toastr.warning(errorMsg);
                this.focusBarcodeInput();
                return;
            }
            
            // Use pending quantity if set, otherwise use parsed quantity
            const finalQuantity = this.pendingQuantity || quantity;
            this.pendingQuantity = null; // Reset pending quantity
            
            const product = await this.searchProductByBarcode(barcode);
            if (product) {
                const productPrice = product.price || product.sellingPrice;
                const currencyId = product.currencyId || this.defaultCurrency?.id;
                const productUnit = product.unit || 'piece';
                
                console.log(`Barcode lookup: ${product.name}, Price: ${productPrice}, Quantity: ${finalQuantity}, Unit: ${productUnit}, CurrencyId: ${currencyId}, Default Currency ID: ${this.defaultCurrency?.id}`);
                
                // Convert price to default currency if needed
                let convertedPrice = productPrice;
                if (this.defaultCurrency && currencyId && currencyId !== this.defaultCurrency.id) {
                    console.log(`Converting ${productPrice} from currency ${currencyId} to ${this.defaultCurrency.id}`);
                    convertedPrice = await this.convertPrice(productPrice, currencyId, this.defaultCurrency.id);
                    console.log(`Converted price: ${convertedPrice}`);
                } else {
                    console.log(`No conversion - same currency or default not loaded`);
                }
                
                // Handle unit conversion if needed
                let quantity = finalQuantity;
                let unit = productUnit;
                
                if (typeof UnitConverter !== 'undefined' && UnitConverter.requiresConversion(productUnit)) {
                    // For barcode scans, we'll use the product's base unit
                    // User can adjust quantity/unit in cart if needed
                    quantity = finalQuantity;
                    unit = productUnit;
                }
                
                const newItem = new CartItem(
                    product.id, 
                    product.name, 
                    productPrice, 
                    quantity,
                    product.barcode, 
                    product.categoryId || product.category?.id,
                    currencyId,
                    productPrice,
                    convertedPrice,
                    this.defaultCurrency?.notation || 'LBP',  // Cart always displays in default currency
                    productUnit,  // Product's base unit
                    unit          // Selected unit
                );
                this.addToCart(newItem);
                this.elements.barcodeInput.val('');
                
                // Show confirmation if multiple items were added
                if (finalQuantity > 1) {
                    toastr.success(window.i18n.t('pos.addedMultipleItems', `Added ${finalQuantity}x ${product.name}`).replace('${quantity}', finalQuantity).replace('${name}', product.name));
                }
                
                // Refocus after adding item
                this.focusBarcodeInput();
            } else {
                toastr.error(window.i18n.t('pos.productNotFound', 'Product not found for the given barcode.'));
                // Refocus even on error so user can scan again
                this.focusBarcodeInput();
            }
        } catch (err) {
            console.error("Error fetching product by barcode:", err);
            toastr.error(window.i18n.t('pos.errorFetchingProduct', 'Error fetching product. Please try again.'));
            // Refocus on error
            this.focusBarcodeInput();
        }
    }
    
    barcodeInputHandler() {
        const self = this;
        
        this.elements.barcodeInput.off("keydown").on("keydown", function (event) {
            if (event.key === 'Enter') {
                event.preventDefault();
                
                const input = $(this).val().trim();
                if (input) {
                    // Check if it's just a quantity prefix (e.g., "5*" or "5x")
                    const justQuantityPrefix = /^(\d+)\s*[*xX]\s*$/.test(input);
                    
                    if (justQuantityPrefix) {
                        // Extract and store the quantity for next scan
                        const qty = parseInt(input.match(/^(\d+)/)[1], 10);
                        if (qty > 0 && qty <= 999) {
                            self.pendingQuantity = qty;
                            console.log(`📦 Pending quantity SET to: ${qty}`);
                            toastr.info(window.i18n.t('pos.quantitySetTo', `Quantity set to ${qty}. Scan the product now.`).replace('${qty}', qty).replace('${quantity}', qty));
                            $(this).val('');
                            return;
                        }
                    }
                    
                    window.pos.barcodeLookup(input);
                }

                $(this).val('');
            }
        });
        
        // Show visual indicator when pending quantity is set
        this.elements.barcodeInput.on("input", function() {
            const input = $(this).val();
            // Check if typing a quantity prefix
            if (/^\d+[*xX]?$/.test(input)) {
                $(this).addClass('quantity-prefix-mode');
            } else {
                $(this).removeClass('quantity-prefix-mode');
            }
        });
        
        // Also handle the add button click
        this.elements.addProductBtn.off("click").on("click", () => {
            const input = this.elements.barcodeInput.val().trim();
            if (input) {
                this.barcodeLookup(input);
                this.elements.barcodeInput.val('');
                this.elements.barcodeInput.removeClass('quantity-prefix-mode');
                // Refocus after button click
                this.focusBarcodeInput();
            }
        });
    }

    async hold() {
        if (this.cart.length === 0) {
            toastr.warning(window.i18n.t('pos.cartIsEmptyNothingToHold', 'Cart is empty. Nothing to hold.'));
            return;
        }

        try {
            // Ask for customer selection (optional)
            const result = await Swal.fire({
                title: window.i18n.t('pos.holdCart', 'Hold Cart'),
                html: `
                    <div class="mb-3">
                        <label class="form-label">${window.i18n.t('pos.customerOptional', 'Customer (Optional)')}</label>
                        <select id="hold-customer-select" class="form-select">
                            <option value="">${window.i18n.t('pos.loadingCustomers', 'Loading customers...')}</option>
                        </select>
                    </div>
                    <div class="mb-3">
                        <label class="form-label">${window.i18n.t('pos.notesOptional', 'Notes (Optional)')}</label>
                        <textarea id="hold-notes" class="form-control" rows="3" placeholder="${window.i18n.t('pos.addNotes', 'Add notes...')}"></textarea>
                    </div>
                `,
                showCancelButton: true,
                confirmButtonText: window.i18n.t('pos.holdCart', 'Hold Cart'),
                cancelButtonText: window.i18n.t('common.cancel', 'Cancel'),
                customClass: {
                    confirmButton: 'btn btn-primary',
                    cancelButton: 'btn btn-secondary'
                },
                didOpen: async () => {
                    // Load customers
                    try {
                        const response = await $.ajax({
                            url: '/POS/GetCustomers',
                            method: 'GET'
                        });

                        const $select = $('#hold-customer-select');
                        $select.empty(); // Clear the loading placeholder

                        // Walk-in customer (no specific customer) is the default option
                        $select.append(`<option value="0" selected>${window.i18n.t('pos.walkInCustomer', 'Walk-in Customer')}</option>`);

                        if (response.success && response.data.length > 0) {
                            response.data.forEach((customer) => {
                                $select.append(`<option value="${customer.id}">${customer.name}</option>`);
                            });
                        }
                    } catch (error) {
                        console.error('Error loading customers:', error);
                        $('#hold-customer-select').html(`<option value="0" selected>${window.i18n.t('pos.walkInCustomer', 'Walk-in Customer')}</option>`);
                    }
                },
                preConfirm: () => {
                    return {
                        customerId: $('#hold-customer-select').val() || null,
                        notes: $('#hold-notes').val()
                    };
                }
            });

            if (!result.isConfirmed) return;

            // Prepare cart items for hold
            const items = this.cart.map(item => ({
                productId: item.Id,
                quantity: item.Quantity,
                price: item.Price,
                weightOrVolume: item.WeightOrVolume // Weight/volume for non-piece products
            }));

            // Prepare hold request with discount information
            // "0" (or empty) means walk-in customer -> no specific customer
            const customerId = result.value.customerId && result.value.customerId !== '' && result.value.customerId !== '0'
                ? parseInt(result.value.customerId)
                : null;
            
            console.log('Hold cart - Customer ID:', customerId, 'Raw value:', result.value.customerId);
            
            const holdRequest = {
                customerId: customerId,
                items: items,
                notes: result.value.notes
            };

            // Include discount if applied
            if (this.currentDiscount) {
                if (this.currentDiscount.type === 'percentage') {
                    holdRequest.discountPercentage = this.currentDiscount.value;
                } else if (this.currentDiscount.type === 'amount') {
                    holdRequest.discountAmount = this.currentDiscount.value;
                }
                if (this.currentDiscount.name) {
                    holdRequest.notes = (holdRequest.notes ? holdRequest.notes + '\n' : '') + 
                                       `Discount: ${this.currentDiscount.name}`;
                }
            }

            // Send hold request
            const response = await $.ajax({
                url: '/POS/HoldCart',
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify(holdRequest)
            });

            if (response.success) {
                toastr.success(window.i18n.t('pos.cartHeldSuccessfully', 'Cart held successfully!'));
                this.cart = [];
                this.currentDiscount = null;
                this.updateCartDisplay();
            } else {
                toastr.error(response.message || window.i18n.t('pos.failedToHoldCart', 'Failed to hold cart'));
            }
        } catch (error) {
            console.error('Error holding cart:', error);
            toastr.error(window.i18n.t('pos.failedToHoldCart', 'Failed to hold cart. Please try again.'));
        }
    }

    // ===== QUICK ACTIONS =====

    async retrieveHeldCart() {
        try {
            // Get held carts
            const response = await $.ajax({
                url: '/POS/GetHeldCarts',
                method: 'GET'
            });

            if (!response.success || !response.data || response.data.length === 0) {
                toastr.info(window.i18n.t('pos.noHeldCartsFound', 'No held carts found'));
                return;
            }

            // Build options for SweetAlert
            const cartsHtml = response.data.map(cart => {
                const formattedDate = new Date(cart.createdAt).toLocaleString();
                return `
                    <div class="cart-option p-3 mb-2 border rounded" data-cart-id="${cart.id}">
                        <div class="d-flex justify-content-between align-items-center">
                            <div class="flex-grow-1" style="cursor: pointer;" data-action="load">
                                <strong>${cart.customerName}</strong>
                                <div class="text-muted small">${cart.itemCount} items - ${formattedDate}</div>
                            </div>
                            <div class="d-flex align-items-center gap-2">
                                <strong class="text-primary">${this.defaultCurrency?.notation || 'LBP'} ${cart.totalAmount.toFixed(2)}</strong>
                                <button class="btn btn-sm btn-danger" data-action="delete" data-cart-id="${cart.id}" onclick="event.stopPropagation();">
                                    <i class="fas fa-trash"></i>
                                </button>
                            </div>
                        </div>
                    </div>
                `;
            }).join('');

            const result = await Swal.fire({
                title: window.i18n.t('pos.heldCarts', 'Held Carts'),
                html: `
                    <div class="text-start" style="max-height: 400px; overflow-y: auto;">
                        ${cartsHtml}
                    </div>
                `,
                showConfirmButton: true,
                showDenyButton: true,
                confirmButtonText: window.i18n.t('common.close', 'Close'),
                denyButtonText: `<i class="fas fa-trash-alt me-1"></i>${window.i18n.t('pos.deleteAll', 'Delete All')}`,
                showCancelButton: false,
                customClass: {
                    confirmButton: 'btn btn-secondary',
                    denyButton: 'btn btn-danger'
                },
                didOpen: () => {
                    // Add click handlers for loading carts
                    document.querySelectorAll('[data-action="load"]').forEach(element => {
                        element.addEventListener('click', async (e) => {
                            const cartOption = e.currentTarget.closest('.cart-option');
                            const cartId = parseInt(cartOption.dataset.cartId);
                            await this.loadHeldCart(cartId);
                            Swal.close();
                        });
                    });

                    // Add click handlers for delete buttons
                    document.querySelectorAll('[data-action="delete"]').forEach(button => {
                        button.addEventListener('click', async (e) => {
                            e.stopPropagation();
                            const cartId = parseInt(e.currentTarget.dataset.cartId);
                            await this.deleteHeldCart(cartId);
                            // Refresh the held carts list
                            Swal.close();
                            await this.retrieveHeldCart();
                        });
                    });
                }
            });
            
            // Handle Delete All button click
            if (result.isDenied) {
                await this.deleteAllHeldCarts();
            }
        } catch (error) {
            console.error('Error retrieving held carts:', error);
            toastr.error(window.i18n.t('pos.failedToLoadCart', 'Failed to retrieve held carts'));
        }
    }

    async deleteHeldCart(cartId) {
        try {
            const confirmResult = await Swal.fire({
                title: window.i18n.t('pos.deleteHeldCart', 'Delete Held Cart?'),
                text: window.i18n.t('pos.actionCannotBeUndone', 'This action cannot be undone.'),
                icon: 'warning',
                showCancelButton: true,
                confirmButtonText: window.i18n.t('pos.yesDelete', 'Yes, Delete'),
                cancelButtonText: window.i18n.t('common.cancel', 'Cancel'),
                customClass: {
                    confirmButton: 'btn btn-danger',
                    cancelButton: 'btn btn-secondary'
                }
            });

            if (!confirmResult.isConfirmed) {
                return;
            }

            const response = await $.ajax({
                url: '/POS/DeleteHeldCart',
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify({ cartId: cartId })
            });

            if (response.success) {
                toastr.success(window.i18n.t('pos.cartDeletedSuccessfully', 'Cart deleted successfully!'));
            } else {
                toastr.error(response.message || window.i18n.t('pos.failedToDeleteCart', 'Failed to delete cart'));
            }
        } catch (error) {
            console.error('Error deleting held cart:', error);
            toastr.error(window.i18n.t('pos.failedToDeleteCart', 'Failed to delete cart'));
        }
    }

    async deleteAllHeldCarts() {
        try {
            const confirmResult = await Swal.fire({
                title: window.i18n.t('pos.deleteAllHeldCarts', 'Delete All Held Carts?'),
                text: window.i18n.t('pos.allHeldCartsWillBeDeleted', 'All held carts will be permanently deleted. This action cannot be undone.'),
                icon: 'warning',
                showCancelButton: true,
                confirmButtonText: window.i18n.t('pos.yesDeleteAll', 'Yes, Delete All'),
                cancelButtonText: window.i18n.t('common.cancel', 'Cancel'),
                customClass: {
                    confirmButton: 'btn btn-danger',
                    cancelButton: 'btn btn-secondary'
                }
            });

            if (!confirmResult.isConfirmed) {
                return;
            }

            const response = await $.ajax({
                url: '/POS/DeleteAllHeldCarts',
                method: 'POST',
                contentType: 'application/json'
            });

            if (response.success) {
                toastr.success(window.i18n.t('pos.allCartsDeletedSuccessfully', `${response.deletedCount} cart(s) deleted successfully!`));
            } else {
                toastr.error(response.message || window.i18n.t('pos.failedToDeleteCarts', 'Failed to delete carts'));
            }
        } catch (error) {
            console.error('Error deleting all held carts:', error);
            toastr.error(window.i18n.t('pos.failedToDeleteCarts', 'Failed to delete carts'));
        }
    }

    async loadHeldCart(cartId) {
        try {
            // If current cart has items, auto-save it first
            if (this.cart.length > 0) {
                const confirmResult = await Swal.fire({
                    title: window.i18n.t('pos.saveCurrentCart', 'Save Current Cart?'),
                    text: window.i18n.t('pos.saveBeforeLoadingHeld', 'You have items in your current cart. Save it before loading the held cart?'),
                    icon: 'question',
                    showCancelButton: true,
                    showDenyButton: true,
                    confirmButtonText: window.i18n.t('pos.yesSaveCurrent', 'Yes, Save Current'),
                    denyButtonText: window.i18n.t('pos.noDiscardCurrent', 'No, Discard Current'),
                    cancelButtonText: window.i18n.t('common.cancel', 'Cancel'),
                    customClass: {
                        confirmButton: 'btn btn-success',
                        denyButton: 'btn btn-danger',
                        cancelButton: 'btn btn-secondary'
                    }
                });

                if (confirmResult.isDismissed) {
                    return; // User cancelled
                }

                if (confirmResult.isConfirmed) {
                    // Auto-save current cart
                    const items = this.cart.map(item => ({
                        productId: item.Id,
                        quantity: item.Quantity,
                        price: item.Price,
                        weightOrVolume: item.WeightOrVolume // Weight/volume for non-piece products
                    }));

                    const holdRequest = {
                        customerId: null, // Walk-in customer
                        items: items,
                        notes: 'Auto-saved before loading another cart'
                    };

                    // Include discount if applied
                    if (this.currentDiscount) {
                        if (this.currentDiscount.type === 'percentage') {
                            holdRequest.discountPercentage = this.currentDiscount.value;
                        } else if (this.currentDiscount.type === 'amount') {
                            holdRequest.discountAmount = this.currentDiscount.value;
                        }
                    }

                    try {
                        const holdResponse = await $.ajax({
                            url: '/POS/HoldCart',
                            method: 'POST',
                            contentType: 'application/json',
                            data: JSON.stringify(holdRequest)
                        });

                        if (holdResponse.success) {
                            toastr.success(window.i18n.t('pos.currentCartSaved', 'Current cart saved!'));
                        }
                    } catch (error) {
                        console.error('Error auto-saving cart:', error);
                        toastr.warning(window.i18n.t('pos.failedToSaveCurrentCart', 'Failed to save current cart, but continuing...'));
                    }
                }
                // If denied, we just discard the current cart and continue
            }

            // Now load the held cart
            const response = await $.ajax({
                url: `/POS/ResumeHeldCart`,
                method: 'POST',
                contentType: 'application/json',
                data: JSON.stringify({ cartId: cartId })
            });

            if (response.success) {
                console.log('Cart loaded successfully:', response.data);
                console.log('Items count:', response.data.items?.length || 0);
                
                // Clear current cart
                this.cart = [];
                this.currentDiscount = null;

                // Load items from held cart
                if (response.data.items && response.data.items.length > 0) {
                    for (const item of response.data.items) {
                        console.log('Loading item:', item);
                        const cartItem = new CartItem(
                            item.id,
                            item.name,
                            item.price,
                            item.quantity,
                            item.barcode,
                            item.categoryId,
                            item.currencyId,
                            item.price,
                            item.price,
                            item.currencyNotation
                        );
                        this.cart.push(cartItem);
                        console.log('Added to cart:', cartItem);
                    }
                } else {
                    console.warn('No items in held cart');
                }

                // Restore discount if it was saved
                if (response.data.discountPercentage) {
                    this.currentDiscount = {
                        type: 'percentage',
                        value: response.data.discountPercentage
                    };
                } else if (response.data.discountAmount) {
                    this.currentDiscount = {
                        type: 'amount',
                        value: response.data.discountAmount
                    };
                }

                console.log('Cart after loading:', this.cart);
                console.log('Calling updateCartDisplay...');
                this.updateCartDisplay();
                toastr.success(`${window.i18n.t('pos.cartLoadedSuccessfully', 'Cart loaded successfully!')} ${this.cart.length} ${window.i18n.t('pos.items', 'item(s)')}`);
                
                if (this.currentDiscount) {
                    const discountText = this.currentDiscount.type === 'percentage' 
                        ? `${this.currentDiscount.value}%` 
                        : `${this.defaultCurrency?.notation || 'LBP'} ${this.currentDiscount.value}`;
                    toastr.info(`${window.i18n.t('pos.discountRestored', 'Discount restored')}: ${discountText}`);
                }
            } else {
                console.error('Failed to load cart:', response.message);
                toastr.error(response.message || window.i18n.t('pos.failedToLoadCart', 'Failed to load cart'));
            }
        } catch (error) {
            console.error('Error loading held cart:', error);
            toastr.error(window.i18n.t('pos.failedToLoadCart', 'Failed to load cart'));
        }
    }

    addManualItem() {
        Swal.fire({
            title: window.i18n.t('pos.addManualItem', 'Add Manual Item'),
            html: `
                <div class="text-start">
                    <div class="mb-3">
                        <label class="form-label">${window.i18n.t('pos.productName', 'Product Name')}</label>
                        <input type="text" id="manual-item-name" class="form-control" placeholder="${window.i18n.t('pos.enterProductName', 'Enter product name')}">
                    </div>
                    <div class="mb-3">
                        <label class="form-label">${window.i18n.t('pos.price', 'Price')}</label>
                        <input type="number" id="manual-item-price" class="form-control" step="0.01" placeholder="0.00">
                    </div>
                    <div class="mb-3">
                        <label class="form-label">${window.i18n.t('pos.quantity', 'Quantity')}</label>
                        <input type="number" id="manual-item-quantity" class="form-control" value="1" min="1">
                    </div>
                </div>
            `,
            showCancelButton: true,
            confirmButtonText: window.i18n.t('pos.addItem', 'Add Item'),
            cancelButtonText: window.i18n.t('common.cancel', 'Cancel'),
            customClass: {
                confirmButton: 'btn btn-primary',
                cancelButton: 'btn btn-secondary'
            },
            preConfirm: () => {
                const name = $('#manual-item-name').val() || 'Diver';
                const price = parseFloat($('#manual-item-price').val());
                const quantity = parseInt($('#manual-item-quantity').val());

                if (!name) {
                    Swal.showValidationMessage(window.i18n.t('pos.pleaseEnterProductName', 'Please enter product name'));
                    return false;
                }
                if (!price || price <= 0) {
                    Swal.showValidationMessage(window.i18n.t('pos.pleaseEnterValidPrice', 'Please enter valid price'));
                    return false;
                }
                if (!quantity || quantity <= 0) {
                    Swal.showValidationMessage(window.i18n.t('pos.pleaseEnterValidQuantity', 'Please enter valid quantity'));
                    return false;
                }

                return { name, price, quantity };
            }
        }).then((result) => {
            if (result.isConfirmed) {
                const { name, price, quantity } = result.value;
                
                // Generate a temporary negative ID for manual items
                const manualId = -Date.now();
                const manualBarcode = `MANUAL-${manualId}`;
                
                const manualItem = new CartItem(
                    manualId,
                    name,
                    price,
                    quantity,
                    manualBarcode,
                    0, // No category
                    this.defaultCurrency?.id,
                    price,
                    price,
                    this.defaultCurrency?.notation || 'LBP'
                );
                
                this.cart.push(manualItem);
                this.updateCartDisplay();
                toastr.success(`${name} ${window.i18n.t('pos.addedToCart', 'added to cart')}`);
            }
        });
    }

    async applyDiscount() {
        if (this.cart.length === 0) {
            toastr.warning(window.i18n.t('pos.cartIsEmptyAddItems', 'Cart is empty. Add items before applying discount.'));
            return;
        }

        try {
            // Load available discounts
            const discountsResponse = await $.ajax({
                url: '/POS/GetAvailableDiscounts',
                method: 'GET'
            });

            let discountOptions = '';
            if (discountsResponse.success && discountsResponse.data.length > 0) {
                discountOptions = discountsResponse.data.map(d => 
                    `<option value="${d.id}" data-rate="${d.rate}">${d.name} (${d.rate}%)</option>`
                ).join('');
            }

            const result = await Swal.fire({
                title: window.i18n.t('pos.applyDiscount', 'Apply Discount'),
                html: `
                    <div class="text-start">
                        <div class="mb-3">
                            <label class="form-label">${window.i18n.t('pos.discountType', 'Discount Type')}</label>
                            <select id="discount-type" class="form-select">
                                <option value="percentage">${window.i18n.t('pos.percentage', 'Percentage')}</option>
                                <option value="amount">${window.i18n.t('pos.fixedAmount', 'Fixed Amount')}</option>
                                ${discountOptions ? `<option value="preset">${window.i18n.t('pos.presetDiscount', 'Preset Discount')}</option>` : ''}
                            </select>
                        </div>
                        ${discountOptions ? `
                        <div class="mb-3" id="preset-discount-section" style="display: none;">
                            <label class="form-label">${window.i18n.t('pos.selectDiscount', 'Select Discount')}</label>
                            <select id="preset-discount" class="form-select">
                                ${discountOptions}
                            </select>
                        </div>
                        ` : ''}
                        <div class="mb-3" id="discount-value-section">
                            <label class="form-label" id="discount-value-label">${window.i18n.t('pos.percentage', 'Percentage')} (%)</label>
                            <input type="number" id="discount-value" class="form-control" step="0.01" placeholder="0">
                        </div>
                        <div class="alert alert-info">
                            <small><strong>${window.i18n.t('pos.currentTotal', 'Current Total')}:</strong> ${this.defaultCurrency?.notation || 'LBP'} ${this.total.toFixed(2)}</small>
                        </div>
                    </div>
                `,
                showCancelButton: true,
                confirmButtonText: window.i18n.t('common.apply', 'Apply'),
                cancelButtonText: window.i18n.t('common.cancel', 'Cancel'),
                customClass: {
                    confirmButton: 'btn btn-primary',
                    cancelButton: 'btn btn-secondary'
                },
                didOpen: () => {
                    const typeSelect = $('#discount-type');
                    const valueInput = $('#discount-value');
                    const valueLabel = $('#discount-value-label');
                    const valueSection = $('#discount-value-section');
                    const presetSection = $('#preset-discount-section');

                    typeSelect.on('change', function() {
                        const type = $(this).val();
                        if (type === 'percentage') {
                            valueLabel.text('Percentage (%)');
                            valueSection.show();
                            presetSection.hide();
                        } else if (type === 'amount') {
                            valueLabel.text(`Amount (${window.pos.defaultCurrency?.notation || 'LBP'})`);
                            valueSection.show();
                            presetSection.hide();
                        } else if (type === 'preset') {
                            valueSection.hide();
                            presetSection.show();
                        }
                    });
                },
                preConfirm: () => {
                    const type = $('#discount-type').val();
                    
                    if (type === 'preset') {
                        const selectedOption = $('#preset-discount option:selected');
                        return {
                            type: 'percentage',
                            value: parseFloat(selectedOption.data('rate')),
                            name: selectedOption.text()
                        };
                    } else {
                        const value = parseFloat($('#discount-value').val());
                        
                        if (!value || value <= 0) {
                            Swal.showValidationMessage(window.i18n.t('pos.pleaseEnterValidDiscount', 'Please enter a valid discount value'));
                            return false;
                        }

                        if (type === 'percentage' && value > 100) {
                            Swal.showValidationMessage(window.i18n.t('pos.percentageCannotExceed100', 'Percentage cannot exceed 100%'));
                            return false;
                        }

                        if (type === 'amount' && value > window.pos.total) {
                            Swal.showValidationMessage(window.i18n.t('pos.discountCannotExceedTotal', 'Discount cannot exceed total amount'));
                            return false;
                        }

                        return { type, value };
                    }
                }
            });

            if (result.isConfirmed) {
                const { type, value, name } = result.value;
                
                // Store discount information
                this.currentDiscount = { type, value, name };
                
                // Update display to show discount
                this.updateCartDisplay();
                
                const discountText = name || (type === 'percentage' ? `${value}%` : `${this.defaultCurrency?.notation || 'LBP'} ${value}`);
                toastr.success(`${window.i18n.t('pos.discountApplied', 'Discount applied')}: ${discountText}`);
            }
        } catch (error) {
            console.error('Error applying discount:', error);
            toastr.error(window.i18n.t('pos.failedToLoadDiscounts', 'Failed to load discounts'));
        }
    }

    async priceCheck() {
        const result = await Swal.fire({
            title: window.i18n.t('pos.priceCheck', 'Price Check'),
            html: `
                <div class="price-check-modal text-start">
                    <div class="pc-search">
                        <i class="fas fa-barcode pc-search-icon"></i>
                        <input type="text" id="price-check-barcode" class="pc-search-input" placeholder="${window.i18n.t('pos.scanOrEnterBarcode', 'Scan or enter barcode')}" autocomplete="off" autofocus>
                    </div>

                    <div id="price-check-empty" class="pc-empty">
                        <i class="fas fa-magnifying-glass-dollar"></i>
                        <span>${window.i18n.t('pos.scanOrEnterBarcode', 'Scan or enter barcode')}</span>
                    </div>

                    <div id="price-check-result" class="pc-result" style="display: none;">
                        <div class="pc-result-head">
                            <div class="pc-result-icon"><i class="fas fa-box"></i></div>
                            <div class="pc-result-title">
                                <h5 id="price-check-name"></h5>
                                <span class="pc-barcode"><i class="fas fa-barcode"></i> <span id="price-check-barcode-display"></span></span>
                            </div>
                        </div>

                        <div class="pc-price-tag">
                            <span class="pc-price-label">${window.i18n.t('pos.price', 'Price')}</span>
                            <span class="pc-price-value" id="price-check-price"></span>
                        </div>

                        <div class="pc-meta">
                            <div class="pc-meta-item">
                                <span class="pc-meta-label"><i class="fas fa-warehouse"></i> ${window.i18n.t('pos.stock', 'Stock')}</span>
                                <span class="pc-meta-value" id="price-check-stock"></span>
                            </div>
                            <div class="pc-meta-item">
                                <span class="pc-meta-label"><i class="fas fa-tags"></i> ${window.i18n.t('pos.category', 'Category')}</span>
                                <span class="pc-meta-value" id="price-check-category"></span>
                            </div>
                            <div class="pc-meta-item">
                                <span class="pc-meta-label"><i class="fas fa-truck-field"></i> ${window.i18n.t('pos.supplier', 'Supplier')}</span>
                                <span class="pc-meta-value" id="price-check-supplier"></span>
                            </div>
                        </div>
                    </div>
                </div>

                <style>
                    .price-check-modal { --pc-accent: #0d9488; }
                    .price-check-modal .pc-search {
                        position: relative; margin-bottom: 1rem;
                    }
                    .price-check-modal .pc-search-icon {
                        position: absolute; top: 50%; left: 0.9rem; transform: translateY(-50%);
                        color: #94a3b8; font-size: 0.95rem; pointer-events: none;
                    }
                    .price-check-modal .pc-search-input {
                        width: 100%; padding: 0.8rem 1rem 0.8rem 2.5rem;
                        border: 1.5px solid #e2e8f0; border-radius: 12px;
                        font-size: 1rem; color: #1e293b; background: #f8fafc;
                        transition: border-color .15s, box-shadow .15s, background .15s;
                    }
                    .price-check-modal .pc-search-input:focus {
                        outline: none; border-color: var(--pc-accent); background: #fff;
                        box-shadow: 0 0 0 4px rgba(13,148,136,.12);
                    }
                    .price-check-modal .pc-empty {
                        display: flex; flex-direction: column; align-items: center; gap: .5rem;
                        padding: 1.75rem 1rem; color: #94a3b8;
                        border: 1.5px dashed #e2e8f0; border-radius: 12px;
                    }
                    .price-check-modal .pc-empty i { font-size: 1.6rem; }
                    .price-check-modal .pc-empty span { font-size: .85rem; }

                    .price-check-modal .pc-result {
                        border: 1px solid #e2e8f0; border-radius: 14px; overflow: hidden;
                        background: #fff; box-shadow: 0 4px 16px rgba(15,23,42,.06);
                    }
                    .price-check-modal .pc-result-head {
                        display: flex; align-items: center; gap: .8rem;
                        padding: 1rem; border-bottom: 1px solid #f1f5f9;
                    }
                    .price-check-modal .pc-result-icon {
                        flex: 0 0 auto; width: 42px; height: 42px; border-radius: 10px;
                        display: flex; align-items: center; justify-content: center;
                        background: rgba(13,148,136,.1); color: var(--pc-accent); font-size: 1.1rem;
                    }
                    .price-check-modal .pc-result-title { min-width: 0; }
                    .price-check-modal .pc-result-title h5 {
                        margin: 0; font-size: 1.05rem; font-weight: 700; color: #1e293b;
                        white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
                    }
                    .price-check-modal .pc-barcode {
                        font-size: .8rem; color: #64748b; letter-spacing: .02em;
                    }
                    .price-check-modal .pc-price-tag {
                        display: flex; align-items: baseline; justify-content: space-between;
                        margin: 1rem; padding: .85rem 1rem; border-radius: 12px;
                        background: linear-gradient(135deg, rgba(13,148,136,.12), rgba(13,148,136,.04));
                    }
                    .price-check-modal .pc-price-label {
                        font-size: .8rem; font-weight: 600; text-transform: uppercase;
                        letter-spacing: .06em; color: var(--pc-accent);
                    }
                    .price-check-modal .pc-price-value {
                        font-size: 1.6rem; font-weight: 800; color: #0f766e;
                    }
                    .price-check-modal .pc-meta {
                        padding: 0 1rem 1rem;
                    }
                    .price-check-modal .pc-meta-item {
                        display: flex; align-items: center; justify-content: space-between;
                        padding: .6rem 0; border-top: 1px solid #f1f5f9; gap: 1rem;
                    }
                    .price-check-modal .pc-meta-label {
                        font-size: .85rem; color: #64748b; display: inline-flex; align-items: center; gap: .5rem;
                    }
                    .price-check-modal .pc-meta-label i { color: #94a3b8; width: 1rem; text-align: center; }
                    .price-check-modal .pc-meta-value {
                        font-size: .9rem; font-weight: 600; color: #1e293b; text-align: right;
                    }
                    .price-check-modal .pc-stock-badge {
                        display: inline-block; padding: .2rem .6rem; border-radius: 999px;
                        font-size: .8rem; font-weight: 700;
                    }
                    .price-check-modal .pc-stock-ok { background: rgba(22,163,74,.12); color: #15803d; }
                    .price-check-modal .pc-stock-low { background: rgba(234,179,8,.15); color: #a16207; }
                    .price-check-modal .pc-stock-out { background: rgba(220,38,38,.12); color: #b91c1c; }
                </style>
            `,
            showCancelButton: true,
            showConfirmButton: false,
            cancelButtonText: window.i18n.t('common.close', 'Close'),
            customClass: {
                cancelButton: 'btn btn-secondary'
            },
            didOpen: () => {
                const barcodeInput = $('#price-check-barcode');
                const resultDiv = $('#price-check-result');
                const emptyDiv = $('#price-check-empty');

                barcodeInput.on('keypress', async function(e) {
                    if (e.key === 'Enter') {
                        e.preventDefault();
                        const barcode = $(this).val().trim();
                        
                        if (!barcode) {
                            toastr.warning(window.i18n.t('pos.pleaseEnterBarcode', 'Please enter a barcode'));
                            return;
                        }

                        try {
                            const response = await $.ajax({
                                url: `/POS/CheckPrice?barcode=${encodeURIComponent(barcode)}`,
                                method: 'GET'
                            });

                            if (response.success) {
                                const product = response.data;
                                
                                const qty = product.stockQuantity;
                                const unitsLabel = window.i18n.t('pos.units', 'units');
                                let stockClass = 'pc-stock-ok';
                                if (qty <= 0) stockClass = 'pc-stock-out';
                                else if (qty <= 5) stockClass = 'pc-stock-low';

                                $('#price-check-name').text(product.name);
                                $('#price-check-barcode-display').text(product.barcode);
                                $('#price-check-price').text(`${product.currencyNotation} ${product.price.toFixed(2)}`);
                                $('#price-check-stock').html(`<span class="pc-stock-badge ${stockClass}">${qty} ${unitsLabel}</span>`);
                                $('#price-check-category').text(product.categoryName || 'N/A');
                                $('#price-check-supplier').text(product.supplierName || 'N/A');

                                emptyDiv.hide();
                                resultDiv.slideDown();

                                // Play a success sound (optional)
                                toastr.success(window.i18n.t('pos.productFound', 'Product found!'));
                            } else {
                                toastr.error(response.message || window.i18n.t('pos.productNotFound', 'Product not found'));
                                resultDiv.slideUp();
                                emptyDiv.show();
                            }
                        } catch (error) {
                            console.error('Error checking price:', error);
                            toastr.error(window.i18n.t('pos.failedToCheckPrice', 'Failed to check price'));
                            resultDiv.slideUp();
                            emptyDiv.show();
                        }

                        // Clear input for next scan
                        $(this).val('').focus();
                    }
                });
            }
        });
    }

    // ===== UI UPDATES =====
    calculateTotal() {
        // Calculate subtotal from all items in cart
        let subtotal = this.cart.reduce((sum, item) => sum + item.Total, 0);
        
        // Apply discount if present
        let discountAmount = 0;
        if (this.currentDiscount) {
            if (this.currentDiscount.type === 'percentage') {
                discountAmount = subtotal * (this.currentDiscount.value / 100);
            } else if (this.currentDiscount.type === 'amount') {
                discountAmount = this.currentDiscount.value;
            }
        }
        
        return Math.max(0, subtotal - discountAmount);
    }

    updateCartDisplay() {
        this.elements.cartTableBody.empty();
        let subtotal = 0;
        let itemQty = 0;
        
        // Render cart items
        this.cart.forEach((item, index) => {
            subtotal += item.Total;
            const itemHtml = $(item.createHtmlElement(index));
            
            // Bind event handlers for this specific item
            itemHtml.find(".qty-increase").on("click", () => this.updateQuantity(index, 1));
            itemHtml.find(".qty-decrease").on("click", () => this.updateQuantity(index, -1));
            itemHtml.find(".remove-item").on("click", () => this.removeFromCart(index));
            
            this.elements.cartTableBody.append(itemHtml);
            itemQty++;
        });
        
        // Calculate total with discount
        let discountAmount = 0;
        if (this.currentDiscount) {
            if (this.currentDiscount.type === 'percentage') {
                discountAmount = subtotal * (this.currentDiscount.value / 100);
            } else if (this.currentDiscount.type === 'amount') {
                discountAmount = this.currentDiscount.value;
            }
        }
        
        this.total = Math.max(0, subtotal - discountAmount);
        
        // Show/hide table vs empty state
        if (itemQty === 0) {
            this.elements.cartTableContainer.addClass("d-none");
            this.elements.emptyCart.removeClass("d-none");
            this.elements.cartCount.text("0");
            this.currentDiscount = null; // Clear discount when cart is empty
        } else {
            this.elements.cartTableContainer.removeClass("d-none");
            this.elements.emptyCart.addClass("d-none");
            this.elements.cartCount.text(itemQty);
        }
        
        // Update totals - show in default currency
        const defaultNotation = this.defaultCurrency?.notation || 'LBP';
        
        // Build total display with discount info
        let totalText = `${defaultNotation} ${this.total.toLocaleString('en-US', {minimumFractionDigits: 0, maximumFractionDigits: 2})}`;
        if (this.currentDiscount && discountAmount > 0) {
            totalText = `
                <div>
                    <small class="text-muted" style="text-decoration: line-through;">
                        ${defaultNotation} ${subtotal.toLocaleString('en-US', {minimumFractionDigits: 0, maximumFractionDigits: 2})}
                    </small>
                </div>
                <div>
                    ${defaultNotation} ${this.total.toLocaleString('en-US', {minimumFractionDigits: 0, maximumFractionDigits: 2})}
                </div>
                <div>
                    <small class="badge bg-success">
                        -${defaultNotation} ${discountAmount.toLocaleString('en-US', {minimumFractionDigits: 0, maximumFractionDigits: 2})}
                    </small>
                </div>
            `;
        }
        this.elements.totalDisplay.html(totalText);
        
        // Find the alternative currency (the one that's NOT default)
        const alternativeCurrency = this.currencies.find(c => !c.isDefault);
        
        if (alternativeCurrency && this.defaultCurrency) {
            // Convert total from default currency to alternative currency
            // Convert to USD first, then to alternative currency
            const totalInUSD = this.total / this.defaultCurrency.rateToUSD;
            const totalInAlternative = totalInUSD * alternativeCurrency.rateToUSD;
            
            this.elements.totalUsdDisplay.text(`${alternativeCurrency.notation} ${totalInAlternative.toLocaleString('en-US', {minimumFractionDigits: 0, maximumFractionDigits: 2})}`);
        } else {
            // Fallback if no alternative currency found
            this.elements.totalUsdDisplay.text(`USD ${(this.total / (this.defaultCurrency?.rateToUSD || 1)).toLocaleString('en-US', {minimumFractionDigits: 2, maximumFractionDigits: 2})}`);
        }
        
        // Enable/disable checkout button
        this.elements.checkoutButton.prop("disabled", itemQty === 0);
    }

    // ===== PRODUCT FILTERING =====
    filterProducts(searchTerm) {
        $(".product-card").each(function() {
            const $card = $(this);
            const productName = $card.find(".product-name").text().toLowerCase();
            const productPrice = $card.find(".product-price").text().toLowerCase();
            
            if (productName.includes(searchTerm) || productPrice.includes(searchTerm)) {
                $card.show();
            } else {
                $card.hide();
            }
        });
    }
    
    filterProductsByCategory(category) {
        if (!category) {
            // Show all products
            $(".product-card").show();
        } else {
            $(".product-card").each(function() {
                const $card = $(this);
                const cardCategory = $card.data("category");
                
                if (cardCategory === category) {
                    $card.show();
                } else {
                    $card.hide();
                }
            });
        }
    }


    async selectProductFromAutocomplete(productId) {
        try {
            const response = await $.ajax({
                url: `/POS/GetProductById?id=${productId}`,
                method: 'GET'
            });

            if (response && response.success && response.data) {
                const product = response.data;
                
                // Get the price (could be sellingPrice or price)
                const originalPrice = product.sellingPrice || product.price || 0;
                
                // Convert price to default currency if needed
                let convertedPrice = originalPrice;
                let displayNotation = this.defaultCurrency?.notation || 'LBP';
                
                if (this.defaultCurrency && product.currencyId && product.currencyId !== this.defaultCurrency.id) {
                    convertedPrice = await this.convertPrice(originalPrice, product.currencyId, this.defaultCurrency.id);
                }

                // Use CartItem constructor to match the pattern used elsewhere
                const item = new CartItem(
                    product.id,
                    product.name,
                    originalPrice,
                    1,
                    product.barcode || '',
                    product.categoryId || 0,
                    product.currencyId,
                    originalPrice, // OriginalPrice
                    convertedPrice, // ConvertedPrice
                    displayNotation // CurrencyNotation (always show in default currency)
                );

                this.addToCart(item);
                toastr.success(`${product.name} added to cart`);
            } else {
                toastr.error('Product not found');
            }
        } catch (error) {
            console.error('Error loading product:', error);
            toastr.error('Failed to load product');
        }
    }

    escapeHtml(text) {
        const map = {
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#039;'
        };
        return text.replace(/[&<>"']/g, m => map[m]);
    }

    // ===== UTILITY METHODS =====
    getQtyInCart(id) {
        if (!this.cart || this.cart.length === 0) {
            return 1;
        }
        return this.cart.filter(ci => ci.Id === id)?.length + 1 ?? 1;
    }

    async handlePackProduct(packId, packName, packPrice, packSku, category, currencyId, productCurrencyNotation, packSize, singleProductId) {
        try {
            // If single product is already set, use it directly
            if (singleProductId > 0) {
                // Load single product details
                const singleProductResponse = await $.ajax({
                    url: `/POS/GetProductById?id=${singleProductId}`,
                    method: 'GET'
                });
                
                if (singleProductResponse.success && singleProductResponse.data) {
                    const singleProduct = singleProductResponse.data;
                    const singlePrice = singleProduct.sellingPrice || singleProduct.price || 0;
                    const singleCurrencyId = singleProduct.currencyId || currencyId;
                    
                    // Convert price to default currency if needed
                    let convertedPrice = singlePrice;
                    let displayNotation = this.defaultCurrency?.notation || 'LBP';
                    
                    if (this.defaultCurrency && singleCurrencyId && singleCurrencyId !== this.defaultCurrency.id) {
                        convertedPrice = await this.convertPrice(singlePrice, singleCurrencyId, this.defaultCurrency.id);
                    }
                    
                    const newItem = new CartItem(
                        singleProduct.id,
                        `${singleProduct.name} (from ${packName})`,
                        singlePrice,
                        1,
                        singleProduct.barcode || packSku,
                        singleProduct.categoryId || category,
                        singleCurrencyId,
                        singlePrice,
                        convertedPrice,
                        displayNotation
                    );
                    
                    this.addToCart(newItem);
                    toastr.success(`Added ${singleProduct.name} from pack`);
                    return;
                }
            }
            
            // Show modal to select or create single product
            const result = await Swal.fire({
                title: window.i18n.t('pos.selectSingleProduct', 'Select Single Product'),
                html: `
                    <div class="text-start">
                        <p class="mb-3">${window.i18n.t('pos.packContains', 'This pack contains')} <strong>${packSize}</strong> ${window.i18n.t('pos.singleProducts', 'single products')}.</p>
                        <div class="mb-3">
                            <label class="form-label">${window.i18n.t('pos.selectSingleProduct', 'Select Single Product')}</label>
                            <select id="single-product-select" class="form-select">
                                <option value="">${window.i18n.t('pos.loading', 'Loading...')}</option>
                            </select>
                        </div>
                        <div class="text-center">
                            <button type="button" id="create-single-product-btn" class="btn btn-outline-primary">
                                <i class="fas fa-plus me-2"></i>${window.i18n.t('pos.createNewProduct', 'Create New Product')}
                            </button>
                        </div>
                    </div>
                `,
                showCancelButton: true,
                confirmButtonText: window.i18n.t('pos.addToCart', 'Add to Cart'),
                cancelButtonText: window.i18n.t('common.cancel', 'Cancel'),
                customClass: {
                    confirmButton: 'btn btn-primary',
                    cancelButton: 'btn btn-secondary'
                },
                didOpen: async () => {
                    // Load available single products (non-pack products)
                    try {
                        const productsResponse = await $.ajax({
                            url: '/POS/GetProducts?excludePacks=true',
                            method: 'GET'
                        });
                        
                        const $select = $('#single-product-select');
                        $select.empty();
                        
                        if (productsResponse.success && productsResponse.data && productsResponse.data.length > 0) {
                            $select.append(`<option value="">${window.i18n.t('pos.selectProduct', 'Select a product...')}</option>`);
                            productsResponse.data.forEach(product => {
                                $select.append(`<option value="${product.id}">${product.name} - ${product.barcode || ''}</option>`);
                            });
                        } else {
                            $select.append(`<option value="">${window.i18n.t('pos.noProductsAvailable', 'No products available')}</option>`);
                        }
                    } catch (error) {
                        console.error('Error loading products:', error);
                        $('#single-product-select').html(`<option value="">${window.i18n.t('pos.errorLoadingProducts', 'Error loading products')}</option>`);
                    }
                    
                    // Handle create new product button
                    $('#create-single-product-btn').on('click', () => {
                        const currentLang = window.location.pathname.split('/')[1] || 'en';
                        const createUrl = `/${currentLang}/Products/Create?noSidebar=true&packId=${packId}&packSize=${packSize}`;
                        window.open(createUrl, '_blank');
                        Swal.close();
                    });
                },
                preConfirm: () => {
                    const selectedProductId = $('#single-product-select').val();
                    if (!selectedProductId) {
                        Swal.showValidationMessage(window.i18n.t('pos.pleaseSelectProduct', 'Please select a product'));
                        return false;
                    }
                    return selectedProductId;
                }
            });
            
            if (result.isConfirmed && result.value) {
                // Load selected product and add to cart
                const selectedProductResponse = await $.ajax({
                    url: `/POS/GetProductById?id=${result.value}`,
                    method: 'GET'
                });
                
                if (selectedProductResponse.success && selectedProductResponse.data) {
                    const singleProduct = selectedProductResponse.data;
                    const singlePrice = singleProduct.sellingPrice || singleProduct.price || 0;
                    const singleCurrencyId = singleProduct.currencyId || currencyId;
                    
                    // Convert price to default currency if needed
                    let convertedPrice = singlePrice;
                    let displayNotation = this.defaultCurrency?.notation || 'LBP';
                    
                    if (this.defaultCurrency && singleCurrencyId && singleCurrencyId !== this.defaultCurrency.id) {
                        convertedPrice = await this.convertPrice(singlePrice, singleCurrencyId, this.defaultCurrency.id);
                    }
                    
                    const newItem = new CartItem(
                        singleProduct.id,
                        `${singleProduct.name} (from ${packName})`,
                        singlePrice,
                        1,
                        singleProduct.barcode || packSku,
                        singleProduct.categoryId || category,
                        singleCurrencyId,
                        singlePrice,
                        convertedPrice,
                        displayNotation
                    );
                    
                    this.addToCart(newItem);
                    toastr.success(`Added ${singleProduct.name} from pack`);
                }
            }
        } catch (error) {
            console.error('Error handling pack product:', error);
            toastr.error(window.i18n.t('pos.errorHandlingPack', 'Error handling pack product'));
        }
    }

    /**
     * Show quantity input modal for products that require weight/volume entry
     * @param {string} productName - Product name
     * @param {string} productUnit - Product's base unit (kg, L, etc.)
     * @param {number} pricePerUnit - Price per unit (e.g., $2/kg)
     * @param {string} currencyNotation - Currency notation (e.g., "LBP", "$")
     */
    async showQuantityInputModal(productName, productUnit, pricePerUnit = 0, currencyNotation = 'LBP') {
        if (typeof UnitConverter === 'undefined') {
            // Fallback if UnitConverter is not loaded
            console.warn('UnitConverter not loaded, using default quantity');
            return { quantity: 1, unit: productUnit || 'piece', weightOrVolume: null };
        }

        if (!productUnit || productUnit === 'piece') {
            // No conversion needed for piece units
            return { quantity: 1, unit: 'piece', weightOrVolume: null };
        }

        const availableUnits = UnitConverter.getAvailableUnits(productUnit);
        if (!availableUnits || availableUnits.length === 0) {
            console.warn('No available units found, using default');
            return { quantity: 1, unit: productUnit, weightOrVolume: null };
        }

        const productUnitDisplay = UnitConverter.formatUnit(productUnit);
        const isWeight = UnitConverter.isWeightUnit(productUnit);
        const inputLabel = isWeight ? 'Weight' : 'Volume';
        const placeholder = isWeight ? 'e.g., 300 for 300g' : 'e.g., 500 for 500ml';

        const result = await Swal.fire({
            title: `${productName}`,
            html: `
                <div class="text-start">
                    <div class="alert alert-secondary mb-3">
                        <div class="d-flex justify-content-between align-items-center">
                            <span>Price per ${productUnitDisplay}:</span>
                            <strong class="fs-5">${currencyNotation} ${pricePerUnit.toLocaleString('en-US', {minimumFractionDigits: 2, maximumFractionDigits: 2})}</strong>
                        </div>
                    </div>
                    <div class="mb-3">
                        <label class="form-label fw-bold">${inputLabel}</label>
                        <div class="input-group">
                            <input type="number" 
                                   id="quantity-input" 
                                   class="form-control form-control-lg" 
                                   placeholder="${placeholder}"
                                   step="1" 
                                   min="1"
                                   autofocus>
                            <select id="unit-select" class="form-select" style="max-width: 100px;">
                                ${availableUnits.map(u => {
                                    // Default to smaller unit (g instead of kg, ml instead of L) for easier input
                                    const smallerUnits = ['g', 'ml'];
                                    const isSmaller = smallerUnits.includes(u.value.toLowerCase());
                                    return `<option value="${u.value}" ${isSmaller ? 'selected' : ''}>${UnitConverter.formatUnit(u.value)}</option>`;
                                }).join('')}
                            </select>
                        </div>
                        <small class="text-muted">Enter the amount you want to buy</small>
                    </div>
                    <div id="price-calculation" class="alert alert-success mb-0">
                        <div class="d-flex justify-content-between align-items-center">
                            <span>Total:</span>
                            <strong class="fs-4" id="calculated-total">${currencyNotation} 0.00</strong>
                        </div>
                        <small class="text-muted" id="conversion-info"></small>
                    </div>
                </div>
            `,
            showCancelButton: true,
            confirmButtonText: '<i class="fas fa-cart-plus me-2"></i>Add to Cart',
            cancelButtonText: 'Cancel',
            customClass: {
                confirmButton: 'btn btn-success btn-lg',
                cancelButton: 'btn btn-secondary'
            },
            didOpen: () => {
                const $quantityInput = $('#quantity-input');
                const $unitSelect = $('#unit-select');
                const $calculatedTotal = $('#calculated-total');
                const $conversionInfo = $('#conversion-info');

                // Calculate and display total when input changes
                const updateCalculation = () => {
                    const inputValue = parseFloat($quantityInput.val()) || 0;
                    const selectedUnit = $unitSelect.val();
                    
                    if (inputValue > 0) {
                        // Convert to base unit (kg or L) to calculate price
                        const convertedQty = UnitConverter.convert(inputValue, selectedUnit, productUnit);
                        const total = pricePerUnit * convertedQty;
                        
                        $calculatedTotal.text(`${currencyNotation} ${total.toLocaleString('en-US', {minimumFractionDigits: 2, maximumFractionDigits: 2})}`);
                        
                        // Show conversion info
                        const selectedUnitDisplay = UnitConverter.formatUnit(selectedUnit);
                        if (selectedUnit.toLowerCase() !== productUnit.toLowerCase()) {
                            $conversionInfo.html(`${inputValue} ${selectedUnitDisplay} = ${convertedQty.toFixed(3)} ${productUnitDisplay}`);
                        } else {
                            $conversionInfo.html(`${inputValue} ${selectedUnitDisplay}`);
                        }
                    } else {
                        $calculatedTotal.text(`${currencyNotation} 0.00`);
                        $conversionInfo.html('');
                    }
                };

                $quantityInput.on('input', updateCalculation);
                $unitSelect.on('change', updateCalculation);
                
                // Focus and select input
                setTimeout(() => $quantityInput.focus().select(), 100);
            },
            preConfirm: () => {
                const inputValue = parseFloat($('#quantity-input').val());
                const selectedUnit = $('#unit-select').val();

                if (!inputValue || inputValue <= 0) {
                    Swal.showValidationMessage(`Please enter a valid ${inputLabel.toLowerCase()}`);
                    return false;
                }

                // Convert to base unit for storage
                const convertedQty = UnitConverter.convert(inputValue, selectedUnit, productUnit);

                return {
                    quantity: 1, // Always 1 for weight/volume items (represents 1 entry)
                    unit: selectedUnit,
                    weightOrVolume: convertedQty, // Store the weight/volume in base unit (kg or L)
                    displayValue: inputValue, // Original input value for display
                    displayUnit: selectedUnit // Original unit for display
                };
            }
        });

        // Check if user cancelled
        if (result.isDismissed || !result.value) {
            return null;
        }

        return result.value;
    }

    async refreshProductGrid() {
        try {
            // Reload the product grid component to show updated stock quantities
            const response = await $.ajax({
                url: window.location.pathname,
                method: 'GET',
                dataType: 'html'
            });

            // Extract just the products grid from the response
            const $newContent = $(response);
            const $newProductsGrid = $newContent.find('#products-grid').html();
            
            if ($newProductsGrid) {
                // Replace the products grid content
                $('#products-grid').html($newProductsGrid);
                
                console.log('Product grid refreshed with updated stock quantities');
            } else {
                console.warn('Could not find products grid in response, reloading entire page section');
                // Fallback: reload the entire component
                location.reload();
            }
        } catch (error) {
            console.error('Error refreshing product grid:', error);
            // Silently fail - don't disrupt the checkout flow
        }
    }

}

// Initialize POS when document is ready
$(document).ready(function() {
    console.log('📱 Initializing POS system...');
    try {
        window.pos = new POS();
        console.log('✅ POS system initialized successfully');
    } catch (error) {
        console.error('❌ Error initializing POS:', error);
    }
});
