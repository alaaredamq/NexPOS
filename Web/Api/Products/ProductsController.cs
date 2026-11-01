using Application.DTOs.Products;
using Application.IServices.Products;
using Application.IServices.Settings;
using Core.Auditing;
using Core.IRepositories.PrinterTransport;
using Core.Printing;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Products.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Products;

/// <summary>Products: list, details, create/edit/delete, barcode labels, CSV import and export.</summary>
/// <remarks>Port of the old <c>ProductsController</c>: every action keeps its permission (the whole controller needed Products.View).</remarks>
[Route("api/v1/products")]
[SwaggerTag("Products")]
public sealed class ProductsController : ApiControllerBase
{
    private const long MaxImportFileBytes = 10 * 1024 * 1024;

    private readonly IProductCatalogService _catalog;
    private readonly IProductImportExportService _importExport;
    private readonly ISystemSettingsService _settings;
    private readonly ILabelPdfBuilder _labels;
    private readonly IAuditTrail _audit;

    public ProductsController(
        IProductCatalogService catalog,
        IProductImportExportService importExport,
        ISystemSettingsService settings,
        ILabelPdfBuilder labels,
        IAuditTrail audit)
    {
        _catalog = catalog;
        _importExport = importExport;
        _settings = settings;
        _labels = labels;
        _audit = audit;
    }

