using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.DTOs.Sales;
using Application.Exceptions;
using Application.IServices.Base;
using Application.IServices.Inventory;
using Application.IServices.Sales;
using Application.IServices.Users;
using Core.Auditing;
using Core.Entities.Logging;
using Core.Entities.Sales;
using Core.Entities.Users;
using Core.IRepositories.Inventory;
using Core.IRepositories.Sales;
using Microsoft.Extensions.Localization;
using Web.Api.Pos.Contracts;

namespace Web.Api.Pos;

/// <summary>The sale itself and what happens to it afterwards: checkout, the cashier's last sales, reprint, PDF.</summary>
public interface IPosSaleService
{
    Task<PosSaleDto> CheckoutAsync(PosCheckoutRequest request, int userId);

    /// <summary>The cashier's own latest sales (today and the days before), newest first.</summary>
    Task<IReadOnlyList<PosSaleSummaryDto>> GetRecentAsync(int userId, int count);

    Task<PosPrintResultDto> PrintAsync(int orderId);

    /// <summary>The receipt as a PDF (the same document the printer gets). Throws <see cref="PosRuleException"/> 404 for an unknown order.</summary>
    Task<(byte[] Pdf, string FileName)> GetPdfAsync(int orderId);
}

public sealed class PosSaleService : IPosSaleService
{
    private static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Checkouts of this process by (user, idempotency key). A repeated key gets the first sale back instead of selling twice, and
    /// a second request arriving while the first is still running waits for it. Failed attempts are removed so the cashier can fix
    /// the cart (or confirm "sell anyway") and retry with the same key. In memory like the legacy <c>CheckoutIdempotencyGuard</c>:
    /// a restart of the app forgets it, and so does the 30-minute window - the order itself is, of course, in the database.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Attempt> Attempts = new();

    private sealed record Attempt(string Fingerprint, DateTime StartedAtUtc, Task<PosSaleDto> Result);

    private readonly IPosPricingService _pricing;
    private readonly ICartService _carts;
    private readonly ICartRepository _cartRepo;
    private readonly ICartItemRepository _cartItems;
    private readonly IPOSCheckoutService _checkout;
    private readonly ICustomerService _customers;
    private readonly ICashDrawerService _drawer;
    private readonly IReceiptRepository _receipts;
    private readonly IOrderRepository _orderRepo;
    private readonly IOrderService _orders;
    private readonly IThermalPrinterService _printer;
    private readonly IAuditTrail _audit;
    private readonly IStringLocalizer<SharedResource> _text;
    private readonly ILogger<PosSaleService> _logger;

    public PosSaleService(
        IPosPricingService pricing, ICartService carts, ICartRepository cartRepo, ICartItemRepository cartItems,
        IPOSCheckoutService checkout, ICustomerService customers, ICashDrawerService drawer, IReceiptRepository receipts, IOrderRepository orderRepo,
        IOrderService orders, IThermalPrinterService printer, IAuditTrail audit, IStringLocalizer<SharedResource> text,
        ILogger<PosSaleService> logger)
    {
        _pricing = pricing;
        _carts = carts;
        _cartRepo = cartRepo;
        _cartItems = cartItems;
        _checkout = checkout;
        _customers = customers;
        _drawer = drawer;
        _receipts = receipts;
        _orderRepo = orderRepo;
        _orders = orders;
        _printer = printer;
        _audit = audit;
        _text = text;
        _logger = logger;
    }

    // ---- checkout --------------------------------------------------------------------------------------------------

    public async Task<PosSaleDto> CheckoutAsync(PosCheckoutRequest request, int userId)
    {
        var now = DateTime.UtcNow;
        foreach (var stale in Attempts.Where(a => now - a.Value.StartedAtUtc > ReplayWindow).Select(a => a.Key).ToList())
            Attempts.TryRemove(stale, out _);

        var key = $"{userId}:{request.IdempotencyKey}";
        var fingerprint = Fingerprint(request);

        var done = new TaskCompletionSource<PosSaleDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mine = new Attempt(fingerprint, now, done.Task);
        var existing = Attempts.GetOrAdd(key, mine);

        if (!ReferenceEquals(existing, mine))
        {
            // The same key must mean the same sale; a different cart under an old key is a client bug that must not return someone else's receipt.
            if (existing.Fingerprint != fingerprint)
                throw new PosRuleException(StatusCodes.Status409Conflict, PosCodes.IdempotencyKeyReused, "Pos_IdempotencyKeyReused");

            return (await existing.Result) with { Replayed = true };
        }

        try
        {
            var sale = await SellAsync(request, userId);
            done.SetResult(sale);
            return sale;
        }
        catch (Exception ex)
        {
            Attempts.TryRemove(new KeyValuePair<string, Attempt>(key, mine));
            done.SetException(ex);
            _ = done.Task.Exception; // observed: waiting duplicates get it, nobody else is left to look
            throw;
        }
    }

