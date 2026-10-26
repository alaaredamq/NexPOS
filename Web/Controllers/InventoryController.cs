using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Application.IServices.Products;
using Application.IServices.Sales;
using Application.DTOs.Products;
using Web.Attributes;
using Web.Helpers;
using Core.Entities.Products;
using Application.Services.Printing;
using Core.Printing;
using static Web.Helpers.CurrencyHelper;

namespace Web.Controllers;

[Authorize]
[RequirePermission(PermissionHelper.Permissions.Inventory_View)]
public class InventoryController : BaseController
{
    private readonly IProductService _productService;
    private readonly IProductCategoryService _categoryService;
    private readonly Application.IServices.Base.ICurrencyService _currencyService;
    private readonly IThermalPrinterService _thermalPrinterService;

    public InventoryController(
        IProductService productService, 
        IProductCategoryService categoryService,
        Application.IServices.Base.ICurrencyService currencyService,
        IThermalPrinterService thermalPrinterService)
    {
        _productService = productService;
        _categoryService = categoryService;
        _currencyService = currencyService;
        _thermalPrinterService = thermalPrinterService;
    }

    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Inventory Management";
        ViewData["Subtitle"] = "Track stock levels and manage inventory";
        
        // Get inventory stats
        var products = await _productService.GetAllAsync();
        var lowStockProducts = products.Where(p => p.MinimumStock.HasValue && p.StockQuantity <= p.MinimumStock.Value).ToList();
        var outOfStockProducts = products.Where(p => p.StockQuantity <= 0).ToList();
        var totalProducts = products.Count();
        var totalStockValue = products.Sum(p => p.StockQuantity * (p.CostPrice ?? p.SellingPrice));
        
        // Get default currency for display
        var defaultCurrency = await _currencyService.GetDefaultAsync();
        var currencyNotation = defaultCurrency?.Notation ?? "$";

