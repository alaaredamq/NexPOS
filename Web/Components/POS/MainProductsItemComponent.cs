using Application.IServices.Products;
using Core.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace Web.Components.POS;

public class MainProductsItemComponent : ViewComponent
{
    private readonly IProductService _productService;
    private readonly IProductDisplayOrderService _productDisplayOrderService;
    
    public MainProductsItemComponent(
        IProductService productService,
        IProductDisplayOrderService productDisplayOrderService)
    {
        _productService = productService;
        _productDisplayOrderService = productDisplayOrderService;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        try
        {
            // Try to get products from display order first
            var displayOrderProducts = await _productDisplayOrderService.GetActiveDisplayOrderAsync();
            
            if (displayOrderProducts.Any())
            {
                // Use products from display order (limited to 30 for compact display)
                var orderedProducts = displayOrderProducts
                    .Take(30)
                    .Select(pdo => new ProductDTO
                    {
                        Id = pdo.Product.Id,
                        Name = pdo.Product.Name,
                        Description = pdo.Product.Description,
                        Barcode = pdo.Product.Barcode,
                        CostPrice = pdo.Product.CostPrice,
                        SellingPrice = pdo.Product.SellingPrice,
                        StockQuantity = pdo.Product.StockQuantity,
                        MinimumStock = pdo.Product.MinimumStock,
                        CategoryId = pdo.Product.CategoryId,
                        Category = pdo.Product.Category,
                        SupplierId = pdo.Product.SupplierId,
                        Supplier = pdo.Product.Supplier,
                        CurrencyId = pdo.Product.CurrencyId,
                        Currency = pdo.Product.Currency,
                        IsActive = pdo.Product.IsActive,
                        Unit = pdo.Product.Unit, // Include unit property
                        Image = pdo.Product.Image,
                        CreatedAt = pdo.Product.CreatedAt,
                        UpdatedAt = pdo.Product.UpdatedAt,
                        // Apply supplier discount rate
                        SupplierDiscountRate = pdo.Product.Supplier?.DiscountRate ?? 0
                    })
                    .ToList();
                return View(orderedProducts);
            }
            else
            {
                // Fallback to all active products if no display order is set
                var products = await _productService.GetAllAsync();
                var activeProducts = products
                    .Where(p => p.IsActive)
                    .Take(30) // Increased to 30 for compact display
                    .Select(p => new ProductDTO
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Description = p.Description,
                        Barcode = p.Barcode,
                        CostPrice = p.CostPrice,
                        SellingPrice = p.SellingPrice,
                        StockQuantity = p.StockQuantity,
                        MinimumStock = p.MinimumStock,
                        CategoryId = p.CategoryId,
                        Category = p.Category,
                        SupplierId = p.SupplierId,
                        Supplier = p.Supplier,
                        CurrencyId = p.CurrencyId,
                        Currency = p.Currency,
                        IsActive = p.IsActive,
                        Unit = p.Unit, // Include unit property
                        Image = p.Image,
                        CreatedAt = p.CreatedAt,
                        UpdatedAt = p.UpdatedAt,
                        // Apply supplier discount rate
                        SupplierDiscountRate = p.Supplier?.DiscountRate ?? 0
                    })
                    .ToList();
                return View(activeProducts);
            }
        }
        catch (Exception)
        {
            // Fallback to all active products if there's an error
            var products = await _productService.GetAllAsync();
                var activeProducts = products
                    .Where(p => p.IsActive)
                    .Take(30)
                    .Select(p => new ProductDTO
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Description = p.Description,
                        Barcode = p.Barcode,
                        CostPrice = p.CostPrice,
                        SellingPrice = p.SellingPrice,
                        StockQuantity = p.StockQuantity,
                        MinimumStock = p.MinimumStock,
                        CategoryId = p.CategoryId,
                        Category = p.Category,
                        SupplierId = p.SupplierId,
                        Supplier = p.Supplier,
                        CurrencyId = p.CurrencyId,
                        Currency = p.Currency,
                        IsActive = p.IsActive,
                        Unit = p.Unit, // Include unit property
                        Image = p.Image,
                        CreatedAt = p.CreatedAt,
                        UpdatedAt = p.UpdatedAt,
                        // Apply supplier discount rate
                        SupplierDiscountRate = p.Supplier?.DiscountRate ?? 0
                    })
                    .ToList();
            return View(activeProducts);
        }
    }
}