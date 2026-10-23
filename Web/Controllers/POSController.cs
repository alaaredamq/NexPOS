using Application.Exceptions;
using Application.IServices.Products;
using Application.IServices.Sales;
using Application.IServices.Inventory;
using Application.IServices.Users;
using Application.IServices.Base;
using Application.IServices.Settings;
using Application.DTOs;
using Application.DTOs.Sales;
using Core.DTOs;
using Core.Entities.Inventory;
using Core.IRepositories.Inventory;
using Core.IRepositories.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Helpers;
using System.Text.RegularExpressions;

namespace Web.Controllers
{
    [Authorize]
    public class POSController : BaseController
    {
        private readonly IFastProductSearchService _fastProductSearch;
        private readonly IPOSCheckoutService _checkoutService;
        private readonly ICartService _cartService;
        private readonly ICustomerService _customerService;
        private readonly ICurrencyService _currencyService;
        private readonly ICartRepository _cartRepository;
        private readonly IOrderRepository _orderRepository;
        private readonly ISystemSettingsService _settingsService;
        private readonly ILogger<POSController> _logger;

        public POSController(
            IFastProductSearchService fastProductSearch,
            IPOSCheckoutService checkoutService,
            ICartService cartService,
            ICustomerService customerService,
            ICurrencyService currencyService,
            ICartRepository cartRepository,
            IOrderRepository orderRepository,
            ISystemSettingsService settingsService,
            ILogger<POSController> logger)
        {
            _fastProductSearch = fastProductSearch;
            _checkoutService = checkoutService;
            _cartService = cartService;
            _customerService = customerService;
            _currencyService = currencyService;
            _cartRepository = cartRepository;
            _orderRepository = orderRepository;
            _settingsService = settingsService;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                // Load products for the POS page (if needed for quick access)
                var products = await _fastProductSearch.GetAllActiveProductsAsync();
                ViewBag.QuickProducts = products?.Take(12).ToList() ?? new List<ProductDTO>();
                
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading POS page");
                ViewBag.QuickProducts = new List<ProductDTO>();
                return View();
            }
        }

        /// <summary>
        /// Fast product search by barcode - optimized for POS
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> SearchProduct(string searchTerm, bool barcodeOnly = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(searchTerm))
                    return Json(new { success = false, message = "Search term is required" });

                // Trim search term to handle whitespace issues
                searchTerm = searchTerm.Trim();

                // Try barcode search first (fastest)
                var product = await _fastProductSearch.SearchByBarcodeAsync(searchTerm);
                if (product != null)
                {
                    return Ok(new { success = true, data = product, searchType = "barcode" });
                }

                // If barcodeOnly is true, don't fall back to name search
                if (barcodeOnly)
                {
                    return BadRequest(new { success = false, message = "Product not found" });
                }

                // Try name search (limited results) - only if barcodeOnly is false
                var products = await _fastProductSearch.SearchByNameAsync(searchTerm, 5);
                if (products.Any())
                {
                    return Ok(new { success = true, data = products, searchType = "name" });
                }

