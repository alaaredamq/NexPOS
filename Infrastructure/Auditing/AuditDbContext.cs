using Core.Entities.Logging;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Auditing;

/// <summary>
/// The audit trail's own SQLite database. Kept apart from <c>ErpDbContext</c> on purpose:
/// SQLite allows one writer at a time and a read-then-write transaction (the POS checkout) fails
/// outright if another connection writes in the middle of it - so audit writes must never share a
/// file with sales. A separate file also means restoring a business backup cannot erase the history,
/// and support can be sent one small file.
/// </summary>
public class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options)
    {
    }

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.UpdatedAt);

            // Text, not numbers: people open this file in a SQLite browser and read it.
            entity.Property(e => e.Category).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.Severity).HasConversion<string>().HasMaxLength(20).IsRequired();

            entity.Property(e => e.Action).HasMaxLength(120).IsRequired().UseCollation("NOCASE");
            entity.Property(e => e.EntityType).HasMaxLength(100).IsRequired().UseCollation("NOCASE");
            entity.Property(e => e.Username).HasMaxLength(100).UseCollation("NOCASE");
            entity.Property(e => e.UserRole).HasMaxLength(50);
            entity.Property(e => e.EntityLabel).HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.ChangedFields).HasMaxLength(1000);
            entity.Property(e => e.IpAddress).HasMaxLength(64).IsRequired();
            entity.Property(e => e.UserAgent).HasMaxLength(300).IsRequired();
            entity.Property(e => e.MachineName).HasMaxLength(100);
            entity.Property(e => e.AppVersion).HasMaxLength(60);
            entity.Property(e => e.CorrelationId).HasMaxLength(100);
            entity.Property(e => e.Source).HasMaxLength(100);
            entity.Property(e => e.HttpMethod).HasMaxLength(10);
            entity.Property(e => e.RequestPath).HasMaxLength(500);

            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
            entity.HasIndex(e => e.CorrelationId);
            entity.HasIndex(e => e.Category);
        });
    }
}
