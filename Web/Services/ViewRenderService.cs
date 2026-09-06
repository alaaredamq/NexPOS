using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Web.Services;

public interface IViewRenderService
{
    Task<string> RenderViewToStringAsync<TModel>(string viewName, TModel model);
    Task<string> RenderViewToStringAsync<TModel>(string viewName, TModel model, IDictionary<string, object?>? viewData);
    Task<string> RenderViewToStringAsync(string viewName);
}

public class ViewRenderService : IViewRenderService
{
    private readonly IRazorViewEngine _razorViewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ViewRenderService(
        IRazorViewEngine razorViewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider,
        IHttpContextAccessor httpContextAccessor)
    {
        _razorViewEngine = razorViewEngine;
        _tempDataProvider = tempDataProvider;
        _serviceProvider = serviceProvider;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<string> RenderViewToStringAsync<TModel>(string viewName, TModel model)
    {
        var httpContext = _httpContextAccessor.HttpContext ?? new DefaultHttpContext { RequestServices = _serviceProvider };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        using var sw = new StringWriter();

        var viewResult = _razorViewEngine.FindView(actionContext, viewName, false);

        if (viewResult.View == null)
        {
            // Try to find view with full path
            viewResult = _razorViewEngine.GetView(null, viewName, false);
        }

        if (viewResult.View == null)
        {
            throw new ArgumentNullException($"View '{viewName}' not found. Searched locations: {string.Join(", ", viewResult.SearchedLocations ?? Array.Empty<string>())}");
        }

        var viewDictionary = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };

        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewDictionary,
            new TempDataDictionary(actionContext.HttpContext, _tempDataProvider),
            sw,
            new HtmlHelperOptions()
        );

        await viewResult.View.RenderAsync(viewContext);
        return sw.ToString();
    }

    public async Task<string> RenderViewToStringAsync<TModel>(string viewName, TModel model, IDictionary<string, object?>? viewData)
    {
        var httpContext = _httpContextAccessor.HttpContext ?? new DefaultHttpContext { RequestServices = _serviceProvider };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        using var sw = new StringWriter();

        var viewResult = _razorViewEngine.FindView(actionContext, viewName, false);

        if (viewResult.View == null)
        {
            viewResult = _razorViewEngine.GetView(null, viewName, false);
        }

        if (viewResult.View == null)
        {
            throw new ArgumentNullException($"View '{viewName}' not found. Searched locations: {string.Join(", ", viewResult.SearchedLocations ?? Array.Empty<string>())}");
        }

        var viewDictionary = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };
        if (viewData != null)
        {
            foreach (var kv in viewData)
            {
                viewDictionary[kv.Key] = kv.Value;
            }
        }

        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewDictionary,
            new TempDataDictionary(actionContext.HttpContext, _tempDataProvider),
            sw,
            new HtmlHelperOptions()
        );

        await viewResult.View.RenderAsync(viewContext);
        return sw.ToString();
    }

    public async Task<string> RenderViewToStringAsync(string viewName)
    {
        return await RenderViewToStringAsync<object?>(viewName, null);
    }
}

