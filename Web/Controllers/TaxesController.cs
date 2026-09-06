using Application.IServices.Sales;
using Core.Entities.Sales;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;
using Web.Helpers;

namespace Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(PermissionHelper.Permissions.Tax_View)]
public class TaxesController : ControllerBase
{
    private readonly ITaxService _service;

    public TaxesController(ITaxService service)
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
        var defaultTax = await _service.GetDefaultBySupplierIdAsync(supplierId);
        return Ok(defaultTax);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var entity = await _service.GetByIdAsync(id);
        return Ok(entity);
    }

    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Tax_Create)]
    public async Task<IActionResult> Create([FromBody] Tax tax)
    {
        var entity = await _service.CreateAsync(tax);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, entity);
    }

    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Tax_Edit)]
    public async Task<IActionResult> Update(int id, [FromBody] Tax tax)
    {
        await _service.UpdateAsync(id, tax);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Tax_Delete)]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }
}
