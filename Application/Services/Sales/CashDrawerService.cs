using Application.IServices.Sales;
using Core.Discounts;
using Core.Entities.Sales;
using Core.Enum;
using Core.IRepositories.Sales;
using Microsoft.Extensions.Logging;

namespace Application.Services.Sales;

public class CashDrawerService : ICashDrawerService
{
    private readonly ICashDrawerSessionRepository _sessionRepository;
    private readonly ICashDropRepository _cashDropRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly ILogger<CashDrawerService> _logger;

    public CashDrawerService(
        ICashDrawerSessionRepository sessionRepository,
        ICashDropRepository cashDropRepository,
        IOrderRepository orderRepository,
        ILogger<CashDrawerService> logger)
    {
        _sessionRepository = sessionRepository;
        _cashDropRepository = cashDropRepository;
        _orderRepository = orderRepository;
        _logger = logger;
    }

    /// <summary>
    /// The session's orders that still count as sales. A cancelled/returned order (e.g. from a voided
    /// receipt) was handed back, so the drawer must not expect its money.
    /// </summary>
    private static List<Order> SoldOrders(CashDrawerSession session) =>
        session.Orders
            .Where(o => o.Status != (int)OrderStatus.Cancelled && o.Status != (int)OrderStatus.Returned)
            .ToList();

    /// <summary>
    /// Discounts given and cash-rounding adjustments across the orders, worked out from each order's cart.
    /// A percentage discount has no DiscountAmount on its cart, so it goes through <see cref="DiscountBreakdown"/>;
    /// reading DiscountAmount alone left those sales out of the discount total and counted their discount as
    /// an adjustment.
    /// </summary>
    private static (decimal Discounts, decimal Adjustments) DiscountsAndAdjustments(IEnumerable<Order> orders)
    {
        decimal discounts = 0, adjustments = 0;
        foreach (var order in orders.Where(o => o.Cart != null))
        {
            var subtotal = order.Cart.CartItems?.Sum(ci => ci.TotalPrice) ?? 0;
            var discount = DiscountBreakdown.From(subtotal, order.Cart.DiscountAmount, order.Cart.DiscountPercentage);
            discounts += discount.Amount;
            adjustments += order.TotalAmount - (subtotal - discount.Amount);
        }

        return (discounts, adjustments);
    }

    public async Task<CashDrawerSession> OpenDrawerAsync(int userId, decimal openingCash, string? notes = null)
    {
        // Check if user already has an open session
        var existingSession = await _sessionRepository.GetOpenSessionByUserIdAsync(userId);
        if (existingSession != null)
        {
            throw new InvalidOperationException($"User {userId} already has an open cash drawer session (ID: {existingSession.Id})");
        }

        var session = new CashDrawerSession(userId, openingCash, notes);
        var createdSession = await _sessionRepository.SaveAsync(session);
        
        _logger.LogInformation("Cash drawer opened for user {UserId} with opening cash {OpeningCash}", userId, openingCash);
        return createdSession;
    }

