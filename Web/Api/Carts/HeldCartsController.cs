using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Carts.Contracts;
using Web.Api.Infrastructure;
using Web.Api.Pos;

namespace Web.Api.Carts;

/// <summary>
/// Parked ("held") carts: park the current cart for the next customer, list what is parked, carry one on, delete. Open to every signed-in user as the old
/// <c>POSController</c> actions were. Only parking is a sale-changing action (needs a licence that allows selling).
/// </summary>
[Route("api/v1/carts")]
public sealed class HeldCartsController : PosControllerBase
{
    private readonly IHeldCartService _carts;

    public HeldCartsController(IHeldCartService carts) => _carts = carts;

    /// <summary>Parked carts that were not sold, newest first.</summary>
    [HttpGet]
    [AnySignedInUser]
    [ProducesResponseType<IReadOnlyList<HeldCartDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getHeldCarts")]
    public async Task<ActionResult<IReadOnlyList<HeldCartDto>>> List() => Ok(await _carts.ListAsync());

    /// <summary>Parks a cart. The same checks as a sale apply to its lines (see <c>pos/price</c>), so a parked cart can always be resumed.</summary>
    [HttpPost]
    [AnySignedInUser]
    [RequireSalesLicense]
    [ProducesResponseType<HeldCartDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "holdCart")]
    public async Task<ActionResult<HeldCartDto>> Hold(PosHoldCartRequest request)
    {
        var held = await _carts.HoldAsync(request);
        return Created($"/api/v1/carts/{held.Id}", held);
    }

    /// <summary>A parked cart's lines and discount, to put back on the till. The parked cart stays in the list until deleted.</summary>
    [HttpGet("{id:int}")]
    [AnySignedInUser]
    [ProducesResponseType<ResumedCartDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getHeldCart")]
    public async Task<ActionResult<ResumedCartDto>> Get(int id) => await _carts.GetAsync(id);

    /// <summary>Deletes one parked cart.</summary>
    [HttpDelete("{id:int}")]
    [AnySignedInUser]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deleteHeldCart")]
    public async Task<IActionResult> Delete(int id)
    {
        await _carts.DeleteAsync(id);
        return NoContent();
    }

    /// <summary>Deletes every parked cart that was not sold.</summary>
    [HttpDelete]
    [AnySignedInUser]
    [ProducesResponseType<HeldCartsDeletedDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "deleteAllHeldCarts")]
    public async Task<ActionResult<HeldCartsDeletedDto>> DeleteAll() => new HeldCartsDeletedDto { DeletedCount = await _carts.DeleteAllAsync() };
}
