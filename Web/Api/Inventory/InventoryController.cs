using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Inventory.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Inventory;

/// <summary>Stock levels: the inventory list, stock adjustments, minimum stock and the low-stock / stock-value reports.</summary>
/// <remarks>Port of the old <c>InventoryController</c>: the whole controller needed Inventory.View, adjusting needed Inventory.Edit.</remarks>
[Route("api/v1/inventory")]
[SwaggerTag("Inventory")]
public sealed class InventoryController : ApiControllerBase
{
    private readonly IInventoryApiService _inventory;
    private readonly IStringLocalizer<SharedResource> _text;

    public InventoryController(IInventoryApiService inventory, IStringLocalizer<SharedResource> text)
    {
        _inventory = inventory;
        _text = text;
    }

    /// <summary>One page of products with their stock figures.</summary>
    /// <remarks>
    /// <c>q</c> matches name, barcode, description or category. <c>sort</c> is one of <c>name</c> (default), <c>barcode</c>, <c>category</c>,
    /// <c>stock</c>, <c>minimum</c>, <c>maximum</c>, <c>cost</c>, <c>price</c>, <c>value</c>, <c>status</c>.
    /// </remarks>
    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Inventory_View)]
    [ProducesResponseType<PagedResult<InventoryItemDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listInventory")]
    public async Task<PagedResult<InventoryItemDto>> List([FromQuery] PagedQuery query) => await _inventory.ListAsync(query);

    /// <summary>The tiles above the list (all products, not just the current page).</summary>
    [HttpGet("summary")]
    [RequirePermission(PermissionHelper.Permissions.Inventory_View)]
    [ProducesResponseType<InventorySummaryDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getInventorySummary")]
    public async Task<InventorySummaryDto> Summary() => await _inventory.SummaryAsync();

    /// <summary>Adds or removes units of a product. 400 on the <c>quantity</c> field when a removal would take stock below zero.</summary>
    [HttpPost("{productId:int}/adjust")]
    [RequirePermission(PermissionHelper.Permissions.Inventory_Edit)]
    [ProducesResponseType<StockAdjustmentResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "adjustStock")]
    public async Task<ActionResult<StockAdjustmentResultDto>> Adjust(int productId, StockAdjustmentRequest request)
    {
        try
        {
            return await _inventory.AdjustAsync(productId, request) is { } result ? result : NotFound();
        }
        catch (InsufficientStockException ex)
        {
            return ValidationFailure(nameof(StockAdjustmentRequest.Quantity), _text["Inv_InsufficientStock", ex.Available.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture), ex.Requested.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)].Value);
        }
    }

    /// <summary>Sets the level under which the product counts as low stock.</summary>
    [HttpPut("{productId:int}/minimum-stock")]
    [RequirePermission(PermissionHelper.Permissions.Inventory_Edit)]
    [ProducesResponseType<InventoryItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "setMinimumStock")]
    public async Task<ActionResult<InventoryItemDto>> SetMinimumStock(int productId, MinimumStockRequest request)
        => await _inventory.SetMinimumAsync(productId, request.MinimumStock) is { } item ? item : NotFound();

    /// <summary>Products at or below their minimum, least stock first, with the totals of the whole report. <c>q</c> matches name, barcode or category.</summary>
    [HttpGet("reports/low-stock")]
    [RequirePermission(PermissionHelper.Permissions.Inventory_View)]
    [ProducesResponseType<LowStockReportDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getLowStockReport")]
    public async Task<LowStockReportDto> LowStock([FromQuery] PagedQuery query) => await _inventory.LowStockReportAsync(query);

    /// <summary>Stock value of the active products, the two chart series and one page of the table. <c>sort</c>: <c>value</c> (default, descending), <c>name</c>, <c>category</c>, <c>stock</c>, <c>cost</c>.</summary>
    [HttpGet("reports/stock-value")]
    [RequirePermission(PermissionHelper.Permissions.Inventory_View)]
    [ProducesResponseType<StockValueReportDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getStockValueReport")]
    public async Task<StockValueReportDto> StockValue([FromQuery] PagedQuery query) => await _inventory.StockValueReportAsync(query);

    /// <summary>Sends the low-stock report to the receipt printer. Answers 200 with <c>printed: false</c> when the printer is unavailable.</summary>
    [HttpPost("reports/low-stock/print")]
    [RequirePermission(PermissionHelper.Permissions.Inventory_View)]
    [ProducesResponseType<InventoryPrintResultDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "printLowStockReport")]
    public async Task<InventoryPrintResultDto> PrintLowStock() => await _inventory.PrintAsync(InventoryReport.LowStock);

    /// <summary>The low-stock report as an A4 PDF.</summary>
    [HttpGet("reports/low-stock.pdf")]
    [RequirePermission(PermissionHelper.Permissions.Inventory_View)]
    [Produces("application/pdf")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "application/pdf")]
    [SwaggerOperation(OperationId = "downloadLowStockReportPdf")]
    public async Task<IActionResult> LowStockPdf()
    {
        var (pdf, name) = await _inventory.PdfAsync(InventoryReport.LowStock);
        return File(pdf, "application/pdf", name);
    }

    /// <summary>Sends the stock-value report to the receipt printer. Answers 200 with <c>printed: false</c> when the printer is unavailable.</summary>
    [HttpPost("reports/stock-value/print")]
    [RequirePermission(PermissionHelper.Permissions.Inventory_View)]
    [ProducesResponseType<InventoryPrintResultDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "printStockValueReport")]
    public async Task<InventoryPrintResultDto> PrintStockValue() => await _inventory.PrintAsync(InventoryReport.StockValue);

    /// <summary>The stock-value report as an A4 PDF.</summary>
    [HttpGet("reports/stock-value.pdf")]
    [RequirePermission(PermissionHelper.Permissions.Inventory_View)]
    [Produces("application/pdf")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "application/pdf")]
    [SwaggerOperation(OperationId = "downloadStockValueReportPdf")]
    public async Task<IActionResult> StockValuePdf()
    {
        var (pdf, name) = await _inventory.PdfAsync(InventoryReport.StockValue);
        return File(pdf, "application/pdf", name);
    }
}
