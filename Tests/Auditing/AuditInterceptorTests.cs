using System.Text.Json.Nodes;
using Core.Auditing;
using Core.Entities.Configuration;
using Core.Entities.Logging;
using Core.Entities.Products;
using Core.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.Auditing;

/// <summary>
/// The data-change audit runs inside EF Core, so these tests drive the real pipeline (SQLite in-memory)
/// and read back what the interceptors handed to the trail.
/// </summary>
public class AuditInterceptorTests : IDisposable
{
    private readonly AuditedDb _db = new();

    public void Dispose() => _db.Dispose();

    private static JsonNode Json(string? json) => JsonNode.Parse(json!)!;

    // ---- create / update / delete -------------------------------------------------------------------------------

    [Fact]
    public async Task Create_IsRecordedOnce_WithTheGeneratedIdAndLabel()
    {
        await using var db = _db.CreateContext();
        var category = new ProductCategory("Beverages", "Cold drinks");

        db.ProductCategories.Add(category);
        await db.SaveChangesAsync();

        var row = Assert.Single(_db.Trail.Entries); // and no extra "bulk insert" row for EF's own statements
        Assert.Equal(AuditCategory.Data, row.Category);
        Assert.Equal(AuditActions.Create, row.Action);
        Assert.Equal("ProductCategory", row.EntityType);
        Assert.NotEqual(0, category.Id);
        Assert.Equal(category.Id, row.EntityId); // the id only exists after the INSERT
        Assert.Equal("Beverages", row.EntityLabel);
        Assert.Equal("Beverages", (string?)Json(row.NewValuesJson)["Name"]);
        Assert.Null(row.OldValuesJson);
        Assert.Contains("Created ProductCategory 'Beverages'", row.Description);
    }

    [Fact]
    public async Task Update_RecordsOnlyWhatChanged_WithOldAndNewValues()
    {
        await using var db = _db.CreateContext();
        var category = new ProductCategory("Beverages", "Cold drinks");
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync();
        _db.Trail.Clear();

        category.Name = "Drinks";
        await db.SaveChangesAsync();

        var row = Assert.Single(_db.Trail.Entries);
        Assert.Equal(AuditActions.Update, row.Action);
        Assert.Equal(category.Id, row.EntityId);
        Assert.Equal("Name", row.ChangedFields);
        Assert.Equal("Beverages", (string?)Json(row.OldValuesJson)["Name"]);
        Assert.Equal("Drinks", (string?)Json(row.NewValuesJson)["Name"]);
        Assert.Null(Json(row.NewValuesJson)["Description"]); // untouched columns are not repeated
        Assert.Contains("Name Beverages → Drinks", row.Description);
    }

    [Fact]
    public async Task Update_ThroughUpdateCallOnTrackedEntity_StillReportsOnlyRealChanges()
    {
        // BaseRepository.SaveAsync calls DbSet.Update(), which flags EVERY column as modified.
        await using var db = _db.CreateContext();
        var category = new ProductCategory("Beverages", "Cold drinks");
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync();
        _db.Trail.Clear();

        category.IsActive = false;
        db.ProductCategories.Update(category);
        await db.SaveChangesAsync();

        var row = Assert.Single(_db.Trail.Entries);
        Assert.Equal("IsActive", row.ChangedFields);
    }

    [Fact]
    public async Task Update_OfADetachedEntity_ReadsTheRealOldValuesFromTheDatabase()
    {
        int id;
        DateTime createdAt;
        await using (var first = _db.CreateContext())
        {
            var category = new ProductCategory("Beverages", "Cold drinks");
            first.ProductCategories.Add(category);
            await first.SaveChangesAsync();
            id = category.Id;
            createdAt = category.CreatedAt;
        }

        _db.Trail.Clear();

        // A different context that never loaded the row: original == current for every column.
        // (CreatedAt is carried over; a freshly constructed entity would otherwise stamp "now" - and the trail
        // would, rightly, report that CreatedAt got overwritten.)
        await using var second = _db.CreateContext();
        second.ProductCategories.Update(
            new ProductCategory("Drinks", "Cold drinks") { Id = id, IsActive = true, CreatedAt = createdAt });
        await second.SaveChangesAsync();

        var row = Assert.Single(_db.Trail.Entries);
        Assert.Equal("Name", row.ChangedFields);
        Assert.Equal("Beverages", (string?)Json(row.OldValuesJson)["Name"]);
        Assert.Equal("Drinks", (string?)Json(row.NewValuesJson)["Name"]);
    }

