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
    private readonly ISupplierDebtService _supplierDebtService;
    private readonly IViewLocalizer _localizer;
    private readonly IExportService _exportService;
    private readonly ICurrencyService _currencyService;

    public SuppliersController(ISupplierService supplierService, ISupplierDebtService supplierDebtService, IViewLocalizer localizer, IExportService exportService, ICurrencyService currencyService)
    {
        _supplierService = supplierService;
        _supplierDebtService = supplierDebtService;
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

            // The list query doesn't load Supplier.Currency, so resolve notations by id for the balance column.
            var currencyNotations = (await _currencyService.GetAllAsync()).ToDictionary(c => c.Id, c => c.Notation);

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
                    balance = s.Balance,
                    currency = currencyNotations.GetValueOrDefault(s.CurrencyId),
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

    /// <summary>
    /// Records a manual debt adjustment (editing what the business owes the supplier, or what
    /// the supplier owes the business) from the supplier's list or Details page.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionHelper.Permissions.Suppliers_Edit)]
    public async Task<IActionResult> AddDebtTransaction(AddSupplierDebtTransactionRequest model)
    {
        if (!ModelState.IsValid)
        {
            var firstError = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
            return Json(new { success = false, message = firstError ?? "Invalid request." });
        }

        try
        {
            var signedAmount = model.IOweMore ? model.Amount : -model.Amount;
            await _supplierDebtService.AddManualTransactionAsync(model.SupplierId, GetCurrentUserId(), signedAmount, model.Note);
            return Json(new { success = true, message = "Debt updated successfully!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"An error occurred: {ex.Message}" });
        }
    }

    private int GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Unable to determine the current user from the authentication claims.");
        }
        return userId;
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
                <button type='button' class='btn btn-sm btn-outline-warning' onclick='addSupplierDebt({supplierId})' title='Add Debt Transaction'>
                    <i class='fas fa-hand-holding-usd'></i>
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
                DiscountRate = model.DiscountRate
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

            ViewBag.DebtTransactions = await _supplierDebtService.GetTransactionsAsync(id);

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
                DiscountRate = supplier.DiscountRate
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
        catch (InvalidOperationException ex)
        {
            return Json(new { success = false, message = ex.Message });
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