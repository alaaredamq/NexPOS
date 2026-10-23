using System.Text;
using Application.Services.Logging;
using Core.Auditing;
using Core.Entities.Logging;
using Core.IRepositories.Logging;
using Infrastructure.Auditing;
using Infrastructure.Repositories.Logging;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace Tests.Auditing;

/// <summary>The viewer's side: filtering, paging, stats, details and CSV export.</summary>
public sealed class AuditLogQueryTests : IDisposable
{
    private static readonly DateTime Noon = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AuditDbContext _db;
    private readonly AuditLogRepository _repository;

    public AuditLogQueryTests()
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

    private AuditLog Add(
        string action, AuditCategory category = AuditCategory.Data, string? user = "alice", int? userId = 7,
        DateTime? at = null, string entityType = "Product", int? entityId = null, string? description = null,
        AuditOutcome outcome = AuditOutcome.Success, AuditLogSeverity severity = AuditLogSeverity.Info,
        string? correlation = null)
    {
        var row = new AuditLog
        {
            Action = action, Category = category, Username = user, UserId = userId, CreatedAt = at ?? Noon,
            EntityType = entityType, EntityId = entityId, Description = description ?? action,
            Outcome = outcome, Severity = severity, CorrelationId = correlation,
            IpAddress = "127.0.0.1", UserAgent = "test"
        };
        _db.AuditLogs.Add(row);
        _db.SaveChanges();
        return row;
    }

    private async Task<List<AuditLog>> Find(AuditLogFilter filter)
        => (await _repository.SearchAsync(filter, 0, 100)).Items;

    [Fact]
    public async Task Search_NewestFirst_ThenOldestFirstOnRequest()
    {
        Add("old", at: Noon.AddHours(-2));
        Add("new", at: Noon);
        Add("mid", at: Noon.AddHours(-1));

        Assert.Equal(new[] { "new", "mid", "old" }, (await Find(new())).Select(r => r.Action));
        Assert.Equal(new[] { "old", "mid", "new" }, (await Find(new() { Ascending = true })).Select(r => r.Action));
    }

    [Fact]
    public async Task Search_PagesAndReportsTheTotalBeforePaging()
    {
        for (var i = 0; i < 25; i++) Add($"a{i:D2}", at: Noon.AddMinutes(i));

        var (items, total) = await _repository.SearchAsync(new AuditLogFilter(), skip: 10, take: 10);

        Assert.Equal(25, total);
        Assert.Equal(10, items.Count);
        Assert.Equal("a14", items[0].Action); // newest first: a24..a15 on page 1, a14..a05 on page 2
    }

    [Fact]
    public async Task Filters_CombineWithAnd()
    {
        Add("Create", user: "alice", userId: 7, entityType: "Product", entityId: 5);
        Add("Update", user: "alice", userId: 7, entityType: "Product", entityId: 6);
        Add("Update", user: "bob", userId: 8, entityType: "Customer", entityId: 5);
        Add("POST POS/Checkout", AuditCategory.Request, user: "bob", userId: 8, entityType: "Http");

        Assert.Equal(2, (await Find(new() { UserId = 7 })).Count);
        Assert.Equal(2, (await Find(new() { Username = "BOB" })).Count);            // case-insensitive
        Assert.Single(await Find(new() { Category = AuditCategory.Request }));
        Assert.Equal(2, (await Find(new() { EntityType = "product" })).Count);      // case-insensitive
        Assert.Equal(2, (await Find(new() { EntityId = 5 })).Count);
        Assert.Single(await Find(new() { EntityType = "Product", EntityId = 5 }));
        Assert.Equal(2, (await Find(new() { Action = "update" })).Count);
        Assert.Single(await Find(new() { UserId = 7, Action = "Update" }));
    }

    [Fact]
    public async Task DateRange_IsInclusiveAtTheStart_AndExclusiveAtTheEnd()
    {
        Add("before", at: Noon.AddMinutes(-1));
        Add("start", at: Noon);
        Add("inside", at: Noon.AddMinutes(30));
        Add("end", at: Noon.AddHours(1));

        var rows = await Find(new() { FromUtc = Noon, ToUtc = Noon.AddHours(1), Ascending = true });

        Assert.Equal(new[] { "start", "inside" }, rows.Select(r => r.Action));
    }

    [Fact]
    public async Task MinSeverity_MeansThatLevelOrWorse()
    {
        Add("i", severity: AuditLogSeverity.Info);
        Add("w", severity: AuditLogSeverity.Warning);
        Add("e", severity: AuditLogSeverity.Error);
        Add("c", severity: AuditLogSeverity.Critical);

        Assert.Equal(new[] { "c", "e", "w" }.Order(),
            (await Find(new() { MinSeverity = AuditLogSeverity.Warning })).Select(r => r.Action).Order());
    }

    [Fact]
    public async Task FreeTextSearch_MatchesManyColumns_AndTreatsWildcardsLiterally()
    {
        Add("Update", description: "Stock 100% reserved", entityType: "Product");
        Add("Update", description: "Stock 1000 reserved", entityType: "Product");
        Add("Login", AuditCategory.Security, user: "carol", description: "signed in");
        Add("Update", description: "price_old changed", entityType: "Product");
        Add("Update", description: "priceXold changed", entityType: "Product");

        Assert.Single(await Find(new() { Search = "100%" }));           // % is not a wildcard
        Assert.Single(await Find(new() { Search = "price_old" }));      // neither is _
        Assert.Single(await Find(new() { Search = "CAROL" }));          // user name, any case
        Assert.Single(await Find(new() { Search = "signed" }));
    }