                return BadRequest(new { success = false, message = "Product not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching product: {SearchTerm}", searchTerm);
                return BadRequest(new { success = false, message = "Search failed" });
            }
        }

        /// <summary>
        /// Get product by ID - fast lookup from cache
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetProduct(int id)
        {
            try
            {
                var product = await _fastProductSearch.GetByIdAsync(id);
                if (product != null)
                {
                    return Json(new { success = true, data = product });
                }

                return Json(new { success = false, message = "Product not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting product: {ProductId}", id);
                return Json(new { success = false, message = "Failed to get product" });
            }
        }

        /// <summary>
        /// Refresh product cache (call when products are modified)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> RefreshCache()
        {
            try
            {
                await _fastProductSearch.RefreshCacheAsync();
                return Json(new { success = true, message = "Cache refreshed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing product cache");
                return Json(new { success = false, message = "Failed to refresh cache" });
            }
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetCacheStats()
        {
            try
            {
                var stats = await _fastProductSearch.GetCacheStatsAsync();
                return Json(new { success = true, data = stats });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cache stats");
                return Json(new { success = false, message = "Failed to get cache stats" });
            }
        }

        /// <summary>
        /// Get all active products for initial load
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAllProducts()
        {
            try
            {
                var products = await _fastProductSearch.GetAllActiveProductsAsync();
                return Json(new { success = true, data = products });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all products");
                return Json(new { success = false, message = "Failed to get products" });
            }
        }

        /// <summary>
        /// Clear POS product cache (call when products are modified)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ClearCache()
        {
            try
            {
                await _fastProductSearch.ClearCacheAsync();
                return Json(new { success = true, message = "Cache cleared successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing POS cache");
                return Json(new { success = false, message = "Failed to clear cache" });
            }
        }

        /// <summary>
        /// Process checkout
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Checkout([FromBody] CheckoutRequest? request)
        {
            try
            {
                if (request == null)
                {
                    _logger.LogError("Checkout request is null. ModelState errors: {Errors}", 
                        string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                    return Json(new { success = false, message = "Invalid request data. Please check the form and try again." });
                }
                
                // Set the current user ID (cashier)
                request.UserId = GetCurrentUserId();
                
                var result = await _checkoutService.ProcessCheckoutAsync(request);
                return Json(new { success = true, data = result });
            }
            catch (InsufficientStockException ex)
            {
                // Distinct code so the POS can offer the cashier a "sell anyway" override.
                _logger.LogWarning(ex, "Checkout blocked by insufficient stock");
                return Json(new { success = false, message = ex.Message, errorCode = "insufficient_stock" });
            }
            catch (LicenseRequiredException ex)
            {
                // Normally LicenseEnforcementMiddleware answers first (with a localized message);
                // this is the backstop if a sale reaches the service anyway.
                _logger.LogWarning(ex, "Checkout blocked by license");
                return Json(new { success = false, message = ex.Message, errorCode = "license_required" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing checkout");
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                // This controller is [Authorize]-protected, so the claim should always be present.
                // Fail loudly instead of silently attributing the sale to whichever user has Id 1.
                throw new UnauthorizedAccessException("Unable to determine the current user from the authentication claims.");
            }
            return userId;
        }

        /// <summary>
        /// Calculate cart total with discounts
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CalculateTotal([FromBody] CalculateTotalRequest request)
        {
            try
            {
                var total = await _checkoutService.CalculateTotalAsync(
                    request.CartId,
                    request.DiscountAmount,
                    request.DiscountCode,
                    request.DiscountPercentage);
                
                return Json(new { success = true, total = total });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating total");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get available payment methods
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPaymentMethods()
        {
            try
            {
                var paymentMethods = await _checkoutService.GetPaymentMethodsAsync();
                return Json(new { success = true, data = paymentMethods });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting payment methods");
                return Json(new { success = false, message = "Failed to get payment methods" });
            }
        }

        /// <summary>
        /// Validate cart before checkout
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ValidateCart([FromBody] ValidateCartRequest request)
        {
            try
            {
                var isValid = await _checkoutService.ValidateCartAsync(request.CartId);
                return Json(new { success = true, valid = isValid });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating cart");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Hold cart in database
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> HoldCart([FromBody] HoldCartRequest request)
        {
            try
            {
                // Log the received customer ID
                _logger.LogInformation($"HoldCart called with CustomerId: {request.CustomerId}");
                
                // Track if this is a walk-in customer (no customer explicitly selected).
                // The client sends null or 0 for walk-in; anything > 0 is a real customer.
                bool isWalkIn = !request.CustomerId.HasValue || request.CustomerId.Value <= 0;

                // Walk-in carts have no associated customer (null), not a fabricated default.
                int? customerId = isWalkIn ? null : request.CustomerId;

                _logger.LogInformation($"Using CustomerId: {(customerId?.ToString() ?? "walk-in")} for cart creation (Walk-in: {isWalkIn})");

                // Create a new cart for holding
                var cart = await _cartService.CreateCartAsync(customerId);

                // Add items to the cart in a single unit of work (one round trip, not one per item)
                _logger.LogInformation($"Adding {request.Items.Count} items to cart {cart.Id}");
                await _cartService.AddItemsToCartAsync(cart.Id, request.Items
                    .Select(i => new CartItemInput(i.ProductId, i.Quantity, i.WeightOrVolume))
                    .ToList());

                // Apply discount if provided
                if (request.DiscountAmount.HasValue || !string.IsNullOrEmpty(request.DiscountCode) || request.DiscountPercentage.HasValue)
                {
                    // Update cart with discount information
                    cart.DiscountAmount = request.DiscountAmount;
                    cart.DiscountCode = request.DiscountCode;
                    cart.DiscountPercentage = request.DiscountPercentage;
                }

                // Add notes if provided, or mark as walk-in customer
                string notes = request.Notes ?? "";
                if (isWalkIn)
                {
                    // Mark walk-in customers with a special prefix so we can identify them later
                    notes = string.IsNullOrEmpty(notes) ? "__WALKIN__" : $"__WALKIN__{notes}";
                }

                if (!string.IsNullOrEmpty(notes))
                {
                    // DiscountCode doubles as a free-text note field here (pre-existing design) -
                    // append rather than overwrite so this doesn't silently erase a discount label
                    // set just above.
                    cart.DiscountCode = string.IsNullOrEmpty(cart.DiscountCode)
                        ? notes
                        : $"{cart.DiscountCode} | {notes}";
                }

                // Persist the discount/notes fields set above. CreateCartAsync/AddItemToCartAsync
                // already saved the cart's core state, but these were only ever set on this
                // in-memory copy and never written back - so a discount silently vanished for
                // every live checkout, since POS.js always sends isForCheckout: true and the old
                // code only persisted these fields via the HoldCartAsync branch below (A6).
                await _cartRepository.SaveAsync(cart);

                // Hold the cart (sets IsActive = false) - only if not for immediate checkout
                if (!request.IsForCheckout)
                {
                    await _cartService.HoldCartAsync(cart.Id);
                }

                return Json(new { success = true, data = new { cartId = cart.Id } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error holding cart");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get held carts (excludes checked-out carts)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetHeldCarts()
        {
            try
            {
                // Get all held carts from all customers
                var allCarts = await _cartService.GetAllHeldCartsAsync();
                
                // Get all orders to check which carts have been checked out
                var recentOrders = await _orderRepository.GetByDateRangeAsync(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);
                var checkedOutCartIds = recentOrders.Select(o => o.CartId).ToHashSet();
                
                // Filter to only held carts that haven't been checked out
                var heldCarts = allCarts
                    .Where(c => !checkedOutCartIds.Contains(c.Id))
                    .Select(c => {
                        // Check if this is a walk-in customer by looking for the __WALKIN__ marker
                        bool isWalkIn = !string.IsNullOrEmpty(c.DiscountCode) && c.DiscountCode.StartsWith("__WALKIN__");
                        string notes = isWalkIn && c.DiscountCode.Length > 11 
                            ? c.DiscountCode.Substring(11) // Remove "__WALKIN__" prefix
                            : c.DiscountCode ?? "";
                        
                        return new
                        {
                            id = c.Id,
                            customerId = c.CustomerId,
                            customerName = isWalkIn 
                                ? "Walk-in Customer"
                                : (c.Customer != null 
                                    ? $"{c.Customer.FirstName} {c.Customer.LastName}".Trim()
                                    : "Walk-in Customer"),
                            totalAmount = c.CartItems.Sum(ci => ci.TotalPrice),
                            itemCount = c.CartItems.Sum(ci => ci.Quantity),
                            createdAt = c.CreatedAt,
                            notes = notes
                        };
                    }).ToList();
                
                return Json(new { success = true, data = heldCarts });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting held carts");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get specific held cart
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetHeldCart(int id)
        {
            try
            {
                var cart = await _cartService.GetByIdAsync(id);
                if (cart == null)
                {
                    return Json(new { success = false, message = "Cart not found" });
                }
                
                var items = cart.CartItems.Select(ci => new
                {
                    productId = ci.ProductId,
                    productName = ci.Product?.Name ?? "Unknown Product",
                    quantity = ci.Quantity,
                    unitPrice = ci.UnitPrice,
                    totalPrice = ci.TotalPrice
                }).ToList();
                
                return Json(new { success = true, data = new { items = items } });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting held cart");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get customers for selection
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetCustomers()
        {
            try
            {
                var customers = await _customerService.GetAllAsync();
                var customerList = customers.Select(c => new
                {
                    id = c.Id,
                    name = $"{c.FirstName} {c.LastName}",
                    email = c.Email
                }).ToList();
                
                return Json(new { success = true, data = customerList });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting customers");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get default currency for POS operations
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetDefaultCurrency()
        {
            try
            {
                var defaultCurrency = await _currencyService.GetDefaultAsync();
                if (defaultCurrency == null)
                {
                    return Json(new { success = false, message = "No default currency found" });
                }

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        id = defaultCurrency.Id,
                        code = defaultCurrency.Code,
                        name = defaultCurrency.Name,
                        notation = defaultCurrency.Notation,
                        rateToUSD = defaultCurrency.RateToUSD
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting default currency");
                return Json(new { success = false, message = "Failed to get default currency" });
            }
        }

        /// <summary>
        /// Get all active currencies
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetCurrencies()
        {
            try
            {
                var currencies = await _currencyService.GetAllAsync();
                var activeCurrencies = currencies.Where(c => c.IsActive).Select(c => new
                {
                    id = c.Id,
                    code = c.Code,
                    name = c.Name,
                    notation = c.Notation,
                    rateToUSD = c.RateToUSD,
                    isDefault = c.IsDefault
                }).ToList();

                return Json(new { success = true, data = activeCurrencies });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting currencies");
                return Json(new { success = false, message = "Failed to get currencies" });
            }
        }

        /// <summary>
        /// Convert price between currencies
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ConvertPrice([FromBody] ConvertPriceRequest request)
        {
            try
            {
                var fromCurrency = await _currencyService.GetByIdAsync(request.FromCurrencyId);
                var toCurrency = await _currencyService.GetByIdAsync(request.ToCurrencyId);

                if (fromCurrency == null || toCurrency == null)
                {
                    return Json(new { success = false, message = "Currency not found" });
                }

                // Convert through USD as intermediate
                // First convert from source currency to USD
                decimal amountInUSD = request.Amount / (decimal)fromCurrency.RateToUSD;
                
                // Then convert from USD to target currency
                decimal convertedAmount = amountInUSD * (decimal)toCurrency.RateToUSD;

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        originalAmount = request.Amount,
                        convertedAmount = Math.Round(convertedAmount, 2),
                        fromCurrency = fromCurrency.Code,
                        toCurrency = toCurrency.Code
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting price");
                return Json(new { success = false, message = "Failed to convert price" });
            }
        }

        /// <summary>
        /// Resume a held cart
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ResumeHeldCart([FromBody] ResumeCartRequest request)
        {
            try
            {
                // Get the cart WITHOUT setting it to active (so it stays in held carts list)
                var cart = await _cartService.GetByIdAsync(request.CartId);
                
                if (cart == null)
                {
                    return Json(new { success = false, message = "Cart not found" });
                }
                
                var items = cart.CartItems.Select(ci => new
                {
                    id = ci.ProductId,
                    name = ci.Product?.Name ?? "Unknown Product",
                    barcode = ci.Product?.Barcode ?? "",
                    price = ci.UnitPrice,
                    quantity = ci.Quantity,
                    categoryId = ci.Product?.CategoryId ?? 0,
                    currencyId = ci.Product?.CurrencyId ?? 0,
                    currencyNotation = ci.Product?.Currency?.Notation ?? "LBP"
                }).ToList();
                
                return Json(new
                {
                    success = true,
                    data = new
                    {
                        cartId = cart.Id,
                        items = items,
                        discountAmount = cart.DiscountAmount,
                        discountCode = cart.DiscountCode,
                        discountPercentage = cart.DiscountPercentage
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resuming held cart");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get available discounts
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAvailableDiscounts()
        {
            try
            {
                // Get all discounts (you may want to filter by active status if you add that field)
                var discounts = await _checkoutService.GetDiscountsAsync();
                
                var discountList = discounts.Select(d => new
                {
                    id = d.Id,
                    name = d.Name,
                    rate = d.Rate,
                    isDefault = d.IsDefault,
                    supplierId = d.SupplierId
                }).ToList();
                
                return Json(new { success = true, data = discountList });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available discounts");
                return Json(new { success = false, message = "Failed to get discounts" });
            }
        }

        /// <summary>
        /// Get product by ID
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetProductById(int id)
        {
            try
            {
                var product = await _fastProductSearch.GetByIdAsync(id);
                if (product != null)
                {
                    return Json(new { success = true, data = product });
                }
                return Json(new { success = false, message = "Product not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting product by ID: {ProductId}", id);
                return Json(new { success = false, message = "Error retrieving product" });
            }
        }

        /// <summary>
        /// Get all products (optionally excluding packs)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetProducts(bool excludePacks = false)
        {
            try
            {
                var products = (await _fastProductSearch.GetAllActiveProductsAsync()).ToList();
                if (excludePacks)
                {
                    products = products.Where(p => !p.IsPack).ToList();
                }
                return Json(new { success = true, data = products });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting products");
                return Json(new { success = false, message = "Error retrieving products" });
            }
        }

        /// <summary>
        /// Search products for Select2 AJAX (excludes packs, supports pagination)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> SearchProductsForSelect2(string q, int page = 1, int pageSize = 20)
        {
            try
            {
                // Only search if query has at least 3 characters
                if (string.IsNullOrWhiteSpace(q) || q.Length < 3)
                {
                    return Json(new { results = new List<object>(), pagination = new { more = false } });
                }

                var searchTerm = q.Trim();
                var products = (await _fastProductSearch.GetAllActiveProductsAsync())
                    .Where(p => !p.IsPack && 
                           (p.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrWhiteSpace(p.Barcode) && p.Barcode.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))))
                    .ToList();

                // Pagination
                var totalCount = products.Count;
                var skip = (page - 1) * pageSize;
                var pagedProducts = products.Skip(skip).Take(pageSize).ToList();

                var results = pagedProducts.Select(p => new
                {
                    id = p.Id,
                    text = $"{p.Name} {(string.IsNullOrWhiteSpace(p.Barcode) ? "" : $"({p.Barcode})")}"
                }).ToList();

                return Json(new
                {
                    results = results,
                    pagination = new { more = (skip + pageSize) < totalCount }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching products for Select2");
                return Json(new { results = new List<object>(), pagination = new { more = false } });
            }
        }

        /// <summary>
        /// Autocomplete search for products (returns products matching search term after 3 characters)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> AutocompleteProducts(string q)
        {
            try
            {
                // Only search if query has at least 3 characters
                if (string.IsNullOrWhiteSpace(q) || q.Length < 3)
                {
                    return Json(new List<object>());
                }

                var searchTerm = q.Trim();
                var products = (await _fastProductSearch.GetAllActiveProductsAsync())
                    .Where(p => !p.IsPack && 
                           (p.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrWhiteSpace(p.Barcode) && p.Barcode.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))))
                    .Take(20) // Limit to 20 results for autocomplete
                    .ToList();

                var results = products.Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    barcode = p.Barcode ?? "",
                    price = p.SellingPrice,
                    effectivePrice = p.EffectivePrice,
                    supplierDiscountRate = p.SupplierDiscountRate,
                    hasSupplierDiscount = p.HasSupplierDiscount,
                    currency = p.Currency?.Notation ?? "LBP",
                    category = p.Category?.Name ?? "",
                    displayText = $"{p.Name} {(string.IsNullOrWhiteSpace(p.Barcode) ? "" : $"({p.Barcode})")}{(p.HasSupplierDiscount ? $" (-{p.SupplierDiscountRate:F0}%)" : "")}"
                }).ToList();

                return Json(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in product autocomplete");
                return Json(new List<object>());
            }
        }

        /// <summary>
        /// Check product price by barcode
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> CheckPrice(string barcode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(barcode))
                    return Json(new { success = false, message = "Barcode is required" });

                var product = await _fastProductSearch.SearchByBarcodeAsync(barcode);
                
                if (product == null)
                {
                    return Json(new { success = false, message = "Product not found" });
                }
                
                return Json(new
                {
                    success = true,
                    data = new
                    {
                        id = product.Id,
                        name = product.Name,
                        barcode = product.Barcode,
                        price = product.SellingPrice,
                        effectivePrice = product.EffectivePrice,
                        discountedPrice = product.DiscountedPrice,
                        supplierDiscountRate = product.SupplierDiscountRate,
                        hasSupplierDiscount = product.HasSupplierDiscount,
                        costPrice = product.CostPrice,
                        stockQuantity = product.StockQuantity,
                        currencyId = product.CurrencyId,
                        currencyNotation = product.Currency?.Notation ?? "LBP",
                        categoryId = product.CategoryId,
                        categoryName = product.Category?.Name ?? "",
                        supplierName = product.Supplier?.Name ?? "",
                        isPack = product.IsPack
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking price for barcode: {Barcode}", barcode);
                return Json(new { success = false, message = "Failed to check price" });
            }
        }

        /// <summary>
        /// Delete a held cart
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteHeldCart([FromBody] DeleteCartRequest request)
        {
            try
            {
                var cart = await _cartService.GetByIdAsync(request.CartId);
                
                if (cart == null)
                {
                    return Json(new { success = false, message = "Cart not found" });
                }
                
                // Soft delete the cart by marking it as deleted
                cart.IsDeleted = true;
                cart.UpdatedAt = DateTime.UtcNow;
                
                // Save the cart with IsDeleted flag using repository
                await _cartRepository.SaveAsync(cart);
                
                _logger.LogInformation($"Deleted held cart {request.CartId}");
                return Json(new { success = true, message = "Cart deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting held cart");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Delete all held carts
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteAllHeldCarts()
        {
            try
            {
                // Get all held carts
                var allCarts = await _cartService.GetAllHeldCartsAsync();
                
                // Get all orders to check which carts have been checked out
                var recentOrders = await _orderRepository.GetByDateRangeAsync(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);
                var checkedOutCartIds = recentOrders.Select(o => o.CartId).ToHashSet();
                
                // Filter to only held carts that haven't been checked out
                var heldCarts = allCarts.Where(c => !checkedOutCartIds.Contains(c.Id)).ToList();
                
                int deletedCount = 0;
                foreach (var cart in heldCarts)
                {
                    cart.IsDeleted = true;
                    cart.UpdatedAt = DateTime.UtcNow;
                    await _cartRepository.SaveAsync(cart);
                    deletedCount++;
                }
                
                _logger.LogInformation($"Deleted {deletedCount} held carts");
                return Json(new { success = true, message = $"{deletedCount} cart(s) deleted successfully", deletedCount = deletedCount });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting all held carts");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Print a receipt to a USB printer (XP-T80A) by fetching the generated receipt HTML
        /// and sending it as a raw print job to the named printer on the server.
        /// Expects a JSON body with { orderId, receiptUrl, printerName }
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> PrintReceiptToUsb([FromBody] PrintToUsbRequest request)
        {
            try
            {
                if (request == null)
                    return Json(new { success = false, message = "Invalid request" });

                // Build the URL to fetch the receipt HTML. If receiptUrl is provided use it,
                // otherwise attempt to use the ReceiptsController PrintReceipt action with the provided orderId.
                string receiptPath = request.ReceiptUrl;
                if (string.IsNullOrWhiteSpace(receiptPath))
                {
                    // Assume orderId is actually the receipt id or order number - try to use as receipt id
                    receiptPath = $"/Receipts/PrintReceipt?id={request.OrderId}";
                }

                // Ensure receiptPath is an absolute URL
                string receiptUrl = receiptPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? receiptPath
                    : $"{Request.Scheme}://{Request.Host}{(receiptPath.StartsWith("/") ? receiptPath : "/" + receiptPath)}";

                using var http = new System.Net.Http.HttpClient();
                var htmlResponse = await http.GetAsync(receiptUrl);
                if (!htmlResponse.IsSuccessStatusCode)
                {
                    return Json(new { success = false, message = $"Failed to fetch receipt from {receiptUrl} (status {htmlResponse.StatusCode})" });
                }

                var html = await htmlResponse.Content.ReadAsStringAsync();

                // Default printer name
                var printerName = string.IsNullOrWhiteSpace(request.PrinterName) ? "XP-T80A" : request.PrinterName;

                // Try sending the HTML as plain text to the printer. Many thermal printers accept plain text or ESC/POS commands.
                bool printed = RawPrinterHelper.SendStringToPrinter(printerName, html);

                if (!printed)
                {
                    return Json(new { success = false, message = "Failed to send print job to printer" });
                }

                return Json(new { success = true, message = "Print job queued" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error printing receipt to USB");
                return Json(new { success = false, message = ex.Message });
            }
        }

        // DTO for print request
        public class PrintToUsbRequest
        {
            public string OrderId { get; set; }
            public string ReceiptUrl { get; set; }
            public string PrinterName { get; set; }
        }
        /// <summary>
        /// Test endpoint to verify routing and logging
        /// </summary>
        [HttpGet("/POS/PrintReceiptToUsbTest")]
        [AllowAnonymous]
        public IActionResult PrintReceiptToUsbTest()
        {
            _logger.LogInformation("PrintReceiptToUsbTest endpoint invoked");
            return Json(new { success = true, message = "PrintReceiptToUsbTest reachable" });
        }


        // Raw printer helper using Win32 API - sends raw bytes to the printer
        // Note: This works on Windows servers where the printer is installed and accessible by the server process.
        internal static class RawPrinterHelper
        {
            [System.Runtime.InteropServices.DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Ansi, ExactSpelling = true)]
            private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

            [System.Runtime.InteropServices.DllImport("winspool.Drv", SetLastError = true)]
            private static extern bool ClosePrinter(IntPtr hPrinter);

            [System.Runtime.InteropServices.DllImport("winspool.Drv", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
            private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [System.Runtime.InteropServices.In, System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStruct)] DOCINFOA di);

            [System.Runtime.InteropServices.DllImport("winspool.Drv", SetLastError = true)]
            private static extern bool EndDocPrinter(IntPtr hPrinter);

            [System.Runtime.InteropServices.DllImport("winspool.Drv", SetLastError = true)]
            private static extern bool StartPagePrinter(IntPtr hPrinter);

            [System.Runtime.InteropServices.DllImport("winspool.Drv", SetLastError = true)]
            private static extern bool EndPagePrinter(IntPtr hPrinter);

            [System.Runtime.InteropServices.DllImport("winspool.Drv", SetLastError = true)]
            private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Ansi)]
            private class DOCINFOA
            {
                [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)]
                public string pDocName;
                [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)]
                public string pOutputFile;
                [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStr)]
                public string pDataType;
            }

            public static bool SendStringToPrinter(string szPrinterName, string szString)
            {
                IntPtr pBytes;
                int dwCount = System.Text.Encoding.UTF8.GetByteCount(szString);

                // Allocate unmanaged memory for the string
                pBytes = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(dwCount);
                System.Runtime.InteropServices.Marshal.Copy(System.Text.Encoding.UTF8.GetBytes(szString), 0, pBytes, dwCount);

                bool success = false;
                IntPtr hPrinter = IntPtr.Zero;

                try
                {
                    if (!OpenPrinter(szPrinterName, out hPrinter, IntPtr.Zero))
                    {
                        return false;
                    }

                    var di = new DOCINFOA()
                    {
                        pDocName = "Receipt",
                        pDataType = "RAW"
                    };

                    if (!StartDocPrinter(hPrinter, 1, di))
                    {
                        ClosePrinter(hPrinter);
                        return false;
                    }

                    if (!StartPagePrinter(hPrinter))
                    {
                        EndDocPrinter(hPrinter);
                        ClosePrinter(hPrinter);
                        return false;
                    }

                    success = WritePrinter(hPrinter, pBytes, dwCount, out int written);

                    EndPagePrinter(hPrinter);
                    EndDocPrinter(hPrinter);
                    ClosePrinter(hPrinter);

                    return success && written == dwCount;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    if (pBytes != IntPtr.Zero)
                        System.Runtime.InteropServices.Marshal.FreeCoTaskMem(pBytes);
                }
            }
        }
    }

    // Request DTOs
    public class ConvertPriceRequest
    {
        public decimal Amount { get; set; }
        public int FromCurrencyId { get; set; }
        public int ToCurrencyId { get; set; }
    }

    public class HoldCartRequest
    {
        public int? CustomerId { get; set; }
        public List<HoldCartItem> Items { get; set; } = new();
        public decimal? DiscountAmount { get; set; }
        public string? DiscountCode { get; set; }
        public decimal? DiscountPercentage { get; set; }
        public string? Notes { get; set; }
        public bool IsForCheckout { get; set; } = false;
    }

    public class HoldCartItem
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        
        /// <summary>
        /// Weight or volume amount for products sold by weight/volume (e.g., 0.5 for 500g or 0.5L).
        /// Null for products sold by piece.
        /// </summary>
        public decimal? WeightOrVolume { get; set; }
    }

    public class ValidateCartRequest
    {
        public int CartId { get; set; }
    }

    public class ResumeCartRequest
    {
        public int CartId { get; set; }
    }

    public class DeleteCartRequest
    {
        public int CartId { get; set; }
    }
}

