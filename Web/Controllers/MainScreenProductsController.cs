using Application.IServices.Products;
using Core.Entities.Products;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;
using Web.Helpers;

namespace Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(PermissionHelper.Permissions.Products_View)]
public class MainScreenProductsController : ControllerBase
{
    private readonly IMainScreenProductService _service;

    public MainScreenProductsController(IMainScreenProductService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var products = await _service.GetMainScreenProductsAsync();
        return Ok(products);
    }

    [HttpGet("pinned")]
    public async Task<IActionResult> GetPinned()
    {
        var products = await _service.GetPinnedProductsAsync();
        return Ok(products);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var product = await _service.GetByIdAsync(id);
        if (product == null) return NotFound();
        return Ok(product);
    }

    [HttpGet("by-product/{productId:int}")]
    public async Task<IActionResult> GetByProductId(int productId)
    {
        var product = await _service.GetByProductIdAsync(productId);
        if (product == null) return NotFound();
        return Ok(product);
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? searchTerm = null, [FromQuery] int limit = 20)
    {
        var products = await _service.SearchProductsForMainScreenAsync(searchTerm ?? "", limit);
        return Ok(products);
    }

    [HttpPost("pin")]
    [RequirePermission(PermissionHelper.Permissions.Products_Edit)]
    public async Task<IActionResult> PinProduct([FromBody] PinProductRequest request)
    {
        await _service.PinProductAsync(request.ProductId, request.DisplayOrder);
        return Ok(new { message = "Product pinned successfully" });
    }

    [HttpPost("unpin")]
    [RequirePermission(PermissionHelper.Permissions.Products_Edit)]
    public async Task<IActionResult> UnpinProduct([FromBody] UnpinProductRequest request)
    {
        await _service.UnpinProductAsync(request.ProductId);
        return Ok(new { message = "Product unpinned successfully" });
    }

    [HttpPut("reorder")]
    [RequirePermission(PermissionHelper.Permissions.Products_Edit)]
    public async Task<IActionResult> ReorderProducts([FromBody] ReorderProductsRequest request)
    {
        await _service.ReorderProductsAsync(request.ProductIds);
        return Ok(new { message = "Products reordered successfully" });
    }

    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Products_Edit)]
    public async Task<IActionResult> Update(int id, [FromBody] MainScreenProduct product)
    {
        product.Id = id;
        await _service.UpdateAsync(product);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Products_Delete)]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }
}

public record PinProductRequest(int ProductId, int DisplayOrder);
public record UnpinProductRequest(int ProductId);
public record ReorderProductsRequest(List<int> ProductIds);
