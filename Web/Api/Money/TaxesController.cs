using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Money.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Money;

/// <summary>Tax rates. Permissions as the Razor <c>TaxController</c>: <c>Tax.View</c> for the class, then <c>Create</c> / <c>Edit</c> / <c>Delete</c>, and <c>Manage</c> for "default" and "deactivate".</summary>
[Route("api/v1/taxes")]
[RequirePermission(PermissionHelper.Permissions.Tax_View)]
public sealed class TaxesController : MoneyControllerBase
{
    private readonly ITaxApiService _taxes;

    public TaxesController(ITaxApiService taxes) => _taxes = taxes;

    /// <summary>One page of tax rates. <c>sort</c>: <c>name</c> (default), <c>rate</c>, <c>type</c>, <c>isDefault</c>, <c>isActive</c>, <c>effectiveFrom</c>, <c>effectiveTo</c>, <c>category</c>; <c>q</c> matches name, code, description and category.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<TaxRateDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listTaxes")]
    public async Task<PagedResult<TaxRateDto>> List([FromQuery] PagedQuery query) => await _taxes.ListAsync(query);

    /// <summary>Categories a tax can be limited to (so the form does not need <c>Categories.View</c>).</summary>
    [HttpGet("categories")]
    [ProducesResponseType<IReadOnlyList<MoneyOptionDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listTaxCategories")]
    public async Task<IReadOnlyList<MoneyOptionDto>> Categories() => await _taxes.CategoryOptionsAsync();

    /// <summary>One tax rate.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<TaxRateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getTax")]
    public async Task<ActionResult<TaxRateDto>> Get(int id) => await _taxes.GetAsync(id) is { } found ? found : NotFound();

    /// <summary>Creates a tax rate; <c>isDefault</c> takes the default away from the previous one.</summary>
    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Tax_Create)]
    [ProducesResponseType<TaxRateDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "createTax")]
    public Task<ActionResult<TaxRateDto>> Create(TaxRateRequest request) => GuardCreated(() => _taxes.CreateAsync(request), t => t.Id);

    /// <summary>Changes a tax rate.</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Tax_Edit)]
    [ProducesResponseType<TaxRateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "updateTax")]
    public Task<ActionResult<TaxRateDto>> Update(int id, TaxRateRequest request) => Guard(() => _taxes.UpdateAsync(id, request));

    /// <summary>Deletes a tax rate.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Tax_Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deleteTax")]
    public Task<IActionResult> Delete(int id) => Done(() => _taxes.DeleteAsync(id));

    /// <summary>Makes this the one default tax.</summary>
    [HttpPost("{id:int}/default")]
    [RequirePermission(PermissionHelper.Permissions.Tax_Manage)]
    [ProducesResponseType<TaxRateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "setDefaultTax")]
    public Task<ActionResult<TaxRateDto>> SetDefault(int id) => Guard(() => _taxes.SetDefaultAsync(id));

    /// <summary>Switches the tax off and ends it today.</summary>
    [HttpPost("{id:int}/deactivate")]
    [RequirePermission(PermissionHelper.Permissions.Tax_Manage)]
    [ProducesResponseType<TaxRateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deactivateTax")]
    public Task<ActionResult<TaxRateDto>> Deactivate(int id) => Guard(() => _taxes.DeactivateAsync(id));

    /// <summary>What a tax adds to an amount (the calculator of the page). Changes nothing; zero when the tax is not in force today.</summary>
    [HttpPost("calculate")]
    [ProducesResponseType<TaxCalculationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "calculateTax")]
    public Task<ActionResult<TaxCalculationDto>> Calculate(TaxCalculationRequest request) => Rules(() => _taxes.CalculateAsync(request));
}