    [Fact]
    public async Task Update_ThatChangesNothing_RecordsNothing()
    {
        await using var db = _db.CreateContext();
        var category = new ProductCategory("Beverages", "Cold drinks");
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync();
        _db.Trail.Clear();

        db.ProductCategories.Update(category); // marks everything modified, but nothing differs
        await db.SaveChangesAsync();

        Assert.Empty(_db.Trail.Entries);
    }

    [Fact]
    public async Task Delete_RecordsTheValuesThatWereRemoved()
    {
        await using var db = _db.CreateContext();
        var category = new ProductCategory("Beverages", "Cold drinks");
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync();
        var id = category.Id;
        _db.Trail.Clear();

        db.ProductCategories.Remove(category);
        await db.SaveChangesAsync();

        var row = Assert.Single(_db.Trail.Entries);
        Assert.Equal(AuditActions.Delete, row.Action);
        Assert.Equal(id, row.EntityId);
        Assert.Equal("Beverages", row.EntityLabel);
        Assert.Equal("Cold drinks", (string?)Json(row.OldValuesJson)["Description"]);
        Assert.Null(row.NewValuesJson);
    }

    [Fact]
    public async Task SoftDelete_IsClassifiedAsSuchAndRestoreToo()
    {
        await using var db = _db.CreateContext();
        var role = new Role("Auditor", "Reads logs");
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        _db.Trail.Clear();
        role.IsDeleted = true;
        await db.SaveChangesAsync();
        Assert.Equal(AuditActions.SoftDelete, Assert.Single(_db.Trail.Entries).Action);

        _db.Trail.Clear();
        role.IsDeleted = false;
        await db.SaveChangesAsync();
        Assert.Equal(AuditActions.Restore, Assert.Single(_db.Trail.Entries).Action);
    }

    [Fact]
    public async Task SeveralChangesInOneSave_ProduceOneRowEach()
    {
        await using var db = _db.CreateContext();
        db.ProductCategories.AddRange(new ProductCategory("A"), new ProductCategory("B"), new ProductCategory("C"));
        await db.SaveChangesAsync();

        Assert.Equal(3, _db.Trail.Entries.Count);
        Assert.Equal(new[] { "A", "B", "C" }, _db.Trail.Entries.Select(e => e.EntityLabel).Order());
        Assert.Equal(3, _db.Trail.Entries.Select(e => e.EntityId).Distinct().Count());
    }

