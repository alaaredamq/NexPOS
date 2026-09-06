using Application.IServices.Sales;
using Core.Entities.Sales;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;
using Web.Helpers;

namespace Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(PermissionHelper.Permissions.Discounts_View)]
public class DiscountsController : ControllerBase
{
    private readonly IDiscountService _service;

    public DiscountsController(IDiscountService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? supplierId)
    {
        var items = supplierId.HasValue 
            ? await _service.GetBySupplierIdAsync(supplierId.Value)
            : await _service.GetAllAsync();
        return Ok(items);
    }

    [HttpGet("default")]
    public async Task<IActionResult> GetDefault([FromQuery] int? supplierId)
    {
        var defaultDiscount = await _service.GetDefaultBySupplierIdAsync(supplierId);
        return Ok(defaultDiscount);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var entity = await _service.GetByIdAsync(id);
        return Ok(entity);
    }

    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Discounts_Manage)]
    public async Task<IActionResult> Create([FromBody] Discount discount)
    {
        var entity = await _service.CreateAsync(discount);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, entity);
    }

    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Discounts_Manage)]
    public async Task<IActionResult> Update(int id, [FromBody] Discount discount)
    {
        await _service.UpdateAsync(id, discount);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Discounts_Manage)]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }
}
