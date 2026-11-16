using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.OnlineMenu.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.OnlineMenu;

/// <summary>
/// The "Online Menu" screen: sections for the shop's public menu, and which products or whole categories each one shows. Needs
/// <c>Products.Edit</c>: whoever may change products may decide how they are shown online. Not on the selling path, so not behind
/// <c>[RequireSalesLicense]</c>.
/// </summary>
[Route("api/v1/online-menu")]
[RequirePermission(PermissionHelper.Permissions.Products_Edit)]
public sealed class OnlineMenuController : ApiControllerBase
{
    private readonly IOnlineMenuApiService _menu;

    public OnlineMenuController(IOnlineMenuApiService menu) => _menu = menu;

    /// <summary>The sections in order with what they hold, the counts, and whether the menu is linked.</summary>
    [HttpGet]
    [ProducesResponseType<OnlineMenuDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getOnlineMenu")]
    public async Task<OnlineMenuDto> Get() => await _menu.GetAsync();

    /// <summary>Adds an empty section at the end.</summary>
    [HttpPost("sections")]
    [ProducesResponseType<OnlineMenuSectionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "addOnlineMenuSection")]
    public async Task<IActionResult> AddSection(OnlineMenuSectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return ValidationFailure(nameof(request.Name), "Give the section a name.");
        var section = await _menu.AddSectionAsync(request.Name);
        return Created($"api/v1/online-menu/sections/{section.Id}", section);
    }

    [HttpPut("sections/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "renameOnlineMenuSection")]
    public async Task<IActionResult> RenameSection(int id, OnlineMenuSectionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return ValidationFailure(nameof(request.Name), "Give the section a name.");
        return await _menu.RenameSectionAsync(id, request.Name) ? NoContent() : NotFound();
    }

    /// <summary>Removes the section and its picks. The products themselves are untouched.</summary>
    [HttpDelete("sections/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deleteOnlineMenuSection")]
    public async Task<IActionResult> DeleteSection(int id) => await _menu.DeleteSectionAsync(id) ? NoContent() : NotFound();

    /// <summary>The sections listed first, in that order.</summary>
    [HttpPut("sections/order")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [SwaggerOperation(OperationId = "reorderOnlineMenuSections")]
    public async Task<IActionResult> ReorderSections(OnlineMenuOrderRequest request)
    {
        await _menu.ReorderSectionsAsync(request.Ids);
        return NoContent();
    }

    /// <summary>Puts products and whole categories at the end of the section; ones already in another section move here.</summary>
    [HttpPost("sections/{id:int}/entries")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "placeInOnlineMenuSection")]
    public async Task<IActionResult> Place(int id, OnlineMenuPlaceRequest request)
    {
        var products = request.ProductIds ?? [];
        var categories = request.CategoryIds ?? [];
        if (products.Count + categories.Count == 0) return ValidationFailure(nameof(request.ProductIds), "Pick at least one product or category.");
        if (products.Count + categories.Count > 500) return ValidationFailure(nameof(request.ProductIds), "Pick at most 500 at a time.");
        return await _menu.PlaceAsync(id, products, categories) ? NoContent() : NotFound();
    }

    /// <summary>One page of a section's picks, in order (take at most 50). The overview does not include them: a section can hold thousands.</summary>
    [HttpGet("sections/{id:int}/entries")]
    [ProducesResponseType<OnlineMenuEntriesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "listOnlineMenuEntries")]
    public async Task<ActionResult<OnlineMenuEntriesDto>> Entries(int id, [FromQuery] int skip = 0, [FromQuery] int take = 30)
        => await _menu.EntriesAsync(id, skip, take) is { } page ? page : NotFound();

    /// <summary>Moves a pick one step up or down in its section.</summary>
    [HttpPost("entries/{id:int}/move")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "moveOnlineMenuEntry")]
    public async Task<IActionResult> MoveEntry(int id, OnlineMenuMoveRequest request)
        => await _menu.MoveEntryAsync(id, request.Direction) ? NoContent() : NotFound();

    /// <summary>Takes a product or category out of its section (and so off the online menu).</summary>
    [HttpDelete("entries/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "removeOnlineMenuEntry")]
    public async Task<IActionResult> RemoveEntry(int id) => await _menu.RemoveEntryAsync(id) ? NoContent() : NotFound();

    /// <summary>Active products by name or barcode, <c>skip</c>/<c>take</c> paged (take at most 50), with where each is on the menu now.</summary>
    [HttpGet("products")]
    [ProducesResponseType<OnlineMenuProductOptionsDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listOnlineMenuProductOptions")]
    public async Task<OnlineMenuProductOptionsDto> Products([FromQuery] string? q, [FromQuery] int skip = 0, [FromQuery] int take = 20)
        => await _menu.ProductsAsync(q, skip, take);

    /// <summary>Every category with its active product count and the section it was picked into.</summary>
    [HttpGet("categories")]
    [ProducesResponseType<IReadOnlyList<OnlineMenuCategoryOptionDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listOnlineMenuCategoryOptions")]
    public async Task<IReadOnlyList<OnlineMenuCategoryOptionDto>> Categories() => await _menu.CategoriesAsync();

    /// <summary>Asks this till to check its online menu and send its products now, instead of within the minute.</summary>
    [HttpPost("send-now")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [SwaggerOperation(OperationId = "sendOnlineMenuNow")]
    public IActionResult SendNow()
    {
        _menu.SendNow();
        return NoContent();
    }
}
