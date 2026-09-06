using Application.IServices.Base;
using Application.IServices.Products;
using Application.Services.Base;
using Application.Services.Products;
using AspNetCoreRateLimit;
using Infrastructure.Data;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Web.Middlewares;
using Serilog;
using Microsoft.AspNetCore.Localization;
using System.Globalization;
using System.Linq;
using Application.IServices.Sales;
using Core.IRepositories;
using Core.IRepositories.PrinterTransport;
using Infrastructure.Repositories;
using Infrastructure.Repositories.PrinterTransport;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ✅ Serilog — replaces the default provider; console always, rolling file always,
// so unhandled-exception and diagnostic logging actually goes somewhere durable.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    // EF Core logs every SQL statement at Information — writing all of that to a rolling file
    // on the request path is a measurable cost. Keep it at Warning.
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(context.HostingEnvironment.ContentRootPath, "logs", "log-.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14));

// ✅ Add Localization *before* AddControllersWithViews
builder.Services.AddLocalization(options => 
{
    options.ResourcesPath = "Resources";
});

builder.Services
    .AddControllersWithViews()
    .AddViewLocalization(LanguageViewLocationExpanderFormat.Suffix)
    .AddDataAnnotationsLocalization(options =>
    {
        options.DataAnnotationLocalizerProvider = (type, factory) =>
            factory.Create(typeof(Web.SharedResource));
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.WriteIndented = false;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true; // Accept both camelCase and PascalCase from JSON
    });

// ✅ Configure supported cultures
var supportedCultures = new[]
{
    new CultureInfo("en"),
    new CultureInfo("ar")
};

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = supportedCultures.Select(c => c.Name).ToArray();
    options.SetDefaultCulture("en")
        .AddSupportedCultures(cultures)
        .AddSupportedUICultures(cultures);

    // LanguageMiddleware runs first and resolves the language (URL path → query → cookie →
    // session → …) into HttpContext.Items["CurrentLanguage"]. This provider hands that value
    // to RequestLocalizationMiddleware so the two agree on the SAME request — otherwise the
    // default Cookie provider wins with the pre-switch language and a language switch only
    // takes effect on the following request. Index 0 = highest priority; the default
    // QueryString/Cookie/Accept-Language providers stay as fallbacks.
    options.RequestCultureProviders.Insert(0, new Web.Middlewares.CurrentLanguageItemRequestCultureProvider());
});

// ✅ Register route constraint for languages
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteOptions>(options =>
{
    options.ConstraintMap.Add("language", typeof(Web.Constraints.LanguageRouteConstraint));
});

// ✅ Session
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// ✅ CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("ElectronApp", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// ✅ Database
builder.Services.AddDbContext<ErpDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql =>
        {
            sql.EnableRetryOnFailure(
                maxRetryCount: 2,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorNumbersToAdd: null);
            sql.CommandTimeout(10);
        }));

// Add DbContextFactory for services that need concurrent access
builder.Services.AddDbContextFactory<ErpDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql =>
        {
            sql.EnableRetryOnFailure(
                maxRetryCount: 2,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorNumbersToAdd: null);
            sql.CommandTimeout(10);
        }), ServiceLifetime.Scoped);

builder.Services.AddMemoryCache();
// Backs ISession (DistributedSessionStore). In-process only — CacheService no longer
// uses IDistributedCache, so nothing here pays a serialization cost.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpContextAccessor();

// Preloaded JS translation tables — read the locale JSON files once at startup instead
// of on every layout render.
builder.Services.AddSingleton<Web.Services.ILocaleCatalog, Web.Services.LocaleCatalog>();

// ✅ Response compression (Brotli/Gzip) for text payloads — smaller transfers and faster
// parse for the Electron renderer.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes
        .Concat(new[] { "application/json", "image/svg+xml" });
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(o => o.Level = System.IO.Compression.CompressionLevel.Fastest);

// ✅ Rate limiting — options were already configured in appsettings.json but never
// registered, so login and every other endpoint were unthrottled. Wiring it up here;
// app.UseIpRateLimiting() is added to the pipeline below, after UseRouting.
builder.Services.AddOptions();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.Configure<IpRateLimitPolicies>(builder.Configuration.GetSection("IpRateLimitPolicies"));
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// ✅ Authentication
// No fallback for Jwt:SecretKey: the old fallback value was itself committed to
// appsettings.json, so it's public. Fail fast at startup instead of silently signing/validating
// tokens with a known key. Configure via `dotnet user-secrets set "Jwt:SecretKey" "<value>"` in
// development or the Jwt__SecretKey environment variable in production/Electron packaging.
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"]
    ?? throw new InvalidOperationException(
        "Jwt:SecretKey is not configured. Set it via 'dotnet user-secrets set \"Jwt:SecretKey\" \"<value>\"' " +
        "in development, or the Jwt__SecretKey environment variable in production. It must be at least 32 characters.");
