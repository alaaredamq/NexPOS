using Application.IServices.Configuration;
using Core.Entities.Configuration;
using Core.IRepositories.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;

namespace Application.Services.Configuration;

public class UrlRouteService : IUrlRouteService
{
    private readonly IUrlRouteRepository _repository;

    public UrlRouteService(IUrlRouteRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<UrlRoute>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<UrlRoute?> GetByIdAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task<UrlRoute> CreateAsync(UrlRoute urlRoute)
    {
        return await _repository.SaveAsync(urlRoute);
    }

    public async Task<UrlRoute> UpdateAsync(UrlRoute urlRoute)
    {
        return await _repository.SaveAsync(urlRoute);
    }

    public async Task DeleteAsync(int id)
    {
        var urlRoute = await _repository.GetByIdAsync(id);
        if (urlRoute != null)
        {
            await _repository.DeleteAsync(urlRoute);
        }
    }

    public async Task<IEnumerable<UrlRoute>> GetActiveRoutesAsync()
    {
        return await _repository.GetActiveRoutesAsync();
    }

    public async Task<IEnumerable<UrlRoute>> GetRoutesByLanguageAsync(string language)
    {
        return await _repository.GetRoutesByLanguageAsync(language);
    }

    public async Task<UrlRoute?> GetRouteByKeyAsync(string key)
    {
        return await _repository.GetRouteByKeyAsync(key);
    }

    public async Task<UrlRoute?> GetRouteByPatternAsync(string pattern)
    {
        return await _repository.GetRouteByPatternAsync(pattern);
    }

    public async Task<IEnumerable<UrlRoute>> GetRoutesByControllerAsync(string controller)
    {
        return await _repository.GetRoutesByControllerAsync(controller);
    }

    public async Task<IEnumerable<UrlRoute>> GetPublicRoutesAsync()
    {
        return await _repository.GetPublicRoutesAsync();
    }

    public async Task<IEnumerable<UrlRoute>> GetAuthenticatedRoutesAsync()
    {
        return await _repository.GetAuthenticatedRoutesAsync();
    }

    public async Task<string> GenerateUrlAsync(string routeKey, string language, object? routeValues = null)
    {
        var route = await GetRouteByKeyAsync(routeKey);
        if (route == null)
            throw new ArgumentException($"Route with key '{routeKey}' not found");

        return GenerateUrlFromRoute(route, language, routeValues);
    }

    public async Task<string> GenerateUrlAsync(string controller, string action, string language, object? routeValues = null)
    {
        var routes = await GetRoutesByControllerAsync(controller);
        var route = routes.FirstOrDefault(r => r.Action == action);
        
        if (route == null)
            throw new ArgumentException($"Route for '{controller}/{action}' not found");

        return GenerateUrlFromRoute(route, language, routeValues);
    }

    public async Task<bool> IsValidRouteAsync(string url, string language)
    {
        var route = await MatchRouteAsync(url, language);
        return route != null;
    }

    public async Task<UrlRoute?> MatchRouteAsync(string url, string language)
    {
        var routes = await GetActiveRoutesAsync();
        
        foreach (var route in routes)
        {
            if (MatchesRoute(url, route.Pattern, language))
            {
                return route;
            }
        }

        return null;
    }

    private string GenerateUrlFromRoute(UrlRoute route, string language, object? routeValues)
    {
        var url = route.Pattern.Replace("{lang}", language);
        
        // Replace other route parameters if provided
        if (routeValues != null)
        {
            var properties = routeValues.GetType().GetProperties();
            foreach (var prop in properties)
            {
                var value = prop.GetValue(routeValues)?.ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    url = url.Replace($"{{{prop.Name}}}", value);
                }
            }
        }

        // Remove any remaining unreplaced parameters
        url = System.Text.RegularExpressions.Regex.Replace(url, @"\{[^}]+\}", "");

        return url;
    }

    private bool MatchesRoute(string url, string pattern, string language)
    {
        // Simple pattern matching - in a real implementation, you might want more sophisticated matching
        var normalizedUrl = url.Trim('/');
        var normalizedPattern = pattern.Replace("{lang}", language).Trim('/');
        
        // Convert pattern to regex
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(normalizedPattern)
            .Replace("\\{", "{")
            .Replace("\\}", "}")
            .Replace("{", "(?<")
            .Replace("}", ">[^/]+)") + "$";

        return System.Text.RegularExpressions.Regex.IsMatch(normalizedUrl, regexPattern);
    }
}