    private async Task<PosSaleDto> SellAsync(PosCheckoutRequest request, int userId)
    {
        // 1. Everything that can be refused is refused before anything is written.
        var priced = await _pricing.PriceDetailedAsync(request.Cart);
        var cart = priced.Cart;

        var customerId = request.CustomerId is > 0 ? request.CustomerId : null;
        Customer? customer = null;
        if (customerId is not null)
        {
            customer = await TryGetCustomerAsync(customerId.Value)
                ?? throw new PosRuleException(StatusCodes.Status409Conflict, PosCodes.CustomerNotFound, "Pos_CustomerNotFound", "customerId");
        }

        if (request.AddToDebt)
        {
            if (customerId is null)
                throw new PosRuleException(StatusCodes.Status400BadRequest, PosCodes.DebtRequiresCustomer, "Pos_DebtRequiresCustomer", "customerId");
        }
        else if (!(await _checkout.GetPaymentMethodsAsync()).Any(m => m.Id == request.PaymentMethodId))
        {
            throw new PosRuleException(StatusCodes.Status400BadRequest, PosCodes.PaymentMethodInvalid, "Pos_PaymentMethodInvalid", "paymentMethodId");
        }

        var drawerSessionId = await _drawer.GetOpenSessionIdByUserIdAsync(userId);
        if (request.RequireOpenDrawer && drawerSessionId is null)
            throw new PosRuleException(StatusCodes.Status409Conflict, PosCodes.DrawerNotOpen, "Pos_DrawerNotOpen");

        // The cashier may lower the total (a discount) or nudge it for cash rounding, but not by more than the checkout service itself
        // allows (greater of 1.00 or 2 %). Saying so here gives the UI a stable code instead of a message to parse.
        var allowed = Math.Max(1.00m, cart.Total * 0.02m);
        if (Math.Abs(request.TotalAmount - cart.Total) > allowed)
            throw new PosRuleException(StatusCodes.Status409Conflict, PosCodes.TotalMismatch, "Pos_TotalMismatch", "totalAmount",
                request.TotalAmount.ToString("N2"), cart.Total.ToString("N2"), allowed.ToString("N2"));

        // 2. Build the cart the old till built (it created one per sale too), then let the unchanged checkout service sell it.
        var cartEntity = await _carts.CreateCartAsync(customerId);
        try
        {
            var built = await _carts.AddItemsToCartAsync(cartEntity.Id, request.Cart.Lines
                .Select(l => new CartItemInput(l.ProductId, l.Quantity, l.WeightOrVolume, l.PriceTierId, l.CustomPrice, request.Cart.CurrencyId))
                .ToList());

            // The cart stores each price in its product's currency, but a sale is made in ONE currency (the order and the receipt say so).
            // A line whose product is in another currency is stored at the converted price the cashier saw - so the service's subtotal is the one
            // shown on screen. (Left alone, a mixed-currency cart added up dollars and pounds and failed the total check.)
            foreach (var line in priced.Lines.Where(l => l.ProductCurrencyId != request.Cart.CurrencyId))
            {
                var item = built.CartItems.First(i => i.ProductId == line.Input.ProductId);
                item.UnitPrice = line.UnitPriceInCartCurrency;
                await _cartItems.SaveAsync(item);
            }

            built.DiscountAmount = request.Cart.DiscountPercentage is > 0 ? null : request.Cart.DiscountAmount;
            built.DiscountPercentage = request.Cart.DiscountPercentage;
            var label = request.Cart.DiscountLabel?.Trim();
            var notes = request.Notes?.Trim();
            // DiscountCode doubles as a free-text note (pre-existing design, kept so receipts and orders read the same as before).
            built.DiscountCode = string.Join(" | ", new[] { label, notes }.Where(s => !string.IsNullOrEmpty(s)));
            if (built.DiscountCode.Length == 0) built.DiscountCode = null;
            await _cartRepo.SaveAsync(built);

            var response = await _checkout.ProcessCheckoutAsync(new CheckoutRequest
            {
                CartId = cartEntity.Id,
                CustomerId = customerId,
                UserId = userId,
                TotalAmount = request.TotalAmount,
                PaymentMethodId = request.PaymentMethodId,
                AddToDebt = request.AddToDebt,
                DiscountAmount = built.DiscountAmount,
                DiscountPercentage = built.DiscountPercentage,
                DiscountCode = built.DiscountCode,
                Notes = request.Notes,
                CurrencyId = request.Cart.CurrencyId,
                PrintReceipt = request.PrintReceipt,
                AllowInsufficientStock = request.AllowInsufficientStock,
            });

            return await ToSaleAsync(response, cart, customer, drawerSessionId, request.AddToDebt);
        }
        catch (InsufficientStockException ex)
        {
            await DiscardCartAsync(cartEntity.Id);
            throw PosRuleException.Raw(StatusCodes.Status409Conflict, PosCodes.InsufficientStock, ex.Message);
        }
        catch (LicenseRequiredException)
        {
            await DiscardCartAsync(cartEntity.Id);
            throw; // mapped to 403 license_sales_stopped by the API's exception mapper
        }
        catch (ArgumentException ex) // the cart service refusing a line (price, quantity, product)
        {
            await DiscardCartAsync(cartEntity.Id);
            throw PosRuleException.Raw(StatusCodes.Status400BadRequest, ApiValidation, ex.Message, "lines");
        }
        catch (InvalidOperationException ex) // the checkout service's own refusals (total, customer, debt method, empty cart ...)
        {
            await DiscardCartAsync(cartEntity.Id);
            throw PosRuleException.Raw(StatusCodes.Status409Conflict, PosCodes.CheckoutFailed, ex.Message);
        }
        catch
        {
            await DiscardCartAsync(cartEntity.Id);
            throw;
        }
    }