        ViewBag.InventoryStats = new
        {
            TotalProducts = totalProducts,
            LowStockCount = lowStockProducts.Count,
            OutOfStockCount = outOfStockProducts.Count,
            TotalStockValue = totalStockValue,
            TotalStockValueFormatted = FormatCurrency(totalStockValue, currencyNotation),
            LowStockProducts = lowStockProducts.Take(5).ToList(),
            OutOfStockProducts = outOfStockProducts.Take(5).ToList()
        };

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetInventory(int start = 0, int length = 10, string searchValue = "", string sortColumn = "Name", string sortDirection = "asc")
    {
        try
        {
            var products = await _productService.GetAllAsync();

            // Apply search filter
            if (!string.IsNullOrEmpty(searchValue))
            {
                products = products.Where(p => 
                    p.Name.Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    p.Barcode.Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    (p.Description ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                    (p.Category?.Name ?? "").Contains(searchValue, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            // Apply sorting
            var sortedProducts = sortDirection.ToLower() == "desc" 
                ? products.OrderByDescending(p => GetInventoryPropertyValue(p, sortColumn))
                : products.OrderBy(p => GetInventoryPropertyValue(p, sortColumn));

            // Apply pagination
            var pagedProducts = sortedProducts.Skip(start).Take(length);

            return Json(new
            {
                draw = Request.Query["draw"].FirstOrDefault(),
                recordsTotal = products.Count,
                recordsFiltered = products.Count,
                data = pagedProducts.Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    barcode = p.Barcode,
                    categoryName = p.Category?.Name ?? "No Category",
                    currentStock = p.StockQuantity,
                    minimumStock = p.MinimumStock ?? 0,
                    maximumStock = p.MaximumStock ?? 0,
                    costPrice = FormatCurrency(p.CostPrice ?? 0, p.Currency?.Notation),
                    sellingPrice = FormatCurrency(p.SellingPrice, p.Currency?.Notation),
                    stockValue = FormatCurrency(p.StockQuantity * (p.CostPrice ?? p.SellingPrice), p.Currency?.Notation),
                    stockStatus = GetStockStatus(p),
                    stockStatusClass = GetStockStatusClass(p),
                    isActive = p.IsActive ? "Active" : "Inactive",
                    actions = GetInventoryActionButtons(p.Id)
                })
            });
        }
        catch (Exception ex)
        {
            return Json(new { error = ex.Message });
        }
    }

    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Inventory_Edit)]
    public async Task<IActionResult> AdjustStock(int id)
    {
        var product = await _productService.GetByIdAsync(id);
        if (product == null)
        {
            TempData["ErrorMessage"] = "Product not found.";
            return RedirectToAction("Index");
        }

        ViewData["Title"] = $"Adjust Stock - {product.Name}";
        ViewData["Subtitle"] = $"Current Stock: {product.StockQuantity}";

        var model = new StockAdjustmentRequest
        {
            ProductId = product.Id,
            ProductName = product.Name,
            CurrentStock = product.StockQuantity,
            AdjustmentType = StockAdjustmentType.Add
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionHelper.Permissions.Inventory_Edit)]
    public async Task<IActionResult> AdjustStock(StockAdjustmentRequest model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var product = await _productService.GetByIdAsync(model.ProductId);
            if (product == null)
            {
                ModelState.AddModelError(string.Empty, "Product not found.");
                return View(model);
            }

            // Calculate new stock
            decimal newStock = model.AdjustmentType == StockAdjustmentType.Add 
                ? product.StockQuantity + model.Quantity
                : product.StockQuantity - model.Quantity;

            // Prevent negative stock
            if (newStock < 0)
            {
                ModelState.AddModelError("Quantity", "Insufficient stock for this adjustment.");
                return View(model);
            }

            // Update stock
            product.StockQuantity = newStock;
            await _productService.UpdateAsync(model.ProductId, product);

            TempData["SuccessMessage"] = $"Stock adjusted successfully! New stock level: {newStock}";
            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"An error occurred: {ex.Message}");
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> LowStockReport()
    {
        ViewData["Title"] = "Low Stock Report";
        ViewData["Subtitle"] = "Products with low stock levels";

        var products = await _productService.GetAllAsync();
        var lowStockProducts = products
            .Where(p => p.MinimumStock.HasValue && p.StockQuantity <= p.MinimumStock.Value)
            .OrderBy(p => p.StockQuantity)
            .ToList();

        ViewBag.LowStockProducts = lowStockProducts;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> StockValueReport()
    {
        ViewData["Title"] = "Stock Value Report";
        ViewData["Subtitle"] = "Total value of current inventory";

        var products = await _productService.GetAllAsync();
        
        // Get default currency for display
        var defaultCurrency = await _currencyService.GetDefaultAsync();
        var currencyNotation = defaultCurrency?.Notation ?? "$";
        
        var stockValueData = products
            .Where(p => p.IsActive)
            .Select(p => new
            {
                ProductName = p.Name,
                Category = p.Category?.Name ?? "No Category",
                StockQuantity = p.StockQuantity,
                CostPrice = p.CostPrice ?? p.SellingPrice,
                CostPriceFormatted = FormatCurrency(p.CostPrice ?? p.SellingPrice, p.Currency?.Notation ?? currencyNotation),
                StockValue = p.StockQuantity * (p.CostPrice ?? p.SellingPrice),
                StockValueFormatted = FormatCurrency(p.StockQuantity * (p.CostPrice ?? p.SellingPrice), p.Currency?.Notation ?? currencyNotation)
            })
            .OrderByDescending(p => p.StockValue)
            .ToList();

        var totalStockValue = stockValueData.Sum(p => p.StockValue);
        var avgStockValue = stockValueData.Count > 0 ? totalStockValue / stockValueData.Count : 0;
        
        ViewBag.StockValueData = stockValueData;
        ViewBag.CategoryChartData = stockValueData
            .GroupBy(p => p.Category)
            .Select(g => new { Label = g.Key, Value = Math.Round(g.Sum(p => p.StockValue), 2) })
            .OrderByDescending(g => g.Value)
            .Take(8)
            .ToList();
        ViewBag.TopProductsChartData = stockValueData
            .Take(10)
            .Select(p => new { Label = p.ProductName, Value = Math.Round(p.StockValue, 2) })
            .ToList();
        ViewBag.TotalStockValue = totalStockValue;
        ViewBag.TotalStockValueFormatted = FormatCurrency(totalStockValue, currencyNotation);
        ViewBag.AvgStockValueFormatted = FormatCurrency(avgStockValue, currencyNotation);

        return View();
    }

    // ----- Report printing: receipt paper or PDF, same content either way -----

    [HttpPost]
    public async Task<IActionResult> PrintLowStockReport()
        => await PrintToReceiptPrinterAsync(_thermalPrinterService, await BuildLowStockDocumentAsync(),
            Url.Action(nameof(LowStockReportPdf), "Inventory"));

    [HttpGet]
    public async Task<IActionResult> LowStockReportPdf()
        => PdfFile(_thermalPrinterService, await BuildLowStockDocumentAsync(), PdfPaper.A4);

    [HttpPost]
    public async Task<IActionResult> PrintStockValueReport()
        => await PrintToReceiptPrinterAsync(_thermalPrinterService, await BuildStockValueDocumentAsync(),
            Url.Action(nameof(StockValueReportPdf), "Inventory"));

    [HttpGet]
    public async Task<IActionResult> StockValueReportPdf()
        => PdfFile(_thermalPrinterService, await BuildStockValueDocumentAsync(), PdfPaper.A4);

    private async Task<PrintDocument> BuildLowStockDocumentAsync()
    {
        var products = await _productService.GetAllAsync();
        var items = products
            .Where(p => p.MinimumStock.HasValue && p.StockQuantity <= p.MinimumStock.Value)
            .OrderBy(p => p.StockQuantity)
            .Select(p => new LowStockItem(p.Name, p.Category?.Name ?? "No Category", p.StockQuantity, p.MinimumStock!.Value))
            .ToList();
        return ReportDocuments.LowStock(items);
    }

    private async Task<PrintDocument> BuildStockValueDocumentAsync()
    {
        var products = await _productService.GetAllAsync();
        var defaultCurrency = await _currencyService.GetDefaultAsync();
        var currencyNotation = defaultCurrency?.Notation ?? "$";

        var rows = products
            .Where(p => p.IsActive)
            .Select(p =>
            {
                var notation = p.Currency?.Notation ?? currencyNotation;
                var unitCost = p.CostPrice ?? p.SellingPrice;
                return (Value: p.StockQuantity * unitCost, Item: new StockValueItem(
                    p.Name, p.Category?.Name ?? "No Category", p.StockQuantity,
                    FormatCurrency(unitCost, notation), FormatCurrency(p.StockQuantity * unitCost, notation)));
            })
            .OrderByDescending(r => r.Value)
            .ToList();

        return ReportDocuments.StockValue(
            rows.Select(r => r.Item).ToList(),
            FormatCurrency(rows.Sum(r => r.Value), currencyNotation));
    }

    #region Helper Methods

    private object GetInventoryPropertyValue(Product product, string propertyName)
    {
        return propertyName switch
        {
            "Name" => product.Name,
            "StockQuantity" => product.StockQuantity,
            "MinimumStock" => product.MinimumStock ?? 0,
            "CostPrice" => product.CostPrice ?? 0,
            "SellingPrice" => product.SellingPrice,
            "CategoryName" => product.Category?.Name ?? "",
            _ => product.Name
        };
    }

    private string GetStockStatus(Product product)
    {
        if (product.StockQuantity <= 0)
            return "Out of Stock";
        else if (product.MinimumStock.HasValue && product.StockQuantity <= product.MinimumStock.Value)
            return "Low Stock";
        else
            return "In Stock";
    }

    private string GetStockStatusClass(Product product)
    {
        if (product.StockQuantity <= 0)
            return "badge-danger";
        else if (product.MinimumStock.HasValue && product.StockQuantity <= product.MinimumStock.Value)
            return "badge-warning";
        else
            return "badge-success";
    }

    private string GetInventoryActionButtons(int productId)
    {
        // Use language-based titles since IViewLocalizer doesn't work reliably in controllers
        var isArabic = CurrentLanguage == "ar";
        var viewHistoryTitle = isArabic ? "عرض السجل" : "View History";
        var adjustStockTitle = isArabic ? "تعديل المخزون" : "Adjust Stock";
        var setMinStockTitle = isArabic ? "تعيين الحد الأدنى" : "Set Min Stock";
        
        return $@"
            <div class='d-flex gap-1 justify-content-center'>
                <button type='button' class='btn btn-sm btn-outline-info' onclick='viewStockHistory({productId})' title='{viewHistoryTitle}'>
                    <i class='fas fa-history'></i>
                </button>
                <a href='{Url.Action("AdjustStock", "Inventory", new { id = productId, lang = CurrentLanguage })}' class='btn btn-sm btn-outline-primary' title='{adjustStockTitle}'>
                    <i class='fas fa-edit'></i>
                </a>
                <button type='button' class='btn btn-sm btn-outline-success' onclick='setMinimumStock({productId})' title='{setMinStockTitle}'>
                    <i class='fas fa-sliders-h'></i>
                </button>
            </div>";
    }

    #endregion
}

// Stock Adjustment DTO
public class StockAdjustmentRequest
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal CurrentStock { get; set; }
    public StockAdjustmentType AdjustmentType { get; set; }
    public decimal Quantity { get; set; }
    public string? Reason { get; set; }
}

public enum StockAdjustmentType
{
    Add,
    Remove
}
