using Application.DTOs.Sales;
using Application.Helpers;
using Application.IServices.Sales;
using Application.IServices.Inventory;
using Application.IServices.Licensing;
using Application.IServices.Users;
using Core.Auditing;
using Core.Entities.Logging;
using Core.Entities.Sales;
using Core.Entities.Inventory;
using Core.ICache;
using Core.IRepositories.Sales;
using Core.IRepositories.Inventory;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Sales
{
    public class POSCheckoutService : IPOSCheckoutService
    {
        private readonly IOrderService _orderService;
        private readonly ICartService _cartService;
        private readonly IPaymentMethodService _paymentMethodService;
        private readonly IDiscountService _discountService;
        private readonly ICartRepository _cartRepository;
        private readonly ICartItemRepository _cartItemRepository;
        private readonly ICashDrawerService _cashDrawerService;
        private readonly IReceiptService _receiptService;
        private readonly IThermalPrinterService _thermalPrinterService;
        private readonly ICheckoutIdempotencyGuard _idempotencyGuard;
        private readonly ICustomerDebtService _customerDebtService;
        private readonly ICacheService _cache;
        private readonly ErpDbContext _dbContext;
        private readonly ILicenseService _licenseService;
        private readonly ILogger<POSCheckoutService> _logger;
        private readonly IAuditTrail _auditTrail;

        private static readonly TimeSpan IdempotencyWindow = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Name of the system payment method used to mark a sale as placed on a customer's
        /// account (seeded in ErpDbContext). Resolved by name rather than a hardcoded id so a
        /// re-seed/migration doesn't silently break this.
        /// </summary>
        private const string DebtPaymentMethodName = "Customer Debt";

        public POSCheckoutService(
            IOrderService orderService,
            ICartService cartService,
            IPaymentMethodService paymentMethodService,
            IDiscountService discountService,
            ICartRepository cartRepository,
            ICartItemRepository cartItemRepository,
            ICashDrawerService cashDrawerService,
            IReceiptService receiptService,
            IThermalPrinterService thermalPrinterService,
            ICheckoutIdempotencyGuard idempotencyGuard,
            ICustomerDebtService customerDebtService,
            ICacheService cache,
            ErpDbContext dbContext,
            ILicenseService licenseService,
            ILogger<POSCheckoutService> logger,
            IAuditTrail auditTrail)
        {
            _orderService = orderService;
            _cartService = cartService;
            _paymentMethodService = paymentMethodService;
            _discountService = discountService;
            _cartRepository = cartRepository;
            _cartItemRepository = cartItemRepository;
            _cashDrawerService = cashDrawerService;
            _receiptService = receiptService;
            _thermalPrinterService = thermalPrinterService;
            _idempotencyGuard = idempotencyGuard;
            _customerDebtService = customerDebtService;
            _cache = cache;
            _dbContext = dbContext;
            _licenseService = licenseService;
            _logger = logger;
            _auditTrail = auditTrail;
        }

        /// <summary>
        /// Builds a key that identifies "the same checkout attempt" so a double-click can be
        /// rejected instead of creating a second order (S6). Carts are the natural key when one
        /// exists; the ad-hoc "Items" path (no pre-created cart - see POS.js) has no such handle,
        /// so it's keyed off the content that a genuine duplicate click would resend unchanged.
        /// </summary>
        private static string BuildIdempotencyKey(CheckoutRequest request)
        {
            if (request.CartId.HasValue)
            {
                return $"cart:{request.CartId.Value}";
            }

            var itemsSignature = string.Join("|", (request.Items ?? new List<CheckoutItemRequest>())
                .OrderBy(i => i.ProductId)
                .Select(i => $"{i.ProductId}:{i.Quantity}:{i.WeightOrVolume}"));

            return $"items:{request.UserId}:{request.CustomerId ?? 0}:{request.PaymentMethodId}:{request.TotalAmount:F2}:{itemsSignature}";
        }

        public async Task<CheckoutResponse> ProcessCheckoutAsync(CheckoutRequest request)
        {
            // Backstop for the middleware: whatever route a sale arrives by, an expired or
            // unlicensed install cannot create new orders. Checked before the idempotency
            // key is taken so a refused attempt does not consume it.
            _licenseService.EnsureSalesAllowed();

            var idempotencyKey = BuildIdempotencyKey(request);
            if (!_idempotencyGuard.TryBeginCheckout(idempotencyKey, IdempotencyWindow))
            {
                throw new InvalidOperationException(
                    "This checkout was already submitted. Please check the order history instead of retrying.");
            }

            try
            {
                // The DbContext is configured with EnableRetryOnFailure, so its execution
                // strategy may re-run this delegate on a transient fault. A user-initiated
                // transaction must be created *inside* the strategy (SqlServerRetryingExecution
                // Strategy rejects it otherwise) so the whole unit - the transaction plus every
                // SaveChangesAsync below - is retried together (S4).
                var strategy = _dbContext.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    // All repositories/services touched below (CartService, OrderService,
                    // ReceiptService, CashDrawerService, ProductRepository) resolve to the same
                    // scoped ErpDbContext, so this single ambient transaction covers all of their
                    // SaveChangesAsync/ExecuteUpdateAsync calls - a failure partway through (e.g.
                    // insufficient stock) rolls everything back instead of leaving a cart
                    // deactivated with no order, or stock deducted with no order.
                    await using var transaction = await _dbContext.Database.BeginTransactionAsync();

                    int cartId;
                    Core.Entities.Inventory.Cart cart;
                    List<Core.Entities.Inventory.CartItem> cartItems;

                    // Handle walk-in customers: CustomerId = 0 means no specific customer
                    // We use null in the database to represent walk-in/anonymous customers
                    int? effectiveCustomerId = request.CustomerId is > 0 ? request.CustomerId : null;
                    _logger.LogInformation("Processing checkout for customer {CustomerId} (null = walk-in)", effectiveCustomerId?.ToString() ?? "walk-in");

                    // Resolve the system "Customer Debt" payment method once - used both to
                    // route an AddToDebt sale onto it, and to block a client from picking it
                    // directly as if it were a normal payment method (S-loophole: that would
                    // record a "paid" sale that never actually touched the customer's balance).
                    var debtPaymentMethod = (await _paymentMethodService.GetAllAsync())
                        .FirstOrDefault(pm => pm.Name.Equals(DebtPaymentMethodName, StringComparison.OrdinalIgnoreCase));

                    if (request.AddToDebt)
                    {
                        if (effectiveCustomerId == null)
                        {
                            throw new InvalidOperationException(
                                "Cannot add a walk-in sale to customer debt. Please select a customer first.");
                        }

                        if (debtPaymentMethod == null)
                        {
                            throw new InvalidOperationException("The 'Customer Debt' payment method is not configured.");
                        }

                        // The client never needs to know this id - it only sends AddToDebt.
                        request.PaymentMethodId = debtPaymentMethod.Id;
                    }
                    else if (debtPaymentMethod != null && request.PaymentMethodId == debtPaymentMethod.Id)
                    {
                        throw new InvalidOperationException("Use \"Add to Customer Debt\" to place a sale on the customer's account.");
                    }

                    // If CartId is provided, use existing cart
                    if (request.CartId.HasValue)
                    {
                        cartId = request.CartId.Value;
                        _logger.LogInformation("Processing checkout for existing cart {CartId}", cartId);

                        // Single load - GetByIdAsync already includes CartItems -> Product, so there's
                        // no need for a separate ValidateCartAsync round trip or a GetByCartIdAsync.
                        cart = await _cartRepository.GetByIdAsync(cartId);
                        if (cart == null || !cart.IsActive)
                        {
                            throw new InvalidOperationException("Cart is invalid or empty");
                        }

                        cartItems = cart.CartItems?.ToList() ?? new List<Core.Entities.Inventory.CartItem>();
                        if (!cartItems.Any())
                        {
                            throw new InvalidOperationException("Cart is empty");
                        }
                    }
                    // If Items are provided, create cart from items
                    else if (request.Items != null && request.Items.Any())
                    {
                        _logger.LogInformation("Creating cart from items for checkout");

                        // Create a new cart (use effectiveCustomerId which is 0 for walk-in)
                        cart = await _cartService.CreateCartAsync(effectiveCustomerId);
                        cartId = cart.Id;

                        // Add all items in one unit of work, then reuse the reloaded cart it returns.
                        cart = await _cartService.AddItemsToCartAsync(cartId, request.Items
                            .Select(i => new CartItemInput(i.ProductId, (int)i.Quantity, i.WeightOrVolume))
                            .ToList());

                        // Apply discount if provided
                        if (request.DiscountAmount.HasValue || !string.IsNullOrEmpty(request.DiscountCode) || request.DiscountPercentage.HasValue)
                        {
                            cart.DiscountAmount = request.DiscountAmount;
                            cart.DiscountCode = request.DiscountCode;
                            cart.DiscountPercentage = request.DiscountPercentage;
                            await _cartRepository.SaveAsync(cart);
                        }

                        cartItems = cart.CartItems?.ToList() ?? new List<Core.Entities.Inventory.CartItem>();
                    }
                    else
                    {
                        throw new ArgumentException("Either CartId or Items must be provided");
                    }

                    // Calculate the original cart total. Line items were repriced from the database
                    // by CartService when they were added, so this total is already authoritative (S2)
                    // regardless of what the client's own running total said.
                    var cartSubtotal = cartItems.Sum(ci => ci.TotalPrice);

                    // A percentage discount takes precedence over a flat amount when both are somehow
                    // set, matching CartSessionService's existing convention (A6) - this used to only
                    // ever subtract DiscountAmount, so a percentage discount was silently ignored here
                    // even when correctly persisted on the cart.
                    var cartTotal = cart.DiscountPercentage.HasValue && cart.DiscountPercentage.Value > 0
                        ? cartSubtotal * (1 - cart.DiscountPercentage.Value / 100m)
                        : cartSubtotal - (cart.DiscountAmount ?? 0);

                    // The client-supplied total may differ slightly for cash rounding, but it must
                    // never be trusted as the sale amount outright. Bound how far it can move the
                    // server-calculated total (greater of $1 or 2%) and reject anything past that -
                    // a bigger discrepancy means either a bug or a tampered request, not rounding.
                    var adjustment = request.TotalAmount - cartTotal;
                    var maxAllowedAdjustment = Math.Max(1.00m, cartTotal * 0.02m);
                    if (Math.Abs(adjustment) > maxAllowedAdjustment)
                    {
                        throw new InvalidOperationException(
                            $"Requested total {request.TotalAmount:F2} differs from the calculated total {cartTotal:F2} by more than the allowed rounding adjustment ({maxAllowedAdjustment:F2}).");
                    }

                    var finalTotal = request.TotalAmount;

                    // Store adjustment in cart's discount code field for reference
                    if (adjustment != 0)
                    {
                        var adjustmentNote = adjustment > 0 
                            ? $"Adjustment: +${Math.Abs(adjustment):F2}" 
                            : $"Adjustment: -${Math.Abs(adjustment):F2}";
                        cart.DiscountCode = string.IsNullOrEmpty(cart.DiscountCode) 
                            ? adjustmentNote 
                            : $"{cart.DiscountCode} | {adjustmentNote}";
                        await _cartRepository.SaveAsync(cart);
                    }
                
                    // Process payment with the final total
                    var paymentResult = await ProcessPaymentAsync(finalTotal, request.PaymentMethodId);
                    if (!paymentResult.Success)
                    {
                        throw new InvalidOperationException($"Payment failed: {paymentResult.Message}");
                    }

                    // Get the current open cash drawer session for this user (if any). Only the id
                    // is needed here, so use the cheap lookup - the full session pulls in every
                    // order rung up since the drawer opened, which makes checkout slower as the
                    // shift goes on.
                    int? cashDrawerSessionId = null;
                    try
                    {
                        cashDrawerSessionId = await _cashDrawerService.GetOpenSessionIdByUserIdAsync(request.UserId);
                        if (cashDrawerSessionId != null)
                        {
                            _logger.LogInformation("Linking order to cash drawer session {SessionId}", cashDrawerSessionId);
                        }
                        else
                        {
                            _logger.LogWarning("No open cash drawer session found for user {UserId}. Order will not be linked to a session.", request.UserId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error getting open cash drawer session for user {UserId}. Order will proceed without session link.", request.UserId);
                    }

                    // Create order with the final total and cash drawer session
                    var order = await _orderService.CreateOrderFromCartAsync(
                        cart,
                        cartItems,
                        effectiveCustomerId,
                        finalTotal,
                        request.CurrencyId,
                        cashDrawerSessionId,
                        request.AllowInsufficientStock);

                    // Generate order number
                    var orderNumber = $"ORD-{order.Id:D6}-{DateTime.UtcNow:yyyyMMdd}";

                    if (request.AddToDebt)
                    {
                        // Still inside the ambient transaction, so a failure here rolls back
                        // the order/stock changes too instead of leaving a sale with no debt
                        // record.
                        await _customerDebtService.AddSaleTransactionAsync(
                            effectiveCustomerId!.Value, request.UserId, order.Id, finalTotal);
                    }

                    // Create receipt for the order
                    Receipt receipt = null;
                    try
                    {
                        _logger.LogInformation("Creating receipt for order {OrderId}", order.Id);
                    
                        // Calculate tax and discount amounts
                        // For weight/volume products, backend CartItem.TotalPrice is wrong because Quantity is stored as int
                        // (e.g., 300 * $2.20 = $660 instead of 0.3 * $2.20 = $0.66)
                        // So we recalculate subtotal from receipt items using the correct converted quantities
                        var discountAmount = cart.DiscountAmount ?? 0;
                        var taxAmount = 0m; // Calculate if you have tax logic
                    
                        // Calculate subtotal from frontend's finalTotal (which is correct for weight/volume products)
                        // Backend CartItem.TotalPrice is wrong (e.g., 300 * $2.20 = $660 instead of 0.3 * $2.20 = $0.66)
                        var subTotal = finalTotal + discountAmount; // frontend total + discount = subtotal
                    
                        // Create receipt request
                        var receiptRequest = new CreateReceiptRequest
                        {
                            OrderId = order.Id,
                            CustomerId = effectiveCustomerId,
                            CashierId = request.UserId,
                            SubTotal = subTotal,
                            TaxAmount = taxAmount,
                            DiscountAmount = discountAmount,
                            TotalAmount = finalTotal,
                            AmountPaid = request.AmountPaid ?? finalTotal,
                            PaymentMethodId = request.PaymentMethodId,
                            CurrencyId = request.CurrencyId,
                            ExchangeRate = 1.0m,
                            CustomerName = null, // Will use Customer entity
                            CustomerPhone = null,
                            Items = cartItems.Select(ci => {
                                // Get product unit (preserve original case from database)
                                var productUnit = ci.Product?.Unit;
                                string? unit = productUnit;

                                if (_logger.IsEnabled(LogLevel.Debug))
                                {
                                    _logger.LogDebug(
                                        "CartItem raw values: ProductId={ProductId}, ProductName={ProductName}, Quantity={Quantity}, WeightOrVolume={WeightOrVolume}, UnitPrice={UnitPrice}, TotalPrice={TotalPrice}",
                                        ci.ProductId, ci.Product?.Name, ci.Quantity, ci.WeightOrVolume, ci.UnitPrice, ci.TotalPrice);
                                }

                                // For weight/volume products, use WeightOrVolume directly.
                                // For piece products, use Quantity.
                                decimal quantity = ci.WeightOrVolume.HasValue && ci.WeightOrVolume.Value > 0
                                    ? ci.WeightOrVolume.Value
                                    : ci.Quantity;

                                return new ReceiptItemRequest
                                {
                                    ProductId = ci.ProductId,
                                    ProductName = ci.Product?.Name ?? "Unknown Product",
                                    ProductBarcode = ci.Product?.Barcode,
                                    Quantity = quantity,
                                    Unit = unit,
                                    WeightOrVolume = ci.WeightOrVolume,
                                    UnitPrice = ci.UnitPrice,
                                    DiscountAmount = 0,
                                    TaxRate = 0,
                                    Notes = null
                                };
                            }).ToList(),
                            Payments = new List<ReceiptPaymentRequest>
                            {
                                new ReceiptPaymentRequest
                                {
                                    PaymentMethodId = request.PaymentMethodId,
                                    Amount = finalTotal,
                                    ReferenceNumber = paymentResult.TransactionId,
                                    Notes = null
                                }
                            }
                        };
                    
                        receipt = await _receiptService.CreateReceiptAsync(receiptRequest);
                        _logger.LogInformation("Receipt created successfully. Receipt Number: {ReceiptNumber}", receipt.ReceiptNumber);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error creating receipt for order {OrderId}. Checkout will continue.", order.Id);
                        // Don't fail checkout if receipt creation fails - but the sale now has no receipt, which support must be able to see.
                        _auditTrail.LogBusiness(AuditActions.ReceiptFailed,
                            $"Order {orderNumber} was saved but its receipt could not be created: {ex.Message}",
                            "Order", AuditOutcome.Failure, AuditLogSeverity.Error, order.Id, orderNumber,
                            new { orderId = order.Id, error = ex.GetType().Name });
                    }

                    // Build response
                    var response = new CheckoutResponse
                    {
                        OrderId = order.Id,
                        OrderNumber = orderNumber,
                        TotalAmount = finalTotal,
                        PaidAmount = paymentResult.PaidAmount,
                        ChangeAmount = paymentResult.ChangeAmount,
                        // Resolve the name from the cached payment-methods list rather than another DB hit.
                        PaymentMethod = (await _paymentMethodService.GetAllAsync())
                            .FirstOrDefault(pm => pm.Id == request.PaymentMethodId)?.Name ?? "Unknown",
                        OrderDate = order.OrderDate,
                        Status = "Completed",
                        ReceiptUrl = receipt != null ? $"/en/receipts/{receipt.Id}" : $"/Orders/Receipt/{order.Id}",
                        Items = cartItems.Select(item => new CheckoutItemResponse
                        {
                            ProductId = item.ProductId,
                            ProductName = item.Product?.Name ?? "Unknown Product",
                            Quantity = item.Quantity,
                            UnitPrice = item.UnitPrice,
                            TotalPrice = item.TotalPrice,
                            WeightOrVolume = item.WeightOrVolume
                        }).ToList()
                    };

                    // All DB state for this sale (cart, order, stock deduction, receipt) is final -
                    // commit before the printer I/O below, which has nothing to do with the transaction.
                    await transaction.CommitAsync();
                    // Stock was deducted via ExecuteUpdate - drop the POS search cache so it doesn't serve stale quantities.
                    await _cache.ClearPOSCachesAsync();

                    if (request.PrintReceipt)
                    {
                        response.ReceiptPrinted = false;
                        if (receipt == null)
                        {
                            _logger.LogWarning("No receipt was created for order {OrderId}, nothing to print", order.Id);
                        }
                        else
                        {
                            try
                            {
                                _logger.LogInformation("Printing receipt for order {OrderId} on thermal printer", order.Id);
                                response.ReceiptPrinted = await _thermalPrinterService.PrintReceiptAsync(receipt);
                                if (response.ReceiptPrinted == true)
                                    _logger.LogInformation("Receipt printed successfully");
                                else
                                    _logger.LogWarning("Thermal printer rejected the receipt for order {OrderId}", order.Id);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error printing receipt for order {OrderId}", order.Id);
                            }
                        }
                    }

                    _logger.LogInformation("Checkout completed successfully. Order ID: {OrderId}, Receipt: {ReceiptNumber}",
                        order.Id, receipt?.ReceiptNumber ?? "N/A");

                    // The sale in one line, with everything needed to reconstruct it later (the row-level changes -
                    // order, receipt, stock, drawer - are recorded separately and share this request id).
                    _auditTrail.LogBusiness(AuditActions.SaleCompleted,
                        $"Sale completed: {orderNumber}, receipt {receipt?.ReceiptNumber ?? "none"}, total {finalTotal:F2}, " +
                        $"{cartItems.Count} line(s), paid by {response.PaymentMethod}{(request.AddToDebt ? " (added to customer debt)" : "")}",
                        "Order", entityId: order.Id, entityLabel: orderNumber,
                        details: new
                        {
                            orderId = order.Id,
                            orderNumber,
                            receiptId = receipt?.Id,
                            receiptNumber = receipt?.ReceiptNumber,
                            total = finalTotal,
                            paid = paymentResult.PaidAmount,
                            change = paymentResult.ChangeAmount,
                            paymentMethod = response.PaymentMethod,
                            currencyId = request.CurrencyId,
                            customerId = effectiveCustomerId,
                            addToDebt = request.AddToDebt,
                            cashDrawerSessionId,
                            discountAmount = cart.DiscountAmount,
                            discountPercentage = cart.DiscountPercentage,
                            receiptPrinted = response.ReceiptPrinted,
                            items = cartItems.Select(i => new
                            {
                                i.ProductId,
                                name = i.Product?.Name,
                                i.Quantity,
                                i.WeightOrVolume,
                                i.UnitPrice,
                                lineTotal = i.TotalPrice
                            })
                        });

                    if (request.PrintReceipt && response.ReceiptPrinted != true)
                    {
                        _auditTrail.LogBusiness(AuditActions.PrintFailed,
                            $"The receipt for {orderNumber} was not printed (receipt printer did not accept it)",
                            "Receipt", AuditOutcome.Failure, AuditLogSeverity.Warning, receipt?.Id, receipt?.ReceiptNumber,
                            new { orderId = order.Id, hadReceipt = receipt != null });
                    }

                    return response;
                });
            }
            catch (Exception ex)
            {
                // Release the claim so a genuine retry after a real failure (e.g. payment
                // declined) isn't blocked for the rest of the idempotency window - only a
                // successful checkout should make this key stay claimed.
                _idempotencyGuard.ReleaseCheckout(idempotencyKey);
                _logger.LogError(ex, "Error processing checkout for cart {CartId}", request.CartId);
                _auditTrail.LogBusiness(AuditActions.SaleFailed,
                    $"Sale failed and was rolled back: {ex.Message}",
                    "Order", AuditOutcome.Failure, AuditLogSeverity.Error,
                    details: new
                    {
                        cartId = request.CartId,
                        customerId = request.CustomerId,
                        paymentMethodId = request.PaymentMethodId,
                        requestedTotal = request.TotalAmount,
                        addToDebt = request.AddToDebt,
                        itemCount = request.Items?.Count,
                        error = ex.GetType().Name
                    });
                throw;
            }
        }

        public async Task<decimal> CalculateTotalAsync(int cartId, decimal? discountAmount = null, string? discountCode = null, decimal? discountPercentage = null)
        {
            var subtotal = await _cartService.CalculateCartTotalAsync(cartId);
            var total = subtotal;

            // discountCode is a display label only (Discount entities are per-supplier rates, not
            // customer-redeemable codes - there's nothing to look up here) - it used to grant a
            // flat, hardcoded 10% to ANY non-empty code, which let a cashier get 10% off by typing
            // anything at all (A6). Only an explicit amount/percentage actually reduces the total,
            // matching how ProcessCheckoutAsync and CartSessionService compute it.
            if (discountPercentage.HasValue && discountPercentage.Value > 0)
            {
                total = subtotal * (1 - discountPercentage.Value / 100m);
            }
            else if (discountAmount.HasValue && discountAmount.Value > 0)
            {
                total = Math.Max(0, subtotal - discountAmount.Value);
            }

            return total;
        }

        public async Task<bool> ValidateCartAsync(int cartId)
        {
            var cart = await _cartRepository.GetByIdAsync(cartId);
            if (cart == null || !cart.IsActive)
            {
                return false;
            }

            var cartItems = await _cartItemRepository.GetByCartIdAsync(cartId);
            return cartItems.Any();
        }

        public async Task<List<PaymentMethodDto>> GetPaymentMethodsAsync()
        {
            var paymentMethods = await _paymentMethodService.GetAllAsync();
            return paymentMethods
                // Not a real payment method - only reachable through the "Add to Customer
                // Debt" checkout action, which resolves it on the server.
                .Where(pm => !pm.Name.Equals(DebtPaymentMethodName, StringComparison.OrdinalIgnoreCase))
                .Select(pm => new PaymentMethodDto
            {
                Id = pm.Id,
                Name = pm.Name,
                IsDefault = pm.IsDefault,
                RequiresChange = pm.Name.ToLower().Contains("cash")
            }).ToList();
        }

        public async Task<PaymentResult> ProcessPaymentAsync(decimal amount, int paymentMethodId, string? reference = null)
        {
            try
            {
                var paymentMethod = await _paymentMethodService.GetByIdAsync(paymentMethodId);
                if (paymentMethod == null)
                {
                    return new PaymentResult
                    {
                        Success = false,
                        Message = "Payment method not found"
                    };
                }

                // For now, simulate successful payment
                // In a real system, this would integrate with payment processors
                var transactionId = $"TXN-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";

                return new PaymentResult
                {
                    Success = true,
                    TransactionId = transactionId,
                    PaidAmount = amount,
                    ChangeAmount = 0, // For non-cash payments
                    Message = "Payment processed successfully"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payment for amount {Amount}", amount);
                return new PaymentResult
                {
                    Success = false,
                    Message = "Payment processing failed"
                };
            }
        }

        public async Task<List<Discount>> GetDiscountsAsync()
        {
            try
            {
                return await _discountService.GetAllAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting discounts");
                return new List<Discount>();
            }
        }
    }
}