    /// <summary>One page of the products list.</summary>
    /// <remarks>
    /// <c>q</c> matches name, barcode or description. <c>sort</c> is one of <c>name</c> (default), <c>barcode</c>, <c>category</c>,
    /// <c>supplier</c>, <c>purchasePrice</c>, <c>sellingPrice</c>, <c>stock</c>, <c>status</c>.
    /// </remarks>
    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Products_View)]
    [SwaggerOperation(OperationId = "listProducts")]
    [ProducesResponseType<PagedResult<ProductListItemDto>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<ProductListItemDto>> List([FromQuery] PagedQuery query, [FromQuery] ProductListFilter filter)
        => await _catalog.ListAsync(query, filter);

    /// <summary>The tiles above the list: total, active, low stock, out of stock.</summary>
    [HttpGet("summary")]
    [RequirePermission(PermissionHelper.Permissions.Products_View)]
    [SwaggerOperation(OperationId = "getProductsSummary")]
    [ProducesResponseType<ProductListSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ProductListSummaryDto> Summary() => await _catalog.SummaryAsync();

    /// <summary>Categories, suppliers and currencies for the product form and the list filters.</summary>
    [HttpGet("lookups")]
    [RequirePermission(PermissionHelper.Permissions.Products_View)]
    [SwaggerOperation(OperationId = "getProductLookups")]
    [ProducesResponseType<ProductLookupsDto>(StatusCodes.Status200OK)]
    public async Task<ProductLookupsDto> Lookups() => await _catalog.LookupsAsync();

    /// <summary>Active products matching a name or barcode (the single-product picker of a pack). At most 20.</summary>
    [HttpGet("options")]
    [RequirePermission(PermissionHelper.Permissions.Products_View)]
    [SwaggerOperation(OperationId = "searchProductOptions")]
    [ProducesResponseType<IReadOnlyList<ProductOptionDto>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ProductOptionDto>> Options([FromQuery] string? q)
        => await _catalog.OptionsAsync(q, 20);

    /// <summary>A new unused EAN-13 barcode in the shop's own range.</summary>
    [HttpGet("generate-barcode")]
    [RequireAnyPermission(PermissionHelper.Permissions.Products_Create, PermissionHelper.Permissions.Products_Edit)]
    [SwaggerOperation(OperationId = "generateProductBarcode")]
    [ProducesResponseType<GeneratedBarcodeDto>(StatusCodes.Status200OK)]
    public async Task<GeneratedBarcodeDto> GenerateBarcode()
        => new() { Barcode = await _catalog.GenerateBarcodeAsync() };

    /// <summary>One product with everything the details page and the edit form show.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Products_View)]
    [SwaggerOperation(OperationId = "getProduct")]
    [ProducesResponseType<ProductDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDetailDto>> Get(int id)
        => await _catalog.GetAsync(id) is { } product ? product : NotFoundProduct();

    /// <summary>Creates a product (with its extra prices).</summary>
    /// <remarks>400 <c>validation_failed</c> with <c>errors.barcode</c> when the barcode belongs to another product.</remarks>
    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Products_Create)]
    [SwaggerOperation(OperationId = "createProduct")]
    [ProducesResponseType<ProductDetailDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] ProductSaveRequest request)
    {
        var errors = await _catalog.ValidateAsync(request, id: null);
        if (errors.Count > 0) return Invalid(errors);

        var created = await _catalog.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Saves a product (the pack setting is not editable and is ignored).</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Products_Edit)]
    [SwaggerOperation(OperationId = "updateProduct")]
    [ProducesResponseType<ProductDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDetailDto>> Update(int id, [FromBody] ProductSaveRequest request)
    {
        var errors = await _catalog.ValidateAsync(request, id);
        if (errors.Count > 0) return Invalid(errors);

        return await _catalog.UpdateAsync(id, request) is { } saved ? saved : NotFoundProduct();
    }

    /// <summary>Deletes a product (soft delete: past sales keep their history).</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Products_Delete)]
    [SwaggerOperation(OperationId = "deleteProduct")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
        => await _catalog.DeleteAsync(id) ? NoContent() : NotFoundProduct();

    // ---- barcode labels ---------------------------------------------------------------------------

    /// <summary>The label size from App Settings (the print dialog's starting point).</summary>
    [HttpGet("sticker-settings")]
    [RequirePermission(PermissionHelper.Permissions.Products_View)]
    [SwaggerOperation(OperationId = "getStickerSettings")]
    [ProducesResponseType<StickerSettingsDto>(StatusCodes.Status200OK)]
    public async Task<StickerSettingsDto> StickerSettings()
    {
        var settings = await _settings.GetAppSettingsAsync();
        return new StickerSettingsDto { WidthMm = settings.StickerWidthMm, HeightMm = settings.StickerHeightMm };
    }

    /// <summary>Barcode labels as a PDF, for a plain browser (the desktop app prints to the label printer itself).</summary>
    [HttpPost("sticker-pdf")]
    [RequirePermission(PermissionHelper.Permissions.Products_View)]
    [SwaggerOperation(OperationId = "createStickerPdf")]
    [Produces("application/pdf", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    public IActionResult StickerPdf([FromBody] StickerPdfRequest request)
    {
        var label = StickerLabel.TryCreate(request.Name, request.Code, request.Price, request.Bars,
            request.WidthMm, request.HeightMm, request.Copies, request.ShowPrice);
        if (label == null) return ValidationFailure(nameof(StickerPdfRequest.Bars), Text("Prod_LabelInvalid"));

        return File(_labels.Build(label), "application/pdf", $"Labels-{label.Code}.pdf");
    }

    // ---- CSV import / export ----------------------------------------------------------------------

    /// <summary>The whole catalogue as a CSV, in the layout the import reads.</summary>
    [HttpGet("export")]
    [RequirePermission(PermissionHelper.Permissions.Products_View)]
    [SwaggerOperation(OperationId = "exportProducts")]
    [Produces("text/csv", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "text/csv")]
    public async Task<IActionResult> Export()
    {
        var csv = await _importExport.ExportAsync();
        _audit.LogBusiness(AuditActions.ProductsExported, "Products exported to CSV", "Product");
        return File(csv, "text/csv", $"products_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    }

    /// <summary>An empty import file: the column headers and one example row.</summary>
    [HttpGet("import-template")]
    [RequirePermission(PermissionHelper.Permissions.Products_View)]
    [SwaggerOperation(OperationId = "getProductImportTemplate")]
    [Produces("text/csv", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "text/csv")]
    public IActionResult ImportTemplate()
        => File(_importExport.BuildTemplate(), "text/csv", "products_import_template.csv");

    /// <summary>Reads a CSV. With <c>apply=false</c> it only reports what would happen; with <c>true</c> it saves the valid rows.</summary>
    /// <remarks>A row whose barcode exists updates that product (blank cells stay unchanged); any other row creates one. A file that cannot be read at all answers 200 with <c>fileError</c>.</remarks>
    [HttpPost("import")]
    [RequireAllPermissions(PermissionHelper.Permissions.Products_Create, PermissionHelper.Permissions.Products_Edit)]
    [Consumes("multipart/form-data")]
    [SwaggerOperation(OperationId = "importProducts")]
    [ProducesResponseType<ProductImportResultDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductImportResultDto>> Import([FromForm] ProductImportForm form)
    {
        var file = form.File;
        if (file == null || file.Length == 0) return ValidationFailure(nameof(ProductImportForm.File), Text("Prod_ImportNoFile"));
        if (file.Length > MaxImportFileBytes) return ValidationFailure(nameof(ProductImportForm.File), Text("Prod_ImportTooBig"));

        await using var stream = file.OpenReadStream();
        var result = await _importExport.ImportAsync(stream, form.Apply);

        if (result is { Applied: true, Success: true })
        {
            _audit.LogBusiness(AuditActions.ProductsImported,
                $"Products imported from CSV: {result.Created} created, {result.Updated} updated, {result.Failed} skipped",
                "Product",
                details: new { fileName = file.FileName, result.TotalRows, result.Created, result.Updated, result.Unchanged, result.NewCategories, result.Failed });
        }

        return new ProductImportResultDto
        {
            Applied = result.Applied,
            FileError = result.FileError,
            TotalRows = result.TotalRows,
            Created = result.Created,
            Updated = result.Updated,
            Unchanged = result.Unchanged,
            NewCategories = result.NewCategories,
            Failed = result.Failed,
            Issues = result.Issues.Select(i => new ProductImportIssueDto { Row = i.Row, Message = i.Message }).ToList(),
        };
    }

    // -----------------------------------------------------------------------------------------------

    private string Text(string key) => ApiProblem.Localize(HttpContext, key);

    private ObjectResult NotFoundProduct() => Failure(StatusCodes.Status404NotFound, ApiCodes.NotFound, Text("Prod_NotFound"));

    private ObjectResult Invalid(Dictionary<string, string[]> errors)
        => new(ApiProblem.Validation(HttpContext, errors))
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { ApiProblem.ContentType },
        };
}
