using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Money.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Money;

/// <summary>
/// Currencies. The Razor <c>CurrencyController</c> only asked for <c>Currency.View</c> on every action, so a view-only manager could
/// create, edit and delete currencies; here writes need <c>Currency.Create</c> / <c>Currency.Edit</c> / <c>Currency.Delete</c> like the exchange rates always did.
/// </summary>
[Route("api/v1/currencies")]
[RequirePermission(PermissionHelper.Permissions.Currency_View)]
public sealed class CurrenciesController : MoneyControllerBase
{
    private readonly ICurrencyApiService _currencies;

    public CurrenciesController(ICurrencyApiService currencies) => _currencies = currencies;

    /// <summary>One page of currencies. <c>sort</c>: <c>code</c> (default), <c>name</c>, <c>notation</c>, <c>rateToUsd</c>, <c>isDefault</c>, <c>isActive</c>, <c>createdAt</c>; <c>q</c> matches code, name and symbol.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<CurrencyDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "listCurrencies")]
    public async Task<PagedResult<CurrencyDto>> List([FromQuery] PagedQuery query) => await _currencies.ListAsync(query);

    /// <summary>One currency.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<CurrencyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getCurrency")]
    public async Task<ActionResult<CurrencyDto>> Get(int id) => await _currencies.GetAsync(id) is { } found ? found : NotFound();

    /// <summary>Creates a currency. A taken code answers 400 <c>validation_failed</c> on <c>code</c>. <c>isDefault</c> takes the default away from the previous one.</summary>
    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Currency_Create)]
    [ProducesResponseType<CurrencyDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "createCurrency")]
    public Task<ActionResult<CurrencyDto>> Create(CurrencyRequest request) => GuardCreated(() => _currencies.CreateAsync(request), c => c.Id);

    /// <summary>Changes a currency, including its rate. 409 <c>currency_default_required</c> when asked to stop being the default (make another one the default instead).</summary>
    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Currency_Edit)]
    [ProducesResponseType<CurrencyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "updateCurrency")]
    public Task<ActionResult<CurrencyDto>> Update(int id, CurrencyRequest request) => Guard(() => _currencies.UpdateAsync(id, request));

    /// <summary>Makes this currency the default (the old <c>PUT default/{id}</c>). 409 <c>currency_inactive</c> for an inactive one.</summary>
    [HttpPut("{id:int}/default")]
    [RequirePermission(PermissionHelper.Permissions.Currency_Edit)]
    [ProducesResponseType<CurrencyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "setDefaultCurrency")]
    public Task<ActionResult<CurrencyDto>> SetDefault(int id) => Guard(() => _currencies.SetDefaultAsync(id));

    /// <summary>Deletes a currency. 409 <c>currency_is_default</c> for the default one; 409 <c>currency_in_use</c> while suppliers, receipts or exchange rates point at it (mark it inactive instead).</summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Currency_Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "deleteCurrency")]
    public Task<IActionResult> Delete(int id) => Done(() => _currencies.DeleteAsync(id));

    /// <summary>What the till would show for an amount in another currency (USD bridge, 2 decimals, banker's rounding). Changes nothing.</summary>
    [HttpPost("convert")]
    [ProducesResponseType<CurrencyConversionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "convertCurrency")]
    public Task<ActionResult<CurrencyConversionDto>> Convert(CurrencyConvertRequest request) => Rules(() => _currencies.ConvertAsync(request));
}