    [Fact]
    public async Task CorrelationId_GroupsOneRequest()
    {
        Add("POST Products/Edit", AuditCategory.Request, correlation: "REQ-A");
        Add("Update", correlation: "REQ-A");
        Add("Update", correlation: "REQ-B");

        Assert.Equal(2, (await Find(new() { CorrelationId = "REQ-A" })).Count);
    }

    [Fact]
    public async Task Stats_CountTheLast24Hours()
    {
        Add("old", at: DateTime.UtcNow.AddDays(-3));
        Add("fresh", at: DateTime.UtcNow.AddHours(-1));
        Add("denied", AuditCategory.Security, at: DateTime.UtcNow.AddHours(-2), outcome: AuditOutcome.Denied);
        Add("failed", at: DateTime.UtcNow.AddMinutes(-5), outcome: AuditOutcome.Failure);

        var stats = await _repository.GetStatsAsync(DateTime.UtcNow);

        Assert.Equal(4, stats.Total);
        Assert.Equal(3, stats.Last24Hours);
        Assert.Equal(2, stats.Failures24Hours);
        Assert.Equal(1, stats.Security24Hours);
        Assert.NotNull(stats.OldestUtc);
    }

    [Fact]
    public async Task FilterOptions_ListDistinctUsersEntityTypesAndNonRequestActions()
    {
        Add("Create", user: "alice", userId: 7, entityType: "Product");
        Add("Create", user: "Alice", userId: 7, entityType: "Product");
        Add("Delete", user: "bob", userId: 8, entityType: "Customer");
        Add("GET Products/Index", AuditCategory.Request, user: "bob", userId: 8, entityType: "Http");

        var options = await _repository.GetFilterOptionsAsync();

        Assert.Equal(2, options.Users.Count);
        Assert.Equal(new[] { "Customer", "Http", "Product" }, options.EntityTypes);
        Assert.Equal(new[] { "Create", "Delete" }, options.Actions); // route actions are left to the free-text search
    }

    [Fact]
    public async Task Purge_RemovesOnlyOlderEntries()
    {
        Add("ancient", at: Noon.AddDays(-400));
        Add("recent", at: Noon);

        var removed = await _repository.PurgeOlderThanAsync(Noon.AddDays(-365));

        Assert.Equal(1, removed);
        Assert.Equal("recent", Assert.Single(await Find(new())).Action);
    }

    // ---- details & CSV ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Details_BuildOldToNewPerField_AndCountTheRequestsRows()
    {
        var update = Add("Update", correlation: "REQ-9", entityId: 3);
        update.OldValuesJson = """{"Name":"Milk","StockQuantity":40}""";
        update.NewValuesJson = """{"Name":"Milk","StockQuantity":39}""";
        _db.SaveChanges();
        Add("POST POS/Checkout", AuditCategory.Request, correlation: "REQ-9");

        var service = new AuditLogService(_repository, new MemoryCache(new MemoryCacheOptions()));
        var details = await service.GetDetailsAsync(update.Id);

        Assert.NotNull(details);
        Assert.Equal(2, details!.RowsInSameRequest);
        var stock = Assert.Single(details.Changes, c => c.Field == "StockQuantity");
        Assert.Equal("40", stock.Old);
        Assert.Equal("39", stock.New);
    }

    [Fact]
    public async Task Details_OfACreate_HaveOnlyNewValues()
    {
        var create = Add("Create");
        create.NewValuesJson = """{"Name":"Milk"}""";
        _db.SaveChanges();

        var details = await new AuditLogService(_repository, new MemoryCache(new MemoryCacheOptions())).GetDetailsAsync(create.Id);

        var name = Assert.Single(details!.Changes);
        Assert.Null(name.Old);
        Assert.Equal("Milk", name.New);
    }

    [Fact]
    public async Task Details_OfAnUnknownId_AreNull()
    {
        var service = new AuditLogService(Mock.Of<IAuditLogRepository>(), new MemoryCache(new MemoryCacheOptions()));
        Assert.Null(await service.GetDetailsAsync(12345));
    }

    [Fact]
    public async Task Csv_HasABom_AHeader_AndDefusesSpreadsheetFormulas()
    {
        Add("Create", description: "=HYPERLINK(\"http://evil\",\"click\")", user: "al,ice");
        Add("Update", description: "plain, with comma and \"quotes\"");
        Add("Update", description: "+1 stock");

        var service = new AuditLogService(_repository, new MemoryCache(new MemoryCacheOptions()));
        using var stream = new MemoryStream();
        var count = await service.ExportCsvAsync(new AuditLogFilter { Ascending = true }, stream);

        Assert.Equal(3, count);
        var bytes = stream.ToArray();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]); // UTF-8 BOM so Excel reads Arabic correctly
        var text = Encoding.UTF8.GetString(bytes);

        Assert.StartsWith("﻿Id,TimeUtc,TimeLocal,User,", text);
        Assert.DoesNotContain(",=HYPERLINK", text);                     // never a live formula...
        Assert.Contains("'=HYPERLINK", text);                           // ...it is neutralised with a leading quote
        Assert.Contains("'+1 stock", text);
        Assert.Contains("\"al,ice\"", text);                            // commas are quoted
        Assert.Contains("\"plain, with comma and \"\"quotes\"\"\"", text);
    }
}