    private const string ApiValidation = "validation_failed";

    /// <summary>A refused sale leaves a half-built active cart behind; take it out of the way (never one an order already uses).</summary>
    private async Task DiscardCartAsync(int cartId)
    {
        try
        {
            // The failed attempt was rolled back, but this context still holds the cart as the attempt left it (IsActive may read false),
            // so do not trust that flag: look for an order of this cart instead. Only reached when a sale was refused.
            var now = DateTime.UtcNow;
            if ((await _orderRepo.GetByDateRangeAsync(now.AddMinutes(-10), now.AddMinutes(1))).Any(o => o.CartId == cartId)) return;

            var cart = await _cartRepo.GetByIdAsync(cartId);
            if (cart is null) return;
            cart.IsDeleted = true;
            cart.UpdatedAt = DateTime.UtcNow;
            await _cartRepo.SaveAsync(cart);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not discard the cart {CartId} of a refused sale", cartId);
        }
    }

    private async Task<PosSaleDto> ToSaleAsync(CheckoutResponse response, PosPricedCartDto priced, Customer? customer, int? drawerSessionId, bool addedToDebt)
    {
        var receipt = (await _receipts.GetByOrderIdAsync(response.OrderId)).OrderByDescending(r => r.Id).FirstOrDefault();
        return new PosSaleDto
        {
            OrderId = response.OrderId,
            OrderNumber = response.OrderNumber,
            ReceiptId = receipt?.Id,
            ReceiptNumber = receipt?.ReceiptNumber,
            OrderDate = response.OrderDate,
            CurrencyId = priced.CurrencyId,
            CurrencyNotation = priced.CurrencyNotation,
            // The receipt knows the discount the service finally applied (a total the cashier lowered is folded into it).
            Subtotal = receipt?.SubTotal ?? priced.Subtotal,
            DiscountAmount = receipt?.DiscountAmount ?? priced.DiscountAmount,
            TaxAmount = receipt?.TaxAmount ?? 0m,
            Total = response.TotalAmount,
            Paid = response.PaidAmount,
            Change = response.ChangeAmount,
            PaymentMethod = response.PaymentMethod,
            CustomerId = customer?.Id,
            CustomerName = customer is null ? null : $"{customer.FirstName} {customer.LastName}".Trim(),
            AddedToDebt = addedToDebt,
            Replayed = false,
            CashDrawerSessionId = drawerSessionId,
            ReceiptPrinted = response.ReceiptPrinted,
            Lines = response.Items.Select(i => new PosSaleLineDto
            {
                ProductId = i.ProductId,
                Name = i.ProductName,
                Quantity = (int)i.Quantity,
                WeightOrVolume = i.WeightOrVolume,
                UnitPrice = i.UnitPrice,
                LineTotal = i.TotalPrice,
            }).ToList(),
        };
    }

