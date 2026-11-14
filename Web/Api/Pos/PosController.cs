using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Web.Api.Infrastructure;
using Web.Api.Pos.Contracts;
using Web.Attributes;
using Web.Helpers;

namespace Web.Api.Pos;

/// <summary>
/// The point of sale's reading side: what to show (catalog, search, price choices), the reference lists around the till, and the server's
/// price of a cart. Open to every signed-in user, like the old <c>POSController</c> (the page itself is gated by the nav); only creating a customer needs its permission.
/// </summary>
[Route("api/v1/pos")]
public sealed class PosController : PosControllerBase
{
    private readonly IPosCatalogService _catalog;
    private readonly IPosLookupService _lookup;
    private readonly IPosPricingService _pricing;

    public PosController(IPosCatalogService catalog, IPosLookupService lookup, IPosPricingService pricing)
    {
        _catalog = catalog;
        _lookup = lookup;
        _pricing = pricing;
    }

    // ---- catalog ---------------------------------------------------------------------------------------------------

    /// <summary>Categories that have something to sell, for the chip row.</summary>
    [HttpGet("categories")]
    [AnySignedInUser]
    [ProducesResponseType<IReadOnlyList<PosCategoryDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getPosCategories")]
    public async Task<ActionResult<IReadOnlyList<PosCategoryDto>>> Categories() => Ok(await _catalog.GetCategoriesAsync());

    /// <summary>A page of the product grid.</summary>
    /// <remarks>
    /// <c>mainScreen=true</c> returns the shop's configured display order (the old "main screen" tiles) and falls back to every active product when none is set
    /// (<c>isMainScreen</c> says which). Otherwise products are sorted by name. <c>q</c> filters by name or barcode; <c>categoryId</c> by category. <c>sort</c> only accepts <c>name</c>.
    /// </remarks>
    [HttpGet("products")]
    [AnySignedInUser]
    [ProducesResponseType<PosProductPageDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getPosProducts")]
    public async Task<ActionResult<PosProductPageDto>> Products([FromQuery] PagedQuery query, [FromQuery] int? categoryId = null, [FromQuery] bool mainScreen = false)
    {
        if (!string.IsNullOrEmpty(query.Sort) && !string.Equals(query.Sort, "name", StringComparison.OrdinalIgnoreCase))
            return ValidationFailure("sort", "Only 'name' is supported.");
        return await _catalog.GetProductsAsync(categoryId, mainScreen, query);
    }

    /// <summary>Product search for the search box and the barcode scanner.</summary>
    /// <remarks>
    /// An exact barcode wins (one item, <c>searchType: barcode</c>). Otherwise name matches come back 20 at a time: ask again with <c>skip</c> = items received so far while <c>hasMore</c>.
    /// <c>barcodeOnly=true</c> (the scanner) never falls back to a name search and answers 404 <c>product_not_found</c> for an unknown code.
    /// </remarks>
    [HttpGet("products/search")]
    [AnySignedInUser]
    [ProducesResponseType<PosSearchResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "searchPosProducts")]
    public async Task<ActionResult<PosSearchResultDto>> Search(
        [FromQuery, Required, StringLength(100, MinimumLength = 1)] string q, [FromQuery] bool barcodeOnly = false, [FromQuery, Range(0, int.MaxValue)] int skip = 0)
    {
        if (string.IsNullOrWhiteSpace(q)) return ValidationFailure("q", "A search term is required.");

        var result = await _catalog.SearchAsync(q, barcodeOnly, skip);
        if (barcodeOnly && result.Items.Count == 0)
            return Failure(StatusCodes.Status404NotFound, PosCodes.ProductNotFound);
        return result;
    }

    /// <summary>One product (from the POS cache, so it is quick and carries the current stock).</summary>
    [HttpGet("products/{id:int}")]
    [AnySignedInUser]
    [ProducesResponseType<PosProductDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getPosProduct")]
    public async Task<ActionResult<PosProductDto>> Product(int id)
        => await _catalog.GetProductAsync(id) is { } product ? product : Failure(StatusCodes.Status404NotFound, PosCodes.ProductNotFound);

    /// <summary>The prices a cashier can choose for a product, converted into <c>currencyId</c>, with the lowest custom price allowed.</summary>
    [HttpGet("products/{id:int}/prices")]
    [AnySignedInUser]
    [ProducesResponseType<PosPriceOptionsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "getPosProductPrices")]
    public async Task<ActionResult<PosPriceOptionsDto>> Prices(int id, [FromQuery, Range(1, int.MaxValue)] int currencyId)
        => await _catalog.GetPriceOptionsAsync(id, currencyId) is { } options ? options : Failure(StatusCodes.Status404NotFound, PosCodes.ProductNotFound);

    // ---- reference lists -------------------------------------------------------------------------------------------

    /// <summary>Active currencies; the default one is first.</summary>
    [HttpGet("currencies")]
    [AnySignedInUser]
    [ProducesResponseType<IReadOnlyList<PosCurrencyDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getPosCurrencies")]
    public async Task<ActionResult<IReadOnlyList<PosCurrencyDto>>> Currencies() => Ok(await _lookup.GetCurrenciesAsync());

    /// <summary>Payment methods for the checkout dialog. The system "Customer Debt" method is not listed: send <c>addToDebt</c> instead.</summary>
    [HttpGet("payment-methods")]
    [AnySignedInUser]
    [ProducesResponseType<IReadOnlyList<PosPaymentMethodDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getPosPaymentMethods")]
    public async Task<ActionResult<IReadOnlyList<PosPaymentMethodDto>>> PaymentMethods() => Ok(await _lookup.GetPaymentMethodsAsync());

    /// <summary>The stored discounts the cashier can apply (percentages).</summary>
    [HttpGet("discounts")]
    [AnySignedInUser]
    [ProducesResponseType<IReadOnlyList<PosDiscountDto>>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getPosDiscounts")]
    public async Task<ActionResult<IReadOnlyList<PosDiscountDto>>> Discounts() => Ok(await _lookup.GetDiscountsAsync());

    /// <summary>One page of customers for the pickers, lazy-loaded as the cashier scrolls. <c>q</c> filters by name; pages are 1-based, at most 50 rows.</summary>
    [HttpGet("customers")]
    [AnySignedInUser]
    [ProducesResponseType<PosCustomerPageDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "searchPosCustomers")]
    public async Task<ActionResult<PosCustomerPageDto>> Customers([FromQuery] PagedQuery query)
        => await _lookup.SearchCustomersAsync(query.Q, query.Page, Math.Min(query.PageSize, PosLookupService.MaxCustomerPageSize));

    /// <summary>Adds a customer from inside the checkout / hold dialogs so the cashier never leaves (and loses) the cart. First and last name only.</summary>
    [HttpPost("customers")]
    [RequirePermission(PermissionHelper.Permissions.Customers_Create)]
    [ProducesResponseType<PosCustomerDto>(StatusCodes.Status201Created)]
    [SwaggerOperation(OperationId = "quickCreatePosCustomer")]
    public async Task<ActionResult<PosCustomerDto>> CreateCustomer(PosQuickCustomerRequest request)
    {
        var created = await _lookup.QuickCreateCustomerAsync(request.FirstName, request.LastName, request.PhoneNumber);
        return Created($"/api/v1/pos/customers?q={Uri.EscapeDataString(created.Name)}", created);
    }

    /// <summary>The signed-in user's cash drawer, for the "open your drawer first" prompt. See <c>requireOpenDrawer</c> on checkout.</summary>
    [HttpGet("drawer")]
    [AnySignedInUser]
    [ProducesResponseType<PosDrawerDto>(StatusCodes.Status200OK)]
    [SwaggerOperation(OperationId = "getPosDrawer")]
    public async Task<ActionResult<PosDrawerDto>> Drawer() => await _lookup.GetDrawerAsync(CurrentUserId);

    // ---- pricing ---------------------------------------------------------------------------------------------------

    /// <summary>The server's price of a cart: unit prices in the cart currency, line totals, discount, total, stock warnings.</summary>
    /// <remarks>
    /// Call it (debounced) whenever the cart changes and show what it says. A POST because the cart is the body; it writes nothing.
    /// Refusals: 404 <c>product_not_found</c>; 400 <c>price_not_available</c> (a price tier that is not the product's), <c>price_below_minimum</c> (custom price under cost),
    /// <c>invalid_discount</c>, <c>duplicate_product</c> (a product on two lines), <c>currency_not_found</c>.
    /// </remarks>
    [HttpPost("price")]
    [AnySignedInUser]
    [ProducesResponseType<PosPricedCartDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, ApiProblem.ContentType)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, ApiProblem.ContentType)]
    [SwaggerOperation(OperationId = "pricePosCart")]
    public async Task<ActionResult<PosPricedCartDto>> Price(PosCartInput cart) => await _pricing.PriceAsync(cart);
}
