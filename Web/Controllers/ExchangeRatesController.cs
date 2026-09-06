using Application.IServices.Base;
using Core.Entities.Sales;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;
using Web.Helpers;

namespace Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[RequirePermission(PermissionHelper.Permissions.Currency_View)]
public class ExchangeRatesController : ControllerBase
{
    private readonly IExchangeRateService _service;

    public ExchangeRatesController(IExchangeRateService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? supplierId)
    {
        var items = await _service.GetAllAsync(supplierId);
        return Ok(items);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var entity = await _service.GetByIdAsync(id);
        return Ok(entity);
    }

    public record ExchangeRateDto(int FromCurrencyId, int ToCurrencyId, decimal Rate, int? SupplierId);

    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Currency_Create)]
    public async Task<IActionResult> Create([FromBody] ExchangeRateDto dto)
    {
        var entity = new ExchangeRate(dto.FromCurrencyId, dto.ToCurrencyId, dto.Rate, dto.SupplierId);
        entity = await _service.CreateAsync(entity);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, entity);
    }

    [HttpPut("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Currency_Edit)]
    public async Task<IActionResult> Update(int id, [FromBody] ExchangeRateDto dto)
    {
        var entity = new ExchangeRate(dto.FromCurrencyId, dto.ToCurrencyId, dto.Rate, dto.SupplierId);
        await _service.UpdateAsync(id, entity);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [RequirePermission(PermissionHelper.Permissions.Currency_Delete)]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }
}


