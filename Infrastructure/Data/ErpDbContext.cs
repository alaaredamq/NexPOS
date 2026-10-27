using Core.Entities.Backup;
using Core.Entities.Configuration;
using Core.Entities.Inventory;
using Core.Entities.Notifications;
using Core.Entities.Products;
using Core.Entities.Sales;
using Core.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data;

public class ErpDbContext : DbContext
{
    public ErpDbContext(DbContextOptions<ErpDbContext> options) : base(options)
    {
    }

    public DbSet<Currency> Currencies { get; set; }
    public DbSet<Customer> Customers { get; set; }
    public DbSet<CustomerDebtTransaction> CustomerDebtTransactions { get; set; }
    public DbSet<SupplierDebtTransaction> SupplierDebtTransactions { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<Cart> Carts { get; set; }
    public DbSet<CartItem> CartItems { get; set; }
    public DbSet<CartSession> CartSessions { get; set; }
    public DbSet<CartSessionItem> CartSessionItems { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<ProductCategory> ProductCategories { get; set; }
    public DbSet<MainScreenProduct> MainScreenProducts { get; set; }
        public DbSet<ProductDisplayOrder> ProductDisplayOrders { get; set; }
    public DbSet<ProductPrice> ProductPrices { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<SystemSettings> SystemSettings { get; set; }
        public DbSet<StoreSettings> StoreSettings { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }
        public DbSet<CodeList> CodeLists { get; set; }
        public DbSet<CodeListValue> CodeListValues { get; set; }
        public DbSet<UrlRoute> UrlRoutes { get; set; }
        public DbSet<ExchangeRate> ExchangeRates { get; set; }
        public DbSet<Tax> Taxes { get; set; }
        public DbSet<TaxRate> TaxRates { get; set; }
        public DbSet<Discount> Discounts { get; set; }
        public DbSet<PaymentMethod> PaymentMethods { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<Alert> Alerts { get; set; }
        // The audit trail is NOT in this database: it has its own file (see Infrastructure.Auditing.AuditDbContext).
        public DbSet<BackupRecord> BackupRecords { get; set; }
        public DbSet<Backup> Backups { get; set; }
        public DbSet<BackupSchedule> BackupSchedules { get; set; }
        public DbSet<CustomerCart> CustomerCarts { get; set; }
        
        // Receipts
        public DbSet<Receipt> Receipts { get; set; }
        public DbSet<ReceiptItem> ReceiptItems { get; set; }
        public DbSet<ReceiptPayment> ReceiptPayments { get; set; }
        
        // User Management
        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<UserSession> UserSessions { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }
        public DbSet<UserPermission> UserPermissions { get; set; }

    #region Navigations

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Tax>()
            .HasOne(t => t.Supplier)
            .WithMany()
            .HasForeignKey(t => t.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TaxRate>()
            .HasOne(t => t.Category)
            .WithMany()
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Discount>()
            .HasOne(d => d.Supplier)
            .WithMany()
            .HasForeignKey(d => d.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PaymentMethod>()
            .HasOne(pm => pm.Supplier)
            .WithMany()
            .HasForeignKey(pm => pm.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ExchangeRate>()
            .HasOne(er => er.FromCurrency)
            .WithMany()
            .HasForeignKey(er => er.FromCurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ExchangeRate>()
            .HasOne(er => er.ToCurrency)
            .WithMany()
            .HasForeignKey(er => er.ToCurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ExchangeRate>()
            .HasOne(er => er.Supplier)
            .WithMany()
            .HasForeignKey(er => er.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ExchangeRate>()
            .HasIndex(er => new { er.FromCurrencyId, er.ToCurrencyId, er.SupplierId })
            .IsUnique();

        // =========================
        // Relationships
        // =========================
        modelBuilder.Entity<Cart>()
            .HasMany(c => c.CartItems)
            .WithOne(ci => ci.Cart)
            .HasForeignKey(ci => ci.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.Property(e => e.WeightOrVolume).HasPrecision(18, 3);
        });

        modelBuilder.Entity<CartSession>()
            .HasMany(cs => cs.CartItems)
            .WithOne(csi => csi.CartSession)
            .HasForeignKey(csi => csi.CartSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CartSessionItem>(entity =>
        {
            entity.Property(e => e.WeightOrVolume).HasPrecision(18, 3);
        });

        modelBuilder.Entity<CartSession>()
            .HasOne(cs => cs.Customer)
            .WithMany()
            .HasForeignKey(cs => cs.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<CartSession>()
            .HasIndex(cs => cs.SessionId)
            .IsUnique();

        // ClientSetNull, not Cascade: Order already cascades from Cart (Order.CartId is
        // required, and Cart cascades from Customer below), so a second Cascade path straight
        // from Customer to Order via the nullable CustomerId is the same multi-path conflict
        // hit above. ClientSetNull also matches the actual semantics better - CustomerId is
        // nullable specifically to represent "walk-in, no customer," and deleting a customer
        // shouldn't delete their order history, just anonymize it.
        modelBuilder.Entity<Customer>().HasQueryFilter(c => !c.IsDeleted);
        // Rows that require a Customer get the matching filter. Without it, EF warns at every start
        // and a soft-deleted customer's carts / debt ledger would still be returned, with the
        // Customer navigation silently filtered out from under them.
        modelBuilder.Entity<CustomerCart>().HasQueryFilter(cc => !cc.Customer.IsDeleted);
        modelBuilder.Entity<CustomerDebtTransaction>().HasQueryFilter(t => !t.Customer.IsDeleted);

        modelBuilder.Entity<Customer>()
            .HasMany(c => c.Orders)
            .WithOne(o => o.Customer)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.ClientSetNull);

        modelBuilder.Entity<Customer>()
            .HasMany(c => c.Carts)
            .WithOne(ca => ca.Customer)
            .HasForeignKey(ca => ca.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Customer>()
            .HasMany(c => c.DebtTransactions)
            .WithOne(t => t.Customer)
            .HasForeignKey(t => t.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not Cascade: Order already has its own delete path from Customer via
        // ClientSetNull above, so cascading through it too would hit the same multi-path
        // conflict that ClientSetNull was introduced to avoid.
        modelBuilder.Entity<CustomerDebtTransaction>()
            .HasOne(t => t.Order)
            .WithMany()
            .HasForeignKey(t => t.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CustomerDebtTransaction>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProductCategory>()
            .HasMany(pc => pc.Products)
            .WithOne(p => p.Category)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Supplier>()
            .HasMany(s => s.Products)
            .WithOne(p => p.Supplier)
            .HasForeignKey(p => p.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Supplier>()
            .HasMany(s => s.DebtTransactions)
            .WithOne(t => t.Supplier)
            .HasForeignKey(t => t.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SupplierDebtTransaction>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CustomerCart>()
            .HasOne(cc => cc.Customer)
            .WithMany(c => c.CustomerCarts)
            .HasForeignKey(cc => cc.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CustomerCart>()
            .HasOne(cc => cc.Cart)
            .WithMany()
            .HasForeignKey(cc => cc.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        // Receipt Relationships
        modelBuilder.Entity<Receipt>(entity =>
        {
            entity.ToTable("Receipts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ReceiptNumber).HasMaxLength(50).IsRequired();

            entity.HasIndex(e => e.ReceiptNumber).IsUnique();
            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => e.CustomerId);
            entity.HasIndex(e => e.CashierId);
            entity.HasIndex(e => e.ReceiptDate);
            entity.HasIndex(e => e.IsVoid);
            
            entity.HasOne(e => e.Order)
                .WithMany()
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(e => e.Customer)
                .WithMany()
                .HasForeignKey(e => e.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(e => e.Cashier)
                .WithMany()
                .HasForeignKey(e => e.CashierId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(e => e.VoidedByUser)
                .WithMany()
                .HasForeignKey(e => e.VoidedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(e => e.PaymentMethod)
                .WithMany()
                .HasForeignKey(e => e.PaymentMethodId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasOne(e => e.Currency)
                .WithMany()
                .HasForeignKey(e => e.CurrencyId)
                .OnDelete(DeleteBehavior.Restrict);
            
            entity.HasMany(e => e.ReceiptItems)
                .WithOne(ri => ri.Receipt)
                .HasForeignKey(ri => ri.ReceiptId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasMany(e => e.ReceiptPayments)
                .WithOne(rp => rp.Receipt)
                .HasForeignKey(rp => rp.ReceiptId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReceiptItem>(entity =>
        {
            entity.ToTable("ReceiptItems");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProductName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Unit).HasMaxLength(50);
            entity.Property(e => e.Quantity).HasPrecision(18, 3);
            entity.Property(e => e.WeightOrVolume).HasPrecision(18, 3);

            entity.HasIndex(e => e.ReceiptId);
            entity.HasIndex(e => e.ProductId);
            
            entity.HasOne(e => e.Product)
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReceiptPayment>(entity =>
        {
            entity.ToTable("ReceiptPayments");
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.ReceiptId);
            entity.HasIndex(e => e.PaymentMethodId);
            
            entity.HasOne(e => e.PaymentMethod)
                .WithMany()
                .HasForeignKey(e => e.PaymentMethodId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MainScreenProduct>(entity =>
        {
            entity.ToTable("MainScreenProducts");
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Product)
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
                
            entity.HasIndex(e => new { e.ProductId }).IsUnique();
        });

            modelBuilder.Entity<ProductDisplayOrder>(entity =>
            {
                entity.ToTable("ProductDisplayOrders");
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Product)
                    .WithMany()
                    .HasForeignKey(e => e.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => new { e.ProductId }).IsUnique();
                entity.HasIndex(e => e.DisplayOrder);
            });

        modelBuilder.Entity<ProductPrice>(entity =>
        {
            entity.ToTable("ProductPrices");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(50).IsRequired();

            entity.HasOne<Product>()
                .WithMany(p => p.PriceTiers)
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.ProductId, e.SortOrder });
        });

        modelBuilder.Entity<SystemSettings>(entity =>
        {
            entity.ToTable("SystemSettings");
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.SettingKey).IsUnique();
            entity.HasIndex(e => e.Category);
        });

        // Backup entities configuration
        modelBuilder.Entity<Backup>(entity =>
        {
            entity.ToTable("Backups");
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.BackupType);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.CreatedBy);
        });

        modelBuilder.Entity<BackupSchedule>(entity =>
        {
            entity.ToTable("BackupSchedules");
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.NextRun);
            entity.HasIndex(e => e.ScheduleFrequency);
        });

        // =========================
        // Seeding Data
        // =========================

        // Seed CreatedAt values must be static, not DateTime.UtcNow - a HasData value that
        // changes on every model build makes EF think the model itself changes every time,
        // which breaks `migrations add`/`database update` (PendingModelChangesWarning). This
        // affects every entity below that derives from BaseEntity (whose CreatedAt property
        // defaults to DateTime.UtcNow) and doesn't explicitly override it in its seed data.
        var seedTimestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        modelBuilder.Entity<Currency>().HasData(
            new Currency("US Dollar", 1.0f, true, true, "USD", "$") { Id = 1, CreatedAt = seedTimestamp },
            new Currency("Euro", 1.1f, false, true, "EUR", "€") { Id = 2, CreatedAt = seedTimestamp },
            new Currency("Lebanese Pound", 89500f, false, true, "LBP", "ل.ل") { Id = 3, CreatedAt = seedTimestamp }
        );

        modelBuilder.Entity<ProductCategory>().HasData(
            new ProductCategory("Electronics", "Electronic devices") { Id = 1, CreatedAt = seedTimestamp },
            new ProductCategory("Clothing", "Apparel and garments") { Id = 2, CreatedAt = seedTimestamp }
        );

        modelBuilder.Entity<Supplier>().HasData(
            new Supplier("Default Supplier", "supplier@example.com", "12345678", "Some Address", "Beirut", "Lebanon",
                "1100")
            {
                Id = 1,
                CurrencyId = 1,
                DiscountRate = 0,
                IsActive = true,
                CreatedAt = seedTimestamp
            }
        );

        modelBuilder.Entity<Customer>().HasData(
            new Customer("Alaa", "Reda", "alaa@example.com", "78912345", "Hamra", "Beirut", "Lebanon", "1102")
            {
                Id = 1,
                Balance = 0,
                IsActive = true,
                CreatedAt = seedTimestamp
            }
        );

        modelBuilder.Entity<Tax>().HasData(
            new Tax("VAT", 0.11m, true) { Id = 1, CreatedAt = seedTimestamp }
        );

        modelBuilder.Entity<Discount>().HasData(
            new Discount("No Discount", 0m, true) { Id = 1, CreatedAt = seedTimestamp }
        );

        modelBuilder.Entity<PaymentMethod>().HasData(
            new PaymentMethod("Cash", true) { Id = 1, CreatedAt = seedTimestamp },
            new PaymentMethod("Card", false) { Id = 2, CreatedAt = seedTimestamp },
            // System entry used by POS "Add to Customer Debt" - not selectable as a normal
            // payment method, resolved by name from POSCheckoutService.
            new PaymentMethod("Customer Debt", false) { Id = 3, CreatedAt = seedTimestamp }
        );

        // No seed for notifications/audit logs/customer carts

        modelBuilder.Entity<AppSetting>().HasData(
            new AppSetting("SiteName", "My ERP System", "The name of the site", "string") { Id = 1 },
            new AppSetting("MaxCartItems", "100", "Maximum items allowed in cart", "int") { Id = 2 }
        );

        // CodeList Relationships
        modelBuilder.Entity<CodeListValue>()
            .HasOne(clv => clv.CodeList)
            .WithMany(cl => cl.Values)
            .HasForeignKey(clv => clv.CodeListId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CodeList>()
            .HasIndex(cl => cl.Key)
            .IsUnique();

        // UrlRoute Configuration
        modelBuilder.Entity<UrlRoute>()
            .HasIndex(ur => ur.Key)
            .IsUnique();

        modelBuilder.Entity<UrlRoute>()
            .HasIndex(ur => new { ur.Pattern, ur.IsActive });

        // User Management Relationships
        modelBuilder.Entity<User>()
            .HasOne(u => u.Role)
            .WithMany(r => r.Users)
            .HasForeignKey(u => u.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<User>()
            .HasMany(u => u.Sessions)
            .WithOne(s => s.User)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<User>()
            .HasMany(u => u.UserPermissions)
            .WithOne(up => up.User)
            .HasForeignKey(up => up.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Role>()
            .HasMany(r => r.RolePermissions)
            .WithOne(rp => rp.Role)
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Permission>()
            .HasMany(p => p.RolePermissions)
            .WithOne(rp => rp.Permission)
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Permission>()
            .HasMany(p => p.UserPermissions)
            .WithOne(up => up.Permission)
            .HasForeignKey(up => up.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Notifications
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("Notifications");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Message).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.Type).HasConversion<int>();
            entity.Property(e => e.Priority).HasConversion<int>();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Alert>(entity =>
        {
            entity.ToTable("Alerts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Message).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.Type).HasConversion<int>();
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.Metadata).HasMaxLength(2000);
            
            // ClientSetNull, not SetNull: two ON DELETE SET NULL paths from Users to the same
            // Alerts table (via AcknowledgedByUserId and ResolvedByUserId) is a cascade-path
            // conflict SQL Server refuses to create the constraints for. ClientSetNull gets the
            // same practical effect (the FK is nulled when the referenced user is deleted) via
            // EF's own SaveChanges logic instead of a second real DB-level cascade action.
            entity.HasOne(e => e.AcknowledgedByUser)
                .WithMany()
                .HasForeignKey(e => e.AcknowledgedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull);

            entity.HasOne(e => e.ResolvedByUser)
                .WithMany()
                .HasForeignKey(e => e.ResolvedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull);
        });

        // Restrict, not the convention default of Cascade: CashDrop already cascades from
        // CashDrawerSession (CashDrawerSessionId), which itself cascades from Users - a second
        // Cascade path straight from Users to CashDrop via UserId is the same multi-path
        // conflict SQL Server rejects that Alert's two User FKs hit above.
        modelBuilder.Entity<CashDrop>()
            .HasOne(cd => cd.User)
            .WithMany()
            .HasForeignKey(cd => cd.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BackupRecord>(entity =>
        {
            entity.ToTable("BackupRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.FilePath).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Type).HasConversion<int>();
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000);
        });

        // Unique constraints
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<Role>()
            .HasIndex(r => r.Name)
            .IsUnique();

        modelBuilder.Entity<Permission>()
            .HasIndex(p => new { p.Category, p.Action, p.Resource })
            .IsUnique();

        modelBuilder.Entity<UserSession>()
            .HasIndex(s => s.SessionToken)
            .IsUnique();

        modelBuilder.Entity<RolePermission>()
            .HasIndex(rp => new { rp.RoleId, rp.PermissionId })
            .IsUnique();

        modelBuilder.Entity<UserPermission>()
            .HasIndex(up => new { up.UserId, up.PermissionId })
            .IsUnique();

        // Seed Data
        modelBuilder.Entity<Role>().HasData(
            new Role("SuperAdmin", "Super Administrator with all permissions", true) { Id = 1, CreatedAt = seedTimestamp },
            new Role("Admin", "Administrator with management permissions", false) { Id = 2, CreatedAt = seedTimestamp },
            new Role("Manager", "Manager with limited management permissions", false) { Id = 3, CreatedAt = seedTimestamp },
            new Role("Cashier", "Cashier with POS and basic permissions", false) { Id = 4, CreatedAt = seedTimestamp }
        );

        // Basic permissions
        var permissions = new List<Permission>
        {
            new Permission("Users.Create", "Users", "Create", "Create new users") { Id = 1, CreatedAt = seedTimestamp },
            new Permission("Users.Read", "Users", "Read", "View users") { Id = 2, CreatedAt = seedTimestamp },
            new Permission("Users.Update", "Users", "Update", "Edit users") { Id = 3, CreatedAt = seedTimestamp },
            new Permission("Users.Delete", "Users", "Delete", "Delete users") { Id = 4, CreatedAt = seedTimestamp },
            new Permission("Products.Create", "Products", "Create", "Create products") { Id = 5, CreatedAt = seedTimestamp },
            new Permission("Products.Read", "Products", "Read", "View products") { Id = 6, CreatedAt = seedTimestamp },
            new Permission("Products.Update", "Products", "Update", "Edit products") { Id = 7, CreatedAt = seedTimestamp },
            new Permission("Products.Delete", "Products", "Delete", "Delete products") { Id = 8, CreatedAt = seedTimestamp },
            new Permission("Orders.Create", "Orders", "Create", "Create orders") { Id = 9, CreatedAt = seedTimestamp },
            new Permission("Orders.Read", "Orders", "Read", "View orders") { Id = 10, CreatedAt = seedTimestamp },
            new Permission("Orders.Update", "Orders", "Update", "Edit orders") { Id = 11, CreatedAt = seedTimestamp },
            new Permission("Reports.Read", "Reports", "Read", "View reports") { Id = 12, CreatedAt = seedTimestamp },
            new Permission("Settings.Read", "Settings", "Read", "View settings") { Id = 13, CreatedAt = seedTimestamp },
            new Permission("Settings.Update", "Settings", "Update", "Edit settings") { Id = 14, CreatedAt = seedTimestamp }
        };

        modelBuilder.Entity<Permission>().HasData(permissions);

        // Assign all permissions to SuperAdmin
        var superAdminPermissions = permissions.Select((p, index) => new RolePermission(1, p.Id, true) { Id = index + 1, CreatedAt = seedTimestamp }).ToList();
        modelBuilder.Entity<RolePermission>().HasData(superAdminPermissions);

        // Default admin user (password: "admin", PIN: "0000" - both should be changed on first login).
        // Hashes are pre-computed, static strings rather than inline BCrypt.HashPassword(...) calls -
        // BCrypt generates a new random salt (and therefore a new hash string) on every call, which
        // is exactly the same "model changes every time it's built" problem as DateTime.UtcNow above.
        // These are just BCrypt.HashPassword("admin") / BCrypt.HashPassword("0000") computed once;
        // verified they still validate via BCrypt.Verify before hardcoding them.
        modelBuilder.Entity<User>().HasData(
            new User("admin", "admin@example.com", "$2a$11$5eM6xQvX2IEVGBPa2OzAQOC.OA0dZQQqPlV2YGujceNp0TjSV9uOC", 1, "System", "Administrator")
            {
                Id = 1,
                MustChangePassword = true,
                PinHash = "$2a$11$meJiB4JGLns8fxBY9Kk.j.U8tQ7VC/uewIiQManPMynNpxmIwBak.",
                MustChangePin = true,
                CreatedAt = seedTimestamp
            }
        );

        // Seed URL Routes
        var urlRoutes = new List<UrlRoute>
        {
            new() { Id = 1, Key = "login", Pattern = "/{lang}/account/login", Controller = "Account", Action = "Login", DisplayOrder = 1, IsActive = true, RequiresAuthentication = false, Description = "Login page", CreatedAt = seedTimestamp },
            new() { Id = 2, Key = "home", Pattern = "/{lang}/home", Controller = "Home", Action = "Index", DisplayOrder = 2, IsActive = true, RequiresAuthentication = true, Description = "Home/Dashboard page", CreatedAt = seedTimestamp },
            new() { Id = 3, Key = "dashboard", Pattern = "/{lang}/dashboard", Controller = "Dashboard", Action = "Index", DisplayOrder = 3, IsActive = true, RequiresAuthentication = true, Description = "Dashboard page", CreatedAt = seedTimestamp },
            new() { Id = 4, Key = "pos", Pattern = "/{lang}/pos", Controller = "POS", Action = "Index", DisplayOrder = 4, IsActive = true, RequiresAuthentication = true, Description = "Point of Sale page", CreatedAt = seedTimestamp },
            new() { Id = 5, Key = "products", Pattern = "/{lang}/products", Controller = "Products", Action = "Index", DisplayOrder = 5, IsActive = true, RequiresAuthentication = true, Description = "Products management", CreatedAt = seedTimestamp },
            new() { Id = 6, Key = "customers", Pattern = "/{lang}/customers", Controller = "Customers", Action = "Index", DisplayOrder = 6, IsActive = true, RequiresAuthentication = true, Description = "Customers management", CreatedAt = seedTimestamp },
            new() { Id = 7, Key = "suppliers", Pattern = "/{lang}/suppliers", Controller = "Suppliers", Action = "Index", DisplayOrder = 7, IsActive = true, RequiresAuthentication = true, Description = "Suppliers management", CreatedAt = seedTimestamp },
            new() { Id = 8, Key = "categories", Pattern = "/{lang}/categories", Controller = "Categories", Action = "Index", DisplayOrder = 8, IsActive = true, RequiresAuthentication = true, Description = "Categories management", CreatedAt = seedTimestamp },
            new() { Id = 9, Key = "orders", Pattern = "/{lang}/orders", Controller = "Orders", Action = "Index", DisplayOrder = 9, IsActive = true, RequiresAuthentication = true, Description = "Orders management", CreatedAt = seedTimestamp },
            new() { Id = 10, Key = "reports", Pattern = "/{lang}/reports", Controller = "Reports", Action = "Index", DisplayOrder = 10, IsActive = true, RequiresAuthentication = true, Description = "Reports and analytics", CreatedAt = seedTimestamp },
            new() { Id = 11, Key = "users", Pattern = "/{lang}/users", Controller = "Users", Action = "Index", DisplayOrder = 11, IsActive = true, RequiresAuthentication = true, RequiredRole = "Admin", Description = "Users management", CreatedAt = seedTimestamp },
            new() { Id = 12, Key = "settings", Pattern = "/{lang}/settings", Controller = "Settings", Action = "Index", DisplayOrder = 12, IsActive = true, RequiresAuthentication = true, RequiredRole = "Admin", Description = "System settings", CreatedAt = seedTimestamp }
        };

        modelBuilder.Entity<UrlRoute>().HasData(urlRoutes);

        // =========================
        // Case-insensitive lookups (SQLite only)
        // =========================
        // SQLite compares strings byte-for-byte by default. SQL Server's default collation is
        // case-insensitive, and the app was written against that assumption (login by
        // username/email, barcode scans, config-key lookups, unique constraints on those same
        // columns all rely on it). NOCASE reproduces that behavior for exactly the columns that
        // need it - deliberately an allow-list, not every string column, since free-text fields
        // (descriptions, notes, etc.) have no such assumption baked into the app and NOCASE would
        // just change their sort order for no benefit. A unique index inherits its column's
        // collation, so this also fixes case-insensitive uniqueness (e.g. "Admin" vs "admin"
        // usernames) without any separate index-level configuration.
        ApplyCaseInsensitiveCollations(modelBuilder);
    }

    private static readonly Dictionary<Type, string[]> CaseInsensitiveProperties = new()
    {
        [typeof(User)] = [nameof(User.Username), nameof(User.Email)],
        [typeof(Product)] = [nameof(Product.Barcode), nameof(Product.Name)],
        [typeof(Receipt)] = [nameof(Receipt.ReceiptNumber), nameof(Receipt.CustomerPhone)],
        [typeof(CodeList)] = [nameof(CodeList.Key)],
        [typeof(SystemSettings)] = [nameof(Core.Entities.Configuration.SystemSettings.SettingKey)],
        [typeof(UrlRoute)] = [nameof(UrlRoute.Key), nameof(UrlRoute.Controller)],
        [typeof(AppSetting)] = [nameof(AppSetting.Name)],
        [typeof(CartSession)] = [nameof(CartSession.SessionId)],
        [typeof(Role)] = [nameof(Role.Name)],
        [typeof(Permission)] = [nameof(Permission.Category), nameof(Permission.Action), nameof(Permission.Resource)],
        [typeof(UserSession)] = [nameof(UserSession.SessionToken)],
    };

    private static void ApplyCaseInsensitiveCollations(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!CaseInsensitiveProperties.TryGetValue(entityType.ClrType, out var propertyNames))
                continue;

            foreach (var propertyName in propertyNames)
            {
                entityType.FindProperty(propertyName)?.SetCollation("NOCASE");
            }
        }
    }

    #endregion
}