using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Application.IServices.Products;
using Application.IServices.Reports;
using Application.IServices.Base;
using Application.DTOs.Products;
using Microsoft.AspNetCore.Mvc.Localization;
using Web.Attributes;
using Web.Helpers;
using Core.Entities.Products;
using static Web.Helpers.CurrencyHelper;

namespace Web.Controllers;

[Authorize]
[RequirePermission(PermissionHelper.Permissions.Categories_View)]
public class CategoriesController : BaseController
{
    private readonly IProductCategoryService _categoryService;
    private readonly IViewLocalizer _localizer;
    private readonly IExportService _exportService;
    private readonly ICurrencyService _currencyService;

    public CategoriesController(IProductCategoryService categoryService, IViewLocalizer localizer, IExportService exportService, ICurrencyService currencyService)
    {
        _categoryService = categoryService;
        _localizer = localizer;
        _exportService = exportService;
        _currencyService = currencyService;
    }

    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Categories";
        ViewData["Subtitle"] = "Manage product categories";
        
        // Get categories for display
        var categories = await _categoryService.GetAllAsync();
        ViewBag.Categories = categories;
        
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetCategories(int start = 0, int length = 10, string searchValue = "", string sortColumn = "Name", string sortDirection = "asc")
    {
        try
        {
            var categories = await _categoryService.GetAllAsync();
            var productCounts = await _categoryService.GetProductCountsAsync();

            // Apply search filter
            if (!string.IsNullOrEmpty(searchValue))
            {
                categories = categories.Where(c => 
                    c.Name.Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    (c.Description ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            // Apply sorting
            var sortedCategories = sortDirection.ToLower() == "desc" 
                ? categories.OrderByDescending(c => GetCategoryPropertyValue(c, sortColumn))
                : categories.OrderBy(c => GetCategoryPropertyValue(c, sortColumn));

            // Apply pagination
            var pagedCategories = sortedCategories.Skip(start).Take(length);

            return Json(new
            {
                draw = Request.Query["draw"].FirstOrDefault(),
                recordsTotal = categories.Count,
                recordsFiltered = categories.Count,
                data = pagedCategories.Select(c => new
                {
                    id = c.Id,
                    name = c.Name,
                    description = c.Description,
                    productsCount = productCounts.GetValueOrDefault(c.Id),
                    isActive = c.IsActive, // Return boolean instead of string
                    actions = GetActionButtons(c.Id)
                })
            });
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    private object GetCategoryPropertyValue(ProductCategory category, string propertyName)
    {
        return propertyName switch
        {
            "Name" => category.Name,
            "Description" => category.Description ?? "",
            "IsActive" => category.IsActive,
            _ => category.Name
        };
    }

    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Categories_Create)]
    public async Task<IActionResult> Create()
    {
        ViewData["Title"] = "Create Category";
        ViewData["Subtitle"] = "Add a new product category";
        
        var model = new CreateProductCategoryRequest();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionHelper.Permissions.Categories_Create)]
    public async Task<IActionResult> Create(CreateProductCategoryRequest model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var category = new ProductCategory(model.Name, model.Description)
            {
                IsActive = model.IsActive
            };

            await _categoryService.CreateAsync(category);
            TempData["SuccessMessage"] = "Category created successfully!";
            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"An error occurred: {ex.Message}");
        }

        return View(model);
    }

    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Categories_Edit)]
    public async Task<IActionResult> Edit(int id)
    {
        try
        {
            var category = await _categoryService.GetByIdAsync(id);
            if (category == null)
            {
                TempData["ErrorMessage"] = "Category not found.";
                return RedirectToAction("Index");
            }

            ViewData["Title"] = "Edit Category";
            ViewData["Subtitle"] = $"Edit {category.Name}";
            
            var model = new UpdateProductCategoryRequest
            {
                Id = category.Id,
                Name = category.Name,
                Description = category.Description,
                IsActive = category.IsActive
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
    [RequirePermission(PermissionHelper.Permissions.Categories_Edit)]
    public async Task<IActionResult> Edit(UpdateProductCategoryRequest model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var category = await _categoryService.GetByIdAsync(model.Id);
            if (category == null)
            {
                ModelState.AddModelError(string.Empty, "Category not found.");
                return View(model);
            }

            // Update the category properties
            category.Name = model.Name;
            category.Description = model.Description;
            category.IsActive = model.IsActive;

            await _categoryService.UpdateAsync(model.Id, category);
            TempData["SuccessMessage"] = "Category updated successfully!";
            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"An error occurred: {ex.Message}");
        }

        return View(model);
    }

    [HttpPost]
    [RequirePermission(PermissionHelper.Permissions.Categories_Delete)]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _categoryService.DeleteAsync(id);
            return Json(new { success = true, message = "Category deleted successfully!" });
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

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        try
        {
            var category = await _categoryService.GetByIdWithProductsAsync(id);
            if (category == null)
            {
                TempData["ErrorMessage"] = "Category not found.";
                return RedirectToAction("Index");
            }

            ViewData["Title"] = "Category Details";
            ViewData["Subtitle"] = category.Name;
            
            // Get default currency for aggregate value display
            var defaultCurrency = await _currencyService.GetDefaultAsync();
            var currencyNotation = defaultCurrency?.Notation ?? "$";
            var totalValue = category.Products?.Where(p => !p.IsDeleted).Sum(p => p.StockQuantity * p.SellingPrice) ?? 0;
            ViewBag.TotalValueFormatted = FormatCurrency(totalValue, currencyNotation);
            ViewBag.DefaultCurrencyNotation = currencyNotation;
            
            return View(category);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"An error occurred: {ex.Message}";
            return RedirectToAction("Index");
        }
    }

    private string GetActionButtons(int categoryId)
    {
        return $@"
            <div class='btn-group' role='group'>
                <button type='button' class='btn btn-sm btn-outline-primary' onclick='viewCategory({categoryId})' title='View Details'>
                    <i class='fas fa-eye'></i>
                </button>
                <button type='button' class='btn btn-sm btn-outline-teal' onclick='editCategory({categoryId})' title='Edit'>
                    <i class='fas fa-edit'></i>
                </button>
                <button type='button' class='btn btn-sm btn-outline-danger' onclick='deleteCategory({categoryId})' title='Delete'>
                    <i class='fas fa-trash'></i>
                </button>
            </div>";
    }

    /// <summary>
    /// Export categories to CSV
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ExportCategories()
    {
        try
        {
            var csvData = await _exportService.ExportCategoriesToCsvAsync();
            var fileName = $"categories_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            
            return File(csvData, "text/csv", fileName);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Export failed: {ex.Message}" });
        }
    }
}