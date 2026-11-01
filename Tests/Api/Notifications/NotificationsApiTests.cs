using System.Net;
using System.Text.Json;
using Core.Entities.Notifications;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Api.Catalog;
using Tests.Api.Infrastructure;
using Xunit;

namespace Tests.Api.Notifications;

/// <summary>
/// Notifications API (track I2): who may do what (notifications are open to every signed-in user, alerts need Alerts.*),
/// the bell feed and its unread count, the notifications and alerts lists (paging, filters), mark read / acknowledge,
/// dismiss, delete, clear and cleanup. Rows are seeded straight into the database: they are raised by other code in the app.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationsApiTests : CatalogApiTestBase
{
    public NotificationsApiTests(ApiFactory factory) : base(factory)
    {
    }

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..14];

    private async Task<int> SeedNotificationAsync(string title, NotificationType type = NotificationType.Info, bool isRead = false, DateTime? at = null,
        NotificationPriority priority = NotificationPriority.Normal)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var row = new Notification
        {
            Title = title,
            Message = $"{title} message",
            Type = type,
            Priority = priority,
            IsRead = isRead,
            ReadAt = isRead ? DateTime.UtcNow : null,
            CreatedAt = at ?? DateTime.UtcNow,
        };
        db.Notifications.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private async Task<int> SeedAlertAsync(string title, AlertType type = AlertType.LowStock, AlertStatus status = AlertStatus.Active, DateTime? at = null,
        DateTime? resolvedAt = null, string? metadata = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var row = new Alert
        {
            Title = title,
            Message = $"{title} message",
            Type = type,
            Status = status,
            CreatedAt = at ?? DateTime.UtcNow,
            ResolvedAt = resolvedAt,
            Metadata = metadata,
        };
        db.Alerts.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private async Task<T> ReadAsync<T>(Func<ErpDbContext, Task<T>> read)
    {
        using var scope = Factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<ErpDbContext>());
    }

    private static string[] Titles(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!).ToArray();

    // ---- permissions -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Without_a_cookie_every_endpoint_answers_401()
    {
        var anonymous = Factory.CreateBrowser();

        await AssertProblem(await anonymous.GetAsync("/api/v1/notifications"), HttpStatusCode.Unauthorized, "unauthenticated");
        await AssertProblem(await anonymous.GetAsync("/api/v1/notifications/feed"), HttpStatusCode.Unauthorized, "unauthenticated");
        await AssertProblem(await anonymous.GetAsync("/api/v1/notifications/alerts"), HttpStatusCode.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task A_cashier_has_the_notifications_but_no_alerts()
    {
        var cashier = await Cashier();
        var admin = await Admin();
        var note = await SeedNotificationAsync(Unique("CashNote"));
        var alert = await SeedAlertAsync(Unique("CashAlert"));

        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cashier.GetAsync("/api/v1/notifications/summary")).StatusCode);
        var feed = await Body(await cashier.GetAsync("/api/v1/notifications/feed?take=50"));
        Assert.DoesNotContain(feed.GetProperty("items").EnumerateArray(), i => i.GetProperty("source").GetString() == "alert");

        await AssertProblem(await cashier.GetAsync("/api/v1/notifications/alerts"), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, $"/api/v1/notifications/feed/alert/{alert}/read")), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, $"/api/v1/notifications/alerts/{alert}/dismiss")), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/notifications/alerts/dismiss-all")), HttpStatusCode.Forbidden, "forbidden");
        await AssertProblem(await cashier.SendAsync(Json(HttpMethod.Delete, $"/api/v1/notifications/alerts/{alert}")), HttpStatusCode.Forbidden, "forbidden");

        // Nothing happened to the alert, and the cashier may still work the notification.
        Assert.Equal(AlertStatus.Active, await ReadAsync(db => db.Alerts.Where(a => a.Id == alert).Select(a => a.Status).SingleAsync()));
        Assert.Equal(HttpStatusCode.NoContent, (await cashier.SendAsync(Json(HttpMethod.Post, $"/api/v1/notifications/{note}/read"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/notifications/alerts")).StatusCode);
    }

    [Fact]
    public async Task A_mutation_without_the_client_header_is_refused()
    {
        var admin = await Admin();
        var id = await SeedNotificationAsync(Unique("Hdr"));

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/notifications/{id}/read");

        await AssertProblem(await admin.SendAsync(request), HttpStatusCode.Forbidden, "client_header_required");
        Assert.False(await ReadAsync(db => db.Notifications.Where(n => n.Id == id).Select(n => n.IsRead).SingleAsync()));
    }

    // ---- bell feed ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_feed_counts_active_alerts_and_unread_notifications_for_admin_and_only_notifications_for_a_cashier()
    {
        var admin = await Admin();
        var cashier = await Cashier();
        var before = (await Body(await admin.GetAsync("/api/v1/notifications/feed"))).GetProperty("unreadCount").GetInt32();
        var cashierBefore = (await Body(await cashier.GetAsync("/api/v1/notifications/feed"))).GetProperty("unreadCount").GetInt32();

        await SeedNotificationAsync(Unique("FeedUnread"));
        await SeedNotificationAsync(Unique("FeedRead"), isRead: true);
        await SeedAlertAsync(Unique("FeedActive"));
        await SeedAlertAsync(Unique("FeedAcked"), status: AlertStatus.Acknowledged);
        await SeedAlertAsync(Unique("FeedDismissed"), status: AlertStatus.Dismissed);

        var feed = await Body(await admin.GetAsync("/api/v1/notifications/feed?take=50"));
        // +1 unread notification, +1 active alert; the read, acknowledged and dismissed ones add nothing.
        Assert.Equal(before + 2, feed.GetProperty("unreadCount").GetInt32());
        var titles = Titles(feed);
        Assert.Contains(titles, t => t.StartsWith("FeedActive"));
        Assert.Contains(titles, t => t.StartsWith("FeedAcked"));
        Assert.DoesNotContain(titles, t => t.StartsWith("FeedDismissed"));

        var forCashier = await Body(await cashier.GetAsync("/api/v1/notifications/feed?take=50"));
        Assert.Equal(cashierBefore + 1, forCashier.GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task The_feed_is_newest_first_capped_by_take_and_carries_the_stock_facts_of_an_alert()
    {
        var admin = await Admin();
        var tag = Unique("Order");
        // Dated in the future so rows other tests seeded "just now" cannot crowd these three out of take=3.
        await SeedNotificationAsync($"{tag}-old", at: DateTime.UtcNow.AddDays(10));
        await SeedAlertAsync($"{tag}-stock", AlertType.LowStock, at: DateTime.UtcNow.AddDays(10).AddHours(1),
            metadata: """{"ProductId":42,"ProductName":"Milk","StockQuantity":3,"MinimumStock":10}""");
        await SeedNotificationAsync($"{tag}-new", at: DateTime.UtcNow.AddDays(10).AddHours(2));

        var feed = await Body(await admin.GetAsync("/api/v1/notifications/feed?take=3"));
        var items = feed.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal($"{tag}-new", items[0].GetProperty("title").GetString());
        Assert.Equal($"{tag}-stock", items[1].GetProperty("title").GetString());

        var stock = items[1];
        Assert.Equal("alert", stock.GetProperty("source").GetString());
        Assert.Equal("lowStock", stock.GetProperty("kind").GetString());
        Assert.Equal(42, stock.GetProperty("productId").GetInt32());
        Assert.Equal("Milk", stock.GetProperty("params").GetProperty("productName").GetString());
        Assert.Equal(3m, stock.GetProperty("params").GetProperty("stockQuantity").GetDecimal());
        Assert.Equal(10m, stock.GetProperty("params").GetProperty("minimumStock").GetDecimal());
        Assert.StartsWith("alert-", stock.GetProperty("key").GetString());

        // take is clamped, not rejected.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/notifications/feed?take=9999")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/notifications/feed?take=-3")).StatusCode);
    }

    [Fact]
    public async Task Clicking_a_bell_item_marks_a_notification_read_and_acknowledges_an_alert_as_the_signed_in_user()
    {
        var admin = await Admin();
        var note = await SeedNotificationAsync(Unique("BellNote"));
        var alert = await SeedAlertAsync(Unique("BellAlert"));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/notifications/feed/notification/{note}/read"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/notifications/feed/alert/{alert}/read"))).StatusCode);

        var read = await ReadAsync(db => db.Notifications.Where(n => n.Id == note).Select(n => new { n.IsRead, n.ReadAt }).SingleAsync());
        Assert.True(read.IsRead);
        Assert.NotNull(read.ReadAt);
        var acked = await ReadAsync(db => db.Alerts.Where(a => a.Id == alert).Select(a => new { a.Status, a.AcknowledgedByUserId, a.AcknowledgedAt }).SingleAsync());
        Assert.Equal(AlertStatus.Acknowledged, acked.Status);
        Assert.NotNull(acked.AcknowledgedByUserId);
        Assert.NotNull(acked.AcknowledgedAt);

        // An acknowledged alert is still on the bell but no longer unread.
        var item = (await Body(await admin.GetAsync("/api/v1/notifications/feed?take=50"))).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("key").GetString() == $"alert-{alert}");
        Assert.True(item.GetProperty("isRead").GetBoolean());
    }

    [Fact]
    public async Task A_bell_click_on_something_unknown_is_404()
    {
        var admin = await Admin();

        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/notifications/feed/notification/99999999/read")), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/notifications/feed/alert/99999999/read")), HttpStatusCode.NotFound, "not_found");
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/notifications/feed/banana/1/read")), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Read_all_marks_notifications_read_for_everyone_but_only_acknowledges_alerts_for_someone_who_may_see_them()
    {
        var admin = await Admin();
        var cashier = await Cashier();
        var note = await SeedNotificationAsync(Unique("AllNote"));
        var alert = await SeedAlertAsync(Unique("AllAlert"));

        Assert.Equal(HttpStatusCode.NoContent, (await cashier.SendAsync(Json(HttpMethod.Post, "/api/v1/notifications/read-all"))).StatusCode);
        Assert.True(await ReadAsync(db => db.Notifications.Where(n => n.Id == note).Select(n => n.IsRead).SingleAsync()));
        Assert.Equal(AlertStatus.Active, await ReadAsync(db => db.Alerts.Where(a => a.Id == alert).Select(a => a.Status).SingleAsync()));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/notifications/read-all"))).StatusCode);
        Assert.Equal(AlertStatus.Acknowledged, await ReadAsync(db => db.Alerts.Where(a => a.Id == alert).Select(a => a.Status).SingleAsync()));
        Assert.Equal(0, (await Body(await admin.GetAsync("/api/v1/notifications/feed"))).GetProperty("unreadCount").GetInt32());
    }

    // ---- notifications list ------------------------------------------------------------------------------------

    [Fact]
    public async Task The_list_pages_newest_first_and_filters_by_search_kind_and_unread()
    {
        var admin = await Admin();
        var tag = Unique("List");
        await SeedNotificationAsync($"{tag}-1", NotificationType.Info, at: DateTime.UtcNow.AddHours(-5));
        await SeedNotificationAsync($"{tag}-2", NotificationType.Warning, at: DateTime.UtcNow.AddHours(-4), priority: NotificationPriority.High);
        await SeedNotificationAsync($"{tag}-3", NotificationType.Error, isRead: true, at: DateTime.UtcNow.AddHours(-3));
        await SeedNotificationAsync($"{tag}-4", NotificationType.Warning, at: DateTime.UtcNow.AddHours(-2));

        var all = await Body(await admin.GetAsync($"/api/v1/notifications?q={tag}"));
        Assert.Equal(4, all.GetProperty("total").GetInt32());
        Assert.Equal(new[] { $"{tag}-4", $"{tag}-3", $"{tag}-2", $"{tag}-1" }, Titles(all));

        var oldestFirst = await Body(await admin.GetAsync($"/api/v1/notifications?q={tag}&dir=asc&page=2&pageSize=3"));
        Assert.Equal(4, oldestFirst.GetProperty("total").GetInt32());
        Assert.Equal(new[] { $"{tag}-4" }, Titles(oldestFirst));

        Assert.Equal(new[] { $"{tag}-4", $"{tag}-2" }, Titles(await Body(await admin.GetAsync($"/api/v1/notifications?q={tag}&kind=warning"))));
        Assert.Equal(new[] { $"{tag}-4", $"{tag}-2", $"{tag}-1" }, Titles(await Body(await admin.GetAsync($"/api/v1/notifications?q={tag}&unread=true"))));
        Assert.Equal(new[] { $"{tag}-2" }, Titles(await Body(await admin.GetAsync($"/api/v1/notifications?q={tag}-2"))));

        var row = all.GetProperty("items")[2];
        Assert.Equal("warning", row.GetProperty("kind").GetString());
        Assert.Equal("high", row.GetProperty("priority").GetString());
        Assert.False(row.GetProperty("isRead").GetBoolean());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("readAt").ValueKind);
        Assert.EndsWith("Z", row.GetProperty("createdAt").GetString());
    }

    [Fact]
    public async Task An_unknown_kind_is_a_400_on_that_field()
    {
        var admin = await Admin();

        var response = await admin.GetAsync("/api/v1/notifications?kind=banana");

        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.True((await Body(response)).GetProperty("errors").TryGetProperty("kind", out _));
        await AssertProblem(await admin.GetAsync("/api/v1/notifications/alerts?scope=banana"), HttpStatusCode.BadRequest, "validation_failed");
        // A number is not a kind either (Enum.TryParse would accept "7").
        await AssertProblem(await admin.GetAsync("/api/v1/notifications?kind=1"), HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Marking_a_notification_read_keeps_the_first_read_time()
    {
        var admin = await Admin();
        var id = await SeedNotificationAsync(Unique("Once"));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/notifications/{id}/read"))).StatusCode);
        var first = await ReadAsync(db => db.Notifications.Where(n => n.Id == id).Select(n => n.ReadAt).SingleAsync());
        await Task.Delay(30);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/notifications/{id}/read"))).StatusCode);
        var second = await ReadAsync(db => db.Notifications.Where(n => n.Id == id).Select(n => n.ReadAt).SingleAsync());

        Assert.NotNull(first);
        Assert.Equal(first, second);
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/notifications/99999999/read")), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Deleting_a_notification_removes_it_and_an_unknown_one_is_404()
    {
        var admin = await Admin();
        var id = await SeedNotificationAsync(Unique("Del"));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/notifications/{id}"))).StatusCode);

        Assert.False(await ReadAsync(db => db.Notifications.AnyAsync(n => n.Id == id)));
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/notifications/{id}")), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Clear_removes_every_notification_and_leaves_the_alerts()
    {
        var admin = await Admin();
        await SeedNotificationAsync(Unique("Clr"));
        await SeedNotificationAsync(Unique("Clr"), isRead: true);
        var alert = await SeedAlertAsync(Unique("ClrAlert"));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Delete, "/api/v1/notifications"))).StatusCode);

        Assert.Equal(0, await ReadAsync(db => db.Notifications.CountAsync()));
        Assert.True(await ReadAsync(db => db.Alerts.AnyAsync(a => a.Id == alert)));
        Assert.Equal(0, (await Body(await admin.GetAsync("/api/v1/notifications"))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Cleanup_deletes_notifications_older_than_30_days_and_alerts_dismissed_over_a_week_ago()
    {
        var admin = await Admin();
        var oldNote = await SeedNotificationAsync(Unique("OldNote"), at: DateTime.UtcNow.AddDays(-31));
        var newNote = await SeedNotificationAsync(Unique("NewNote"), at: DateTime.UtcNow.AddDays(-29));
        var oldDismissed = await SeedAlertAsync(Unique("OldDis"), status: AlertStatus.Dismissed, at: DateTime.UtcNow.AddDays(-20), resolvedAt: DateTime.UtcNow.AddDays(-8));
        var newDismissed = await SeedAlertAsync(Unique("NewDis"), status: AlertStatus.Dismissed, at: DateTime.UtcNow.AddDays(-20), resolvedAt: DateTime.UtcNow.AddDays(-2));
        var oldActive = await SeedAlertAsync(Unique("OldAct"), at: DateTime.UtcNow.AddDays(-40));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/notifications/cleanup"))).StatusCode);

        Assert.False(await ReadAsync(db => db.Notifications.AnyAsync(n => n.Id == oldNote)));
        Assert.True(await ReadAsync(db => db.Notifications.AnyAsync(n => n.Id == newNote)));
        Assert.False(await ReadAsync(db => db.Alerts.AnyAsync(a => a.Id == oldDismissed)));
        Assert.True(await ReadAsync(db => db.Alerts.AnyAsync(a => a.Id == newDismissed)));
        // Cleanup never touches an alert somebody still has to deal with, however old.
        Assert.True(await ReadAsync(db => db.Alerts.AnyAsync(a => a.Id == oldActive)));
    }

    // ---- summary -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_summary_counts_in_the_database_and_hides_the_alert_figures_from_a_cashier()
    {
        var admin = await Admin();
        var cashier = await Cashier();
        var before = await Body(await admin.GetAsync("/api/v1/notifications/summary"));
        var cashierBefore = await Body(await cashier.GetAsync("/api/v1/notifications/summary"));

        await SeedNotificationAsync(Unique("SumUnread"));
        await SeedNotificationAsync(Unique("SumRead"), isRead: true);
        await SeedAlertAsync(Unique("SumLow"), AlertType.LowStock);
        await SeedAlertAsync(Unique("SumOut"), AlertType.OutOfStock);
        await SeedAlertAsync(Unique("SumAck"), AlertType.LowStock, AlertStatus.Acknowledged);
        await SeedAlertAsync(Unique("SumGone"), AlertType.SystemError, AlertStatus.Dismissed);

        var after = await Body(await admin.GetAsync("/api/v1/notifications/summary"));
        int Diff(string name) => after.GetProperty(name).GetInt32() - before.GetProperty(name).GetInt32();
        Assert.Equal(2, Diff("totalNotifications"));
        Assert.Equal(1, Diff("unreadNotifications"));
        Assert.Equal(4, Diff("totalAlerts"));
        Assert.Equal(2, Diff("activeAlerts"));
        Assert.Equal(3, Diff("openAlerts"));
        Assert.Equal(1, Diff("lowStockAlerts"));
        Assert.Equal(1, Diff("outOfStockAlerts"));
        Assert.True(after.GetProperty("alertsVisible").GetBoolean());

        var forCashier = await Body(await cashier.GetAsync("/api/v1/notifications/summary"));
        Assert.False(forCashier.GetProperty("alertsVisible").GetBoolean());
        Assert.Equal(0, forCashier.GetProperty("totalAlerts").GetInt32());
        Assert.Equal(0, forCashier.GetProperty("openAlerts").GetInt32());
        Assert.Equal(cashierBefore.GetProperty("totalNotifications").GetInt32() + 2, forCashier.GetProperty("totalNotifications").GetInt32());
    }

    // ---- alerts ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_alert_list_defaults_to_open_alerts_and_can_show_closed_or_all_filtered_by_kind()
    {
        var admin = await Admin();
        var tag = Unique("Alr");
        await SeedAlertAsync($"{tag}-active", AlertType.LowStock, at: DateTime.UtcNow.AddHours(-4));
        await SeedAlertAsync($"{tag}-acked", AlertType.OutOfStock, AlertStatus.Acknowledged, at: DateTime.UtcNow.AddHours(-3));
        await SeedAlertAsync($"{tag}-dismissed", AlertType.LowStock, AlertStatus.Dismissed, at: DateTime.UtcNow.AddHours(-2), resolvedAt: DateTime.UtcNow);
        await SeedAlertAsync($"{tag}-resolved", AlertType.BackupFailed, AlertStatus.Resolved, at: DateTime.UtcNow.AddHours(-1), resolvedAt: DateTime.UtcNow);

        Assert.Equal(new[] { $"{tag}-acked", $"{tag}-active" }, Titles(await Body(await admin.GetAsync($"/api/v1/notifications/alerts?q={tag}"))));
        Assert.Equal(new[] { $"{tag}-resolved", $"{tag}-dismissed" }, Titles(await Body(await admin.GetAsync($"/api/v1/notifications/alerts?q={tag}&scope=closed"))));
        var everything = await Body(await admin.GetAsync($"/api/v1/notifications/alerts?q={tag}&scope=all"));
        Assert.Equal(4, everything.GetProperty("total").GetInt32());
        Assert.Equal(new[] { $"{tag}-active", $"{tag}-dismissed" }, Titles(await Body(await admin.GetAsync($"/api/v1/notifications/alerts?q={tag}&scope=all&kind=lowStock&dir=asc"))));

        var statuses = everything.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("status").GetString()).ToArray();
        Assert.Equal(new[] { "resolved", "dismissed", "acknowledged", "active" }, statuses);
        Assert.Equal("backupFailed", everything.GetProperty("items")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task An_alert_row_carries_the_product_facts_so_the_page_can_localize_it()
    {
        var admin = await Admin();
        var tag = Unique("Facts");
        await SeedAlertAsync(tag, AlertType.OutOfStock, metadata: """{"ProductId":7,"ProductName":"Bread","StockQuantity":0}""");

        var row = (await Body(await admin.GetAsync($"/api/v1/notifications/alerts?q={tag}"))).GetProperty("items")[0];

        Assert.Equal("outOfStock", row.GetProperty("kind").GetString());
        Assert.Equal(7, row.GetProperty("productId").GetInt32());
        Assert.Equal("Bread", row.GetProperty("params").GetProperty("productName").GetString());
    }

    [Fact]
    public async Task An_alert_with_broken_metadata_still_lists_without_a_product()
    {
        var admin = await Admin();
        var tag = Unique("Broken");
        await SeedAlertAsync(tag, AlertType.SystemError, metadata: "{not json");

        var row = (await Body(await admin.GetAsync($"/api/v1/notifications/alerts?q={tag}"))).GetProperty("items")[0];

        Assert.Equal(tag, row.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("productId").ValueKind);
        Assert.Empty(row.GetProperty("params").EnumerateObject());
    }

    [Fact]
    public async Task Dismissing_an_alert_takes_it_off_the_bell_and_the_open_list()
    {
        var admin = await Admin();
        var tag = Unique("Dis");
        var id = await SeedAlertAsync(tag);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Post, $"/api/v1/notifications/alerts/{id}/dismiss"))).StatusCode);

        var row = await ReadAsync(db => db.Alerts.Where(a => a.Id == id).Select(a => new { a.Status, a.ResolvedAt }).SingleAsync());
        Assert.Equal(AlertStatus.Dismissed, row.Status);
        Assert.NotNull(row.ResolvedAt);
        Assert.Empty((await Body(await admin.GetAsync($"/api/v1/notifications/alerts?q={tag}"))).GetProperty("items").EnumerateArray());
        Assert.DoesNotContain(Titles(await Body(await admin.GetAsync("/api/v1/notifications/feed?take=50"))), t => t == tag);
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/notifications/alerts/99999999/dismiss")), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Dismiss_all_dismisses_active_and_acknowledged_alerts_and_leaves_closed_ones_alone()
    {
        var admin = await Admin();
        var active = await SeedAlertAsync(Unique("DAActive"));
        var acked = await SeedAlertAsync(Unique("DAAcked"), status: AlertStatus.Acknowledged);
        var resolved = await SeedAlertAsync(Unique("DARes"), status: AlertStatus.Resolved, resolvedAt: DateTime.UtcNow.AddDays(-1));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Post, "/api/v1/notifications/alerts/dismiss-all"))).StatusCode);

        Assert.Equal(AlertStatus.Dismissed, await ReadAsync(db => db.Alerts.Where(a => a.Id == active).Select(a => a.Status).SingleAsync()));
        Assert.Equal(AlertStatus.Dismissed, await ReadAsync(db => db.Alerts.Where(a => a.Id == acked).Select(a => a.Status).SingleAsync()));
        Assert.Equal(AlertStatus.Resolved, await ReadAsync(db => db.Alerts.Where(a => a.Id == resolved).Select(a => a.Status).SingleAsync()));
        Assert.Equal(0, (await Body(await admin.GetAsync("/api/v1/notifications/summary"))).GetProperty("openAlerts").GetInt32());
    }

    [Fact]
    public async Task Deleting_an_alert_removes_it_and_an_unknown_one_is_404()
    {
        var admin = await Admin();
        var id = await SeedAlertAsync(Unique("DelAlert"));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/notifications/alerts/{id}"))).StatusCode);

        Assert.False(await ReadAsync(db => db.Alerts.AnyAsync(a => a.Id == id)));
        await AssertProblem(await admin.SendAsync(Json(HttpMethod.Delete, $"/api/v1/notifications/alerts/{id}")), HttpStatusCode.NotFound, "not_found");
    }
}
