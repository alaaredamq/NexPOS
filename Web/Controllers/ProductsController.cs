using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Application.IServices.Products;
using Application.IServices.Reports;
using Application.IServices.Base;
using Application.IServices.Settings;
using Application.DTOs.Products;
using Microsoft.AspNetCore.Mvc.Localization;
using Web.Attributes;
using Web.Helpers;
using Core.Entities.Products;
using static Web.Helpers.CurrencyHelper;

namespace Web.Controllers;

[Authorize]
[RequirePermission(PermissionHelper.Permissions.Products_View)]
public class ProductsController : BaseController
{
    private readonly IProductService _productService;
    private readonly IProductCategoryService _categoryService;
    private readonly ISupplierService _supplierService;
    private readonly ICurrencyService _currencyService;
    private readonly IViewLocalizer _localizer;
    private readonly IExportService _exportService;
    private readonly ISystemSettingsService _settingsService;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(IProductService productService, IProductCategoryService categoryService, ISupplierService supplierService, ICurrencyService currencyService, IViewLocalizer localizer, IExportService exportService, ISystemSettingsService settingsService, ILogger<ProductsController> logger)
    {
        _productService = productService;
        _categoryService = categoryService;
        _supplierService = supplierService;
        _currencyService = currencyService;
        _localizer = localizer;
        _exportService = exportService;
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Products";
        ViewData["Subtitle"] = "Manage your product inventory";
        
        // Get products for display
        var products = await _productService.GetAllAsync();
        ViewBag.Products = products;
        ViewBag.ProductCount = products?.Count ?? 0;
        
        return View();
    }
    
