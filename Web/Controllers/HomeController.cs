using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;
using Web.Helpers;
using Application.IServices.Products;

namespace Web.Controllers;

[RequirePermission(PermissionHelper.Permissions.POS_View)]
public class HomeController : BaseController
{
    private readonly ILogger<HomeController> _logger;
    private readonly IFastProductSearchService _fastProductSearch;

    public HomeController(ILogger<HomeController> logger, IFastProductSearchService fastProductSearch)
    {
        _logger = logger;
        _fastProductSearch = fastProductSearch;
    }
    public IActionResult Index()
    {
        var currentLang = CurrentLanguage;
        
        // Redirect to loading screen if user is authenticated, otherwise to login
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("LoadingScreen", "Home", new { lang = currentLang });
        }
        
        return RedirectToAction("Login", "Account", new { lang = currentLang });
    }

    [Authorize]
    public IActionResult LoadingScreen()
    {
        return View();
    }

    [Authorize]
    public IActionResult Dashboard()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel {  RequestId = HttpContext.TraceIdentifier });
    }
}

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}