if (jwtSecretKey.Length < 32)
    throw new InvalidOperationException("Jwt:SecretKey must be at least 32 characters long.");

builder.Services.AddAuthentication("Cookies")
    .AddCookie("Cookies", options =>
    {
        options.LoginPath = "/en/Account/Login";
        options.LogoutPath = "/en/Account/Logout";
        options.AccessDeniedPath = "/en/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
    })
    .AddJwtBearer("Bearer", options =>
    {
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.ASCII.GetBytes(jwtSecretKey)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "NexPos",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "NexPos-Users",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddAuthenticationSchemes("Cookies")
        .Build();
});

// ✅ Custom repositories and services
builder.Services.AddScoped<Core.IRepositories.Products.IProductRepository, Infrastructure.Repositories.Products.ProductRepository>();
builder.Services.AddScoped<Core.IRepositories.Products.IMainScreenProductRepository, Infrastructure.Repositories.Products.MainScreenProductRepository>();
builder.Services.AddScoped<Core.ICache.ICacheService, Infrastructure.Cache.CacheService>();
builder.Services.AddScoped<ICurrencyConversionService, CurrencyConversionService>();
builder.Services.AddScoped<IProductPricingService, ProductPricingService>();
builder.Services.AddScoped<Core.IRepositories.Sales.ICurrencyRepository, Infrastructure.Repositories.Sales.CurrencyRepository>();
builder.Services.AddScoped<ICurrencyService, CurrencyService>();
builder.Services.AddScoped<Core.IRepositories.Sales.IExchangeRateRepository, Infrastructure.Repositories.Sales.ExchangeRateRepository>();
builder.Services.AddScoped<IExchangeRateService, ExchangeRateService>();
builder.Services.AddScoped<Core.IRepositories.Products.ISupplierRepository, Infrastructure.Repositories.Products.SupplierRepository>();
builder.Services.AddScoped<Application.IServices.Products.ISupplierService, Application.Services.Products.SupplierService>();
builder.Services.AddScoped<Application.IServices.Products.IProductService, Application.Services.Products.ProductService>();
builder.Services.AddScoped<Application.IServices.Products.IMainScreenProductService, Application.Services.Products.MainScreenProductService>();
builder.Services.AddScoped<Application.IServices.Products.IFastProductSearchService, Application.Services.Products.FastProductSearchService>();
builder.Services.AddScoped<Application.IServices.Sales.IPOSCheckoutService, Application.Services.Sales.POSCheckoutService>();
// Singleton: the double-click/idempotency guard must survive across the scoped requests it's protecting (S6).
builder.Services.AddSingleton<Application.IServices.Sales.ICheckoutIdempotencyGuard, Application.Services.Sales.CheckoutIdempotencyGuard>();
builder.Services.AddScoped<Core.IRepositories.Products.IProductCategoryRepository, Infrastructure.Repositories.Products.ProductCategoryRepository>();
builder.Services.AddScoped<Application.IServices.Products.IProductCategoryService, Application.Services.Products.ProductCategoryService>();
builder.Services.AddScoped<Core.IRepositories.Users.ICustomerRepository, Infrastructure.Repositories.Users.CustomerRepository>();
builder.Services.AddScoped<Application.IServices.Users.ICustomerService, Application.Services.Users.CustomerService>();
builder.Services.AddScoped<Core.IRepositories.Inventory.ICartRepository, Infrastructure.Repositories.Inventory.CartRepository>();
builder.Services.AddScoped<Core.IRepositories.Inventory.ICartItemRepository, Infrastructure.Repositories.Inventory.CartItemRepository>();
builder.Services.AddScoped<Core.IRepositories.Inventory.ICustomerCartRepository, Infrastructure.Repositories.Inventory.CustomerCartRepository>();
builder.Services.AddScoped<Core.IRepositories.Sales.IOrderRepository, Infrastructure.Repositories.Sales.OrderRepository>();
builder.Services.AddScoped<Application.IServices.Inventory.ICartService, Application.Services.Inventory.CartService>();
builder.Services.AddScoped<Core.IRepositories.Inventory.ICartSessionRepository, Infrastructure.Repositories.Inventory.CartSessionRepository>();
builder.Services.AddScoped<Core.IRepositories.Inventory.ICartSessionItemRepository, Infrastructure.Repositories.Inventory.CartSessionItemRepository>();
builder.Services.AddScoped<Application.IServices.Inventory.ICartSessionService, Application.Services.Inventory.CartSessionService>();
builder.Services.AddScoped<Application.IServices.Sales.IOrderService, Application.Services.Sales.OrderService>();
builder.Services.AddScoped<Core.IRepositories.Sales.IPaymentMethodRepository, Infrastructure.Repositories.Sales.PaymentMethodRepository>();
builder.Services.AddScoped<Core.IRepositories.Sales.ITaxRepository, Infrastructure.Repositories.Sales.TaxRepository>();
builder.Services.AddScoped<Core.IRepositories.Sales.IDiscountRepository, Infrastructure.Repositories.Sales.DiscountRepository>();

