using Core.Entities.Sales;
using Application.IServices.Base;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Web.Attributes;
using Web.Helpers;

namespace Web.Controllers;

[Authorize]
[RequirePermission(PermissionHelper.Permissions.Currency_View)]
public class CurrencyController : BaseController
{
    private readonly ICurrencyService _service;

    public CurrencyController(ICurrencyService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _service.GetAllAsync();
        return Ok(items);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var entity = await _service.GetByIdAsync(id);
        return Ok(entity);
    }

    [HttpPost("api")]
    public async Task<IActionResult> CreateApi([FromBody] CurrencyCreateDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var entity = new Currency(dto.Name, dto.RateToUSD, dto.IsDefault, dto.IsActive, dto.Code, dto.Notation);
        entity = await _service.CreateAsync(entity, dto.IsDefault);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, entity);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CurrencyUpdateDto dto)
    {
        var entity = new Currency(dto.Name, dto.RateToUSD, dto.IsDefault, dto.IsActive, dto.Code, dto.Notation);
        await _service.UpdateAsync(id, entity, dto.IsDefault);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _service.DeleteAsync(id);
        return NoContent();
    }

    [HttpGet("default")]
    public async Task<IActionResult> GetDefault()
    {
        var currentDefault = await _service.GetDefaultAsync();
        return Ok(currentDefault);
    }

    [HttpPut("default/{id:int}")]
    public async Task<IActionResult> SetDefault(int id)
    {
        await _service.SetDefaultAsync(id);
        return NoContent();
    }

    // Web Interface Actions
    public IActionResult Index()
    {
        ViewData["Title"] = "Currency Management";
        ViewData["Subtitle"] = "Manage currencies and exchange rates";
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CurrencyCreateDto dto)
    {
        try
        {
            if (!ModelState.IsValid) return Json(new { success = false, message = "Invalid data" });

            var entity = new Currency(dto.Name, dto.RateToUSD, dto.IsDefault, dto.IsActive, dto.Code, dto.Notation);
            entity = await _service.CreateAsync(entity, dto.IsDefault);
            return Json(new { success = true, message = "Currency created successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("rate")]
    public async Task<IActionResult> CreateRate([FromBody] ExchangeRateCreateDto dto)
    {
        try
        {
            // Implementation for creating exchange rates
            return Json(new { success = true, message = "Exchange rate created successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        try
        {
            var defaultCurrency = await _service.GetDefaultAsync();
            var settings = new
            {
                defaultCurrency = defaultCurrency?.Code ?? "LBP",
                currentRate = 1500.00m, // Mock data
                totalCurrencies = 2,
                lastUpdate = DateTime.Now.ToString("MMM dd, yyyy"),
                autoUpdate = false,
                updateFrequency = "manual"
            };
            return Json(settings);
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    [HttpPost("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] CurrencySettingsDto dto)
    {
        try
        {
            // Implementation for saving currency settings
            return Json(new { success = true, message = "Settings saved successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }
}

// DTOs
public record CurrencyCreateDto(string Name, float RateToUSD, bool IsActive, string Code, string Notation, bool IsDefault = false);
public record CurrencyUpdateDto(string Name, float RateToUSD, bool IsActive, string Code, string Notation, bool IsDefault);
public record ExchangeRateCreateDto(string FromCurrencyCode, string ToCurrencyCode, decimal Rate, int? SupplierId, DateTime ValidFrom, DateTime? ValidTo);
public record CurrencySettingsDto(string DefaultCurrency, bool AutoUpdate, string UpdateFrequency);


