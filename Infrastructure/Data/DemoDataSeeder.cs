using Core.Entities.Configuration;
using Core.Entities.Inventory;
using Core.Entities.Products;
using Core.Entities.Sales;
using Core.Entities.Users;
using Core.Enum;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data;

/// <summary>
/// Fills an empty database with a realistic sample store ("Cedar Market", Beirut): catalogue,
/// suppliers, customers (some with debt), staff, and ~45 days of sales history so the
/// dashboard, reports, receipts and cash-drawer screens all have something to show.
/// Idempotent - does nothing if a demo product catalogue already exists. Uses a fixed random
/// seed so every run produces the same data. Triggered with <c>--seed-demo</c> (see Program.cs).
/// </summary>
public static class DemoDataSeeder
{
    public const string DemoUsername = "layla";
    public const string DemoPin = "1234";
    public const string CashierUsername = "sara";
    public const string CashierPin = "1111";

    private const int HistoryDays = 45;

    public static async Task<bool> SeedAsync(ErpDbContext db)
    {
        if (await db.Products.AnyAsync(p => p.Barcode == "6001000000012"))
            return false;

        var rng = new Random(20260926);

        await SeedStoreSettingsAsync(db);
        var users = await SeedUsersAsync(db);
        var categories = await SeedCategoriesAsync(db);
        var suppliers = await SeedSuppliersAsync(db, users);
        var products = await SeedProductsAsync(db, categories, suppliers);
        var customers = await SeedCustomersAsync(db);
        await SeedMainScreenProductsAsync(db, products);
        await SeedSalesHistoryAsync(db, rng, users, products, customers);
        await SeedAlertsAsync(db, products);

        return true;
    }

    // ---------------------------------------------------------------- settings

    private static async Task SeedStoreSettingsAsync(ErpDbContext db)
    {
        var settings = await db.StoreSettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = StoreSettings.CreateDefault();
            db.StoreSettings.Add(settings);
        }

        settings.StoreName = "Cedar Market";
        settings.AddressLine1 = "Hamra Street, Bldg 14";
        settings.AddressLine2 = "Beirut, Lebanon";
        settings.Phone = "+961 1 345 678";
        settings.Email = "hello@cedarmarket.example";
        settings.Website = "www.cedarmarket.example";
        settings.TaxId = "LB-1234567";
        settings.ReceiptFooterMessage = "Thank you for shopping with us!";
        settings.FooterLine1 = "Please keep this receipt for your records";
        settings.FooterLine2 = "Exchange within 7 days with receipt";
        await db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- users

    private static async Task<DemoUsers> SeedUsersAsync(ErpDbContext db)
    {
        var admin = await db.Users.FirstAsync(u => u.Username == "admin");

        var demo = await db.Users.FirstOrDefaultAsync(u => u.Username == DemoUsername);
        if (demo == null)
        {
            demo = new User(DemoUsername, "layla@cedarmarket.example", BCrypt.Net.BCrypt.HashPassword("demo-password"), 2, "Layla", "Haddad")
            {
                PinHash = BCrypt.Net.BCrypt.HashPassword(DemoPin),
                MustChangePin = false
            };
            db.Users.Add(demo);
        }

        var cashier = await db.Users.FirstOrDefaultAsync(u => u.Username == CashierUsername);
        if (cashier == null)
        {
            cashier = new User(CashierUsername, "sara@cedarmarket.example", BCrypt.Net.BCrypt.HashPassword("cashier-password"), 4, "Sara", "Khoury")
            {
                PinHash = BCrypt.Net.BCrypt.HashPassword(CashierPin),
                MustChangePin = false
            };
            db.Users.Add(cashier);
        }

        var cashier2 = await db.Users.FirstOrDefaultAsync(u => u.Username == "omar");
        if (cashier2 == null)
        {
            cashier2 = new User("omar", "omar@cedarmarket.example", BCrypt.Net.BCrypt.HashPassword("cashier-password"), 4, "Omar", "Nasser")
            {
                PinHash = BCrypt.Net.BCrypt.HashPassword("2222"),
                MustChangePin = false
            };
            db.Users.Add(cashier2);
        }

        await db.SaveChangesAsync();
        return new DemoUsers(admin, demo, cashier, cashier2);
    }