    private async Task<Customer?> TryGetCustomerAsync(int id)
    {
        try { return await _customers.GetByIdAsync(id); }
        catch (Exception) { return null; } // the repository throws for an unknown (or deleted) id
    }

    /// <summary>What makes two requests "the same sale" - everything that changes money, nothing that is a retry flag.</summary>
    private static string Fingerprint(PosCheckoutRequest r)
    {
        var json = JsonSerializer.Serialize(new { r.Cart, r.CustomerId, r.PaymentMethodId, r.AddToDebt, r.TotalAmount });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    // ---- after the sale --------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<PosSaleSummaryDto>> GetRecentAsync(int userId, int count)
    {
        count = Math.Clamp(count, 1, 20);
        var since = DateTime.UtcNow.AddDays(-7);
        var receipts = await _receipts.GetByCashierIdAsync(userId, since, null);

        return receipts
            .Where(r => !r.IsVoid)
            .OrderByDescending(r => r.ReceiptDate).ThenByDescending(r => r.Id)
            .Take(count)
            .Select(r => new PosSaleSummaryDto
            {
                OrderId = r.OrderId,
                OrderNumber = $"ORD-{r.OrderId:D6}-{r.ReceiptDate.ToLocalTime():yyyyMMdd}",
                ReceiptId = r.Id,
                ReceiptNumber = r.ReceiptNumber,
                OrderDate = r.ReceiptDate,
                Total = r.TotalAmount,
                CurrencyNotation = r.Currency?.Notation ?? string.Empty,
                CustomerName = r.Customer is null ? r.CustomerName : $"{r.Customer.FirstName} {r.Customer.LastName}".Trim(),
            })
            .ToList();
    }

    public async Task<PosPrintResultDto> PrintAsync(int orderId)
    {
        var order = await FindOrderAsync(orderId);

        // A reprint of an existing sale: straight to the receipt printer, never a new receipt row.
        var document = await _printer.GetOrderReceiptDocumentAsync(order);
        var printed = await _printer.PrintDocumentAsync(document, jobName: document.Title);

        _audit.LogBusiness(
            printed ? AuditActions.Print : AuditActions.PrintFailed,
            printed ? $"Sent '{document.Title}' to the receipt printer" : $"Could not print '{document.Title}' - receipt printer unavailable",
            "PrintJob",
            printed ? AuditOutcome.Success : AuditOutcome.Failure,
            printed ? AuditLogSeverity.Info : AuditLogSeverity.Warning,
            entityLabel: document.Title,
            details: new { document.Title, document.FileName, orderId });

        return new PosPrintResultDto
        {
            Printed = printed,
            Message = _text[printed ? "Pos_PrintOk" : "Pos_PrintFailed"].Value,
            PdfUrl = $"/api/v1/pos/sales/{orderId}/receipt.pdf",
        };
    }

    public async Task<(byte[] Pdf, string FileName)> GetPdfAsync(int orderId)
    {
        var order = await FindOrderAsync(orderId);
        var document = await _printer.GetOrderReceiptDocumentAsync(order);
        var pdf = _printer.GeneratePdf(document, Core.Printing.PdfPaper.Receipt);

        _audit.LogBusiness(AuditActions.PdfGenerated, $"PDF generated: '{document.Title}'", "PrintJob",
            entityLabel: document.Title,
            details: new { document.Title, document.FileName, paper = nameof(Core.Printing.PdfPaper.Receipt), bytes = pdf.Length });
        return (pdf, $"{document.FileName}.pdf");
    }

    private async Task<Order> FindOrderAsync(int orderId)
    {
        try { return await _orders.GetByIdAsync(orderId); }
        catch (ArgumentNullException) { throw new PosRuleException(StatusCodes.Status404NotFound, PosCodes.OrderNotFound, "Pos_OrderNotFound"); }
    }
}
