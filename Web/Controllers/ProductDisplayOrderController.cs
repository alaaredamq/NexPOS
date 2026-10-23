using Application.IServices.Products;
using Application.IServices.Base;
using Core.Entities.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Attributes;

namespace Web.Controllers
{
    // [Authorize(Roles = "Admin,Manager")]
    [RequirePermission("MainScreenProducts.Manage")]
    public class ProductDisplayOrderController : BaseController
    {
        private readonly IProductDisplayOrderService _productDisplayOrderService;
        private readonly IProductService _productService;
        private readonly ICurrencyService _currencyService;

        public ProductDisplayOrderController(
            IProductDisplayOrderService productDisplayOrderService,
            IProductService productService,
            ICurrencyService currencyService)
        {
            _productDisplayOrderService = productDisplayOrderService;
            _productService = productService;
            _currencyService = currencyService;
        }

        public async Task<IActionResult> Index()
        {
            var displayOrders = await _productDisplayOrderService.GetActiveDisplayOrderAsync();
            
            // Get all currencies for the dropdown
            var currencies = await _currencyService.GetAllAsync();
            ViewBag.Currencies = currencies;
            
            // Get default currency
            var defaultCurrency = await _currencyService.GetDefaultAsync();
            ViewBag.DefaultCurrency = defaultCurrency;
            
            return View(displayOrders);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateOrder([FromBody] List<ProductDisplayOrder> displayOrders)
        {
            try
            {
                await _productDisplayOrderService.UpdateDisplayOrderAsync(displayOrders);
                return Json(new { success = true, message = "Display order updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddProduct(int productId)
        {
            try
            {
                var product = await _productService.GetByIdAsync(productId);
                if (product == null)
                {
                    return Json(new { success = false, message = "Product not found" });
                }

                var existingOrder = await _productDisplayOrderService.GetAllAsync();
                var maxOrder = existingOrder.Any() ? existingOrder.Max(x => x.DisplayOrder) : 0;

                var newDisplayOrder = new ProductDisplayOrder
                {
                    ProductId = productId,
                    DisplayOrder = maxOrder + 1,
                    IsActive = true
                };

                await _productDisplayOrderService.CreateAsync(newDisplayOrder);
                return Json(new { success = true, message = "Product added to display order" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> RemoveProduct(int id)
        {
            try
            {
                await _productDisplayOrderService.DeleteAsync(id);
                return Json(new { success = true, message = "Product removed from display order" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAvailableProducts(string? term = null, int skip = 0, int take = 20)
        {
            try
            {
                skip = Math.Max(skip, 0);
                take = Math.Clamp(take, 1, 50);

                // Fetch one extra row so the client knows whether another page exists
                var page = await _productService.SearchAvailableForDisplayOrderAsync(term, skip, take + 1);
                var hasMore = page.Count > take;

                return Json(new
                {
                    success = true,
                    hasMore,
                    items = page.Take(take).Select(p => new
                    {
                        id = p.Id,
                        name = p.Name,
                        price = p.SellingPrice,
                        currency = p.Currency?.Notation ?? "$",
                        category = p.Category?.Name ?? "No Category",
                        image = p.Image
                    })
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> QuickAddProduct([FromBody] QuickAddProductRequest request)
        {
            try
            {
                // Generate barcode if not provided or if user wants to generate one
                string barcode = request.Barcode;
                if (string.IsNullOrWhiteSpace(barcode) || request.GenerateBarcode)
                {
                    barcode = GenerateRandomBarcode();
                }

                // Get default currency if not specified
                int currencyId = request.CurrencyId;
                if (currencyId <= 0)
                {
                    var defaultCurrency = await _currencyService.GetDefaultAsync();
                    currencyId = defaultCurrency?.Id ?? 1;
                }

                // Create the product
                var product = new Product
                {
                    Name = request.Name,
                    Description = request.Description,
                    Barcode = barcode,
                    SellingPrice = request.Price,
                    CostPrice = request.Price * 0.7m, // Default cost price (70% of selling price)
                    CategoryId = request.CategoryId > 0 ? request.CategoryId : 1, // Default category
                    SupplierId = request.SupplierId > 0 ? request.SupplierId : 1, // Default supplier
                    CurrencyId = currencyId,
                    StockQuantity = request.StockQuantity > 0 ? request.StockQuantity : 0,
                    MinimumStock = 5,
                    BrandId = 1, // Default brand
                    IsActive = true
                };

                await _productService.CreateAsync(product);

                // Add to display order
                var existingOrder = await _productDisplayOrderService.GetAllAsync();
                var maxOrder = existingOrder.Any() ? existingOrder.Max(x => x.DisplayOrder) : 0;

                var newDisplayOrder = new ProductDisplayOrder
                {
                    ProductId = product.Id,
                    DisplayOrder = maxOrder + 1,
                    IsActive = true
                };

                await _productDisplayOrderService.CreateAsync(newDisplayOrder);

                return Json(new 
                { 
                    success = true, 
                    message = "Product created and added to display order successfully",
                    barcode = barcode,
                    productId = product.Id
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private string GenerateRandomBarcode()
        {
            // Generate a random 13-digit EAN-13 style barcode
            var random = new Random();
            var barcode = "2" + DateTime.Now.ToString("yyMMdd"); // Start with 2 and date
            
            // Add random digits to make it 12 digits
            while (barcode.Length < 12)
            {
                barcode += random.Next(0, 10).ToString();
            }
            
            // Calculate check digit for EAN-13
            int sum = 0;
            for (int i = 0; i < 12; i++)
            {
                int digit = int.Parse(barcode[i].ToString());
                sum += (i % 2 == 0) ? digit : digit * 3;
            }
            int checkDigit = (10 - (sum % 10)) % 10;
            barcode += checkDigit;
            
            return barcode;
        }
    }

    public class QuickAddProductRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Barcode { get; set; }
        public bool GenerateBarcode { get; set; }
        public decimal Price { get; set; }
        public int CategoryId { get; set; }
        public int SupplierId { get; set; }
        public int CurrencyId { get; set; }
        public decimal StockQuantity { get; set; }
    }
}
