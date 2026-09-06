using Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Tests.TestHelpers;

/// <summary>
/// Creates a real (SQLite, in-memory) ErpDbContext per test so repository/service code under
/// test runs against actual translated SQL instead of a fake DbSet - needed for anything using
/// ExecuteUpdateAsync (not supported by EF's InMemory provider) and for OnModelCreating's
/// HasData seed to apply the same way it does in production.
/// A SQLite ":memory:" database only exists while its connection is open, so the connection
/// must be disposed by the caller (this class implements IDisposable for that).
/// </summary>
public sealed class SqliteInMemoryDbContextFactory : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteInMemoryDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public ErpDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new ErpDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
