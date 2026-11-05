using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Products.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Products;

/// <summary>Everything that was done to one product: stock added or removed, sales, returns, price and detail changes.</summary>
[Route("api/v1/products/{id:int}/history")]
[SwaggerTag("Products")]
public sealed class ProductHistoryController : ApiControllerBase
{
    private readonly IProductHistoryApiService _history;
    private readonly IStringLocalizer<SharedResource> _text;

    public ProductHistoryController(IProductHistoryApiService history, IStringLocalizer<SharedResource> text)
    {
        _history = history;
        _text = text;
    }

    /// <summary>The product's history, newest first, with its lifetime totals.</summary>
    /// <remarks><c>kind</c> narrows it to <c>stock</c>, <c>sales</c> or <c>changes</c>. Only the history since the feature was introduced exists for older products.</remarks>
    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Products_View)]
    [SwaggerOperation(OperationId = "getProductHistory")]
    [ProducesResponseType<ProductHistoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    public async Task<ActionResult<ProductHistoryDto>> Get(int id, [FromQuery] PagedQuery query, [FromQuery] ProductHistoryFilter filter)
        => await _history.GetAsync(id, query, filter) is { } history
            ? history
            : Failure(StatusCodes.Status404NotFound, ApiCodes.NotFound, _text["Prod_NotFound"].Value);
}
