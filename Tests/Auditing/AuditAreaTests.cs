using Core.Auditing;
using Core.Entities.Logging;
using Infrastructure.Auditing;
using Infrastructure.Repositories.Logging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.Auditing;

/// <summary>The viewer's "section" filter: which part of the product an entry belongs to.</summary>
public sealed class AuditAreaTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AuditDbContext _db;
    private readonly AuditLogRepository _repository;

    public AuditAreaTests()
    {
        _connection.Open();
        _db = new AuditDbContext(new DbContextOptionsBuilder<AuditDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _repository = new AuditLogRepository(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    public static TheoryData<string, string, string?, AuditArea> Samples => new()
    {
        // Data changes by record type
        { "Create", "Receipt", null, AuditArea.Sales },
        { "Update", "Order", null, AuditArea.Sales },
        { "Update", "Discount", null, AuditArea.Sales },
        { "Create", "CartSession", null, AuditArea.Pos },
        { "Update", "CashDrawerSession", null, AuditArea.Pos },
        { "Update", "Product", null, AuditArea.Inventory },
        { "Create", "Supplier", null, AuditArea.Inventory },
        { "Update", "Customer", null, AuditArea.Customers },
        { "Update", "User", null, AuditArea.Account },
        { "Update", "RolePermission", null, AuditArea.Account },
        { "Update", "StoreSettings", null, AuditArea.Settings },
        { "Create", "BackupRecord", null, AuditArea.System },

        // Business, security and system events by action
        { AuditActions.SaleCompleted, "Receipt", null, AuditArea.Sales },
        { AuditActions.ReceiptVoided, "Receipt", null, AuditArea.Sales },
        { AuditActions.StockAdjusted, "Product", null, AuditArea.Inventory },
        { AuditActions.Login, AuditActions.AuthEntity, null, AuditArea.Account },
        { AuditActions.LoginFailed, AuditActions.AuthEntity, null, AuditArea.Account },
        { AuditActions.AccessDenied, "Http", "/api/v1/products", AuditArea.Account },
        { AuditActions.PdfGenerated, "PrintJob", null, AuditArea.Reports },
        { AuditActions.PrintFailed, "PrintJob", null, AuditArea.Reports },
        { AuditActions.BackupCreated, "Backup", null, AuditArea.System },
        { AuditActions.AppStarted, "Application", null, AuditArea.System },

        // The action wins over the record type: a sale refused by the licence is still a sales event
        { AuditActions.SaleFailed, "License", null, AuditArea.Sales },

        // Requests by path
        { "POST /api/v1/pos/sales", "Http", "/api/v1/pos/sales", AuditArea.Pos },
        { "GET /api/v1/orders", "Http", "/api/v1/orders/12", AuditArea.Sales },
        { "GET Products/Index", "Http", "/en/Products/Index", AuditArea.Inventory },
        { "GET Products/Index", "Http", "/ar/products", AuditArea.Inventory },
        { "PUT /api/v1/customers/4", "Http", "/api/v1/customers/4", AuditArea.Customers },
        { "POST /api/v1/auth/login", "Http", "/api/v1/auth/login", AuditArea.Account },
        { "GET /api/v1/reports/sales", "Http", "/api/v1/reports/sales", AuditArea.Reports },
        { "GET /api/v1/dashboard", "Http", "/api/v1/dashboard", AuditArea.Reports },
        { "GET /api/v1/backups", "Http", "/api/v1/backups", AuditArea.System },
        { "GET /api/v1/settings/store", "Http", "/api/v1/settings/store", AuditArea.Settings },

        // Nothing claims these
        { "GET /app", "Http", "/app", AuditArea.Other },
        { "GET /", "Http", "/", AuditArea.Other },
        { AuditActions.UnhandledException, "Http", "/something-new", AuditArea.Other },
        { AuditActions.Error, "Elsewhere", null, AuditArea.Other }
    };

    private static List<AuditLog> SampleRows() => Samples.Select(s => new AuditLog
    {
        Action = (string)s[0]!, EntityType = (string)s[1]!, RequestPath = (string?)s[2], Description = "sample",
        IpAddress = "127.0.0.1", UserAgent = "test", CreatedAt = DateTime.UtcNow
    }).ToList();

    [Theory]
    [MemberData(nameof(Samples))]
    public void An_entry_is_labelled_with_its_area(string action, string entityType, string? path, AuditArea expected)
        => Assert.Equal(expected, AuditAreas.Classify(action, entityType, path));

    [Fact]
    public async Task Filtering_by_an_area_returns_exactly_the_entries_labelled_with_it()
    {
        // Every sample goes in as a row; for each area the database must return the rows Classify puts there.
        var rows = SampleRows();
        _db.AuditLogs.AddRange(rows);
        await _db.SaveChangesAsync();

        foreach (var area in AuditAreas.All)
        {
            var expected = rows.Where(r => AuditAreas.Classify(r.Action, r.EntityType, r.RequestPath) == area).Select(r => r.Id).Order().ToList();
            var found = (await _repository.SearchAsync(new AuditLogFilter { Area = area }, 0, 500)).Items.Select(r => r.Id).Order().ToList();

            Assert.Equal(expected, found);
            Assert.NotEmpty(found); // the samples cover every area, so an empty answer means the filter is broken
        }
    }

    [Fact]
    public async Task Every_entry_is_in_exactly_one_area()
    {
        var rows = SampleRows();
        _db.AuditLogs.AddRange(rows);
        await _db.SaveChangesAsync();

        var seen = new List<int>();
        foreach (var area in AuditAreas.All)
            seen.AddRange((await _repository.SearchAsync(new AuditLogFilter { Area = area }, 0, 500)).Items.Select(r => r.Id));

        Assert.Equal(rows.Count, seen.Count);
        Assert.Equal(rows.Count, seen.Distinct().Count());
    }

    [Fact]
    public async Task An_area_combines_with_the_other_filters()
    {
        _db.AuditLogs.AddRange(
            new AuditLog { Action = "Update", EntityType = "Product", Outcome = AuditOutcome.Success, Description = "a", IpAddress = "x", UserAgent = "x", CreatedAt = DateTime.UtcNow },
            new AuditLog { Action = "Update", EntityType = "Product", Outcome = AuditOutcome.Failure, Description = "b", IpAddress = "x", UserAgent = "x", CreatedAt = DateTime.UtcNow },
            new AuditLog { Action = "Update", EntityType = "Customer", Outcome = AuditOutcome.Failure, Description = "c", IpAddress = "x", UserAgent = "x", CreatedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var found = (await _repository.SearchAsync(new AuditLogFilter { Area = AuditArea.Inventory, Outcome = AuditOutcome.Failure }, 0, 50)).Items;

        Assert.Equal("b", Assert.Single(found).Description);
    }
}