    [HttpGet]
    public async Task<IActionResult> TestProducts()
    {
        var products = await _productService.GetAllAsync();
        return Json(new {
            success = true,
            count = products?.Count ?? 0,
            products = products?.Take(3).Select(p => new {
                id = p.Id,
                name = p.Name,
                barcode = p.Barcode
            })
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetProducts(int draw = 1, int start = 0, int length = 10, string searchValue = "", string sortColumn = "name", string sortDirection = "asc")
    {
        try
        {
            _logger.LogInformation($"GetProducts called: draw={draw}, start={start}, length={length}, searchValue={searchValue}");
            
            var products = await _productService.GetAllAsync();
            
            _logger.LogInformation($"Products fetched: {products?.Count ?? 0} items");
            
            if (products == null || !products.Any())
            {
                _logger.LogWarning("No products found in database");
                return Json(new
                {
                    draw = draw,
                    recordsTotal = 0,
                    recordsFiltered = 0,
                    data = new List<object>()
                });
            }
            
            // Apply search filter
            var filteredProducts = products;
            if (!string.IsNullOrEmpty(searchValue))
            {
                filteredProducts = products.Where(p => 
                    (p.Name?.Contains(searchValue, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (p.Barcode?.Contains(searchValue, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (p.Description?.Contains(searchValue, StringComparison.OrdinalIgnoreCase) ?? false)
                ).ToList();
            }

            // Apply sorting
            var sortedProducts = sortDirection.ToLower() == "desc" 
                ? filteredProducts.OrderByDescending(p => GetPropertyValue(p, sortColumn))
                : filteredProducts.OrderBy(p => GetPropertyValue(p, sortColumn));

            // Apply pagination
            var pagedProducts = sortedProducts.Skip(start).Take(length).ToList();

            var result = new
            {
                draw = draw,
                recordsTotal = products.Count,
                recordsFiltered = filteredProducts.Count,
                data = pagedProducts.Select(p => new
                {
                    id = p.Id,
                    name = p.Name ?? "",
                    barcode = p.Barcode ?? "",
                    categoryName = p.Category?.Name ?? "No Category",
                    purchasePrice = FormatCurrency(p.CostPrice ?? 0, p.Currency?.Notation),
                    sellingPrice = FormatCurrency(p.SellingPrice, p.Currency?.Notation),
                    stockQuantity = p.StockQuantity,
                    isActive = p.IsActive ? "Active" : "Inactive",
                    actions = GetActionButtons(p.Id)
                }).ToList()
            };
            
            _logger.LogInformation($"Returning {result.data.Count} products out of {result.recordsTotal} total");
            
            return Json(result);
        }
        catch (Exception ex)
        {
            return Json(new { 
                draw = draw,
                recordsTotal = 0,
                recordsFiltered = 0,
                data = new List<object>(),
                error = ex.Message 
            });
        }
    }

    private object GetPropertyValue(Product product, string propertyName)
    {
        return propertyName?.ToLower() switch
        {
            "name" => product.Name ?? "",
            "barcode" => product.Barcode ?? "",
            "sellingprice" => product.SellingPrice,
            "stockquantity" => product.StockQuantity,
            "categoryname" => product.Category?.Name ?? "",
            _ => product.Name ?? ""
        };
    }

    /// <summary>
    /// Generates a unique EAN-13 style barcode for products that don't have one.
    /// </summary>
    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Products_Create)]
    public async Task<IActionResult> GenerateBarcode()
    {
        try
        {
            string barcode;
            var attempts = 0;
            do
            {
                barcode = GenerateEan13Barcode();
                attempts++;
            }
            while (await _productService.GetByBarcodeAsync(barcode) != null && attempts < 10);

            return Json(new { success = true, barcode });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating barcode");
            return Json(new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// Default label size from App Settings, used by the "Print label" dialog.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> StickerSettings()
    {
        var settings = await _settingsService.GetAppSettingsAsync();
        return Json(new
        {
            widthMm = settings.StickerWidthMm,
            heightMm = settings.StickerHeightMm
        });
    }

    private static string GenerateEan13Barcode()
    {
        var random = Random.Shared;
        // Prefix "2" (in-store range) + date, then pad with random digits to 12
        var barcode = "2" + DateTime.Now.ToString("yyMMdd");
        while (barcode.Length < 12)
        {
            barcode += random.Next(0, 10).ToString();
        }

        // EAN-13 check digit
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            var digit = barcode[i] - '0';
            sum += (i % 2 == 0) ? digit : digit * 3;
        }
        var checkDigit = (10 - (sum % 10)) % 10;
        return barcode + checkDigit;
    }

    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Products_Create)]
    public async Task<IActionResult> Create(bool noSidebar = false, int? packId = null, int? packSize = null, int? parentProductId = null)
    {
        ViewData["Title"] = "Create Product";
        ViewData["Subtitle"] = "Add a new product to inventory";
        // Check query parameter if not passed as action parameter
        if (!noSidebar && Request.Query.ContainsKey("noSidebar"))
        {
            noSidebar = Request.Query["noSidebar"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        ViewBag.NoSidebar = noSidebar;
        ViewBag.PackId = packId;
        ViewBag.PackSize = packSize;
        ViewBag.ParentProductId = parentProductId;
        
        // Fetch categories from database
        var categories = await _categoryService.GetAllAsync();
        ViewBag.Categories = categories.Where(c => c.IsActive).ToList();
        
        // Fetch suppliers from database
        var suppliers = await _supplierService.GetAllAsync();
        var activeSuppliers = suppliers.Where(s => s.IsActive).ToList();
        
        // Find or create "No Supplier" option
        var noSupplier = activeSuppliers.FirstOrDefault(s => s.Name.Equals("No Supplier", StringComparison.OrdinalIgnoreCase));
        if (noSupplier == null)
        {
            // Try to find it in all suppliers (including inactive)
            noSupplier = suppliers.FirstOrDefault(s => s.Name.Equals("No Supplier", StringComparison.OrdinalIgnoreCase));
        }
        
        // If "No Supplier" exists, move it to the top of the list
        if (noSupplier != null)
        {
            activeSuppliers = activeSuppliers.Where(s => s.Id != noSupplier.Id).ToList();
            activeSuppliers.Insert(0, noSupplier);
        }
        
        ViewBag.Suppliers = activeSuppliers;
        
        // Fetch currencies
        var currencies = await _currencyService.GetAllAsync();
        ViewBag.Currencies = currencies;
        ViewBag.DefaultCurrency = await _currencyService.GetDefaultAsync();
        
        var model = new CreateProductRequest();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionHelper.Permissions.Products_Create)]
    public async Task<IActionResult> Create(CreateProductRequest model)
    {
        _logger.LogInformation($"Create POST called with CategoryId: {model.CategoryId}, SupplierId: {model.SupplierId}");
        
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("ModelState is invalid");
            foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
            {
                _logger.LogWarning($"Validation error: {error.ErrorMessage}");
            }
            
            // Re-populate categories, suppliers, and currencies for validation errors
            var categories = await _categoryService.GetAllAsync();
            ViewBag.Categories = categories.Where(c => c.IsActive).ToList();
            var suppliers = await _supplierService.GetAllAsync();
            var activeSuppliers = suppliers.Where(s => s.IsActive).ToList();
            var noSupplier = activeSuppliers.FirstOrDefault(s => s.Name.Equals("No Supplier", StringComparison.OrdinalIgnoreCase)) 
                ?? suppliers.FirstOrDefault(s => s.Name.Equals("No Supplier", StringComparison.OrdinalIgnoreCase));
            if (noSupplier != null)
            {
                activeSuppliers = activeSuppliers.Where(s => s.Id != noSupplier.Id).ToList();
                activeSuppliers.Insert(0, noSupplier);
            }
            ViewBag.Suppliers = activeSuppliers;
            var currencies = await _currencyService.GetAllAsync();
            ViewBag.Currencies = currencies;
            ViewBag.DefaultCurrency = await _currencyService.GetDefaultAsync();
            return View(model);
        }

        try
        {
            // Get default currency if not specified
            int currencyId = model.CurrencyId;
            if (currencyId <= 0)
            {
                var defaultCurrency = await _currencyService.GetDefaultAsync();
                currencyId = defaultCurrency?.Id ?? 1;
            }

            var product = new Product
            {
                Name = model.Name,
                Description = model.Description,
                Barcode = model.Barcode,
                CostPrice = model.PurchasePrice,
                SellingPrice = model.SellingPrice,
                StockQuantity = model.StockQuantity,
                MinimumStock = model.MinStockLevel,
                CategoryId = model.CategoryId,
                IsPack = model.IsPack,
                PackSize = model.IsPack ? model.PackSize : null,
                SingleProductId = model.IsPack ? model.SingleProductId : null,
                BrandId = 1, // Default brand for now
                SupplierId = model.SupplierId,
                CurrencyId = currencyId,
                IsActive = model.IsActive,
                Unit = model.Unit
            };

            _logger.LogInformation($"Creating product with CategoryId: {product.CategoryId}");
            await _productService.CreateAsync(product);
            TempData["SuccessMessage"] = "Product created successfully!";
            
            // Check if this was opened in a new window (noSidebar mode)
            var noSidebar = Request.Query.ContainsKey("noSidebar") && 
                           Request.Query["noSidebar"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);
            
            if (noSidebar)
            {
                // Return a simple HTML page that will close the window
                return Content(@"
                    <!DOCTYPE html>
                    <html>
                    <head>
                        <title>Product Created</title>
                        <script>
                            window.onload = function() {
                                setTimeout(function() {
                                    window.close();
                                    if (window.opener) {
                                        window.opener.postMessage({ type: 'productCreated', success: true }, '*');
                                    }
                                }, 100);
                            };
                        </script>
                    </head>
                    <body>
                        <p>Product created successfully! This window will close automatically.</p>
                    </body>
                    </html>", "text/html");
            }
            
            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating product");
            ModelState.AddModelError(string.Empty, $"An error occurred: {ex.Message}");
        }

            // Re-populate categories and suppliers for error cases
            var categoriesOnError = await _categoryService.GetAllAsync();
            ViewBag.Categories = categoriesOnError.Where(c => c.IsActive).ToList();
            var suppliersOnError = await _supplierService.GetAllAsync();
            var activeSuppliersOnError = suppliersOnError.Where(s => s.IsActive).ToList();
            var noSupplierOnError = activeSuppliersOnError.FirstOrDefault(s => s.Name.Equals("No Supplier", StringComparison.OrdinalIgnoreCase)) 
                ?? suppliersOnError.FirstOrDefault(s => s.Name.Equals("No Supplier", StringComparison.OrdinalIgnoreCase));
            if (noSupplierOnError != null)
            {
                activeSuppliersOnError = activeSuppliersOnError.Where(s => s.Id != noSupplierOnError.Id).ToList();
                activeSuppliersOnError.Insert(0, noSupplierOnError);
            }
            ViewBag.Suppliers = activeSuppliersOnError;
        return View(model);
    }

    [HttpGet]
    [RequirePermission(PermissionHelper.Permissions.Products_Edit)]
    public async Task<IActionResult> Edit(int id)
    {
        try
        {
            var product = await _productService.GetByIdAsync(id);
            if (product == null)
            {
                TempData["ErrorMessage"] = "Product not found.";
                return RedirectToAction("Index");
            }

            ViewData["Title"] = "Edit Product";
            ViewData["Subtitle"] = $"Edit {product.Name}";
            
            // Fetch categories from database
            var categories = await _categoryService.GetAllAsync();
            ViewBag.Categories = categories.Where(c => c.IsActive).ToList();
            
            // Fetch suppliers from database
            var suppliers = await _supplierService.GetAllAsync();
            var activeSuppliers = suppliers.Where(s => s.IsActive).ToList();
            var noSupplier = activeSuppliers.FirstOrDefault(s => s.Name.Equals("No Supplier", StringComparison.OrdinalIgnoreCase)) 
                ?? suppliers.FirstOrDefault(s => s.Name.Equals("No Supplier", StringComparison.OrdinalIgnoreCase));
            if (noSupplier != null)
            {
                activeSuppliers = activeSuppliers.Where(s => s.Id != noSupplier.Id).ToList();
                activeSuppliers.Insert(0, noSupplier);
            }
            ViewBag.Suppliers = activeSuppliers;
            
            // Fetch currencies
            var currencies = await _currencyService.GetAllAsync();
            ViewBag.Currencies = currencies;
            ViewBag.DefaultCurrency = await _currencyService.GetDefaultAsync();
            
            var model = new UpdateProductRequest
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                Barcode = product.Barcode,
                CategoryId = product.CategoryId,
                SupplierId = product.SupplierId,
                CurrencyId = product.CurrencyId,
                PurchasePrice = product.CostPrice ?? 0,
                SellingPrice = product.SellingPrice,
                StockQuantity = product.StockQuantity,
                MinStockLevel = product.MinimumStock ?? 0,
                IsActive = product.IsActive,
                Unit = product.Unit
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
    [RequirePermission(PermissionHelper.Permissions.Products_Edit)]
    public async Task<IActionResult> Edit(UpdateProductRequest model)
    {
        _logger.LogInformation($"Edit POST called for ProductId: {model.Id} with CategoryId: {model.CategoryId}, SupplierId: {model.SupplierId}");
        
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("ModelState is invalid");
            foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
            {
                _logger.LogWarning($"Validation error: {error.ErrorMessage}");
            }
            
            // Re-populate categories, suppliers, and currencies for validation errors
            var categories = await _categoryService.GetAllAsync();
            ViewBag.Categories = categories.Where(c => c.IsActive).ToList();
            var suppliers = await _supplierService.GetAllAsync();
            var activeSuppliers = suppliers.Where(s => s.IsActive).ToList();
            var noSupplier = activeSuppliers.FirstOrDefault(s => s.Name.Equals("No Supplier", StringComparison.OrdinalIgnoreCase)) 
                ?? suppliers.FirstOrDefault(s => s.Name.Equals("No Supplier", StringComparison.OrdinalIgnoreCase));
            if (noSupplier != null)
            {
                activeSuppliers = activeSuppliers.Where(s => s.Id != noSupplier.Id).ToList();
                activeSuppliers.Insert(0, noSupplier);
            }
            ViewBag.Suppliers = activeSuppliers;
            var currencies = await _currencyService.GetAllAsync();
            ViewBag.Currencies = currencies;
            ViewBag.DefaultCurrency = await _currencyService.GetDefaultAsync();
            return View(model);
        }

        try
        {
            var product = await _productService.GetByIdAsync(model.Id);
            if (product == null)
            {
                _logger.LogWarning($"Product with Id {model.Id} not found");
                ModelState.AddModelError(string.Empty, "Product not found.");
                // Re-populate categories, suppliers, and currencies for error cases
                var categoriesOnNotFound = await _categoryService.GetAllAsync();
                ViewBag.Categories = categoriesOnNotFound.Where(c => c.IsActive).ToList();
                var suppliersOnNotFound = await _supplierService.GetAllAsync();
                var activeSuppliersOnNotFound = suppliersOnNotFound.Where(s => s.IsActive).ToList();
                var noSupplierOnNotFound = activeSuppliersOnNotFound.FirstOrDefault(s => s.Name.Equals("No Supplier", StringComparison.OrdinalIgnoreCase)) 
                    ?? suppliersOnNotFound.FirstOrDefault(s => s.Name.Equals("No Supplier", StringComparison.OrdinalIgnoreCase));
                if (noSupplierOnNotFound != null)
                {
                    activeSuppliersOnNotFound = activeSuppliersOnNotFound.Where(s => s.Id != noSupplierOnNotFound.Id).ToList();
                    activeSuppliersOnNotFound.Insert(0, noSupplierOnNotFound);
                }
                ViewBag.Suppliers = activeSuppliersOnNotFound;
                var currenciesOnNotFound = await _currencyService.GetAllAsync();
                ViewBag.Currencies = currenciesOnNotFound;
                ViewBag.DefaultCurrency = await _currencyService.GetDefaultAsync();
                return View(model);
            }

            _logger.LogInformation($"Updating product. Old CategoryId: {product.CategoryId}, New CategoryId: {model.CategoryId}");
            
            // Get default currency if not specified
            int currencyId = model.CurrencyId;
            if (currencyId <= 0)
            {
                var defaultCurrency = await _currencyService.GetDefaultAsync();
                currencyId = defaultCurrency?.Id ?? product.CurrencyId; // Keep existing if no default
            }
            
            // Update the product properties
            product.Name = model.Name;
            product.Description = model.Description;
            product.Barcode = model.Barcode;
            product.CostPrice = model.PurchasePrice;
            product.SellingPrice = model.SellingPrice;
            product.StockQuantity = model.StockQuantity;
            product.MinimumStock = model.MinStockLevel;
            product.CategoryId = model.CategoryId;
            product.SupplierId = model.SupplierId;
            product.CurrencyId = currencyId;
            product.IsActive = model.IsActive;
            product.Unit = model.Unit;

            await _productService.UpdateAsync(model.Id, product);
            _logger.LogInformation($"Product updated successfully with CategoryId: {product.CategoryId}");
            TempData["SuccessMessage"] = "Product updated successfully!";
            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating product");
            ModelState.AddModelError(string.Empty, $"An error occurred: {ex.Message}");
        }

        // Re-populate categories, suppliers, and currencies for error cases
        var categoriesOnError = await _categoryService.GetAllAsync();
        ViewBag.Categories = categoriesOnError.Where(c => c.IsActive).ToList();
        var suppliersOnError = await _supplierService.GetAllAsync();
        var activeSuppliersOnError = suppliersOnError.Where(s => s.IsActive).ToList();
        var noSupplierOnError = activeSuppliersOnError.FirstOrDefault(s => s.Name.Equals("No Supplier", StringComparison.OrdinalIgnoreCase)) 
            ?? suppliersOnError.FirstOrDefault(s => s.Name.Equals("No Supplier", StringComparison.OrdinalIgnoreCase));
        if (noSupplierOnError != null)
        {
            activeSuppliersOnError = activeSuppliersOnError.Where(s => s.Id != noSupplierOnError.Id).ToList();
            activeSuppliersOnError.Insert(0, noSupplierOnError);
        }
        ViewBag.Suppliers = activeSuppliersOnError;
        var currenciesOnError = await _currencyService.GetAllAsync();
        ViewBag.Currencies = currenciesOnError;
        ViewBag.DefaultCurrency = await _currencyService.GetDefaultAsync();
        return View(model);
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    [RequirePermission(PermissionHelper.Permissions.Products_Delete)]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            if (id <= 0)
            {
                return Json(new { success = false, message = "Invalid product ID" });
            }
            
            await _productService.DeleteAsync(id);
            return Json(new { success = true, message = "Product deleted successfully!" });
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
            var product = await _productService.GetByIdAsync(id);
            if (product == null)
            {
                TempData["ErrorMessage"] = "Product not found.";
                return RedirectToAction("Index");
            }

            ViewData["Title"] = "Product Details";
            ViewData["Subtitle"] = product.Name;
            
            return View(product);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"An error occurred: {ex.Message}";
            return RedirectToAction("Index");
        }
    }

    private string GetActionButtons(int productId)
    {
        return $@"
            <div class='btn-group' role='group'>
                <button type='button' class='btn btn-sm btn-outline-primary' onclick='viewProduct({productId})' title='View Details'>
                    <i class='fas fa-eye'></i>
                </button>
                <button type='button' class='btn btn-sm btn-outline-teal' onclick='editProduct({productId})' title='Edit'>
                    <i class='fas fa-edit'></i>
                </button>
                <button type='button' class='btn btn-sm btn-outline-secondary' onclick='printProductLabel({productId})' title='Print Label'>
                    <i class='fas fa-barcode'></i>
                </button>
                <button type='button' class='btn btn-sm btn-outline-danger' onclick='deleteProduct({productId})' title='Delete'>
                    <i class='fas fa-trash'></i>
                </button>
            </div>";
    }

    /// <summary>
    /// Export products to CSV
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> ExportProducts()
    {
        try
        {
            var csvData = await _exportService.ExportProductsToCsvAsync();
            var fileName = $"products_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            
            return File(csvData, "text/csv", fileName);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Export failed: {ex.Message}" });
        }
    }
}