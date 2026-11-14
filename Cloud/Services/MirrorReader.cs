using Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cloud.Services;

/// <summary>
/// Opens one client's cloud copy of the shop for reading, with the same model the till uses. The copy is opened read-only, so a
/// bug in a read endpoint cannot change shop data, and the file is chosen from the signed-in owner's own site, never from anything
/// the phone sends. Dispose the context when the request is done.
/// </summary>
public sealed class MirrorReader(MirrorStore mirrors)
{
    public ErpDbContext Open(Guid siteId)
    {
        mirrors.EnsureReady(siteId); // a shop that has not synced yet reads as empty rather than failing
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = mirrors.PathFor(siteId),
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();
        return new ErpDbContext(new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlite(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
    }
}
