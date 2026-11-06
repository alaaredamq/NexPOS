using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cloud.Data;

/// <summary>
/// The control plane's own database: who the clients are, what they have paid for, which tills may connect.
/// It holds NO shop data; that lives in one mirror file per site (see MirrorStore).
/// </summary>
public class ControlDbContext(DbContextOptions<ControlDbContext> options) : DbContext(options)
{
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<SyncBatchLog> SyncBatchLogs => Set<SyncBatchLog>();
    public DbSet<ClientEvent> ClientEvents => Set<ClientEvent>();
    public DbSet<EnrollmentCode> EnrollmentCodes => Set<EnrollmentCode>();
    public DbSet<SiteHeartbeat> SiteHeartbeats => Set<SiteHeartbeat>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AdminUser>(e =>
        {
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(100);
        });

        b.Entity<Client>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => x.Name);
            e.HasOne(x => x.Subscription).WithOne(x => x.Client).HasForeignKey<Subscription>(x => x.ClientId);
            e.HasMany(x => x.Sites).WithOne(x => x.Client).HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.ClientId);
        });

        b.Entity<Site>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasMany(x => x.Tokens).WithOne(x => x.Site).HasForeignKey(x => x.SiteId);
        });

        b.Entity<DeviceToken>(e => e.HasIndex(x => x.TokenHash).IsUnique());

        b.Entity<EnrollmentCode>(e =>
        {
            e.HasIndex(x => x.CodeHash).IsUnique();
            e.HasOne(x => x.Site).WithMany(x => x.EnrollmentCodes).HasForeignKey(x => x.SiteId);
        });

        b.Entity<SyncBatchLog>(e => e.HasIndex(x => new { x.SiteId, x.Id }));

        b.Entity<SiteHeartbeat>(e =>
        {
            e.HasIndex(x => new { x.SiteId, x.Hour }).IsUnique();
            e.HasOne(x => x.Site).WithMany(x => x.Heartbeats).HasForeignKey(x => x.SiteId);
        });
    }
}

/// <summary>Lets `dotnet ef migrations add` run without starting the web host.</summary>
public sealed class ControlDbContextFactory : IDesignTimeDbContextFactory<ControlDbContext>
{
    public ControlDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<ControlDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