// Cash Drawer Management
builder.Services.AddScoped<Core.IRepositories.Sales.ICashDrawerSessionRepository, Infrastructure.Repositories.Sales.CashDrawerSessionRepository>();
builder.Services.AddScoped<Core.IRepositories.Sales.ICashDropRepository, Infrastructure.Repositories.Sales.CashDropRepository>();
builder.Services.AddScoped<Application.IServices.Sales.ICashDrawerService, Application.Services.Sales.CashDrawerService>();
builder.Services.AddScoped<IProductDisplayOrderRepository, ProductDisplayOrderRepository>();

// Configuration: AppSetting / CodeList stacks. Previously commented out ("namespace issues")
// which left AppSettingsController and CodeListsController unresolvable at request time; the
// namespaces match and every dependency resolves, so they are registered normally now.
builder.Services.AddScoped<Core.IRepositories.Configuration.IAppSettingRepository, Infrastructure.Repositories.Configuration.AppSettingRepository>();
builder.Services.AddScoped<Core.IRepositories.Configuration.ICodeListRepository, Infrastructure.Repositories.Configuration.CodeListRepository>();
builder.Services.AddScoped<Core.IRepositories.Configuration.ICodeListValueRepository, Infrastructure.Repositories.Configuration.CodeListValueRepository>();
builder.Services.AddScoped<Application.IServices.Configuration.IAppSettingService, Application.Services.Configuration.AppSettingService>();
builder.Services.AddScoped<Application.IServices.Configuration.ICodeListService, Application.Services.Configuration.CodeListService>();
builder.Services.AddScoped<Application.IServices.Configuration.ICodeListValueService, Application.Services.Configuration.CodeListValueService>();

builder.Services.AddScoped<Application.IServices.Sales.IPaymentMethodService, Application.Services.Sales.PaymentMethodService>();
builder.Services.AddScoped<Application.IServices.Sales.ITaxService, Application.Services.Sales.TaxService>();
builder.Services.AddScoped<Application.IServices.Sales.IDiscountService, Application.Services.Sales.DiscountService>();
builder.Services.AddScoped<Core.IRepositories.Users.IUserRepository, Infrastructure.Repositories.Users.UserRepository>();
builder.Services.AddScoped<Core.IRepositories.Users.IUserSessionRepository, Infrastructure.Repositories.Users.UserSessionRepository>();
builder.Services.AddScoped<Core.IRepositories.Users.IRoleRepository, Infrastructure.Repositories.Users.RoleRepository>();
builder.Services.AddScoped<Application.IServices.Users.IUserService, Application.Services.Users.UserService>();
builder.Services.AddScoped<Application.IServices.Auth.IAuthService, Application.Services.Auth.AuthService>();
builder.Services.AddScoped<IProductDisplayOrderService, ProductDisplayOrderService>();

// Reports
builder.Services.AddScoped<Application.IServices.Reports.IReportService, Application.Services.Reports.ReportService>();
builder.Services.AddScoped<Application.IServices.Reports.ISalesReportService, Application.Services.Reports.SalesReportService>();
builder.Services.AddScoped<Application.IServices.Reports.IExportService, Application.Services.Reports.ExportService>();

// Dashboard
builder.Services.AddScoped<Application.IServices.Dashboard.IDashboardService, Application.Services.Dashboard.DashboardService>();

// Receipts
builder.Services.AddScoped<Core.IRepositories.Sales.IReceiptRepository, Infrastructure.Repositories.Sales.ReceiptRepository>();
builder.Services.AddScoped<Application.IServices.Sales.IReceiptService, Application.Services.Sales.ReceiptService>();

// Thermal Printer
builder.Services.AddScoped<IPrinterTransport, Win32PrinterTransport>();
builder.Services.AddScoped<IEscPosBuilder, EscPosBuilder>();
builder.Services.AddScoped<Application.IServices.Sales.IThermalPrinterService, Application.Services.Sales.ThermalPrinterService>();

// Tax Management
builder.Services.AddScoped<Core.IRepositories.Products.ITaxRepository, Infrastructure.Repositories.Products.TaxRepository>();
builder.Services.AddScoped<Application.IServices.Products.ITaxService, Application.Services.Products.TaxService>();

