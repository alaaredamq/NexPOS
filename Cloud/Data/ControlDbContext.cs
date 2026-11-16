using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cloud.Data;

/// <summary>
/// The control plane's own database: who the clients are, what they have paid for, which tills may connect.
/// It holds NO shop data; that lives in one mirror file per site (see MirrorStore).
/// </summary>
public class ControlDbContext : DbContext
{
    public ControlDbContext(DbContextOptions<ControlDbContext> options) : base(options) { }

    /// <summary>For provider-specific subclasses (SqlServerControlDbContext), which carry their own migration set.</summary>
    protected ControlDbContext(DbContextOptions options) : base(options) { }

    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<SyncBatchLog> SyncBatchLogs => Set<SyncBatchLog>();
    public DbSet<ClientEvent> ClientEvents => Set<ClientEvent>();
    public DbSet<EnrollmentCode> EnrollmentCodes => Set<EnrollmentCode>();
    public DbSet<SiteHeartbeat> SiteHeartbeats => Set<SiteHeartbeat>();
    public DbSet<OwnerUser> OwnerUsers => Set<OwnerUser>();
    public DbSet<OwnerSession> OwnerSessions => Set<OwnerSession>();
    public DbSet<SiteCommand> SiteCommands => Set<SiteCommand>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<MenuSection> MenuSections => Set<MenuSection>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();

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
            e.Property(x => x.ShopPlan).HasMaxLength(8);
            e.Property(x => x.ShopLicenseKind).HasMaxLength(12);
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

        b.Entity<OwnerUser>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(255);
            e.Property(x => x.DisplayName).HasMaxLength(100);
            // Logins made before roles existed (this default fills their new column) keep full access.
            e.Property(x => x.Permissions).HasDefaultValue((int)OwnerPermissions.All);
            e.Ignore(x => x.Can);
            e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<OwnerSession>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.OwnerUser).WithMany().HasForeignKey(x => x.OwnerUserId);
        });

        b.Entity<SiteCommand>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Type).HasMaxLength(60);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.RequestedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.SiteId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.SiteId, x.Status, x.CreatedAt });
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId);
        });

        b.Entity<Menu>(e =>
        {
            e.HasIndex(x => x.ClientId).IsUnique();
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.LinkedSiteId);
            e.Property(x => x.Slug).HasMaxLength(60);
            e.Property(x => x.Title).HasMaxLength(120);
            e.Property(x => x.Tagline).HasMaxLength(200);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.Property(x => x.Language).HasMaxLength(2);
            e.Property(x => x.Currency).HasMaxLength(10);
            e.Property(x => x.LogoFile).HasMaxLength(60);
            e.Property(x => x.CatalogHash).HasMaxLength(64);
            e.Property(x => x.PreviewKey).HasMaxLength(32);
            e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Sections).WithOne(x => x.Menu).HasForeignKey(x => x.MenuId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Items).WithOne(x => x.Menu).HasForeignKey(x => x.MenuId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MenuSection>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.PosKey).HasMaxLength(20);
            e.HasIndex(x => new { x.MenuId, x.SortOrder });
        });

        b.Entity<MenuItem>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.PosDescription).HasMaxLength(500);
            e.Property(x => x.Currency).HasMaxLength(10);
            e.Property(x => x.ImageFile).HasMaxLength(60);
            e.Property(x => x.Price).HasPrecision(18, 4);
            e.HasIndex(x => new { x.MenuId, x.SortOrder });
            e.HasIndex(x => new { x.MenuId, x.PosProductId });
        });
    }
}

/// <summary>
/// The same model on SQL Server. A separate type only so it has its own migrations (Data/MigrationsSqlServer): EF picks
/// migrations by the context's runtime type, and SQLite and SQL Server need different SQL. Nothing else refers to it;
/// the app asks for <see cref="ControlDbContext"/> and gets whichever one is configured.
/// </summary>
public sealed class SqlServerControlDbContext(DbContextOptions<SqlServerControlDbContext> options) : ControlDbContext(options);

/// <summary>Lets `dotnet ef migrations add` run without starting the web host.</summary>
public sealed class SqlServerControlDbContextFactory : IDesignTimeDbContextFactory<SqlServerControlDbContext>
{
    public SqlServerControlDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<SqlServerControlDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=NexPosCloudDesignTime;Trusted_Connection=True").Options);
}

/// <summary>Lets `dotnet ef migrations add` run without starting the web host.</summary>
public sealed class ControlDbContextFactory : IDesignTimeDbContextFactory<ControlDbContext>
{
    public ControlDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<ControlDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
