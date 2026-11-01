using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Settings.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Settings;

/// <summary>
/// Settings: the store details printed on receipts, the printers and sticker size, and the white-label branding. Mirrors the Razor
/// <c>SettingsController</c>, whose class-level <c>Settings.Manage</c> applies to every action.
/// </summary>
[Route("api/v1/settings")]
[RequirePermission(PermissionHelper.Permissions.Settings_Manage)]
public sealed class SettingsController : ApiControllerBase
{
    private readonly ISettingsApiService _settings;

    public SettingsController(ISettingsApiService settings) => _settings = settings;

    // ---- store & receipts ----------------------------------------------------------------------------------------

    /// <summary>The business details printed on receipts.</summary>
    [HttpGet("store")]
    [ProducesResponseType<StoreSettingsDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getStoreSettings")]
    public async Task<StoreSettingsDto> GetStore() => await _settings.GetStoreAsync();

    /// <summary>Saves the receipt details. The store name is required; a field over its length answers 400 <c>validation_failed</c>.</summary>
    [HttpPut("store")]
    [ProducesResponseType<StoreSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "saveStoreSettings")]
    public async Task<ActionResult<StoreSettingsDto>> SaveStore(StoreSettingsRequest request)
    {
        try
        {
            return await _settings.SaveStoreAsync(request, CurrentUserId is > 0 and var id ? id : null);
        }
        catch (SettingsRuleException ex)
        {
            return ValidationFailure(ex.Field, ex.Message);
        }
    }

    /// <summary>Puts every receipt field back to the factory text and returns it.</summary>
    [HttpPost("store/reset")]
    [ProducesResponseType<StoreSettingsDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "resetStoreSettings")]
    public async Task<StoreSettingsDto> ResetStore() => await _settings.ResetStoreAsync();

    // ---- printers & labels ---------------------------------------------------------------------------------------

    /// <summary>The receipt printer and the barcode sticker size.</summary>
    [HttpGet("printing")]
    [ProducesResponseType<PrintingSettingsDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getPrintingSettings")]
    public async Task<PrintingSettingsDto> GetPrinting() => await _settings.GetPrintingAsync();

    /// <summary>Saves the receipt printer and sticker size. A sticker size outside 15-150 x 10-150 mm answers 400 <c>validation_failed</c>.</summary>
    [HttpPut("printing")]
    [ProducesResponseType<PrintingSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "savePrintingSettings")]
    public async Task<PrintingSettingsDto> SavePrinting(PrintingSettingsRequest request) => await _settings.SavePrintingAsync(request);

    /// <summary>Printers installed on the machine that runs the server, for the receipt-printer picker.</summary>
    [HttpGet("printers")]
    [ProducesResponseType<InstalledPrintersDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listInstalledPrinters")]
    public InstalledPrintersDto Printers() => new() { Items = _settings.InstalledPrinters() };

    /// <summary>Prints a short test slip so the printer can be checked before it is saved. An unreachable printer is <c>printed: false</c>, not an error.</summary>
    [HttpPost("printers/test")]
    [ProducesResponseType<PrinterTestResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "testPrinter")]
    public async Task<PrinterTestResultDto> TestPrinter(PrinterTestRequest request) => await _settings.TestPrinterAsync(request.PrinterName);

    // ---- branding --------------------------------------------------------------------------------------------------

    /// <summary>The custom app name, logo and favicon (and what the defaults are).</summary>
    [HttpGet("branding")]
    [ProducesResponseType<BrandingSettingsDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getBrandingSettings")]
    public BrandingSettingsDto GetBranding() => _settings.GetBranding();

    /// <summary>
    /// Saves the app name and replaces or removes the logo and favicon. The session payload (<c>GET session</c>) carries the new branding straight away.
    /// A file of a type that is not allowed or over 2 MB answers 400 <c>validation_failed</c> on <c>logo</c> / <c>favicon</c>; nothing is saved then.
    /// </summary>
    [HttpPut("branding")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<BrandingSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "saveBrandingSettings")]
    public ActionResult<BrandingSettingsDto> SaveBranding([FromForm] BrandingForm form)
    {
        try
        {
            return _settings.SaveBranding(form);
        }
        catch (SettingsRuleException ex)
        {
            return ValidationFailure(ex.Field, ex.Message);
        }
    }
}
