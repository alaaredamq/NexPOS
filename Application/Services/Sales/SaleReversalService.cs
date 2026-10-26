using Application.IServices.Sales;
using Application.IServices.Users;
using Core.Auditing;
using Core.Enum;
using Core.ICache;
using Core.IRepositories.Products;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Sales;

public class SaleReversalService : ISaleReversalService
{
    private readonly ErpDbContext _dbContext;
    private readonly IProductRepository _productRepository;
    private readonly ICustomerDebtService _customerDebtService;
    private readonly ICacheService _cache;
    private readonly IAuditTrail _auditTrail;
    private readonly ILogger<SaleReversalService> _logger;

    public SaleReversalService(
        ErpDbContext dbContext,
        IProductRepository productRepository,
        ICustomerDebtService customerDebtService,
        ICacheService cache,
        IAuditTrail auditTrail,
        ILogger<SaleReversalService> logger)
    {
        _dbContext = dbContext;
        _productRepository = productRepository;
        _customerDebtService = customerDebtService;
        _cache = cache;
        _auditTrail = auditTrail;
        _logger = logger;
    }

    public async Task<SaleReversalResult> VoidReceiptAsync(int receiptId, int userId, string? reason)
    {
        var receipt = await _dbContext.Receipts
            .AsNoTracking()
            .Where(r => r.Id == receiptId && !r.IsDeleted)
            .Select(r => new { r.OrderId, r.ReceiptNumber, r.IsVoid })
            .FirstOrDefaultAsync()
            ?? throw new ArgumentException($"Receipt {receiptId} not found.");

        if (receipt.IsVoid)
            throw new InvalidOperationException($"Receipt {receipt.ReceiptNumber} is already void.");

        var result = await ReverseAsync(receipt.OrderId, userId, CleanReason(reason), mustBeActive: false);

        _auditTrail.LogBusiness(AuditActions.ReceiptVoided,
            $"Receipt {receipt.ReceiptNumber} voided. {Describe(result)}",
            "Receipt", entityId: receiptId, entityLabel: receipt.ReceiptNumber,
            details: new { receiptId, orderId = result.OrderId, reason = CleanReason(reason), result });

        return result;
    }

    public async Task<SaleReversalResult> CancelOrderAsync(int orderId, int userId, string? reason)
    {
        var result = await ReverseAsync(orderId, userId, CleanReason(reason), mustBeActive: true);

        _auditTrail.LogBusiness(AuditActions.OrderCancelled,
            $"Order {result.OrderNumber} cancelled. {Describe(result)}",
            "Order", entityId: orderId, entityLabel: result.OrderNumber,
            details: new { orderId, reason = CleanReason(reason), result });

        return result;
    }

    /// <summary>
    /// The one place a sale is undone. Everything happens in a single transaction (the same pattern
    /// as checkout) so a failure part-way - say the debt ledger - leaves the sale exactly as it was
    /// instead of a cancelled order with its stock still missing.
    /// </summary>
    /// <param name="mustBeActive">Throw if the order is already cancelled/returned (the cancel path);
    /// the void path tolerates it, because voiding the receipt is still meaningful.</param>
    private async Task<SaleReversalResult> ReverseAsync(int orderId, int userId, string reason, bool mustBeActive)
    {
        var orderNumber = $"ORD-{orderId:D6}";
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        var result = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            var order = await _dbContext.Orders
                .Include(o => o.Cart)
                .ThenInclude(c => c.CartItems)
                .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted);

            if (order == null && mustBeActive)
                throw new ArgumentException($"Order {orderId} not found.");

            // The order was already taken out of the books (cancelled or returned): there is no sale
            // left to reverse, so stock and debt must not be touched a second time.
            var isActiveSale = order != null
                && order.Status != (int)OrderStatus.Cancelled
                && order.Status != (int)OrderStatus.Returned;

            if (mustBeActive && !isActiveSale)
                throw new InvalidOperationException($"Order {orderNumber} is already {(OrderStatus)order!.Status}.");

            var receipts = await _dbContext.Receipts
                .Where(r => r.OrderId == orderId && !r.IsDeleted && !r.IsVoid)
                .ToListAsync();
            foreach (var receipt in receipts)
                receipt.VoidReceipt(userId, reason);

            var linesRestored = 0;
            var debtReversed = 0m;
            if (isActiveSale)
            {
                foreach (var item in order!.Cart?.CartItems ?? [])
                {
                    // Same quantity the sale deducted: weighed/volume products carry the real amount
                    // in WeightOrVolume, piece products in Quantity.
                    var quantity = item.WeightOrVolume is > 0 ? item.WeightOrVolume.Value : item.Quantity;
                    if (quantity > 0 && await _productRepository.RestoreStockAsync(item.ProductId, quantity))
                        linesRestored++;
                }

                debtReversed = await _customerDebtService.ReverseSaleAsync(
                    order.Id, userId, $"Reversal of {orderNumber}: {reason}");

                // The open drawer's expected cash is computed from the orders that count as sales,
                // so this status change is what takes the money out of it.
                order.Status = (int)OrderStatus.Cancelled;
            }

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            return new SaleReversalResult(orderId, orderNumber, isActiveSale, receipts.Count, linesRestored, debtReversed);
        });

        if (result.StockLinesRestored > 0)
        {
            // Stock moved through ExecuteUpdate - drop the POS search cache so it doesn't keep showing the old quantities.
            await _cache.ClearPOSCachesAsync();
        }

        _logger.LogInformation(
            "Sale {OrderNumber} reversed by user {UserId}: order cancelled={Cancelled}, receipts voided={Receipts}, stock lines restored={Lines}, debt reversed={Debt}",
            orderNumber, userId, result.OrderCancelled, result.ReceiptsVoided, result.StockLinesRestored, result.DebtReversed);

        return result;
    }

    private static string CleanReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? "No reason given" : reason.Trim();

    private static string Describe(SaleReversalResult r) =>
        r.OrderCancelled
            ? $"Order cancelled, {r.StockLinesRestored} item line(s) returned to stock" +
              (r.DebtReversed > 0 ? $", {r.DebtReversed:F2} taken off the customer's account." : ".")
            : "The order had already been cancelled or returned, so stock and balances were left alone.";
}
