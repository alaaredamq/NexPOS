using Core.Auditing;
using Infrastructure.Auditing;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

// In the DI namespace by convention, so `services.AddAuditTrail(...)` is found without an extra using.
namespace Microsoft.Extensions.DependencyInjection;

public static class AuditServiceCollectionExtensions
{
    /// <summary>
    /// Registers the audit trail: the writer, the three EF interceptors that capture every data change, and
    /// the read-side context for the viewer. The host must supply the <see cref="IAuditContext"/> (who is acting).
    /// <para>
    /// The interceptors are attached with <c>ConfigureDbContext</c>, exactly once. EF Core 9 runs *every*
    /// registered configuration against the same options, so attaching them inside both <c>AddDbContext</c> and
    /// <c>AddDbContextFactory</c> would register each interceptor twice and double every bulk-write entry.
    /// Because that configuration applies to the factory as well, contexts it creates are audited too.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAuditTrail(
        this IServiceCollection services, string auditConnectionString, string fallbackDirectory)
    {
        services.AddSingleton(new AuditStoreOptions(auditConnectionString, fallbackDirectory));
        services.AddSingleton<AuditTrail>();
        services.AddSingleton<IAuditTrail>(sp => sp.GetRequiredService<AuditTrail>());

        services.AddSingleton<AuditChangeTracker>();
        services.AddSingleton<AuditSaveChangesInterceptor>();
        services.AddSingleton<AuditTransactionInterceptor>();
        services.AddSingleton<AuditCommandInterceptor>();

        services.ConfigureDbContext<ErpDbContext>((provider, options) => options.AddInterceptors(
            provider.GetRequiredService<AuditSaveChangesInterceptor>(),
            provider.GetRequiredService<AuditTransactionInterceptor>(),
            provider.GetRequiredService<AuditCommandInterceptor>()));

        // Read side (the viewer). Deliberately no interceptors: reading the audit log must not write to it.
        services.AddDbContext<AuditDbContext>(options => options.UseSqlite(auditConnectionString));

        return services;
    }
}
