using Application.IServices.Sales;
using Application.IServices.Users;
using Core.Auditing;
using Core.Enum;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Sales;

public class SaleCustomerService : ISaleCustomerService
{
    private readonly ErpDbContext _dbContext;
    private readonly ICustomerDebtService _customerDebtService;
    private readonly IAuditTrail _auditTrail;
    private readonly ILogger<SaleCustomerService> _logger;

    public SaleCustomerService(
        ErpDbContext dbContext,
        ICustomerDebtService customerDebtService,
        IAuditTrail auditTrail,
        ILogger<SaleCustomerService> logger)
    {
        _dbContext = dbContext;
        _customerDebtService = customerDebtService;
        _auditTrail = auditTrail;
        _logger = logger;
    }

    public async Task<SaleCustomerResult> ChangeCustomerAsync(int orderId, int? customerId, int userId)
    {
        var orderNumber = $"ORD-{orderId:D6}";
        var newCustomerId = customerId is > 0 ? customerId : null;
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        // One transaction, like checkout and the reversal: a failure while moving the debt leaves the sale with its old customer.
        var (result, oldCustomerId) = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            var order = await _dbContext.Orders
                .Include(o => o.Cart)
                .FirstOrDefaultAsync(o => o.Id == orderId && !o.IsDeleted)
                ?? throw new SaleCustomerException(SaleCustomerFailure.OrderNotFound, $"Order {orderId} not found.");

            if (order.Status == (int)OrderStatus.Cancelled || order.Status == (int)OrderStatus.Returned)
                throw new SaleCustomerException(SaleCustomerFailure.OrderClosed, $"Order {orderNumber} is already {(OrderStatus)order.Status}.");

            if (newCustomerId != null && !await _dbContext.Customers.AnyAsync(c => c.Id == newCustomerId))
                throw new SaleCustomerException(SaleCustomerFailure.CustomerNotFound, $"Customer {newCustomerId} not found.");

            var previous = order.CustomerId;
            if (previous == newCustomerId)
                return (new SaleCustomerResult(orderId, orderNumber, newCustomerId, false, 0, 0m), previous);

            var ledger = await _dbContext.CustomerDebtTransactions
                .Where(t => t.OrderId == orderId)
                .Select(t => t.Type)
                .ToListAsync();
            var onDebt = ledger.Contains(CustomerDebtTransactionType.Sale) && !ledger.Contains(CustomerDebtTransactionType.SaleReversal);
            if (onDebt && newCustomerId == null)
                throw new SaleCustomerException(SaleCustomerFailure.DebtNeedsCustomer, $"Order {orderNumber} is on a customer's account, so it needs a customer.");

            order.CustomerId = newCustomerId;
            if (order.Cart != null)
                order.Cart.CustomerId = newCustomerId;

            // The receipt keeps an optional name/phone snapshot that wins over the customer record when set; clear it
            // so the receipt reads the new customer (or "walk-in") instead of the old name.
            var receipts = await _dbContext.Receipts
                .Where(r => r.OrderId == orderId && !r.IsDeleted && !r.IsVoid)
                .ToListAsync();
            foreach (var receipt in receipts)
            {
                receipt.CustomerId = newCustomerId;
                receipt.CustomerName = null;
                receipt.CustomerPhone = null;
            }

            var debtMoved = onDebt
                ? await _customerDebtService.MoveSaleAsync(orderId, newCustomerId!.Value, userId, $"{orderNumber} moved to another customer")
                : 0m;

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            return (new SaleCustomerResult(orderId, orderNumber, newCustomerId, true, receipts.Count, debtMoved), previous);
        });

        if (result.Changed)
        {
            _auditTrail.LogBusiness(AuditActions.SaleCustomerChanged,
                $"Customer of {orderNumber} changed" +
                (result.DebtMoved > 0 ? $"; {result.DebtMoved:F2} of debt moved with it." : "."),
                "Order", entityId: orderId, entityLabel: orderNumber,
                details: new { orderId, from = oldCustomerId, to = result.CustomerId, result.ReceiptsUpdated, result.DebtMoved });

            _logger.LogInformation(
                "Customer of {OrderNumber} changed from {From} to {To} by user {UserId}: receipts updated={Receipts}, debt moved={Debt}",
                orderNumber, oldCustomerId?.ToString() ?? "walk-in", result.CustomerId?.ToString() ?? "walk-in", userId, result.ReceiptsUpdated, result.DebtMoved);
        }

        return result;
    }
}
