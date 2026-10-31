using Application.Services.Notifications;
using Core.Entities.Notifications;
using Infrastructure.Data;
using Infrastructure.Repositories.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.TestHelpers;
using Xunit;

namespace Tests.Services;

/// <summary>
/// The top-bar bell shows open stock alerts and notifications together. An open (Active) alert is "unread";
/// acknowledging it is how the bell marks it read. Resolved/dismissed alerts never appear.
/// </summary>
public class NotificationFeedTests : IDisposable
{
    private readonly SqliteInMemoryDbContextFactory _dbFactory = new();
    private readonly IServiceProvider _services = new ServiceCollection().BuildServiceProvider();

    public void Dispose() => _dbFactory.Dispose();

    private NotificationService CreateSut(ErpDbContext context) =>
        new(new NotificationRepository(context, _services), new AlertRepository(context, _services));

    private static Alert StockAlert(AlertStatus status, DateTime createdAt, string? metadata = null) => new()
    {
        Title = "Low Stock Alert: Widget",
        Message = "Widget is running low",
        Type = AlertType.LowStock,
        Status = status,
        CreatedAt = createdAt,
        Metadata = metadata
    };

    private static Notification Note(bool isRead, DateTime createdAt) => new()
    {
        Title = "Backup completed",
        Message = "Done",
        Type = NotificationType.Success,
        IsRead = isRead,
        CreatedAt = createdAt
    };

    private static async Task<int> AnyUserIdAsync(ErpDbContext context) =>
        await context.Users.Select(u => u.Id).FirstAsync();

    [Fact]
    public async Task Feed_CountsActiveAlertsAndUnreadNotifications_AndHidesDismissedAndResolved()
    {
        using var context = _dbFactory.CreateContext();
        var now = DateTime.UtcNow;
        context.Alerts.AddRange(
            StockAlert(AlertStatus.Active, now.AddMinutes(-1)),
            StockAlert(AlertStatus.Acknowledged, now.AddMinutes(-2)),
            StockAlert(AlertStatus.Dismissed, now.AddMinutes(-3)),
            StockAlert(AlertStatus.Resolved, now.AddMinutes(-4)));
        context.Notifications.AddRange(Note(isRead: false, now.AddMinutes(-5)), Note(isRead: true, now.AddMinutes(-6)));
        await context.SaveChangesAsync();

        var feed = await CreateSut(context).GetFeedAsync(take: 10, includeAlerts: true);

        Assert.Equal(2, feed.UnreadCount); // the one Active alert + the one unread notification
        Assert.Equal(4, feed.Items.Count); // Active + Acknowledged alert, both notifications
        Assert.Equal(2, feed.Items.Count(i => i.Source == "alert"));
        Assert.Equal(2, feed.Items.Count(i => !i.IsRead));
    }

    [Fact]
    public async Task Feed_WithoutAlertsPermission_ShowsOnlyNotifications()
    {
        using var context = _dbFactory.CreateContext();
        var now = DateTime.UtcNow;
        context.Alerts.Add(StockAlert(AlertStatus.Active, now));
        context.Notifications.Add(Note(isRead: false, now));
        await context.SaveChangesAsync();

        var feed = await CreateSut(context).GetFeedAsync(take: 10, includeAlerts: false);

        Assert.Equal(1, feed.UnreadCount);
        Assert.All(feed.Items, i => Assert.Equal("notification", i.Source));
    }

    [Fact]
    public async Task Feed_IsNewestFirstAcrossBothSources_AndCappedAtTake()
    {
        using var context = _dbFactory.CreateContext();
        var now = DateTime.UtcNow;
        context.Alerts.Add(StockAlert(AlertStatus.Active, now.AddMinutes(-10)));
        context.Notifications.AddRange(Note(false, now.AddMinutes(-1)), Note(false, now.AddMinutes(-20)));
        await context.SaveChangesAsync();

        var feed = await CreateSut(context).GetFeedAsync(take: 2, includeAlerts: true);

        Assert.Equal(2, feed.Items.Count);
        Assert.Equal("notification", feed.Items[0].Source);
        Assert.Equal("alert", feed.Items[1].Source);
        Assert.Equal(3, feed.UnreadCount); // the cap limits the list, not the badge
    }