    private sealed record DemoUsers(User Admin, User Manager, User Cashier1, User Cashier2);

    // ---------------------------------------------------------------- catalogue

    private static async Task<Dictionary<string, ProductCategory>> SeedCategoriesAsync(ErpDbContext db)
    {
        var wanted = new (string Name, string Description)[]
        {
            ("Beverages", "Water, juices, soft drinks, coffee and tea"),
            ("Bakery", "Fresh bread and pastries"),
            ("Dairy & Eggs", "Milk, cheese, yogurt and eggs"),
            ("Fruits & Vegetables", "Fresh produce sold by weight"),
            ("Snacks", "Chips, chocolate, nuts and biscuits"),
            ("Pantry", "Rice, oil, pasta, canned goods and spices"),
            ("Household", "Cleaning and paper products"),
            ("Personal Care", "Soap, shampoo and toothpaste")
        };

        var existing = await db.ProductCategories.ToListAsync();
        var result = existing.ToDictionary(c => c.Name, c => c);
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

    private static async Task<Dictionary<string, Supplier>> SeedSuppliersAsync(ErpDbContext db, DemoUsers users)
    {
        var wanted = new (string Name, string Email, string Phone, string Address, string City)[]
        {
            ("Levant Fresh Produce", "orders@levantfresh.example", "+961 3 111 222", "Karantina Market", "Beirut"),
            ("Mount Lebanon Dairy", "sales@mldairy.example", "+961 5 222 333", "Industrial Zone", "Zahle"),
            ("Golden Wheat Bakery", "info@goldenwheat.example", "+961 1 333 444", "Sin El Fil", "Beirut"),
            ("Cedar Beverages Co.", "trade@cedarbev.example", "+961 4 444 555", "Jounieh Highway", "Jounieh"),
            ("Al Amal Wholesale", "wholesale@alamal.example", "+961 7 555 666", "Saida Souk Road", "Saida")
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

        // Two suppliers we're partway through paying, so the supplier screen shows balances
        // and a ledger. Balance must equal the sum of each supplier's transactions.
        var now = DateTime.UtcNow;
        var cedar = result["Cedar Beverages Co."];
        db.SupplierDebtTransactions.Add(new SupplierDebtTransaction(cedar.Id, users.Manager.Id, 1800m,
            SupplierDebtTransactionType.Purchase, "Beverage restock on credit") { CreatedAt = now.AddDays(-21) });
        db.SupplierDebtTransactions.Add(new SupplierDebtTransaction(cedar.Id, users.Manager.Id, -559.50m,
            SupplierDebtTransactionType.Payment, "Partial payment") { CreatedAt = now.AddDays(-9) });
        cedar.Balance = 1240.50m;

        var amal = result["Al Amal Wholesale"];
        db.SupplierDebtTransactions.Add(new SupplierDebtTransaction(amal.Id, users.Manager.Id, 685m,
            SupplierDebtTransactionType.ManualAdjustment, "Opening balance from paper ledger") { CreatedAt = now.AddDays(-30) });
        amal.Balance = 685m;

        await db.SaveChangesAsync();
        return result;
    }

    private sealed record ProductSeed(
        string Key, string Name, string Category, string Supplier, string Barcode,
        decimal Cost, decimal Price, decimal Stock, decimal MinStock, string Unit, string? Description = null);

    private static async Task<Dictionary<string, Product>> SeedProductsAsync(
        ErpDbContext db,
        Dictionary<string, ProductCategory> categories,
        Dictionary<string, Supplier> suppliers)
    {
        const string bev = "Cedar Beverages Co.", dairy = "Mount Lebanon Dairy", bakery = "Golden Wheat Bakery";
        const string fresh = "Levant Fresh Produce", whole = "Al Amal Wholesale";
        var pc = ProductUnit.Piece;
        var kg = ProductUnit.Kilogram;
        var l = ProductUnit.Liter;

        var seeds = new List<ProductSeed>
        {
            // Beverages
            new("water", "Mineral Water 500ml", "Beverages", bev, "6001000000012", 0.20m, 0.50m, 240, 60, pc),
            new("water6", "Mineral Water 500ml - 6 Pack", "Beverages", bev, "6001000000029", 1.10m, 2.60m, 40, 10, ProductUnit.Pack),
            new("cola", "Cola 330ml Can", "Beverages", bev, "6001000000036", 0.45m, 1.00m, 180, 48, pc),
            new("orange", "Orange Juice 1L", "Beverages", bev, "6001000000043", 1.20m, 2.50m, 46, 20, pc),
            new("apple", "Apple Juice 1L", "Beverages", bev, "6001000000050", 1.20m, 2.50m, 8, 20, pc),
            new("energy", "Energy Drink 250ml", "Beverages", bev, "6001000000067", 0.90m, 2.00m, 72, 24, pc),
            new("coffee", "Arabic Coffee 250g", "Beverages", whole, "6001000000074", 3.20m, 6.50m, 34, 12, pc),
            new("tea", "Black Tea 100 Bags", "Beverages", whole, "6001000000081", 1.60m, 3.50m, 28, 10, pc),
            // Bakery
            new("khubz", "Arabic Bread (Khubz) Pack", "Bakery", bakery, "6001000000098", 0.35m, 0.90m, 55, 20, pc),
            new("toast", "Sliced Toast Bread", "Bakery", bakery, "6001000000104", 0.90m, 1.90m, 32, 15, pc),
            new("croissant", "Butter Croissant", "Bakery", bakery, "6001000000111", 0.40m, 1.20m, 26, 12, pc),
            new("manakish", "Za'atar Manakish", "Bakery", bakery, "6001000000128", 0.60m, 1.75m, 18, 10, pc),
            new("baklava", "Baklava Box 500g", "Bakery", bakery, "6001000000135", 6.50m, 12.00m, 12, 5, ProductUnit.Box),
            // Dairy & eggs
            new("milk", "Fresh Milk 1L", "Dairy & Eggs", dairy, "6001000000142", 0.85m, 1.60m, 64, 24, l),
            new("labneh", "Labneh 500g", "Dairy & Eggs", dairy, "6001000000159", 1.40m, 2.90m, 30, 12, pc),
            new("yogurt", "Plain Yogurt 1kg", "Dairy & Eggs", dairy, "6001000000166", 1.30m, 2.70m, 22, 12, pc),
            new("akkawi", "Akkawi Cheese (per kg)", "Dairy & Eggs", dairy, "6001000000173", 6.80m, 11.50m, 14.5m, 5, kg),
            new("eggs", "Eggs Tray (30)", "Dairy & Eggs", dairy, "6001000000180", 3.50m, 5.50m, 19, 8, pc),
            new("butter", "Salted Butter 200g", "Dairy & Eggs", dairy, "6001000000197", 1.80m, 3.40m, 5, 10, pc),
            // Fruit & veg (by weight)
            new("tomato", "Tomatoes (per kg)", "Fruits & Vegetables", fresh, "6001000000203", 0.60m, 1.40m, 38.5m, 10, kg),
            new("cucumber", "Cucumbers (per kg)", "Fruits & Vegetables", fresh, "6001000000210", 0.55m, 1.30m, 26m, 8, kg),
            new("potato", "Potatoes (per kg)", "Fruits & Vegetables", fresh, "6001000000227", 0.50m, 1.10m, 60m, 15, kg),
            new("banana", "Bananas (per kg)", "Fruits & Vegetables", fresh, "6001000000234", 0.80m, 1.90m, 22m, 8, kg),
            new("applekg", "Red Apples (per kg)", "Fruits & Vegetables", fresh, "6001000000241", 1.10m, 2.60m, 31m, 8, kg),
            new("lemon", "Lemons (per kg)", "Fruits & Vegetables", fresh, "6001000000258", 0.90m, 2.20m, 4.5m, 6, kg),
            new("strawberry", "Strawberries (per kg)", "Fruits & Vegetables", fresh, "6001000000265", 2.80m, 5.90m, 9m, 4, kg),
            // Snacks
            new("chips", "Potato Chips 150g", "Snacks", whole, "6001000000272", 0.80m, 1.80m, 90, 30, pc),
            new("choc", "Milk Chocolate Bar 100g", "Snacks", whole, "6001000000289", 0.85m, 1.90m, 76, 30, pc),
            new("nuts", "Roasted Mixed Nuts 200g", "Snacks", whole, "6001000000296", 2.60m, 5.20m, 24, 10, pc),
            new("biscuit", "Digestive Biscuits", "Snacks", whole, "6001000000302", 0.70m, 1.60m, 48, 20, pc),
            new("gum", "Chewing Gum", "Snacks", whole, "6001000000319", 0.25m, 0.75m, 0, 20, pc),
            // Pantry
            new("rice", "Basmati Rice 2kg", "Pantry", whole, "6001000000326", 2.90m, 5.40m, 36, 12, pc),
            new("oil", "Olive Oil 1L", "Pantry", whole, "6001000000333", 6.50m, 11.90m, 20, 8, l),
            new("pasta", "Spaghetti 500g", "Pantry", whole, "6001000000340", 0.60m, 1.40m, 70, 24, pc),
            new("tuna", "Canned Tuna 160g", "Pantry", whole, "6001000000357", 1.10m, 2.30m, 58, 20, pc),
            new("hummus", "Chickpeas Can 400g", "Pantry", whole, "6001000000364", 0.55m, 1.20m, 44, 20, pc),
            new("sugar", "White Sugar 1kg", "Pantry", whole, "6001000000371", 0.75m, 1.50m, 52, 20, pc),
            new("zaatar", "Za'atar Mix 250g", "Pantry", whole, "6001000000388", 1.90m, 4.20m, 15, 8, pc),
            // Household
            new("tissue", "Facial Tissues Box", "Household", whole, "6001000000395", 0.70m, 1.60m, 66, 20, pc),
            new("dish", "Dish Soap 750ml", "Household", whole, "6001000000401", 1.10m, 2.40m, 40, 12, pc),
            new("detergent", "Laundry Detergent 3kg", "Household", whole, "6001000000418", 6.80m, 12.50m, 14, 6, pc),
            new("toiletpaper", "Toilet Paper 12 Rolls", "Household", whole, "6001000000425", 3.60m, 6.90m, 3, 8, pc),
            // Personal care
            new("soap", "Olive Oil Soap Bar", "Personal Care", whole, "6001000000432", 0.60m, 1.50m, 80, 20, pc),
            new("shampoo", "Shampoo 400ml", "Personal Care", whole, "6001000000449", 2.10m, 4.50m, 27, 10, pc),
            new("toothpaste", "Toothpaste 100ml", "Personal Care", whole, "6001000000456", 1.20m, 2.60m, 35, 12, pc),
            // General merchandise (existing seed categories)
            new("charger", "USB-C Phone Charger", "Electronics", whole, "6001000000463", 3.50m, 8.90m, 16, 5, pc),
            new("earbuds", "Wired Earbuds", "Electronics", whole, "6001000000470", 1.80m, 4.90m, 21, 6, pc),
            new("batteries", "AA Batteries 4-Pack", "Electronics", whole, "6001000000487", 1.40m, 3.20m, 42, 12, pc),
            new("socks", "Cotton Socks 3-Pair", "Clothing", whole, "6001000000494", 2.20m, 5.50m, 25, 8, pc)
        };

        var result = new Dictionary<string, Product>();
        foreach (var s in seeds)
        {
            var product = new Product
            {
                Name = s.Name,
                Description = s.Description,
                Barcode = s.Barcode,
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
                CreatedAt = DateTime.UtcNow.AddDays(-HistoryDays - 10)
            };
            db.Products.Add(product);
            result[s.Key] = product;
        }
        await db.SaveChangesAsync();

        // The 6-pack is a pack of the single 500ml bottle.
        var pack = result["water6"];
        pack.IsPack = true;
        pack.PackSize = 6;
        pack.SingleProductId = result["water"].Id;
        await db.SaveChangesAsync();

        return result;
    }

    private static async Task SeedMainScreenProductsAsync(ErpDbContext db, Dictionary<string, Product> products)
    {
        // The POS "quick access" grid: the things most customers walk in for.
        var favourites = new[]
        {
            "water", "cola", "khubz", "milk", "eggs", "tomato", "banana", "chips", "choc", "coffee",
            "croissant", "labneh", "orange", "rice", "oil", "akkawi"
        };

        var order = 1;
        foreach (var key in favourites)
        {
            db.MainScreenProducts.Add(new MainScreenProduct
            {
                ProductId = products[key].Id,
                DisplayOrder = order++,
                IsPinned = order <= 7,
                CreatedAt = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- customers

    private static async Task<List<Customer>> SeedCustomersAsync(ErpDbContext db)
    {
        var rows = new (string First, string Last, string Phone, string Area)[]
        {
            ("Rima", "Saad", "70 123 456", "Hamra"),
            ("Karim", "Mansour", "71 234 567", "Verdun"),
            ("Nadine", "Abi Nader", "03 345 678", "Achrafieh"),
            ("Joseph", "Khalil", "76 456 789", "Mar Mikhael"),
            ("Hala", "Fakhoury", "70 567 890", "Ras Beirut"),
            ("Tarek", "Aoun", "81 678 901", "Gemmayze"),
            ("Maya", "Bitar", "03 789 012", "Badaro"),
            ("Elias", "Sfeir", "71 890 123", "Hamra"),
            ("Lina", "Chahine", "76 901 234", "Sanayeh"),
            ("Walid", "Jaber", "70 012 345", "Verdun")
        };

        var list = new List<Customer>();
        foreach (var r in rows)
        {
            var c = new Customer(r.First, r.Last, $"{r.First}.{r.Last.Replace(" ", "")}@example.com".ToLowerInvariant(),
                r.Phone, r.Area, "Beirut", "Lebanon");
            c.CreatedAt = DateTime.UtcNow.AddDays(-60);
            db.Customers.Add(c);
            list.Add(c);
        }
        await db.SaveChangesAsync();

        // Include the pre-existing seed customer so debt features show up on a familiar name too.
        var existing = await db.Customers.Where(c => c.Id == 1).FirstOrDefaultAsync();
        if (existing != null) list.Insert(0, existing);
        return list;
    }

    // ---------------------------------------------------------------- sales history

    private sealed record Line(Product Product, int Qty, decimal? WeightOrVolume, decimal UnitPrice)
    {
        public decimal Total => WeightOrVolume.HasValue ? Math.Round(UnitPrice * WeightOrVolume.Value, 2) : UnitPrice * Qty;
        public decimal ReceiptQty => WeightOrVolume ?? Qty;
    }

    private static async Task SeedSalesHistoryAsync(
        ErpDbContext db, Random rng, DemoUsers users,
        Dictionary<string, Product> products, List<Customer> customers)
    {
        var cash = await db.PaymentMethods.FirstAsync(p => p.Name == "Cash");
        var card = await db.PaymentMethods.FirstAsync(p => p.Name == "Card");
        var debt = await db.PaymentMethods.FirstAsync(p => p.Name == "Customer Debt");

        // Popularity weights - staples sell far more than baklava.
        var weighted = new List<(Product P, double W)>();
        foreach (var kv in products)
        {
            var w = kv.Key switch
            {
                "water" or "cola" or "khubz" or "milk" or "chips" or "choc" => 9,
                "tomato" or "banana" or "eggs" or "croissant" or "coffee" or "orange" or "cucumber" or "potato" => 6,
                "labneh" or "yogurt" or "toast" or "rice" or "sugar" or "pasta" or "biscuit" or "energy" or "manakish" => 4,
                "baklava" or "detergent" or "charger" or "earbuds" or "socks" or "oil" => 1,
                "gum" => 0,   // out of stock at the end of the history
                _ => 2
            };
            weighted.Add((kv.Value, w));
        }
        var totalWeight = weighted.Sum(x => x.W);

        Product PickProduct()
        {
            var roll = rng.NextDouble() * totalWeight;
            foreach (var (p, w) in weighted)
            {
                roll -= w;
                if (roll <= 0) return p;
            }
            return weighted[^1].P;
        }

        var now = DateTime.UtcNow;
        var cashiers = new[] { users.Cashier1, users.Cashier2, users.Manager };

        // Customers who buy on credit; their ledger is built alongside the orders.
        var debtCustomers = customers.Skip(1).Take(4).ToList();
        var ledger = new List<(Customer C, decimal Amount, CustomerDebtTransactionType T, string? Note, Order? O, DateTime When, User U)>();

        var receiptCounter = new Dictionary<DateTime, int>();

        for (var d = HistoryDays; d >= 0; d--)
        {
            var day = now.Date.AddDays(-d);
            var isToday = d == 0;
            var isWeekend = day.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;
            var ordersToday = rng.Next(isWeekend ? 9 : 5, isWeekend ? 15 : 10);
            if (isToday) ordersToday = rng.Next(4, 8);

            // Slight upward trend so the growth chart on the dashboard isn't flat.
            ordersToday += (HistoryDays - d) / 12;

            var shiftCashier = cashiers[d % 3];
            var opening = 100m;
            var openedAt = day.AddHours(7).AddMinutes(rng.Next(0, 20));
            var session = new CashDrawerSession(shiftCashier.Id, opening, "Morning shift")
            {
                OpenedAt = openedAt,
                CreatedAt = openedAt,
                IsOpen = false
            };
            db.Set<CashDrawerSession>().Add(session);
            await db.SaveChangesAsync();

            decimal cashTotal = 0, cardTotal = 0, otherTotal = 0, salesTotal = 0;
            var txCount = 0;
            var lastTime = openedAt;

            for (var i = 0; i < ordersToday; i++)
            {
                var when = day.AddHours(8).AddMinutes(rng.Next(0, 12 * 60));
                if (isToday && when > now) when = now.AddMinutes(-rng.Next(5, 240));
                if (when < openedAt) when = openedAt.AddMinutes(5);
                if (when > lastTime) lastTime = when;

                // Build the basket
                var lines = new List<Line>();
                var itemCount = rng.Next(1, 7);
                for (var n = 0; n < itemCount; n++)
                {
                    var p = PickProduct();
                    if (lines.Any(x => x.Product.Id == p.Id)) continue;
                    if (ProductUnit.RequiresDecimalQuantity(p.Unit) && p.Name.Contains("per kg", StringComparison.OrdinalIgnoreCase))
                    {
                        var wv = Math.Round((decimal)(0.25 + rng.NextDouble() * 2.25), 2);
                        lines.Add(new Line(p, 1, wv, p.SellingPrice));
                    }
                    else
                    {
                        var qty = rng.Next(1, p.SellingPrice < 2m ? 5 : 3);
                        lines.Add(new Line(p, qty, null, p.SellingPrice));
                    }
                }
                if (lines.Count == 0) continue;

                var total = lines.Sum(x => x.Total);

                // Payment method / customer
                var payRoll = rng.NextDouble();
                Customer? customer = null;
                PaymentMethod method;
                if (payRoll < 0.07 && debtCustomers.Count > 0)
                {
                    customer = debtCustomers[rng.Next(debtCustomers.Count)];
                    method = debt;
                }
                else if (payRoll < 0.45)
                {
                    method = card;
                    if (rng.NextDouble() < 0.35) customer = customers[rng.Next(customers.Count)];
                }
                else
                {
                    method = cash;
                    if (rng.NextDouble() < 0.2) customer = customers[rng.Next(customers.Count)];
                }

                // Occasional cart-level discount
                decimal discount = 0;
                if (rng.NextDouble() < 0.08 && total > 10m)
                {
                    discount = Math.Round(total * 0.1m, 2);
                }
                var finalTotal = total - discount;

                var cart = new Cart(customer?.Id)
                {
                    CreatedAt = when,
                    IsActive = false,
                    DiscountAmount = discount > 0 ? discount : null,
                    DiscountPercentage = discount > 0 ? 10m : null,
                    DiscountCode = discount > 0 ? "LOYALTY10" : null
                };
                foreach (var line in lines)
                {
                    cart.CartItems.Add(new CartItem(0, line.Product.Id, line.Qty, line.UnitPrice, line.WeightOrVolume)
                    {
                        CreatedAt = when
                    });
                }
                db.Carts.Add(cart);
                await db.SaveChangesAsync();

                var cashier = cashiers[rng.Next(cashiers.Length)];
                if (cashier.Id == users.Manager.Id && rng.NextDouble() < 0.6) cashier = shiftCashier;

                var order = new Order(customer?.Id, cart.Id, finalTotal, (int)OrderStatus.Delivered, 1)
                {
                    OrderDate = when,
                    CreatedAt = when,
                    CashDrawerSessionId = session.Id
                };
                db.Orders.Add(order);
                await db.SaveChangesAsync();

                var seq = receiptCounter.GetValueOrDefault(day.Date) + 1;
                receiptCounter[day.Date] = seq;
                var receipt = new Receipt($"RCP-{day:yyyyMMdd}-{seq:D3}", order.Id, customer?.Id, cashier.Id,
                    total, 0m, discount, finalTotal, method.Id, 1)
                {
                    ReceiptDate = when,
                    CreatedAt = when,
                    AmountPaid = method == cash ? RoundUpPayment(finalTotal) : finalTotal
                };
                receipt.CalculateChangeAmount();
                if (method == debt) { receipt.AmountPaid = 0; receipt.ChangeAmount = 0; }

                foreach (var line in lines)
                {
                    receipt.ReceiptItems.Add(new ReceiptItem(0, line.Product.Id, line.Product.Name, line.ReceiptQty,
                        line.UnitPrice, 0m, line.Product.Barcode, line.Product.Unit, line.WeightOrVolume)
                    {
                        CreatedAt = when
                    });
                }
                receipt.ReceiptPayments.Add(new ReceiptPayment(0, method.Id, method == debt ? finalTotal : finalTotal)
                {
                    PaymentDate = when,
                    CreatedAt = when
                });
                db.Receipts.Add(receipt);
                await db.SaveChangesAsync();

                salesTotal += finalTotal;
                txCount++;
                if (method == cash) cashTotal += finalTotal;
                else if (method == card) cardTotal += finalTotal;
                else otherTotal += finalTotal;

                if (method == debt && customer != null)
                {
                    ledger.Add((customer, finalTotal, CustomerDebtTransactionType.Sale, null, order, when, cashier));
                }
            }

            // Mid-shift cash drop on busy days
            decimal drops = 0;
            if (txCount >= 8)
            {
                drops = 150m;
                db.Set<CashDrop>().Add(new CashDrop(session.Id, shiftCashier.Id, drops, "Safe deposit", "Excess cash moved to safe")
                {
                    DroppedAt = openedAt.AddHours(6),
                    CreatedAt = openedAt.AddHours(6)
                });
            }

            var expected = opening + cashTotal - drops;
            session.ExpectedCash = expected;
            // Small, believable counting differences (mostly exact)
            var diffRoll = rng.NextDouble();
            session.ActualCash = diffRoll < 0.7 ? expected : expected + (diffRoll < 0.85 ? -1.50m : 0.75m);
            session.TotalSales = salesTotal;
            session.TotalCash = cashTotal;
            session.TotalCard = cardTotal;
            session.TotalOtherPayments = otherTotal;
            session.TotalTransactions = txCount;
            session.TotalCashDrops = drops;
            session.TotalCashAdded = 0;
            session.TotalDiscounts = 0;
            session.TotalAdjustments = 0;
            session.ClosedAt = lastTime.AddMinutes(30);
            session.ClosingNotes = null;

            // Leave today's shift closed too: the demo flow opens a fresh one live.
            await db.SaveChangesAsync();
        }

        // Debt ledger: sales on credit (from the orders above) plus a few payments and one manual adjustment
        var balances = new Dictionary<int, decimal>();
        foreach (var e in ledger)
        {
            db.CustomerDebtTransactions.Add(new CustomerDebtTransaction(e.C.Id, e.U.Id, e.Amount, e.T, "POS sale on account", e.O!.Id)
            {
                CreatedAt = e.When
            });
            balances[e.C.Id] = balances.GetValueOrDefault(e.C.Id) + e.Amount;
        }

        // Customers pay part of what they owe
        foreach (var c in debtCustomers)
        {
            var owed = balances.GetValueOrDefault(c.Id);
            if (owed <= 0) continue;
            var payment = Math.Round(owed * (c.Id % 2 == 0 ? 0.5m : 0.8m), 2);
            if (payment <= 0) continue;
            db.CustomerDebtTransactions.Add(new CustomerDebtTransaction(c.Id, users.Manager.Id, -payment,
                CustomerDebtTransactionType.Payment, "Cash payment received", null)
            {
                CreatedAt = now.AddDays(-rng.Next(1, 8))
            });
            balances[c.Id] = owed - payment;
        }

        // An old opening balance for one regular
        var regular = customers[5];
        db.CustomerDebtTransactions.Add(new CustomerDebtTransaction(regular.Id, users.Manager.Id, 45m,
            CustomerDebtTransactionType.ManualAdjustment, "Opening balance from paper ledger")
        {
            CreatedAt = now.AddDays(-30)
        });
        balances[regular.Id] = balances.GetValueOrDefault(regular.Id) + 45m;

        foreach (var (id, balance) in balances)
        {
            var customer = customers.First(c => c.Id == id);
            customer.Balance = balance;
        }

        // Stock was decremented by the sales above in real life; apply that on top of the
        // "current stock" figures chosen for the catalogue would double-count, so leave
        // the catalogue's stock figures as the authoritative current levels.
        await db.SaveChangesAsync();
    }

    private static decimal RoundUpPayment(decimal total)
    {
        // Customers usually hand over a round note.
        var rounded = Math.Ceiling(total);
        var note = new[] { 1m, 5m, 10m, 20m, 50m }.FirstOrDefault(n => n >= rounded);
        if (note == 0) note = Math.Ceiling(rounded / 10m) * 10m;
        // Prefer exact-ish change for small differences
        return rounded - total <= 0.5m ? rounded : Math.Max(note, rounded);
    }

    // ---------------------------------------------------------------- alerts

    private static async Task SeedAlertsAsync(ErpDbContext db, Dictionary<string, Product> products)
    {
        var low = products.Values
            .Where(p => p.MinimumStock.HasValue && p.StockQuantity > 0 && p.StockQuantity <= p.MinimumStock)
            .ToList();
        foreach (var p in low)
        {
            db.Alerts.Add(new Core.Entities.Notifications.Alert
            {
                Title = "Low stock",
                Message = $"{p.Name} is running low ({p.StockQuantity:0.##} left, minimum {p.MinimumStock:0.##}).",
                Type = Core.Entities.Notifications.AlertType.LowStock,
                Status = Core.Entities.Notifications.AlertStatus.Active,
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            });
        }

        foreach (var p in products.Values.Where(p => p.StockQuantity <= 0))
        {
            db.Alerts.Add(new Core.Entities.Notifications.Alert
            {
                Title = "Out of stock",
                Message = $"{p.Name} is out of stock.",
                Type = Core.Entities.Notifications.AlertType.OutOfStock,
                Status = Core.Entities.Notifications.AlertStatus.Active,
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            });
        }

        var notes = new (string T, string M, Core.Entities.Notifications.NotificationType Ty, Core.Entities.Notifications.NotificationPriority Pr, int HoursAgo)[]
        {
            ("Daily backup completed", "Your data was backed up successfully.", Core.Entities.Notifications.NotificationType.Success, Core.Entities.Notifications.NotificationPriority.Low, 6),
            ("Low stock warning", "Several products are below their minimum stock level - review the Inventory page.", Core.Entities.Notifications.NotificationType.Warning, Core.Entities.Notifications.NotificationPriority.High, 3),
            ("Delivery expected", "Levant Fresh Produce delivery is scheduled for tomorrow morning.", Core.Entities.Notifications.NotificationType.Info, Core.Entities.Notifications.NotificationPriority.Normal, 5),
            ("Customer payment received", "A customer settled part of their account balance.", Core.Entities.Notifications.NotificationType.Success, Core.Entities.Notifications.NotificationPriority.Normal, 26)
        };
        foreach (var n in notes)
        {
            db.Notifications.Add(new Core.Entities.Notifications.Notification
            {
                Title = n.T,
                Message = n.M,
                Type = n.Ty,
                Priority = n.Pr,
                IsRead = false,
                CreatedAt = DateTime.UtcNow.AddHours(-n.HoursAgo)
            });
        }

        await db.SaveChangesAsync();
    }
}
