using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Data;

/// <summary>
/// Lets `dotnet ef` tooling construct <see cref="ErpDbContext"/> directly, without building the
/// full Web host. Without this, EF design-time tooling falls back to running Web/Program.cs's
/// top-level statements end to end - including its JWT secret fail-fast check, rate limiting
/// setup, and every other unrelated startup concern - just to locate the DbContext. That's
/// fragile (e.g. a missing Jwt:SecretKey blocks `migrations add` even though generating a
/// migration has nothing to do with authentication) and unnecessary: this factory only needs a
/// connection string.
/// </summary>
public class ErpDbContextFactory : IDesignTimeDbContextFactory<ErpDbContext>
{
    public ErpDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "..", "Web"))
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<ErpDbContext>();
        optionsBuilder.UseSqlServer(configuration.GetConnectionString("DefaultConnection"));

        return new ErpDbContext(optionsBuilder.Options);
    }
}
