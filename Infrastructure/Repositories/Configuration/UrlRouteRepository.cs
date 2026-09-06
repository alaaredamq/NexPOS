using Infrastructure.Data;
using Core.Entities.Configuration;
using Core.IRepositories.Configuration;
using Infrastructure.Repositories.Base;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Infrastructure.Repositories.Configuration;

public class UrlRouteRepository : BaseRepository<UrlRoute, int>, IUrlRouteRepository
{
    public UrlRouteRepository(ErpDbContext context, IServiceProvider serviceProvider) : base(context, serviceProvider)
    {
    }

    public async Task<IEnumerable<UrlRoute>> GetActiveRoutesAsync()
    {
        return await Table
            .Where(r => r.IsActive)
            .OrderBy(r => r.DisplayOrder)
            .ToListAsync();
    }

    public async Task<IEnumerable<UrlRoute>> GetRoutesByLanguageAsync(string language)
    {
        return await Table
            .Where(r => r.IsActive && r.Pattern.Contains($"{{{language}}}"))
            .OrderBy(r => r.DisplayOrder)
            .ToListAsync();
    }

    public async Task<UrlRoute?> GetRouteByKeyAsync(string key)
    {
        return await Table
            .FirstOrDefaultAsync(r => r.Key == key && r.IsActive);
    }

    public async Task<UrlRoute?> GetRouteByPatternAsync(string pattern)
    {
        return await Table
            .FirstOrDefaultAsync(r => r.Pattern == pattern && r.IsActive);
    }

    public async Task<IEnumerable<UrlRoute>> GetRoutesByControllerAsync(string controller)
    {
        return await Table
            .Where(r => r.IsActive && r.Controller == controller)
            .OrderBy(r => r.DisplayOrder)
            .ToListAsync();
    }

    public async Task<IEnumerable<UrlRoute>> GetPublicRoutesAsync()
    {
        return await Table
            .Where(r => r.IsActive && !r.RequiresAuthentication)
            .OrderBy(r => r.DisplayOrder)
            .ToListAsync();
    }

    public async Task<IEnumerable<UrlRoute>> GetAuthenticatedRoutesAsync()
    {
        return await Table
            .Where(r => r.IsActive && r.RequiresAuthentication)
            .OrderBy(r => r.DisplayOrder)
            .ToListAsync();
    }
}