    [Fact]
    public async Task Feed_StockAlert_ExposesProductFactsForLocalizedCopyAndTheDeepLink()
    {
        using var context = _dbFactory.CreateContext();
        context.Alerts.Add(StockAlert(AlertStatus.Active, DateTime.UtcNow,
            """{"ProductId":42,"ProductName":"USB-C Cable","StockQuantity":2,"MinimumStock":10}"""));
        await context.SaveChangesAsync();

        var item = Assert.Single((await CreateSut(context).GetFeedAsync(5, includeAlerts: true)).Items);

        Assert.Equal("lowStock", item.Kind);
        Assert.Equal(42, item.ProductId);
        Assert.Equal("USB-C Cable", item.Params["productName"]);
        Assert.Equal(2m, item.Params["stockQuantity"]);
        Assert.Equal(10m, item.Params["minimumStock"]);
    }

    [Fact]
    public async Task Feed_AlertWithMalformedMetadata_StillShowsWithItsStoredText()
    {
        using var context = _dbFactory.CreateContext();
        context.Alerts.Add(StockAlert(AlertStatus.Active, DateTime.UtcNow, "{not json"));
        await context.SaveChangesAsync();

        var item = Assert.Single((await CreateSut(context).GetFeedAsync(5, includeAlerts: true)).Items);

        Assert.Null(item.ProductId);
        Assert.Equal("Widget is running low", item.Message);
    }

    [Fact]
    public async Task MarkAllRead_AcknowledgesActiveAlertsAsTheUser_AndReadsNotifications()
    {
        using var context = _dbFactory.CreateContext();
        var userId = await AnyUserIdAsync(context);
        context.Alerts.Add(StockAlert(AlertStatus.Active, DateTime.UtcNow));
        context.Notifications.Add(Note(isRead: false, DateTime.UtcNow));
        await context.SaveChangesAsync();

        await CreateSut(context).MarkAllReadAsync(userId, includeAlerts: true);

        var alert = await context.Alerts.SingleAsync();
        Assert.Equal(AlertStatus.Acknowledged, alert.Status);
        Assert.Equal(userId, alert.AcknowledgedByUserId);
        Assert.True((await context.Notifications.SingleAsync()).IsRead);
        Assert.Equal(0, (await CreateSut(context).GetFeedAsync(5, includeAlerts: true)).UnreadCount);
    }

    [Fact]
    public async Task MarkAllRead_WithoutAlertsPermission_LeavesAlertsActive()
    {
        using var context = _dbFactory.CreateContext();
        var userId = await AnyUserIdAsync(context);
        context.Alerts.Add(StockAlert(AlertStatus.Active, DateTime.UtcNow));
        await context.SaveChangesAsync();

        await CreateSut(context).MarkAllReadAsync(userId, includeAlerts: false);

        Assert.Equal(AlertStatus.Active, (await context.Alerts.SingleAsync()).Status);
    }

    [Fact]
    public async Task DismissAllAlerts_DismissesActiveAndAcknowledged_ButNotResolved()
    {
        using var context = _dbFactory.CreateContext();
        var now = DateTime.UtcNow;
        context.Alerts.AddRange(
            StockAlert(AlertStatus.Active, now),
            StockAlert(AlertStatus.Acknowledged, now),
            StockAlert(AlertStatus.Resolved, now));
        await context.SaveChangesAsync();

        await CreateSut(context).DismissAllAlertsAsync();

        var statuses = (await context.Alerts.OrderBy(a => a.Id).ToListAsync()).Select(a => a.Status).ToList();
        Assert.Equal(new[] { AlertStatus.Dismissed, AlertStatus.Dismissed, AlertStatus.Resolved }, statuses);
    }

    [Fact]
    public async Task ClearAll_WithNoNotifications_DoesNotThrow()
    {
        using var context = _dbFactory.CreateContext();

        await CreateSut(context).ClearAllNotificationsAsync();

        Assert.Empty(await context.Notifications.ToListAsync());
    }
}
