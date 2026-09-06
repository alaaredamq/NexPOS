using Core.Entities.Configuration;
using Core.IRepositories.Base;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Core.IRepositories.Configuration;

public interface IUrlRouteRepository : IBaseRepository<UrlRoute, int>
{
    Task<IEnumerable<UrlRoute>> GetActiveRoutesAsync();
    Task<IEnumerable<UrlRoute>> GetRoutesByLanguageAsync(string language);
    Task<UrlRoute?> GetRouteByKeyAsync(string key);
    Task<UrlRoute?> GetRouteByPatternAsync(string pattern);
    Task<IEnumerable<UrlRoute>> GetRoutesByControllerAsync(string controller);
    Task<IEnumerable<UrlRoute>> GetPublicRoutesAsync(); // Routes that don't require authentication
    Task<IEnumerable<UrlRoute>> GetAuthenticatedRoutesAsync(); // Routes that require authentication
}
