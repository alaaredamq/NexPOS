using Core.Entities.Products;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data;

/// <summary>
/// Small bilingual (English + Arabic) catalogue for manual testing: products sold by piece,
/// by volume (liter / gallon) and by mass (kg / lb), each with its own category and supplier.
/// Much lighter than <see cref="DemoDataSeeder"/> - no sales history, customers or staff.
/// Idempotent per product (matched on barcode), so re-running only adds what is missing.
/// Triggered with <c>--seed-test</c> (see Program.cs).
/// </summary>
public static class TestContentSeeder
{
    private const string BarcodePrefix = "600200000";

    private const string PieceEn = "Piece Items", VolumeEn = "Liquids (Volume)", MassEn = "Loose Goods (Mass)";
    private const string PieceAr = "منتجات بالقطعة", VolumeAr = "سوائل (بالحجم)", MassAr = "بضائع سائبة (بالوزن)";
    private const string SupplierEn = "Test Supplier Co.", SupplierAr = "شركة النور للتوريد";

    private sealed record Seed(string Name, string Category, string Supplier, decimal Cost, decimal Price,
        decimal Stock, decimal MinStock, string Unit);

    public static async Task<int> SeedAsync(ErpDbContext db)
    {
        var categories = await SeedCategoriesAsync(db);
        var suppliers = await SeedSuppliersAsync(db);

        var pc = ProductUnit.Piece;
        var seeds = new List<Seed>
        {
            // ---- English: by piece
            new("Ballpoint Pen - Blue", PieceEn, SupplierEn, 0.15m, 0.50m, 300, 50, pc),
            new("Spiral Notebook A5", PieceEn, SupplierEn, 0.80m, 1.90m, 120, 30, pc),
            new("LED Light Bulb 10W", PieceEn, SupplierEn, 1.20m, 2.80m, 85, 20, pc),
            new("Scented Candle", PieceEn, SupplierEn, 1.50m, 3.90m, 40, 10, pc),
            new("Ceramic Mug", PieceEn, SupplierEn, 1.10m, 3.50m, 3, 10, pc),
            new("Printer Paper A4 (500 sheets)", PieceEn, SupplierEn, 3.20m, 5.90m, 36, 10, ProductUnit.Pack),
            new("Pencil Set (box of 12)", PieceEn, SupplierEn, 0.90m, 2.20m, 48, 12, ProductUnit.Box),

            // ---- English: by volume
            new("Olive Oil (per liter)", VolumeEn, SupplierEn, 6.50m, 11.90m, 42.5m, 10, ProductUnit.Liter),
            new("Fresh Orange Juice (per liter)", VolumeEn, SupplierEn, 1.20m, 2.60m, 18m, 8, ProductUnit.Liter),
            new("Liquid Hand Soap (per liter)", VolumeEn, SupplierEn, 1.80m, 3.80m, 25.75m, 8, ProductUnit.Liter),
            new("Laundry Liquid (per gallon)", VolumeEn, SupplierEn, 5.50m, 10.50m, 12m, 4, ProductUnit.Gallon),
            new("Distilled Water (per gallon)", VolumeEn, SupplierEn, 0.90m, 2.10m, 0m, 6, ProductUnit.Gallon),

            // ---- English: by mass
            new("Basmati Rice (per kg)", MassEn, SupplierEn, 1.40m, 2.70m, 85m, 20, ProductUnit.Kilogram),
            new("Ground Coffee (per kg)", MassEn, SupplierEn, 9.00m, 16.50m, 12.5m, 5, ProductUnit.Kilogram),
            new("Mixed Nuts (per kg)", MassEn, SupplierEn, 8.20m, 14.90m, 9.25m, 5, ProductUnit.Kilogram),
            new("Medjool Dates (per kg)", MassEn, SupplierEn, 6.00m, 11.00m, 4m, 6, ProductUnit.Kilogram),
            new("Cheddar Cheese (per lb)", MassEn, SupplierEn, 2.60m, 4.90m, 22.5m, 6, ProductUnit.Pound),
            new("Ground Beef (per lb)", MassEn, SupplierEn, 3.10m, 5.80m, 30m, 8, ProductUnit.Pound),

            // ---- Arabic: by piece
            new("قلم حبر - أزرق", PieceAr, SupplierAr, 0.15m, 0.50m, 280, 50, pc),
            new("دفتر ملاحظات A5", PieceAr, SupplierAr, 0.80m, 1.90m, 110, 30, pc),
            new("مصباح LED بقوة 10 واط", PieceAr, SupplierAr, 1.20m, 2.80m, 70, 20, pc),
            new("شمعة معطرة", PieceAr, SupplierAr, 1.50m, 3.90m, 35, 10, pc),
            new("كوب سيراميك", PieceAr, SupplierAr, 1.10m, 3.50m, 2, 10, pc),
            new("ورق طباعة A4 (500 ورقة)", PieceAr, SupplierAr, 3.20m, 5.90m, 30, 10, ProductUnit.Pack),
            new("علبة أقلام رصاص (12 قلماً)", PieceAr, SupplierAr, 0.90m, 2.20m, 40, 12, ProductUnit.Box),

            // ---- Arabic: by volume
            new("زيت زيتون (باللتر)", VolumeAr, SupplierAr, 6.50m, 11.90m, 38m, 10, ProductUnit.Liter),
            new("عصير برتقال طازج (باللتر)", VolumeAr, SupplierAr, 1.20m, 2.60m, 15.5m, 8, ProductUnit.Liter),
            new("صابون سائل لليدين (باللتر)", VolumeAr, SupplierAr, 1.80m, 3.80m, 20.25m, 8, ProductUnit.Liter),
            new("منظف غسيل سائل (بالغالون)", VolumeAr, SupplierAr, 5.50m, 10.50m, 9m, 4, ProductUnit.Gallon),
            new("مياه مقطرة (بالغالون)", VolumeAr, SupplierAr, 0.90m, 2.10m, 0m, 6, ProductUnit.Gallon),

            // ---- Arabic: by mass
            new("أرز بسمتي (بالكيلو)", MassAr, SupplierAr, 1.40m, 2.70m, 70m, 20, ProductUnit.Kilogram),
            new("قهوة عربية مطحونة (بالكيلو)", MassAr, SupplierAr, 9.00m, 16.50m, 10.5m, 5, ProductUnit.Kilogram),
            new("مكسرات مشكلة (بالكيلو)", MassAr, SupplierAr, 8.20m, 14.90m, 7.75m, 5, ProductUnit.Kilogram),
            new("تمر مجدول (بالكيلو)", MassAr, SupplierAr, 6.00m, 11.00m, 3m, 6, ProductUnit.Kilogram),
            new("جبنة شيدر (بالباوند)", MassAr, SupplierAr, 2.60m, 4.90m, 18m, 6, ProductUnit.Pound),
            new("لحم بقري مفروم (بالباوند)", MassAr, SupplierAr, 3.10m, 5.80m, 26.5m, 8, ProductUnit.Pound)
        };

        var added = 0;
        for (var i = 0; i < seeds.Count; i++)
        {
            var s = seeds[i];
            var barcode = Ean13($"{BarcodePrefix}{i + 1:D3}");
            if (await db.Products.AnyAsync(p => p.Barcode == barcode))
                continue;

            db.Products.Add(new Product
            {
                Name = s.Name,
                Barcode = barcode,
                CostPrice = s.Cost,
                SellingPrice = s.Price,
                SupplierId = suppliers[s.Supplier].Id,
                CategoryId = categories[s.Category].Id,
                CurrencyId = 1,
                StockQuantity = s.Stock,
                MinimumStock = s.MinStock,
                MaximumStock = Math.Max(s.MinStock * 6, s.Stock),
                Unit = s.Unit,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            added++;
        }

        await db.SaveChangesAsync();
        return added;
    }

    private static async Task<Dictionary<string, ProductCategory>> SeedCategoriesAsync(ErpDbContext db)
    {
        var wanted = new (string Name, string Description)[]
        {
            (PieceEn, "Items counted and sold one by one"),
            (VolumeEn, "Liquids sold by the liter or gallon"),
            (MassEn, "Loose goods sold by the kilogram or pound"),
            (PieceAr, "منتجات تُباع بالعدد"),
            (VolumeAr, "سوائل تُباع باللتر أو بالغالون"),
            (MassAr, "بضائع سائبة تُباع بالكيلو أو بالباوند")
        };

        var result = (await db.ProductCategories.ToListAsync())
            .GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var (name, description) in wanted)
        {
            if (result.ContainsKey(name)) continue;
            var category = new ProductCategory(name, description);
            db.ProductCategories.Add(category);
            result[name] = category;
        }

        await db.SaveChangesAsync();
        return result;
    }

    private static async Task<Dictionary<string, Supplier>> SeedSuppliersAsync(ErpDbContext db)
    {
        var wanted = new (string Name, string Email, string Phone, string Address, string City)[]
        {
            (SupplierEn, "orders@testsupplier.example", "+961 1 700 100", "Industrial Zone", "Beirut"),
            (SupplierAr, "info@alnoor-supply.example", "+961 1 700 200", "المنطقة الصناعية", "بيروت")
        };

        var result = new Dictionary<string, Supplier>();
        foreach (var w in wanted)
        {
            var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Name == w.Name);
            if (supplier == null)
            {
                supplier = new Supplier(w.Name, w.Email, w.Phone, w.Address, w.City, "Lebanon") { CurrencyId = 1 };
                db.Suppliers.Add(supplier);
            }
            result[w.Name] = supplier;
        }

        await db.SaveChangesAsync();
        return result;
    }

    /// <summary>Appends the EAN-13 check digit to a 12-digit body.</summary>
    private static string Ean13(string body)
    {
        var sum = 0;
        for (var i = 0; i < body.Length; i++)
            sum += (body[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return body + (10 - sum % 10) % 10;
    }
}