// Settings
builder.Services.AddScoped<Core.IRepositories.Configuration.ISystemSettingsRepository, Infrastructure.Repositories.Configuration.SystemSettingsRepository>();
builder.Services.AddScoped<Application.IServices.Settings.ISystemSettingsService, Application.Services.Settings.SystemSettingsService>();
builder.Services.AddScoped<Core.IRepositories.Configuration.IStoreSettingsRepository, Infrastructure.Repositories.Configuration.StoreSettingsRepository>();
builder.Services.AddScoped<Application.IServices.Settings.IStoreSettingsService, Application.Services.Settings.StoreSettingsService>();

// Notifications
builder.Services.AddScoped<Core.IRepositories.Notifications.INotificationRepository, Infrastructure.Repositories.Notifications.NotificationRepository>();
builder.Services.AddScoped<Core.IRepositories.Notifications.IAlertRepository, Infrastructure.Repositories.Notifications.AlertRepository>();
builder.Services.AddScoped<Application.IServices.Notifications.INotificationService, Application.Services.Notifications.NotificationService>();
builder.Services.AddScoped<Application.IServices.Notifications.IStockAlertService, Application.Services.Notifications.StockAlertService>();

// Backup
builder.Services.AddHostedService<Application.Services.Backup.BackupSchedulingService>();
builder.Services.AddScoped<Core.IRepositories.Backup.IBackupRepository, Infrastructure.Repositories.Backup.BackupRepository>();
builder.Services.AddScoped<Core.IRepositories.Backup.IBackupScheduleRepository, Infrastructure.Repositories.Backup.BackupScheduleRepository>();
builder.Services.AddScoped<Application.IServices.Backup.IBackupService, Application.Services.Backup.BackupService>();
builder.Services.AddScoped<Application.IServices.Notifications.IAlertService, Application.Services.Notifications.AlertService>();

// Audit Logging
builder.Services.AddScoped<Core.IRepositories.Logging.IAuditLogRepository, Infrastructure.Repositories.Logging.AuditLogRepository>();
builder.Services.AddScoped<Application.IServices.Logging.IAuditLogService, Application.Services.Logging.AuditLogService>();

// Performance & Optimization
builder.Services.AddScoped<Application.IServices.Performance.IQueryOptimizationService, Application.Services.Performance.QueryOptimizationService>();
builder.Services.AddScoped<Application.IServices.Auth.IJwtService, Application.Services.Auth.JwtService>();
// View Rendering Service (for rendering views to HTML strings)
builder.Services.AddScoped<Web.Services.IViewRenderService, Web.Services.ViewRenderService>();

// Build the app
var app = builder.Build();

// ✅ Global exception handler — must be the very first middleware so it wraps everything
// below it. AJAX/JSON requests get a clean {success,message} response instead of a raw
// 500 with the exception message; normal page requests are rethrown to the dev/prod
// exception handling configured further down.
app.UseMiddleware<Web.Middlewares.ExceptionHandlingMiddleware>();

// ✅ Compress text responses before anything writes to the body.
app.UseResponseCompression();

// ✅ Add Session (must be before LanguageMiddleware since it uses session)
app.UseSession();

// ✅ Custom middleware (MUST be before routing and localization)
// This runs for EVERY request and sets culture BEFORE any view rendering
app.UseLanguageMiddleware();    

// ✅ Localization middleware — must be after our custom middleware
app.UseRequestLocalization();

// ✅ CORS before authentication
app.UseCors("ElectronApp");

// ✅ Static files
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? string.Empty;
        var isVersioned = ctx.Context.Request.Query.ContainsKey("v");
        var isBucketed = path.StartsWith("/lib/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/Metronics/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase);

        // Fingerprinted assets (asp-append-version adds ?v=hash) never change for a given
        // hash → cache hard. Other bucketed static files get a modest TTL with revalidation.
        var headers = ctx.Context.Response.Headers;
        if (isVersioned)
        {
            headers.CacheControl = "public,max-age=31536000,immutable";
        }
        else if (isBucketed)
        {
            headers.CacheControl = "public,max-age=86400";
        }
        else
        {
            headers.CacheControl = "public,max-age=3600";
        }
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseRouting();

// ✅ Rate limiting (needs endpoint info from routing, must be before auth so it also
// throttles unauthenticated requests like login)
app.UseIpRateLimiting();

// ✅ Auth (must be after UseRouting)
app.UseAuthentication();
app.UseAuthorization();

// ✅ Routes
app.MapControllerRoute(
    name: "localized",
    pattern: "{lang:language}/{controller=Home}/{action=Index}/{id?}",
    defaults: new { lang = "en" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}",
    defaults: new { lang = "en" });

app.Run();
