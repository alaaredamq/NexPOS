using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Money.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Money;

/// <summary>Pair-specific exchange rates. Permissions as the legacy <c>ExchangeRatesController</c>: <c>Currency.View</c> to read, <c>Currency.Create</c> / <c>Edit</c> / <c>Delete</c> to change.</summary>
[Route("api/v1/exchange-rates")]
[RequirePermission(PermissionHelper.Permissions.Currency_View)]
public sealed class ExchangeRatesController : MoneyControllerBase
{
    private readonly IExchangeRateApiService _rates;

    public ExchangeRatesController(IExchangeRateApiService rates) => _rates = rates;

    /// <summary>
    /// One page of rates. <c>sort</c>: <c>from</c> (default), <c>to</c>, <c>rate</c>, <c>supplier</c>, <c>createdAt</c>; <c>q</c> matches the currency codes and the supplier;
    /// <c>supplierId</c> keeps one supplier's rates.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ExchangeRateDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listExchangeRates")]
    public async Task<PagedResult<ExchangeRateDto>> List([FromQuery] PagedQuery query, [FromQuery] int? supplierId) => await _rates.ListAsync(query, supplierId);

    /// <summary>Suppliers a rate can be limited to.</summary>
    [HttpGet("suppliers")]
    [ProducesResponseType<IReadOnlyList<MoneyOptionDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listExchangeRateSuppliers")]
    public async Task<IReadOnlyList<MoneyOptionDto>> Suppliers() => await _rates.SupplierOptionsAsync();

    /// <summary>One rate.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<ExchangeRateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getExchangeRate")]
    public async Task<ActionResult<ExchangeRateDto>> Get(int id) => await _rates.GetAsync(id) is { } found ? found : NotFound();

    /// <summary>Creates a rate. Two different currencies, a rate above zero, and no other rate for the same pair and supplier (400 <c>validation_failed</c> on <c>toCurrencyId</c>).</summary>
    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Currency_Create)]
    [ProducesResponseType<ExchangeRateDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "createExchangeRate")]
    public Task<ActionResult<ExchangeRateDto>> Create(ExchangeRateRequest request) => GuardCreated(() => _rates.CreateAsync(request), r => r.Id);

    /// <summary>Changes a rate (same rules as creating one).</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Currency_Edit)]
    [ProducesResponseType<ExchangeRateDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "updateExchangeRate")]
    public Task<ActionResult<ExchangeRateDto>> Update(int id, ExchangeRateRequest request) => Guard(() => _rates.UpdateAsync(id, request));

    /// <summary>Deletes a rate; prices fall back to the USD bridge.</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Currency_Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deleteExchangeRate")]
    public Task<IActionResult> Delete(int id) => Done(() => _rates.DeleteAsync(id));
}
