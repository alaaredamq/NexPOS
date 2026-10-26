using Application.Services.Sales;
using Core.Entities.Sales;
using Core.IRepositories.Sales;
using Moq;
using Xunit;

namespace Tests.Services;

/// <summary>
/// Covers cash-drawer reconciliation: expected cash must equal opening float, plus everything
/// sold, minus everything dropped - the number a cashier's actual count gets compared against.
/// </summary>
public class CashDrawerServiceTests
{
    private static CashDrawerSession NewOpenSession(decimal openingCash, params decimal[] orderTotals)
    {
        var session = new CashDrawerSession(userId: 1, openingCash: openingCash) { Id = 1 };
        foreach (var total in orderTotals)
        {
            session.Orders.Add(new Order(customerId: null, cartId: 1, totalAmount: total));
        }
        return session;
    }

    [Fact]
    public async Task CalculateExpectedCashAsync_OpeningCashPlusSales_MinusDrops()
    {
        var session = NewOpenSession(openingCash: 100m, 25m, 40m); // opening 100 + sales 65 = 165

        var sessionRepo = new Mock<ICashDrawerSessionRepository>();
        sessionRepo.Setup(r => r.GetSessionWithDetailsAsync(1)).ReturnsAsync(session);

        var cashDropRepo = new Mock<ICashDropRepository>();
        cashDropRepo.Setup(r => r.GetTotalDropsBySessionIdAsync(1)).ReturnsAsync(20m);

        var sut = new CashDrawerService(
            sessionRepo.Object,
            cashDropRepo.Object,
            Mock.Of<IOrderRepository>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<CashDrawerService>>());

        var expected = await sut.CalculateExpectedCashAsync(1);

        // 100 (opening) + 65 (sales) - 20 (dropped) = 145
        Assert.Equal(145m, expected);
    }

    [Fact]
    public async Task CalculateExpectedCashAsync_IgnoresCancelledAndReturnedOrders()
    {
        // A voided receipt cancels its order: that money was handed back, so the drawer must not expect it.
        var session = NewOpenSession(openingCash: 100m, 25m, 40m, 30m, 10m);
        session.Orders.ElementAt(1).Status = (int)Core.Enum.OrderStatus.Cancelled; // 40
        session.Orders.ElementAt(3).Status = (int)Core.Enum.OrderStatus.Returned;  // 10

        var sessionRepo = new Mock<ICashDrawerSessionRepository>();
        sessionRepo.Setup(r => r.GetSessionWithDetailsAsync(1)).ReturnsAsync(session);

        var cashDropRepo = new Mock<ICashDropRepository>();
        cashDropRepo.Setup(r => r.GetTotalDropsBySessionIdAsync(1)).ReturnsAsync(0m);

        var sut = new CashDrawerService(
            sessionRepo.Object,
            cashDropRepo.Object,
            Mock.Of<IOrderRepository>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<CashDrawerService>>());

        var expected = await sut.CalculateExpectedCashAsync(1);

        // 100 (opening) + 25 + 30 (the two orders still standing) = 155
        Assert.Equal(155m, expected);
    }

    [Fact]
    public async Task CalculateExpectedCashAsync_NoSalesOrDrops_EqualsOpeningCash()
    {
        var session = NewOpenSession(openingCash: 50m);

        var sessionRepo = new Mock<ICashDrawerSessionRepository>();
        sessionRepo.Setup(r => r.GetSessionWithDetailsAsync(1)).ReturnsAsync(session);

        var cashDropRepo = new Mock<ICashDropRepository>();
        cashDropRepo.Setup(r => r.GetTotalDropsBySessionIdAsync(1)).ReturnsAsync(0m);

        var sut = new CashDrawerService(
            sessionRepo.Object,
            cashDropRepo.Object,
            Mock.Of<IOrderRepository>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<CashDrawerService>>());

        var expected = await sut.CalculateExpectedCashAsync(1);

        Assert.Equal(50m, expected);
    }

    [Fact]
    public async Task CalculateExpectedCashAsync_UnknownSession_Throws()
    {
        var sessionRepo = new Mock<ICashDrawerSessionRepository>();
        sessionRepo.Setup(r => r.GetSessionWithDetailsAsync(It.IsAny<int>())).ReturnsAsync((CashDrawerSession?)null);

        var sut = new CashDrawerService(
            sessionRepo.Object,
            Mock.Of<ICashDropRepository>(),
            Mock.Of<IOrderRepository>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<CashDrawerService>>());

        await Assert.ThrowsAsync<ArgumentException>(() => sut.CalculateExpectedCashAsync(999));
    }
}
