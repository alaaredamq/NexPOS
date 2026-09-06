using Application.IServices.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace Web.Services;

public class UrlHelperService
{
    private readonly IUrlRouteService _urlRouteService;
    private readonly HttpContext _httpContext;

    public UrlHelperService(IUrlRouteService urlRouteService, IHttpContextAccessor httpContextAccessor)
    {
        _urlRouteService = urlRouteService;
        _httpContext = httpContextAccessor.HttpContext ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public async Task<string> GetLanguageSwitchedUrlAsync(string currentUrl, string targetLanguage)
    {
        var currentLanguage = GetCurrentLanguage();
        
        // If already in the target language, return current URL
        if (currentLanguage == targetLanguage)
            return currentUrl;

        // Extract controller and action from current URL
        var routeData = _httpContext.Request.RouteValues;
        var controller = routeData["controller"]?.ToString();
        var action = routeData["action"]?.ToString();

        if (string.IsNullOrEmpty(controller) || string.IsNullOrEmpty(action))
            return currentUrl;

        try
        {
            // Generate URL for the target language
            return await _urlRouteService.GenerateUrlAsync(controller, action, targetLanguage, routeData);
        }
        catch
        {
            // Fallback to simple URL replacement
            return currentUrl.Replace($"/{currentLanguage}/", $"/{targetLanguage}/");
        }
    }

    public async Task<string> GenerateUrlAsync(string routeKey, string language, object? routeValues = null)
    {
        return await _urlRouteService.GenerateUrlAsync(routeKey, language, routeValues);
    }

    public async Task<string> GenerateUrlAsync(string controller, string action, string language, object? routeValues = null)
    {
        return await _urlRouteService.GenerateUrlAsync(controller, action, language, routeValues);
    }

    public string GetCurrentLanguage()
    {
        // Try to get language from route first
        if (_httpContext.Items.TryGetValue("LanguageFromRoute", out var fromRoute) && fromRoute is true)
        {
            return _httpContext.Items["CurrentLanguage"]?.ToString() ?? "en";
        }

        // Fallback to session or default
        return _httpContext.Session.GetString("SelectedLanguage") ?? "en";
    }

    public async Task<string> GetLocalizedUrlAsync(string routeKey, object? routeValues = null)
    {
        var currentLanguage = GetCurrentLanguage();
        return await _urlRouteService.GenerateUrlAsync(routeKey, currentLanguage, routeValues);
    }

    public async Task<string> GetLocalizedUrlAsync(string controller, string action, object? routeValues = null)
    {
        var currentLanguage = GetCurrentLanguage();
        return await _urlRouteService.GenerateUrlAsync(controller, action, currentLanguage, routeValues);
    }
}
