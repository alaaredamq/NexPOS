using Application.IServices.Products;
using Core.Entities.Products;
using Core.Enum;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;
using Web.Helpers;

namespace Web.Controllers;

[Authorize]
[RequirePermission(PermissionHelper.Permissions.Tax_View)]
public class TaxController : BaseController
{
    private readonly ITaxService _taxService;

    public TaxController(ITaxService taxService)
    {
        _taxService = taxService;
    }

    public IActionResult Index()
    {
        ViewData["Title"] = "Tax Management";
        ViewData["Subtitle"] = "Manage tax rates and VAT settings";
        return View();
    }

    [HttpGet("api/taxes")]
    public async Task<IActionResult> GetAllTaxes()
    {
        try
        {
            var taxes = await _taxService.GetAllTaxesAsync();
            return Json(taxes);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("api/taxes/active")]
    public async Task<IActionResult> GetActiveTaxes()
    {
        try
        {
            var taxes = await _taxService.GetActiveTaxesAsync();
            return Json(taxes);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("api/taxes/{id}")]
    public async Task<IActionResult> GetTaxById(int id)
    {
        try
        {
            var tax = await _taxService.GetTaxByIdAsync(id);
            if (tax == null) return NotFound();
            return Json(tax);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("api/taxes")]
    [RequirePermission(PermissionHelper.Permissions.Tax_Create)]
    public async Task<IActionResult> CreateTax([FromBody] CreateTaxDto dto)
    {
        try
        {
            if (!ModelState.IsValid) return Json(new { success = false, message = "Invalid data" });

            var tax = new TaxRate(dto.Name, dto.Rate, dto.Type, dto.IsDefault, dto.IsActive)
            {
                Description = dto.Description,
                TaxCode = dto.TaxCode,
                IsInclusive = dto.IsInclusive,
                CategoryId = dto.CategoryId,
                ApplyToAllProducts = dto.ApplyToAllProducts,
                EffectiveFrom = dto.EffectiveFrom,
                EffectiveTo = dto.EffectiveTo
            };

            tax = await _taxService.CreateTaxAsync(tax);
            return Json(new { success = true, message = "Tax created successfully", taxId = tax.Id });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPut("api/taxes/{id}")]
    [RequirePermission(PermissionHelper.Permissions.Tax_Edit)]
    public async Task<IActionResult> UpdateTax(int id, [FromBody] UpdateTaxDto dto)
    {
        try
        {
            if (!ModelState.IsValid) return Json(new { success = false, message = "Invalid data" });

            var existingTax = await _taxService.GetTaxByIdAsync(id);
            if (existingTax == null) return NotFound();

            existingTax.Name = dto.Name;
            existingTax.Description = dto.Description;
            existingTax.Rate = dto.Rate;
            existingTax.Type = dto.Type;
            existingTax.IsDefault = dto.IsDefault;
            existingTax.IsActive = dto.IsActive;
            existingTax.TaxCode = dto.TaxCode;
            existingTax.IsInclusive = dto.IsInclusive;
            existingTax.CategoryId = dto.CategoryId;
            existingTax.ApplyToAllProducts = dto.ApplyToAllProducts;
            existingTax.EffectiveFrom = dto.EffectiveFrom;
            existingTax.EffectiveTo = dto.EffectiveTo;

            await _taxService.UpdateTaxAsync(id, existingTax);
            return Json(new { success = true, message = "Tax updated successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpDelete("api/taxes/{id}")]
    [RequirePermission(PermissionHelper.Permissions.Tax_Delete)]
    public async Task<IActionResult> DeleteTax(int id)
    {
        try
        {
            await _taxService.DeleteTaxAsync(id);
            return Json(new { success = true, message = "Tax deleted successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("api/taxes/{id}/set-default")]
    [RequirePermission(PermissionHelper.Permissions.Tax_Manage)]
    public async Task<IActionResult> SetDefaultTax(int id)
    {
        try
        {
            var success = await _taxService.SetDefaultTaxAsync(id);
            if (success)
                return Json(new { success = true, message = "Default tax updated successfully" });
            else
                return Json(new { success = false, message = "Failed to set default tax" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("api/taxes/{id}/deactivate")]
    [RequirePermission(PermissionHelper.Permissions.Tax_Manage)]
    public async Task<IActionResult> DeactivateTax(int id)
    {
        try
        {
            var success = await _taxService.DeactivateTaxAsync(id);
            if (success)
                return Json(new { success = true, message = "Tax deactivated successfully" });
            else
                return Json(new { success = false, message = "Failed to deactivate tax" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("api/taxes/calculate")]
    public async Task<IActionResult> CalculateTax([FromBody] TaxCalculationRequest request)
    {
        try
        {
            decimal taxAmount = 0;
            decimal totalAmount = request.BaseAmount;

            if (request.TaxId.HasValue)
            {
                taxAmount = await _taxService.CalculateTaxAmountAsync(request.BaseAmount, request.TaxId, null);
                totalAmount = await _taxService.CalculateAmountWithTaxAsync(request.BaseAmount, request.TaxId, null);
            }
            else if (request.TaxType.HasValue)
            {
                taxAmount = await _taxService.CalculateTaxAmountAsync(request.BaseAmount, null, request.TaxType);
                totalAmount = await _taxService.CalculateAmountWithTaxAsync(request.BaseAmount, null, request.TaxType);
            }

            return Json(new { 
                success = true, 
                baseAmount = request.BaseAmount,
                taxAmount = taxAmount,
                totalAmount = totalAmount
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }
}

// DTOs
public record CreateTaxDto(
    string Name,
    decimal Rate,
    TaxType Type,
    bool IsDefault,
    bool IsActive,
    string? Description = null,
    string? TaxCode = null,
    bool IsInclusive = false,
    int? CategoryId = null,
    bool ApplyToAllProducts = true,
    DateTime? EffectiveFrom = null,
    DateTime? EffectiveTo = null
);

public record UpdateTaxDto(
    string Name,
    string? Description,
    decimal Rate,
    TaxType Type,
    bool IsDefault,
    bool IsActive,
    string? TaxCode,
    bool IsInclusive,
    int? CategoryId,
    bool ApplyToAllProducts,
    DateTime? EffectiveFrom,
    DateTime? EffectiveTo
);

public record TaxCalculationRequest(
    decimal BaseAmount,
    int? TaxId = null,
    TaxType? TaxType = null
);
