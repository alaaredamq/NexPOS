using Application.IServices.Reports;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Catalog.Contracts;
using Web.Api.Infrastructure;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Catalog;

/// <summary>Product categories: list, details, create, edit, delete, CSV export. Mirrors the Razor <c>CategoriesController</c>.</summary>
[Route("api/v1/categories")]
public sealed class CategoriesController : ApiControllerBase
{
    private readonly ICategoryApiService _categories;
    private readonly IExportService _export;

    public CategoriesController(ICategoryApiService categories, IExportService export)
    {
        _categories = categories;
        _export = export;
    }

    /// <summary>One page of categories. <c>sort</c>: <c>name</c> (default), <c>description</c>, <c>isActive</c>, <c>productCount</c>; <c>q</c> matches name and description.</summary>
    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Categories_View)]
    [ProducesResponseType<PagedResult<CategoryDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listCategories")]
    public async Task<PagedResult<CategoryDto>> List([FromQuery] PagedQuery query) => await _categories.ListAsync(query);

    /// <summary>Totals for the tiles above the list (all categories, not just the current page).</summary>
    [HttpGet("summary")]
    [RequirePermission(PermissionHelper.Permissions.Categories_View)]
    [ProducesResponseType<CategorySummaryDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getCategorySummary")]
    public async Task<CategorySummaryDto> Summary() => await _categories.SummaryAsync();

    /// <summary>The category, its first 20 products and its stock figures.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Categories_View)]
    [ProducesResponseType<CategoryDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getCategory")]
    public async Task<ActionResult<CategoryDetailsDto>> Get(int id)
        => await _categories.GetDetailsAsync(id) is { } details ? details : NotFound();

    /// <summary>Creates a category.</summary>
    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Categories_Create)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "createCategory")]
    public async Task<ActionResult<CategoryDto>> Create(CategoryRequest request)
    {
        var created = await _categories.CreateAsync(request);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Renames / describes / (de)activates a category.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Categories_Edit)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "updateCategory")]
    public async Task<ActionResult<CategoryDto>> Update(int id, CategoryRequest request)
        => await _categories.UpdateAsync(id, request) is { } updated ? updated : NotFound();

    /// <summary>Deletes a category nothing points at. 409 <c>category_in_use</c> while products (even deleted ones kept for sales history) still use it.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Categories_Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deleteCategory")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            return await _categories.DeleteAsync(id) ? NoContent() : NotFound();
        }
        catch (CategoryInUseException ex)
        {
            return ConflictFailure("category_in_use", ex.Message);
        }
    }

    /// <summary>All categories as a CSV file (the same export the Razor page offers).</summary>
    [HttpGet("export")]
    [RequirePermission(PermissionHelper.Permissions.Categories_View)]
    [Produces("text/csv")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "text/csv")]
    [SwaggerOperation(OperationId = "exportCategories")]
    public async Task<IActionResult> Export()
    {
        var csv = await _export.ExportCategoriesToCsvAsync();
        return File(csv, "text/csv", $"categories_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    }
}
