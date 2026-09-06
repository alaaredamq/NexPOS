using Application.IServices.Inventory;
using Core.Entities.Inventory;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;
using Web.Helpers;

namespace Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(PermissionHelper.Permissions.POS_Manage)]
public class CartsController : ControllerBase
{
    private readonly ICartService _service;

    public CartsController(ICartService service)
    {
        _service = service;
    }

    [HttpGet("customer/{customerId:int}/active")]
    public async Task<IActionResult> GetActiveCart(int customerId)
    {
        var cart = await _service.GetActiveCartByCustomerIdAsync(customerId);
        if (cart == null) return NotFound();
        return Ok(cart);
    }

    [HttpGet("customer/{customerId:int}")]
    public async Task<IActionResult> GetCartsByCustomer(int customerId)
    {
        var carts = await _service.GetCartsByCustomerIdAsync(customerId);
        return Ok(carts);
    }

    [HttpPost("customer/{customerId:int}")]
    public async Task<IActionResult> CreateCart(int customerId)
    {
        var cart = await _service.CreateCartAsync(customerId);
        return CreatedAtAction(nameof(GetActiveCart), new { customerId }, cart);
    }

    [HttpPost("{cartId:int}/items")]
    public async Task<IActionResult> AddItem(int cartId, [FromBody] AddItemRequest request)
    {
        var cart = await _service.AddItemToCartAsync(cartId, request.ProductId, request.Quantity, request.UnitPrice, request.WeightOrVolume);
        return Ok(cart);
    }

    [HttpPut("{cartId:int}/items/{productId:int}")]
    public async Task<IActionResult> UpdateItemQuantity(int cartId, int productId, [FromBody] UpdateQuantityRequest request)
    {
        var cart = await _service.UpdateCartItemQuantityAsync(cartId, productId, request.Quantity, request.WeightOrVolume);
        return Ok(cart);
    }

    [HttpDelete("{cartId:int}/items/{productId:int}")]
    public async Task<IActionResult> RemoveItem(int cartId, int productId)
    {
        var cart = await _service.RemoveItemFromCartAsync(cartId, productId);
        return Ok(cart);
    }

    [HttpPost("{cartId:int}/hold")]
    public async Task<IActionResult> HoldCart(int cartId)
    {
        var cart = await _service.HoldCartAsync(cartId);
        return Ok(cart);
    }

    [HttpPost("{cartId:int}/resume")]
    public async Task<IActionResult> ResumeCart(int cartId)
    {
        var cart = await _service.ResumeCartAsync(cartId);
        return Ok(cart);
    }

    [HttpGet("customer/{customerId:int}/on-hold")]
    public async Task<IActionResult> GetOnHoldCarts(int customerId)
    {
        var carts = await _service.GetOnHoldCartsByCustomerIdAsync(customerId);
        return Ok(carts);
    }

    [HttpGet("{cartId:int}/total")]
    public async Task<IActionResult> GetCartTotal(int cartId)
    {
        var total = await _service.CalculateCartTotalAsync(cartId);
        return Ok(new { Total = total });
    }

    [HttpGet("{cartId:int}/total/{currencyId:int}")]
    public async Task<IActionResult> GetCartTotalInCurrency(int cartId, int currencyId)
    {
        var total = await _service.CalculateCartTotalInCurrencyAsync(cartId, currencyId);
        return Ok(new { CartId = cartId, CurrencyId = currencyId, Total = total });
    }

    [HttpPost("{cartId:int}/items/with-currency")]
    public async Task<IActionResult> AddItemWithCurrency(int cartId, [FromBody] AddItemWithCurrencyRequest request)
    {
        var cart = await _service.AddItemToCartWithCurrencyAsync(cartId, request.ProductId, request.Quantity, request.CurrencyId);
        return Ok(cart);
    }

    [HttpPut("{cartId:int}/items/{productId:int}/quantity/with-currency")]
    public async Task<IActionResult> UpdateQuantityWithCurrency(int cartId, int productId, [FromBody] UpdateQuantityWithCurrencyRequest request)
    {
        var cart = await _service.UpdateCartItemQuantityWithCurrencyAsync(cartId, productId, request.Quantity, request.CurrencyId);
        return Ok(cart);
    }
}

public record AddItemRequest(int ProductId, int Quantity, decimal UnitPrice, decimal? WeightOrVolume = null);
public record UpdateQuantityRequest(int Quantity, decimal? WeightOrVolume = null);
public record AddItemWithCurrencyRequest(int ProductId, int Quantity, int CurrencyId);
public record UpdateQuantityWithCurrencyRequest(int Quantity, int CurrencyId);
