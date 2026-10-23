using Core.Auditing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Localization;
using System.Globalization;
using System.Threading;

namespace Web.Controllers;

public abstract class BaseController : Controller
{
    protected string CurrentLanguage => HttpContext.Items["CurrentLanguage"]?.ToString() ?? "en";
    
    protected bool IsRtl => CurrentLanguage == "ar";

    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        // Culture is already applied by LanguageMiddleware + UseRequestLocalization before
        // the action runs; this method only needs the view-facing values below.
        var language = CurrentLanguage;
        var culture = new CultureInfo(language);

        // Set ViewBag properties for views
        ViewBag.Language = language;
        ViewBag.CurrentLanguage = language; // Used in views for URL generation
        ViewBag.IsRtl = IsRtl;
        ViewBag.CurrentCulture = culture.Name;
        
        // Set language-switched URLs for views
        ViewBag.EnglishUrl = GetSwitchedLanguageUrl("en");
        ViewBag.ArabicUrl = GetSwitchedLanguageUrl("ar");
        
        base.OnActionExecuting(context);
    }

    /// <summary>
    /// Standard answer of every "print on receipt paper" endpoint. Never throws on a printer
    /// problem - the page shows the message and offers the PDF instead.
    /// </summary>
    protected async Task<IActionResult> PrintToReceiptPrinterAsync(
        Application.IServices.Sales.IThermalPrinterService printer,
        Core.Printing.PrintDocument document,
        string? pdfUrl = null)
    {
        var printed = await printer.PrintDocumentAsync(document, jobName: document.Title);

        // The endpoint answers HTTP 200 either way (the page shows the message), so the request log alone can't tell
        // "printed" from "printer unavailable".
        Audit?.LogBusiness(
            printed ? Core.Auditing.AuditActions.Print : Core.Auditing.AuditActions.PrintFailed,
            printed ? $"Sent '{document.Title}' to the receipt printer" : $"Could not print '{document.Title}' - receipt printer unavailable",
            "PrintJob",
            printed ? Core.Entities.Logging.AuditOutcome.Success : Core.Entities.Logging.AuditOutcome.Failure,
            printed ? Core.Entities.Logging.AuditLogSeverity.Info : Core.Entities.Logging.AuditLogSeverity.Warning,
            entityLabel: document.Title,
            details: new { document.Title, document.FileName, pdfUrl });

        return Json(new
        {
            success = printed,
            message = printed
                ? "Sent to the receipt printer"
                : "Could not print. Check that the receipt printer is connected and selected in Settings > Printers.",
            pdfUrl
        });
    }

    /// <summary>Standard answer of every "download PDF" endpoint.</summary>
    protected FileContentResult PdfFile(
        Application.IServices.Sales.IThermalPrinterService printer,
        Core.Printing.PrintDocument document,
        Core.Printing.PdfPaper paper = Core.Printing.PdfPaper.Receipt)
    {
        var pdf = printer.GeneratePdf(document, paper);
        Audit?.LogBusiness(Core.Auditing.AuditActions.PdfGenerated, $"PDF generated: '{document.Title}'", "PrintJob",
            entityLabel: document.Title, details: new { document.Title, document.FileName, paper = paper.ToString(), bytes = pdf.Length });
        return File(pdf, "application/pdf", $"{document.FileName}.pdf");
    }

    private Core.Auditing.IAuditTrail? Audit => HttpContext.RequestServices.GetService<Core.Auditing.IAuditTrail>();

    protected IActionResult RedirectToLocal(string? returnUrl)
    {
        if (Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        else
        {
            return RedirectToAction("Index", "Home");
        }
    }

    /// <summary>
    /// Generates a URL with the specified language in the route pattern /{lang}/{controller}/{action}
    /// </summary>
    protected string GetLanguageUrl(string language, string? controller = null, string? action = null, object? routeValues = null)
    {
        // Get current controller and action if not specified
        controller ??= ControllerContext.RouteData.Values["controller"]?.ToString() ?? "Home";
        action ??= ControllerContext.RouteData.Values["action"]?.ToString() ?? "Index";

        // Ensure language is valid
        if (language != "en" && language != "ar")
        {
            language = "en";
        }

        // Build route values with language
        var values = new Microsoft.AspNetCore.Routing.RouteValueDictionary(routeValues)
        {
            ["lang"] = language,
            ["controller"] = controller,
            ["action"] = action
        };

        // Generate URL using the localized route
        return Url.RouteUrl("localized", values) ?? $"/{language}/{controller}/{action}";
    }

    /// <summary>
    /// Generates a URL for the same page but with a different language
    /// </summary>
    protected string GetSwitchedLanguageUrl(string targetLanguage)
    {
        var controller = ControllerContext.RouteData.Values["controller"]?.ToString();
        var action = ControllerContext.RouteData.Values["action"]?.ToString();
        var routeValues = new Microsoft.AspNetCore.Routing.RouteValueDictionary(ControllerContext.RouteData.Values);
        
        // Remove language from route values if present
        routeValues.Remove("lang");
        
        // Copy query string parameters
        foreach (var query in Request.Query)
        {
            if (!routeValues.ContainsKey(query.Key))
            {
                routeValues[query.Key] = query.Value.ToString();
            }
        }

        return GetLanguageUrl(targetLanguage, controller, action, routeValues);
    }
}