    public async Task<CashDrawerSession> CloseDrawerAsync(int sessionId, decimal actualCash, string? notes = null)
    {
        var session = await _sessionRepository.GetSessionWithDetailsAsync(sessionId);
        if (session == null)
        {
            throw new ArgumentException($"Session {sessionId} not found");
        }

        if (!session.IsOpen)
        {
            throw new InvalidOperationException($"Session {sessionId} is already closed");
        }

        // Calculate expected cash
        var expectedCash = await CalculateExpectedCashAsync(sessionId);
        
        // Calculate totals from orders
        var orders = SoldOrders(session);
        var totalSales = orders.Sum(o => o.TotalAmount);
        var totalTransactions = orders.Count;
        
        // TODO: Get actual payment method breakdown from orders
        // For now, we'll assume all cash
        var totalCash = totalSales;
        
        // Get cash drops
        var totalCashDrops = await _cashDropRepository.GetTotalDropsBySessionIdAsync(sessionId);
        
        // Calculate totals (discounts and adjustments from cart data)
        var (totalDiscounts, totalAdjustments) = DiscountsAndAdjustments(orders);

        // Update session
        session.ClosedAt = DateTime.UtcNow;
        session.IsOpen = false;
        session.ActualCash = actualCash;
        session.ExpectedCash = expectedCash;
        session.TotalSales = totalSales;
        session.TotalCash = totalCash;
        session.TotalCard = 0; // TODO: Implement when payment method tracking is added
        session.TotalOtherPayments = 0;
        session.TotalTransactions = totalTransactions;
        session.TotalCashDrops = totalCashDrops;
        session.TotalDiscounts = totalDiscounts;
        session.TotalAdjustments = totalAdjustments;
        session.ClosingNotes = notes;
        
        await _sessionRepository.SaveAsync(session);
        
        _logger.LogInformation("Cash drawer closed for session {SessionId}. Expected: {Expected}, Actual: {Actual}, Difference: {Difference}", 
            sessionId, expectedCash, actualCash, session.CashDifference);
        
        return session;
    }

    public async Task<CashDrawerSession?> GetOpenSessionByUserIdAsync(int userId)
    {
        return await _sessionRepository.GetOpenSessionByUserIdAsync(userId);
    }

    public async Task<int?> GetOpenSessionIdByUserIdAsync(int userId)
    {
        return await _sessionRepository.GetOpenSessionIdByUserIdAsync(userId);
    }

    public async Task<CashDrawerSession?> GetSessionWithDetailsAsync(int sessionId)
    {
        return await _sessionRepository.GetSessionWithDetailsAsync(sessionId);
    }

    public async Task<List<CashDrawerSession>> GetSessionHistoryAsync(int userId, DateTime? startDate = null, DateTime? endDate = null)
    {
        return await _sessionRepository.GetSessionsByUserIdAsync(userId, startDate, endDate);
    }

    public async Task<List<CashDrawerSession>> GetAllOpenSessionsAsync()
    {
        return await _sessionRepository.GetAllOpenSessionsAsync();
    }

    public async Task<bool> HasOpenSessionAsync(int userId)
    {
        return await _sessionRepository.HasOpenSessionAsync(userId);
    }

