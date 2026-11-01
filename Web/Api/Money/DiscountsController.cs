using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Money.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Money;

/// <summary>Percentage discounts of the till. Permissions as the legacy <c>DiscountsController</c>: <c>Discounts.View</c> to read, <c>Discounts.Manage</c> to change.</summary>
[Route("api/v1/discounts")]
[RequirePermission(PermissionHelper.Permissions.Discounts_View)]
public sealed class DiscountsController : MoneyControllerBase
{
    private readonly IDiscountApiService _discounts;

    public DiscountsController(IDiscountApiService discounts) => _discounts = discounts;

    /// <summary>One page of discounts. <c>sort</c>: <c>name</c> (default), <c>rate</c>, <c>isDefault</c>, <c>createdAt</c>; <c>q</c> matches the name.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<DiscountDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listDiscounts")]
    public async Task<PagedResult<DiscountDto>> List([FromQuery] PagedQuery query) => await _discounts.ListAsync(query);

    /// <summary>One discount.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<DiscountDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getDiscount")]
    public async Task<ActionResult<DiscountDto>> Get(int id) => await _discounts.GetAsync(id) is { } found ? found : NotFound();

    /// <summary>Creates a discount; <c>isDefault</c> takes the default away from the previous one.</summary>
    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Discounts_Manage)]
    [ProducesResponseType<DiscountDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "createDiscount")]
    public Task<ActionResult<DiscountDto>> Create(DiscountRequest request) => GuardCreated(() => _discounts.CreateAsync(request), d => d.Id);

    /// <summary>Changes a discount.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Discounts_Manage)]
    [ProducesResponseType<DiscountDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "updateDiscount")]
    public Task<ActionResult<DiscountDto>> Update(int id, DiscountRequest request) => Guard(() => _discounts.UpdateAsync(id, request));

    /// <summary>Deletes a discount. 409 <c>discount_in_use</c> when something still points at it.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Discounts_Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deleteDiscount")]
    public Task<IActionResult> Delete(int id) => Done(() => _discounts.DeleteAsync(id));
}