    // ---- secrets --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Users_PinAndPasswordHashes_NeverReachTheTrail()
    {
        await using var db = _db.CreateContext();
        var user = new User("cashier1", "c1@example.com", "BCRYPT-PASSWORD-HASH", roleId: 4, "Cara", "One")
        {
            PinHash = "BCRYPT-PIN-HASH"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var created = Assert.Single(_db.Trail.Entries);
        Assert.DoesNotContain("BCRYPT-PASSWORD-HASH", created.NewValuesJson);
        Assert.DoesNotContain("BCRYPT-PIN-HASH", created.NewValuesJson);
        Assert.Equal(AuditRedactor.Redacted, (string?)Json(created.NewValuesJson)["PinHash"]);
        Assert.Equal("cashier1", created.EntityLabel);
        Assert.Equal("cashier1", (string?)Json(created.NewValuesJson)["Username"]); // ordinary fields stay readable

        _db.Trail.Clear();
        user.PinHash = "ANOTHER-SECRET-HASH";
        user.MustChangePin = true;
        await db.SaveChangesAsync();

        var changed = Assert.Single(_db.Trail.Entries);
        Assert.Contains("PinHash", changed.ChangedFields); // we still learn the PIN was changed...
        Assert.DoesNotContain("ANOTHER-SECRET-HASH", changed.OldValuesJson + changed.NewValuesJson + changed.Description);
        Assert.DoesNotContain("BCRYPT-PIN-HASH", changed.OldValuesJson + changed.NewValuesJson + changed.Description);
        Assert.Equal(AuditRedactor.Redacted, (string?)Json(changed.NewValuesJson)["PinHash"]); // ...but not to what
        Assert.Equal(true, (bool?)Json(changed.NewValuesJson)["MustChangePin"]);                // flags are visible
    }

    [Fact]
    public async Task SessionTokens_AreRedacted()
    {
        await using var db = _db.CreateContext();
        var user = new User("u", "u@example.com", "h", 4);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        _db.Trail.Clear();

        db.UserSessions.Add(new UserSession(user.Id, "SESSION-TOKEN-VALUE", DateTime.UtcNow.AddHours(1),
            "127.0.0.1", "agent", "REFRESH-TOKEN-VALUE"));
        await db.SaveChangesAsync();

        var row = Assert.Single(_db.Trail.Entries);
        Assert.DoesNotContain("SESSION-TOKEN-VALUE", row.NewValuesJson);
        Assert.DoesNotContain("REFRESH-TOKEN-VALUE", row.NewValuesJson);
        Assert.Equal("127.0.0.1", (string?)Json(row.NewValuesJson)["IpAddress"]);
    }

    [Fact]
    public async Task SettingsRows_WithASecretLookingKey_OrEncryptedFlag_HideTheirValue()
    {
        await using var db = _db.CreateContext();
        db.SystemSettings.AddRange(
            new SystemSettings { SettingKey = "SmtpPassword", SettingValue = "hunter2", SettingType = "string", Category = "Mail" },
            new SystemSettings { SettingKey = "Receipt.Footer", SettingValue = "TOP-SECRET-PAYLOAD", SettingType = "string", Category = "Print", IsEncrypted = true },
            new SystemSettings { SettingKey = "Receipt.Header", SettingValue = "Welcome!", SettingType = "string", Category = "Print" });
        await db.SaveChangesAsync();

        var rows = _db.Trail.Entries.ToDictionary(e => e.EntityLabel!);
        Assert.DoesNotContain("hunter2", rows["SmtpPassword"].NewValuesJson);
        Assert.DoesNotContain("TOP-SECRET-PAYLOAD", rows["Receipt.Footer"].NewValuesJson);
        Assert.Contains("Welcome!", rows["Receipt.Header"].NewValuesJson);
    }

    // ---- transactions ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task InsideATransaction_RowsAreHeldUntilCommit()
    {
        await using var db = _db.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();

        db.ProductCategories.Add(new ProductCategory("Pending"));
        await db.SaveChangesAsync();
        Assert.Empty(_db.Trail.Entries); // not committed yet - nothing may be claimed

        await transaction.CommitAsync();
        Assert.Equal("Pending", Assert.Single(_db.Trail.Entries).EntityLabel);
    }

    [Fact]
    public async Task RolledBackTransaction_LeavesNoRowsBehind()
    {
        await using var db = _db.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();

        db.ProductCategories.Add(new ProductCategory("Never happened"));
        await db.SaveChangesAsync();
        await transaction.RollbackAsync();

        Assert.Empty(_db.Trail.Entries);
    }

    [Fact]
    public async Task TransactionAbandonedByAnException_IsNotReleasedByALaterCommit()
    {
        // The checkout's failure path: `await using var tx = ...; throw ...;` - EF reports nothing on dispose.
        await using var db = _db.CreateContext();

        await using (var abandoned = await db.Database.BeginTransactionAsync())
        {
            db.ProductCategories.Add(new ProductCategory("From the failed sale"));
            await db.SaveChangesAsync();
            // no Commit, no Rollback: leaving the block disposes (= rolls back) it
        }

        db.ChangeTracker.Clear(); // the rolled-back insert is gone; don't let its stale tracked entity collide on the reused id

        await using (var next = await db.Database.BeginTransactionAsync())
        {
            db.ProductCategories.Add(new ProductCategory("From the next sale"));
            await db.SaveChangesAsync();
            await next.CommitAsync();
        }

        var row = Assert.Single(_db.Trail.Entries);
        Assert.Equal("From the next sale", row.EntityLabel);
    }

    // ---- failures / suppression / bulk ------------------------------------------------------------------------------

    [Fact]
    public async Task FailedSave_IsRecordedWithItsReason_AndNoDataRows()
    {
        await using var db = _db.CreateContext();
        db.Users.Add(new User("dup", "a@example.com", "h", 4));
        await db.SaveChangesAsync();
        _db.Trail.Clear();

        db.Users.Add(new User("dup", "b@example.com", "h", 4)); // violates the unique username index
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        var row = Assert.Single(_db.Trail.Entries);
        Assert.Equal(AuditActions.SaveFailed, row.Action);
        Assert.Equal(AuditOutcome.Failure, row.Outcome);
        Assert.Equal(AuditLogSeverity.Error, row.Severity);
        Assert.Equal("User", row.EntityType);
        Assert.Contains("UNIQUE", row.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuppressedScope_RecordsNothing()
    {
        await using var db = _db.CreateContext();

        using (_db.Context.SuppressDataChanges("bulk seed"))
        {
            db.ProductCategories.Add(new ProductCategory("Seeded"));
            await db.SaveChangesAsync();
        }

        Assert.Empty(_db.Trail.Entries);

        db.ProductCategories.Add(new ProductCategory("Normal"));
        await db.SaveChangesAsync();
        Assert.Single(_db.Trail.Entries); // and auditing resumes afterwards
    }

    [Fact]
    public async Task ExecuteUpdate_WhichBypassesTheChangeTracker_IsStillRecorded()
    {
        await using var db = _db.CreateContext();
        var category = new ProductCategory("Beverages");
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync();
        _db.Trail.Clear();

        var affected = await db.ProductCategories
            .Where(c => c.Id == category.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Name, "Drinks").SetProperty(c => c.IsActive, false));

        Assert.Equal(1, affected);
        var row = Assert.Single(_db.Trail.Entries);
        Assert.Equal(AuditActions.BulkUpdate, row.Action);
        Assert.Equal("ProductCategory", row.EntityType);           // entity name, not the table name
        Assert.Equal(category.Id, row.EntityId);                    // recovered from the WHERE clause
        Assert.Equal(new[] { "IsActive", "Name" }, row.ChangedFields!.Split(", ").Order()); // EF decides the SET order
        Assert.Contains("1 row(s) affected", row.Description);
        Assert.Equal(1, (int?)Json(row.AdditionalInfoJson)["rowsAffected"]);
    }

    [Fact]
    public async Task ExecuteDelete_IsRecorded()
    {
        await using var db = _db.CreateContext();
        var category = new ProductCategory("Temp");
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync();
        _db.Trail.Clear();

        await db.ProductCategories.Where(c => c.Id == category.Id).ExecuteDeleteAsync();

        var row = Assert.Single(_db.Trail.Entries);
        Assert.Equal(AuditActions.BulkDelete, row.Action);
        Assert.Equal(category.Id, row.EntityId);
    }

    [Fact]
    public async Task BulkWriteInsideATransaction_FollowsTheCommit()
    {
        await using var db = _db.CreateContext();
        var category = new ProductCategory("Beverages");
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync();
        _db.Trail.Clear();

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.ProductCategories.Where(c => c.Id == category.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));
            Assert.Empty(_db.Trail.Entries);
            await tx.CommitAsync();
        }

        Assert.Single(_db.Trail.Entries);
    }

    [Fact]
    public async Task Reads_AreNeverRecorded()
    {
        await using var db = _db.CreateContext();
        db.ProductCategories.Add(new ProductCategory("Beverages"));
        await db.SaveChangesAsync();
        _db.Trail.Clear();

        _ = await db.ProductCategories.ToListAsync();
        _ = await db.Roles.CountAsync();

        Assert.Empty(_db.Trail.Entries);
    }
}