    public async Task<CashDrop> AddCashDropAsync(int sessionId, int userId, decimal amount, string? reason = null, string? notes = null)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId);
        if (!session.IsOpen)
        {
            throw new InvalidOperationException($"Cannot add cash drop to closed session {sessionId}");
        }

        var cashDrop = new CashDrop(sessionId, userId, amount, reason, notes);
        var createdDrop = await _cashDropRepository.SaveAsync(cashDrop);
        
        _logger.LogInformation("Cash drop of {Amount} added to session {SessionId} by user {UserId}", amount, sessionId, userId);
        return createdDrop;
    }

    public async Task<List<CashDrop>> GetSessionCashDropsAsync(int sessionId)
    {
        return await _cashDropRepository.GetBySessionIdAsync(sessionId);
    }

    public async Task<decimal> GetTotalCashDropsAsync(int sessionId)
    {
        return await _cashDropRepository.GetTotalDropsBySessionIdAsync(sessionId);
    }

    public async Task<decimal> CalculateExpectedCashAsync(int sessionId)
    {
        var session = await _sessionRepository.GetSessionWithDetailsAsync(sessionId);
        if (session == null)
        {
            throw new ArgumentException($"Session {sessionId} not found");
        }

        // Opening cash
        var expectedCash = session.OpeningCash;
        
        // Add cash sales (TODO: filter by payment method when implemented)
        var cashSales = SoldOrders(session).Sum(o => o.TotalAmount);
        expectedCash += cashSales;
        
        // Subtract cash drops
        var cashDrops = await _cashDropRepository.GetTotalDropsBySessionIdAsync(sessionId);
        expectedCash -= cashDrops;
        
        return expectedCash;
    }

    public async Task<SessionSummary> GetSessionSummaryAsync(int sessionId)
    {
        var session = await _sessionRepository.GetSessionWithDetailsAsync(sessionId);
        if (session == null)
        {
            throw new ArgumentException($"Session {sessionId} not found");
        }

        var totalCashDrops = await _cashDropRepository.GetTotalDropsBySessionIdAsync(sessionId);
        var expectedCash = await CalculateExpectedCashAsync(sessionId);

        var orders = SoldOrders(session);
        var totalSales = orders.Sum(o => o.TotalAmount);
        
        // Calculate discounts and adjustments
        var (totalDiscounts, totalAdjustments) = DiscountsAndAdjustments(orders);

        return new SessionSummary
        {
            SessionId = session.Id,
            OpenedAt = session.OpenedAt,
            ClosedAt = session.ClosedAt,
            OpeningCash = session.OpeningCash,
            TotalCashSales = totalSales, // TODO: Filter by cash payment method
            TotalCardSales = 0, // TODO: Implement
            TotalOtherSales = 0, // TODO: Implement
            TotalCashDrops = totalCashDrops,
            ExpectedCash = expectedCash,
            ActualCash = session.ActualCash,
            CashDifference = session.CashDifference,
            TotalTransactions = orders.Count,
            TotalSales = totalSales,
            TotalDiscounts = totalDiscounts,
            TotalAdjustments = totalAdjustments
        };
    }

    public async Task<ZReport> GenerateZReportAsync(int sessionId)
    {
        var session = await _sessionRepository.GetSessionWithDetailsAsync(sessionId);
        if (session == null)
        {
            throw new ArgumentException($"Session {sessionId} not found");
        }

        if (session.IsOpen)
        {
            throw new InvalidOperationException($"Cannot generate Z-Report for open session {sessionId}");
        }

        var cashDrops = await _cashDropRepository.GetBySessionIdAsync(sessionId);
        var orders = session.Orders.ToList();
        
        var report = new ZReport
        {
            SessionId = session.Id,
            CashierName = session.User != null
                ? $"{session.User.FirstName} {session.User.LastName}".Trim()
                : string.Empty,
            OpenedAt = session.OpenedAt,
            ClosedAt = session.ClosedAt!.Value,
            OpeningCash = session.OpeningCash,
            ClosingCash = session.ActualCash!.Value,
            TotalTransactions = session.TotalTransactions!.Value,
            TotalSales = session.TotalSales!.Value,
            TotalDiscounts = session.TotalDiscounts!.Value,
            TotalAdjustments = session.TotalAdjustments!.Value,
            NetSales = session.TotalSales!.Value - session.TotalDiscounts!.Value,
            CashSales = session.TotalCash!.Value,
            CardSales = session.TotalCard!.Value,
            OtherPayments = session.TotalOtherPayments!.Value,
            TotalCashDrops = session.TotalCashDrops!.Value,
            CashDropCount = cashDrops.Count,
            ExpectedCash = session.ExpectedCash!.Value,
            ActualCash = session.ActualCash!.Value,
            CashDifference = session.CashDifference!.Value,
            Orders = orders.Select(o => new ZReportOrder
            {
                OrderId = o.Id,
                OrderNumber = $"ORD-{o.Id:D6}",
                OrderDate = o.OrderDate,
                CustomerName = o.Customer != null
                    ? $"{o.Customer.FirstName} {o.Customer.LastName}".Trim()
                    : "Walk-in",
                TotalAmount = o.TotalAmount,
                PaymentMethod = "Cash" // TODO: Get actual payment method
            }).ToList(),
            CashDrops = cashDrops.Select(cd => new ZReportCashDrop
            {
                Id = cd.Id,
                DroppedAt = cd.DroppedAt,
                Amount = cd.Amount,
                Reason = cd.Reason,
                Notes = cd.Notes
            }).ToList()
        };
        
        return report;
    }
}

