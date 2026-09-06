using Core.Entities.Configuration;
using Application.IServices.Base;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.IServices.Configuration;

public interface IUrlRouteService : IBaseService
{
    Task<IEnumerable<UrlRoute>> GetAllAsync();
    Task<UrlRoute?> GetByIdAsync(int id);
    Task<UrlRoute> CreateAsync(UrlRoute urlRoute);
    Task<UrlRoute> UpdateAsync(UrlRoute urlRoute);
    Task DeleteAsync(int id);

    Task<IEnumerable<UrlRoute>> GetActiveRoutesAsync();
    Task<IEnumerable<UrlRoute>> GetRoutesByLanguageAsync(string language);
    Task<UrlRoute?> GetRouteByKeyAsync(string key);
    Task<UrlRoute?> GetRouteByPatternAsync(string pattern);
    Task<IEnumerable<UrlRoute>> GetRoutesByControllerAsync(string controller);
    Task<IEnumerable<UrlRoute>> GetPublicRoutesAsync();
    Task<IEnumerable<UrlRoute>> GetAuthenticatedRoutesAsync();

    // URL generation helpers
    Task<string> GenerateUrlAsync(string routeKey, string language, object? routeValues = null);
    Task<string> GenerateUrlAsync(string controller, string action, string language, object? routeValues = null);
    Task<bool> IsValidRouteAsync(string url, string language);
    Task<UrlRoute?> MatchRouteAsync(string url, string language);
}
