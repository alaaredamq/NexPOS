using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Catalog.Contracts;
using Web.Api.Infrastructure;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Catalog;

/// <summary>
/// Which products appear on the POS main screen, and in what order. Everything here needs <c>MainScreenProducts.Manage</c>, as the
/// Razor <c>ProductDisplayOrderController</c> did.
/// </summary>
[Route("api/v1/product-display-order")]
[RequirePermission(PermissionHelper.Permissions.MainScreenProducts_Manage)]
public sealed class ProductDisplayOrderController : ApiControllerBase
{
    private readonly IDisplayOrderApiService _displayOrder;

    public ProductDisplayOrderController(IDisplayOrderApiService displayOrder) => _displayOrder = displayOrder;

    /// <summary>The main-screen products in order, with the figures for the tiles.</summary>
    [HttpGet]
    [ProducesResponseType<DisplayOrderDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getProductDisplayOrder")]
    public async Task<DisplayOrderDto> Get() => await _displayOrder.GetAsync();

    /// <summary>Saves the order: the rows listed go first in that order, any row not listed follows.</summary>
    [HttpPut("order")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "reorderProductDisplayOrder")]
    public async Task<IActionResult> Reorder(ReorderRequest request)
    {
        await _displayOrder.ReorderAsync(request.Ids);
        return NoContent();
    }

    /// <summary>Puts an existing product at the end of the main screen. 404 <c>product_not_found</c>; 409 <c>already_in_display_order</c>.</summary>
    [HttpPost("items")]
    [ProducesResponseType<DisplayOrderItemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "addProductToDisplayOrder")]
    public async Task<IActionResult> Add(AddToDisplayOrderRequest request)
    {
        try
        {
            var item = await _displayOrder.AddAsync(request.ProductId);
            return Created($"api/v1/product-display-order/items/{item.Id}", item);
        }
        catch (DisplayOrderException ex) when (ex.Code == DisplayOrderException.ProductNotFound)
        {
            return Failure(StatusCodes.Status404NotFound, ex.Code, ex.Message);
        }
        catch (DisplayOrderException ex)
        {
            return ConflictFailure(ex.Code, ex.Message);
        }
    }

    /// <summary>Takes a row off the main screen (the product itself is untouched).</summary>
    [HttpDelete("items/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "removeProductFromDisplayOrder")]
    public async Task<IActionResult> Remove(int id) => await _displayOrder.RemoveAsync(id) ? NoContent() : NotFound();

    /// <summary>Active products not on the main screen yet, filtered by name or barcode, <c>skip</c>/<c>take</c> paged (take at most 50).</summary>
    [HttpGet("available-products")]
    [ProducesResponseType<AvailableProductsDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listAvailableProductsForDisplayOrder")]
    public async Task<AvailableProductsDto> Available([FromQuery] string? q, [FromQuery] int skip = 0, [FromQuery] int take = 20)
        => await _displayOrder.AvailableAsync(q, skip, take);

    /// <summary>Currencies for the quick-add form and the default one.</summary>
    [HttpGet("quick-add-options")]
    [ProducesResponseType<QuickAddOptionsDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getQuickAddOptions")]
    public async Task<QuickAddOptionsDto> QuickAddOptions() => await _displayOrder.QuickAddOptionsAsync();

    /// <summary>
    /// Creates a basic product (default category, supplier and brand; cost = 70 % of the price) and puts it on the main screen.
    /// The full product can be edited later in the Products page.
    /// </summary>
    [HttpPost("quick-add")]
    [ProducesResponseType<QuickAddResultDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "quickAddProduct")]
    public async Task<IActionResult> QuickAdd(QuickAddProductRequest request)
    {
        var result = await _displayOrder.QuickAddAsync(request);
        return Created($"api/v1/product-display-order/items/{result.DisplayOrderId}", result);
    }
}
