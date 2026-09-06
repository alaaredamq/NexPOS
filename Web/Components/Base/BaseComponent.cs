using Core.ICache;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;

namespace Web.Components.Base;

public abstract class BaseComponent : ViewComponent
{
    // Example: shared dependencies
    protected readonly ILogger _logger;

    protected BaseComponent(ILogger logger)
    {
        _logger = logger;
    }

    // Example: shared helper method
    protected ViewViewComponentResult SuccessView(object model)
    {
        return View("Default", model);
    }

    protected ViewViewComponentResult ErrorView(string message)
    {
        _logger.LogError(message);
        return View("Error", message);
    }
}