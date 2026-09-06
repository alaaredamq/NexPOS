using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Application.IServices.Products;
using Application.IServices.Reports;
using Application.IServices.Base;
using Application.DTOs.Suppliers;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Web.Attributes;
using Web.Helpers;
using Core.Entities.Products;

namespace Web.Controllers;

[Authorize]
[RequirePermission(PermissionHelper.Permissions.Suppliers_View)]
public class SuppliersController : BaseController
{
    private readonly ISupplierService _supplierService;
    private readonly IViewLocalizer _localizer;
    private readonly IExportService _exportService;
    private readonly ICurrencyService _currencyService;

    public SuppliersController(ISupplierService supplierService, IViewLocalizer localizer, IExportService exportService, ICurrencyService currencyService)
    {
        _supplierService = supplierService;
        _localizer = localizer;
        _exportService = exportService;
        _currencyService = currencyService;
    }

    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Suppliers";
        ViewData["Subtitle"] = "Manage supplier information";
        
        // Get suppliers for display
        var suppliers = await _supplierService.GetAllAsync();
        ViewBag.Suppliers = suppliers;
        
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetSuppliers(int start = 0, int length = 10, string searchValue = "", string sortColumn = "Name", string sortDirection = "asc")
    {
        try
        {
            var suppliers = await _supplierService.GetAllAsync();
            
            // Apply search filter
            if (!string.IsNullOrEmpty(searchValue))
            {
                suppliers = suppliers.Where(s => 
                    s.Name.Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    (s.Email ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    (s.Phone ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            // Apply sorting
            var sortedSuppliers = sortDirection.ToLower() == "desc" 
                ? suppliers.OrderByDescending(s => GetSupplierPropertyValue(s, sortColumn))
                : suppliers.OrderBy(s => GetSupplierPropertyValue(s, sortColumn));

            // Apply pagination
            var pagedSuppliers = sortedSuppliers.Skip(start).Take(length);

            return Json(new
            {
                draw = Request.Query["draw"].FirstOrDefault(),
                recordsTotal = suppliers.Count,
                recordsFiltered = suppliers.Count,
                data = pagedSuppliers.Select(s => new
                {
                    id = s.Id,
                    name = s.Name,
                    contactPerson = s.Name, // Supplier doesn't have ContactPerson property
                    email = s.Email,
                    phoneNumber = s.Phone,
                    address = s.Address,
                    isActive = s.IsActive, // Return boolean instead of string
                    debtToSupplier = s.DebtToSupplier,
                    debtFromSupplier = s.DebtFromSupplier,
                    netDebt = s.DebtToSupplier - s.DebtFromSupplier,
                    actions = GetActionButtons(s.Id)
                })
            });
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    private object GetSupplierPropertyValue(Supplier supplier, string propertyName)
    {
        return propertyName switch
        {
            "Name" => supplier.Name,
            "Email" => supplier.Email ?? "",
            "Phone" => supplier.Phone ?? "",
            "IsActive" => supplier.IsActive,
            _ => supplier.Name
        };
    }

    private string GetActionButtons(int supplierId)
    {
        return $@"
            <div class='btn-group' role='group'>
                <button type='button' class='btn btn-sm btn-outline-primary' onclick='viewSupplier({supplierId})' title='View Details'>
                    <i class='fas fa-eye'></i>
                </button>
                <button type='button' class='btn btn-sm btn-outline-teal' onclick='editSupplier({supplierId})' title='Edit'>
                    <i class='fas fa-edit'></i>
                </button>
                <button type='button' class='btn btn-sm btn-outline-danger' onclick='deleteSupplier({supplierId})' title='Delete'>
                    <i class='fas fa-trash'></i>
                </button>
            </div>";
    }

    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Suppliers_Create)]
    public async Task<IActionResult> Create()
    {
        ViewData["Title"] = "Create Supplier";
        ViewData["Subtitle"] = "Add a new supplier to the database";
        
        var currencies = await _currencyService.GetAllAsync();
        ViewBag.Currencies = new SelectList(currencies.Where(c => c.IsActive), "Id", "Name");
        
        var model = new CreateSupplierRequest();
        var defaultCurrency = await _currencyService.GetDefaultAsync();
        if (defaultCurrency != null)
        {
            model.CurrencyId = defaultCurrency.Id;
        }
        
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionHelper.Permissions.Suppliers_Create)]
    public async Task<IActionResult> Create(CreateSupplierRequest model)
    {
        if (!ModelState.IsValid)
        {
            var currencies = await _currencyService.GetAllAsync();
            ViewBag.Currencies = new SelectList(currencies.Where(c => c.IsActive), "Id", "Name");
            return View(model);
        }

        try
        {
            var supplier = new Supplier(model.Name, model.Email, model.Phone, model.Address, model.City, model.Country, model.PostalCode)
            {
                IsActive = model.IsActive,
                CurrencyId = model.CurrencyId,
                DiscountRate = model.DiscountRate,
                DebtToSupplier = model.DebtToSupplier,
                DebtFromSupplier = model.DebtFromSupplier
            };

            await _supplierService.CreateAsync(supplier);
            TempData["SuccessMessage"] = "Supplier created successfully!";
            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"An error occurred: {ex.Message}");
            var currencies = await _currencyService.GetAllAsync();
            ViewBag.Currencies = new SelectList(currencies.Where(c => c.IsActive), "Id", "Name");
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        try
        {
            var supplier = await _supplierService.GetByIdAsync(id);
            if (supplier == null)
            {
                TempData["ErrorMessage"] = "Supplier not found.";
                return RedirectToAction("Index");
            }

            ViewData["Title"] = "Supplier Details";
            ViewData["Subtitle"] = supplier.Name;
            
            return View(supplier);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"An error occurred: {ex.Message}";
            return RedirectToAction("Index");
        }
    }

    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Suppliers_Edit)]
    public async Task<IActionResult> Edit(int id)
    {
        try
        {
            var supplier = await _supplierService.GetByIdAsync(id);
            if (supplier == null)
            {
                TempData["ErrorMessage"] = "Supplier not found.";
                return RedirectToAction("Index");
            }

            ViewData["Title"] = "Edit Supplier";
            ViewData["Subtitle"] = $"Edit {supplier.Name}";
            
            var currencies = await _currencyService.GetAllAsync();
            ViewBag.Currencies = new SelectList(currencies.Where(c => c.IsActive), "Id", "Name");
            
            var model = new UpdateSupplierRequest
            {
                Id = supplier.Id,
                Name = supplier.Name,
                Email = supplier.Email,
                Phone = supplier.Phone,
                Address = supplier.Address,
                City = supplier.City,
                Country = supplier.Country,
                PostalCode = supplier.PostalCode,
                IsActive = supplier.IsActive,
                CurrencyId = supplier.CurrencyId,
                DiscountRate = supplier.DiscountRate,
                DebtToSupplier = supplier.DebtToSupplier,
                DebtFromSupplier = supplier.DebtFromSupplier
            };

            return View(model);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"An error occurred: {ex.Message}";
            return RedirectToAction("Index");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionHelper.Permissions.Suppliers_Edit)]
    public async Task<IActionResult> Edit(UpdateSupplierRequest model)
    {
        if (!ModelState.IsValid)
        {
            var currencies = await _currencyService.GetAllAsync();
            ViewBag.Currencies = new SelectList(currencies.Where(c => c.IsActive), "Id", "Name");
            return View(model);
        }

        try
        {
            var supplier = await _supplierService.GetByIdAsync(model.Id);
            if (supplier == null)
            {
                ModelState.AddModelError(string.Empty, "Supplier not found.");
                var currencies = await _currencyService.GetAllAsync();
                ViewBag.Currencies = new SelectList(currencies.Where(c => c.IsActive), "Id", "Name");
                return View(model);
            }

            // Update the supplier properties
            supplier.Name = model.Name;
            supplier.Email = model.Email;
            supplier.Phone = model.Phone;
            supplier.Address = model.Address;
            supplier.City = model.City;
            supplier.Country = model.Country;
            supplier.PostalCode = model.PostalCode;
            supplier.IsActive = model.IsActive;
            supplier.CurrencyId = model.CurrencyId;
            supplier.DiscountRate = model.DiscountRate;
            supplier.DebtToSupplier = model.DebtToSupplier;
            supplier.DebtFromSupplier = model.DebtFromSupplier;

            await _supplierService.UpdateAsync(model.Id, supplier);
            TempData["SuccessMessage"] = "Supplier updated successfully!";
            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"An error occurred: {ex.Message}");
            var currencies = await _currencyService.GetAllAsync();
            ViewBag.Currencies = new SelectList(currencies.Where(c => c.IsActive), "Id", "Name");
        }

        return View(model);
    }

    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Suppliers_Delete)]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _supplierService.DeleteAsync(id);
            return Json(new { success = true, message = "Supplier deleted successfully!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"An error occurred: {ex.Message}" });
        }
    }

    /// <summary>
    /// Export suppliers to CSV
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ExportSuppliers()
    {
        try
        {
            var csvData = await _exportService.ExportSuppliersToCsvAsync();
            var fileName = $"suppliers_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            
            return File(csvData, "text/csv", fileName);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Export failed: {ex.Message}" });
        }
    }
}