using Application.IServices.Base;
using Web.Api.Online.Commands;
using Core.Auditing;
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
using Web.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ✅ Serilog — replaces the default provider; console always, rolling file always,
// so unhandled-exception and diagnostic logging actually goes somewhere durable.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    // Warns "Cache-Control/Pragma headers have been overridden" on every page that renders a form
    // (hundreds per session); that is the antiforgery token doing its job, not a problem.
    .MinimumLevel.Override("Microsoft.AspNetCore.Antiforgery.DefaultAntiforgery", Serilog.Events.LogEventLevel.Error)
    // EF Core logs every SQL statement at Information — writing all of that to a rolling file
    // on the request path is a measurable cost. Keep it at Warning.
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        // The Electron host points this at app-data, since the install dir may be read-only.
        Path.Combine(
            Environment.GetEnvironmentVariable("NEXPOS_LOG_DIR") is { Length: > 0 } logDir
                ? logDir
                : Path.Combine(context.HostingEnvironment.ContentRootPath, "logs"),
            "log-.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14)
    // Every warning/error also goes into the audit trail, tied to the user and request that caused it.
    .WriteTo.Sink(new Web.Services.AuditSerilogSink(services)));

// ✅ Add Localization *before* AddControllersWithViews
builder.Services.AddLocalization(options => 
{
    options.ResourcesPath = "Resources";
});

builder.Services
    .AddControllersWithViews(options =>
    {
        // A migrated screen's old Razor URL opens the React page instead (Web/Helpers/SpaRedirectFilter.cs; removed at the cutover).
        options.Filters.Add<Web.Helpers.SpaRedirectFilter>();
    })
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
        // Stored timestamps are UTC but come back Unspecified; write them with a "Z" so the browser shows local time.
        options.JsonSerializerOptions.Converters.Add(new Core.Time.UtcDateTimeJsonConverter());
        options.JsonSerializerOptions.Converters.Add(new Core.Time.UtcNullableDateTimeJsonConverter());
    })
    // REST conventions of the SPA's api/v1 (problem JSON with codes, filters, naming) - see Web/Api/README.md.
    .AddNexPosApi();

// OpenAPI for api/v1 only; `npm run api:types` turns it into the UI's generated types.
builder.Services.AddNexPosSwagger();

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

// ✅ Database — SQLite file in the per-user app-data folder, so a packaged (Electron)
// install never needs a separately-installed database server.
var sqliteConnectionString = AppDataPathProvider.GetConnectionString();
builder.Services.AddDbContext<ErpDbContext>(options =>
    options.UseSqlite(sqliteConnectionString));

// Add DbContextFactory for services that need concurrent access
builder.Services.AddDbContextFactory<ErpDbContext>(options =>
    options.UseSqlite(sqliteConnectionString), ServiceLifetime.Scoped);

// ✅ Online mirror (optional paid service). Off unless the owner connects it (Settings > Online, which writes
// online.json beside the database): until then the hosted service leaves the database untouched and makes no request.
// See docs/online/PLATFORM.md.
builder.Services.AddOnlineSync(builder.Configuration, AppDataPathProvider.GetOnlineSettingsFilePath());
builder.Services.AddOnlineCommandHandlers(); // changes the owner makes from the phone, applied through the same services as the till's screens

builder.Services.AddMemoryCache();
// Backs ISession (DistributedSessionStore). In-process only — CacheService no longer
// uses IDistributedCache, so nothing here pays a serialization cost.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpContextAccessor();

// Preloaded JS translation tables — read the locale JSON files once at startup instead
// of on every layout render.
builder.Services.AddSingleton<Web.Services.ILocaleCatalog, Web.Services.LocaleCatalog>();
builder.Services.AddSingleton<Web.Services.IBrandingService, Web.Services.BrandingService>();

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

        // /api/* answers 401 / 403 JSON instead of redirecting to the login or access-denied page (a fetch cannot use a redirect to HTML).
        options.Events.OnRedirectToLogin = Web.Api.Infrastructure.ApiAuthorizationFailure.CookieChallenge(
            options.Events.OnRedirectToLogin, StatusCodes.Status401Unauthorized, Web.Api.Infrastructure.ApiCodes.Unauthenticated);
        options.Events.OnRedirectToAccessDenied = Web.Api.Infrastructure.ApiAuthorizationFailure.CookieChallenge(
            options.Events.OnRedirectToAccessDenied, StatusCodes.Status403Forbidden, Web.Api.Infrastructure.ApiCodes.Forbidden);

        // The cookie carries a snapshot of the user's role and permissions from sign-in. Re-read it each
        // request (from a short-lived cache) so editing a role, moving a user to another role, or disabling
        // a user takes effect for people who are already signed in rather than at their next sign-in.
        options.Events.OnValidatePrincipal = async context =>
        {
            if (context.Principal?.Identity is not System.Security.Claims.ClaimsIdentity identity ||
                !int.TryParse(identity.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId))
            {
                return;
            }

            var access = await context.HttpContext.RequestServices
                .GetRequiredService<Web.Services.IRoleAccessService>()
                .GetUserAccessAsync(userId);

            if (access is null)
            {
                context.RejectPrincipal();
                await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignOutAsync(context.HttpContext, "Cookies");
                return;
            }

            foreach (var claim in identity.FindAll(c =>
                         c.Type is "Permission" or "Role" or "RoleId" or System.Security.Claims.ClaimTypes.Role).ToList())
            {
                identity.RemoveClaim(claim);
            }

            identity.AddClaim(new System.Security.Claims.Claim("Role", access.RoleName));
            identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, access.RoleName));
            identity.AddClaim(new System.Security.Claims.Claim("RoleId", access.RoleId.ToString()));
            identity.AddClaims(access.Permissions.Select(p => new System.Security.Claims.Claim("Permission", p)));
        };
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
builder.Services.AddScoped<Application.IServices.Products.IProductPriceService, Application.Services.Products.ProductPriceService>();
builder.Services.AddScoped<Core.IRepositories.Sales.ICurrencyRepository, Infrastructure.Repositories.Sales.CurrencyRepository>();
builder.Services.AddScoped<ICurrencyService, CurrencyService>();
builder.Services.AddScoped<Core.IRepositories.Sales.IExchangeRateRepository, Infrastructure.Repositories.Sales.ExchangeRateRepository>();
builder.Services.AddScoped<IExchangeRateService, ExchangeRateService>();
builder.Services.AddScoped<Core.IRepositories.Products.ISupplierRepository, Infrastructure.Repositories.Products.SupplierRepository>();
builder.Services.AddScoped<Application.IServices.Products.ISupplierService, Application.Services.Products.SupplierService>();
builder.Services.AddScoped<Core.IRepositories.Products.ISupplierDebtTransactionRepository, Infrastructure.Repositories.Products.SupplierDebtTransactionRepository>();
builder.Services.AddScoped<Application.IServices.Products.ISupplierDebtService, Application.Services.Products.SupplierDebtService>();
builder.Services.AddScoped<Core.IRepositories.Products.IProductHistoryRepository, Infrastructure.Repositories.Products.ProductHistoryRepository>();
builder.Services.AddScoped<Application.IServices.Products.IProductHistoryService, Application.Services.Products.ProductHistoryService>();
builder.Services.AddScoped<Application.IServices.Products.IProductService, Application.Services.Products.ProductService>();
builder.Services.AddScoped<Application.IServices.Products.IProductImportExportService, Application.Services.Products.ProductImportExportService>();
builder.Services.AddScoped<Application.IServices.Products.IMainScreenProductService, Application.Services.Products.MainScreenProductService>();
builder.Services.AddScoped<Application.IServices.Products.IFastProductSearchService, Application.Services.Products.FastProductSearchService>();
builder.Services.AddScoped<Application.IServices.Sales.IPOSCheckoutService, Application.Services.Sales.POSCheckoutService>();
// Singleton: the double-click/idempotency guard must survive across the scoped requests it's protecting (S6).
builder.Services.AddSingleton<Application.IServices.Sales.ICheckoutIdempotencyGuard, Application.Services.Sales.CheckoutIdempotencyGuard>();
builder.Services.AddScoped<Core.IRepositories.Products.IProductCategoryRepository, Infrastructure.Repositories.Products.ProductCategoryRepository>();
builder.Services.AddScoped<Application.IServices.Products.IProductCategoryService, Application.Services.Products.ProductCategoryService>();
builder.Services.AddScoped<Core.IRepositories.Users.ICustomerRepository, Infrastructure.Repositories.Users.CustomerRepository>();
builder.Services.AddScoped<Application.IServices.Users.ICustomerService, Application.Services.Users.CustomerService>();
builder.Services.AddScoped<Core.IRepositories.Users.ICustomerDebtTransactionRepository, Infrastructure.Repositories.Users.CustomerDebtTransactionRepository>();
builder.Services.AddScoped<Application.IServices.Users.ICustomerDebtService, Application.Services.Users.CustomerDebtService>();
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
builder.Services.AddSingleton<Web.Services.AccessCache>();
builder.Services.AddScoped<Web.Services.IRoleAccessService, Web.Services.RoleAccessService>();
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
builder.Services.AddScoped<Application.IServices.Sales.ISaleReversalService, Application.Services.Sales.SaleReversalService>();
builder.Services.AddScoped<Application.IServices.Purchasing.IPurchaseService, Application.Services.Purchasing.PurchaseService>();
builder.Services.AddScoped<Application.IServices.Sales.ISaleCustomerService, Application.Services.Sales.SaleCustomerService>();

// Thermal Printer
// NEXPOS_DEMO=1 swaps in a no-op printer so demos/recordings never print on real hardware.
if (Environment.GetEnvironmentVariable("NEXPOS_DEMO") == "1")
    builder.Services.AddScoped<IPrinterTransport, DemoPrinterTransport>();
else
    builder.Services.AddScoped<IPrinterTransport, Win32PrinterTransport>();
builder.Services.AddScoped<IEscPosBuilder, EscPosBuilder>();
builder.Services.AddSingleton<ILabelPdfBuilder, LabelPdfBuilder>();
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
// The trail lives in its own SQLite file (nexpos-audit.db): audit writes must never contend with point-of-sale
// transactions for SQLite's single write lock, and restoring a business backup must not be able to erase the history.
var auditConnectionString = AppDataPathProvider.GetAuditConnectionString();
builder.Services.Configure<Core.Auditing.AuditOptions>(builder.Configuration.GetSection(Core.Auditing.AuditOptions.SectionName));
builder.Services.AddSingleton<Core.Auditing.IAuditContext, Web.Services.HttpAuditContext>();
// The writer, the EF interceptors that capture every data change, and the viewer's read-side context.
builder.Services.AddAuditTrail(auditConnectionString, AppDataPathProvider.GetAuditFallbackDirectory());
builder.Services.AddHostedService<Application.Services.Logging.AuditWriterHostedService>();
builder.Services.AddScoped<Core.IRepositories.Logging.IAuditLogRepository, Infrastructure.Repositories.Logging.AuditLogRepository>();
builder.Services.AddScoped<Application.IServices.Logging.IAuditLogService, Application.Services.Logging.AuditLogService>();

// Performance & Optimization
builder.Services.AddScoped<Application.IServices.Performance.IQueryOptimizationService, Application.Services.Performance.QueryOptimizationService>();
builder.Services.AddScoped<Application.IServices.Auth.IJwtService, Application.Services.Auth.JwtService>();
// View Rendering Service (for rendering views to HTML strings)
builder.Services.AddScoped<Web.Services.IViewRenderService, Web.Services.ViewRenderService>();

// Licensing — offline, signed, machine-bound key (WORKLOG L1). Warns a week before a time-limited
// license expires, a week after, then stops the POS and new sales; data is never touched.
// Debug builds skip enforcement so development never needs a key; set NEXPOS_LICENSE_ENFORCE=1 to test it there.
var enforceLicense = true;
#if DEBUG
enforceLicense = Environment.GetEnvironmentVariable("NEXPOS_LICENSE_ENFORCE") == "1";
#endif
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new Application.Services.Licensing.LicenseOptions
{
    PublicKeySpkiBase64 = Common.Licensing.LicensePublicKey.SpkiBase64,
    BuildDate = DateOnly.ParseExact(Web.BuildInfo.BuildDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
    EnforcementEnabled = enforceLicense
});
builder.Services.AddSingleton<Application.Services.Licensing.ILicenseStorage>(
    _ => new Application.Services.Licensing.FileLicenseStorage(AppDataPathProvider.GetLicenseFilePath()));
builder.Services.AddSingleton<Common.Licensing.IMachineIdProvider, Infrastructure.Licensing.WindowsMachineIdProvider>();
builder.Services.AddSingleton<Application.IServices.Licensing.ILicenseService, Application.Services.Licensing.LicenseService>();

// Build the app
var app = builder.Build();

// `Web.exe --dump-api-contract <folder>` (client/scripts/api-types.mjs): write swagger.json + permissions.ts and exit,
// before the database, the audit writer or a port is touched.
if (Web.Api.Infrastructure.ApiContractDump.TryGetTarget(args, out var contractFolder))
{
    await Web.Api.Infrastructure.ApiContractDump.WriteAsync(app.Services, contractFolder);
    return;
}

var auditContext = app.Services.GetRequiredService<Core.Auditing.IAuditContext>();
var auditTrail = app.Services.GetRequiredService<Core.Auditing.IAuditTrail>();

// ✅ Create the audit database before anything can query it (the hosted writer repeats this idempotently).
// A problem here must not stop the shop from opening: entries queue and spill to the fallback folder.
try
{
    Infrastructure.Auditing.AuditDatabase.EnsureCreated(auditConnectionString);
}
catch (Exception ex)
{
    Log.Error(ex, "The audit database could not be prepared; audit entries will be parked in the fallback folder");
}

// ✅ Apply pending EF migrations on startup — packaged (Electron) client installs have
// no way to run `dotnet ef database update` themselves, so the SQLite schema must be
// brought up to date automatically the moment the app starts.
// The migration's own statements (schema changes, seed rows) are not events anyone investigates, so per-row
// auditing is off inside the scope; one summary entry says which migrations ran.
using (var migrationScope = app.Services.CreateScope())
using (auditContext.BeginSystemScope("Startup"))
using (auditContext.SuppressDataChanges("schema migration"))
{
    var dbContext = migrationScope.ServiceProvider.GetRequiredService<ErpDbContext>();
    try
    {
        // A power cut or crash in the middle of an earlier upgrade leaves a half-upgraded database. Put the copy made before it back
        // first, so the upgrade below starts from a clean state.
        var interruptedUpgrade = Infrastructure.Data.PreUpgradeGuard.RecoverInterruptedUpgrade(AppDataPathProvider.GetDatabaseFilePath());
        if (interruptedUpgrade is not null)
            Log.Warning("A previous database upgrade was interrupted (power cut or crash). The database was put back from {Backup} and the upgrade will run again", interruptedUpgrade);

        var pendingMigrations = dbContext.Database.GetPendingMigrations().ToList();

        // Data safety first (docs/online/STORE-SERVER.md, step 1): copy the database and prove the copy is sound BEFORE changing
        // anything, check the result AFTER, and put the copy back if either the upgrade or the check fails. If the copy cannot be
        // made (disk full ...) the upgrade does not run at all.
        var guard = Infrastructure.Data.PreUpgradeGuard.Begin(AppDataPathProvider.GetDatabaseFilePath(), pendingMigrations);
        try
        {
            dbContext.Database.Migrate();
            guard?.VerifyAfterUpgrade();
        }
        catch (Exception upgradeFailure) when (guard is not null)
        {
            Log.Fatal(upgradeFailure, "The database upgrade failed or its result did not check out; restoring the copy made just before it ({Backup})", guard.BackupPath);
            dbContext.Database.CloseConnection();
            guard.Restore();
            throw new InvalidOperationException(
                $"The database upgrade did not succeed and your data was put back exactly as it was ({guard.BackupPath}). " +
                $"Reason: {upgradeFailure.Message}", upgradeFailure);
        }
        guard?.Commit();

        // Permission rows for everything the code can check, plus the one-time default grants for built-in roles.
        await migrationScope.ServiceProvider.GetRequiredService<Web.Services.IRoleAccessService>().EnsureCatalogAsync();

        if (pendingMigrations.Count > 0)
        {
            auditTrail.LogSystem(AuditActions.MigrationsApplied,
                $"Applied {pendingMigrations.Count} database migration(s): {string.Join(", ", pendingMigrations)}",
                "Database", details: new { migrations = pendingMigrations, safetyCopy = guard?.BackupPath });
        }
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Failed to apply database migrations on startup");
        throw;
    }
}

// Read the license once at startup: surfaces a bad/missing key in the log right away and warms the
// machine-identity lookup (WMI) so the first page does not pay for it.
try
{
    var licenseState = app.Services.GetRequiredService<Application.IServices.Licensing.ILicenseService>().GetState();
    Log.Information("License: {Status} (customer {Customer}, valid until {ValidUntil}, machine {MachineId}, enforced {Enforced})",
        licenseState.Status, licenseState.Customer ?? "-", licenseState.ValidUntil?.ToString("yyyy-MM-dd") ?? "-",
        licenseState.MachineId, !licenseState.EnforcementDisabled);
}
catch (Exception ex)
{
    Log.Warning(ex, "Could not read the license state at startup");
}

// Optional sample data for demos/screenshots: `Web.exe --seed-demo` (or NEXPOS_SEED_DEMO=1).
// Idempotent - only fills a database that doesn't already contain the demo catalogue.
if (args.Contains("--seed-demo") || Environment.GetEnvironmentVariable("NEXPOS_SEED_DEMO") == "1")
{
    // Hundreds of generated rows are not events anyone investigates: one summary entry instead of a row each.
    using var seedScope = app.Services.CreateScope();
    using var seedActor = auditContext.BeginSystemScope("DemoSeed");
    bool seeded;
    using (auditContext.SuppressDataChanges("demo data seeding"))
    {
        seeded = await DemoDataSeeder.SeedAsync(seedScope.ServiceProvider.GetRequiredService<ErpDbContext>());
    }
    Log.Information(seeded ? "Demo data seeded." : "Demo data already present - nothing to do.");
    if (seeded)
    {
        auditTrail.LogSystem(AuditActions.SeedData, "Demo catalogue seeded", "Database");
    }
}

// Optional bilingual (English + Arabic) test catalogue: `Web.exe --seed-test` (or NEXPOS_SEED_TEST=1).
// Products by piece, volume and mass; idempotent per product.
if (args.Contains("--seed-test") || Environment.GetEnvironmentVariable("NEXPOS_SEED_TEST") == "1")
{
    using var testSeedScope = app.Services.CreateScope();
    using var testSeedActor = auditContext.BeginSystemScope("TestSeed");
    int testAdded;
    using (auditContext.SuppressDataChanges("test content seeding"))
    {
        testAdded = await TestContentSeeder.SeedAsync(testSeedScope.ServiceProvider.GetRequiredService<ErpDbContext>());
    }
    Log.Information("Test content seeded: {Count} product(s) added.", testAdded);
    if (testAdded > 0)
    {
        auditTrail.LogSystem(AuditActions.SeedData,
            $"Test catalogue seeded: {testAdded} product(s) added", "Database");
    }
}

// ✅ Global exception handler — must be the very first middleware so it wraps everything
// below it. AJAX/JSON requests get a clean {success,message} response instead of a raw
// 500 with the exception message; normal page requests are rethrown to the dev/prod
// exception handling configured further down.
app.UseMiddleware<Web.Middlewares.ExceptionHandlingMiddleware>();

// ✅ api/v1 requests that match nothing (404 / 405) answer problem JSON, not an empty body.
app.UseNexPosApiStatusPages();

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
        if (path.StartsWith("/app/assets/", StringComparison.OrdinalIgnoreCase))
        {
            // React SPA bundle (Vite): file names carry a content hash, so they never change.
            headers.CacheControl = "public,max-age=31536000,immutable";
        }
        else if (path.StartsWith("/app/", StringComparison.OrdinalIgnoreCase))
        {
            // index.html (and anything else unhashed under /app) names the current hashed assets,
            // so it must be revalidated or an update would keep loading the old bundle.
            headers.CacheControl = "no-cache";
        }
        else if (isVersioned)
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

// ✅ Audit trail: one row per request (who, when, where from, endpoint, status, duration). After the static files
// (assets are not events) and deliberately before the license / auth / error handling below, so refused, redirected
// and crashed requests are recorded as faithfully as successful ones.
app.UseMiddleware<Web.Middlewares.AuditRequestMiddleware>();

// ✅ CSRF gate of the versioned API: every non-GET /api/v1 call must carry `X-NexPOS-Client: web`. After the audit row so refusals are recorded.
app.UseMiddleware<Web.Api.Infrastructure.ApiClientHeaderMiddleware>();

// ✅ License: close the POS screen and refuse new sales when the license does not allow selling.
// After static files (assets are never blocked) and before routing/auth; reads and everything else pass.
app.UseMiddleware<Web.Middlewares.LicenseEnforcementMiddleware>();

// The page-oriented error handlers (HTML dev page / re-execute /Home/Error) are for pages only. They would swallow an
// api/v1 exception before ExceptionHandlingMiddleware could answer it as problem JSON with a code.
app.UseWhen(context => !Web.Api.Infrastructure.ApiPaths.IsV1(context.Request), pages =>
{
    if (app.Environment.IsDevelopment())
    {
        pages.UseDeveloperExceptionPage();
    }
    else
    {
        pages.UseExceptionHandler("/Home/Error");
        pages.UseHsts();
    }
});

app.UseRouting();

// ✅ Rate limiting (needs endpoint info from routing, must be before auth so it also
// throttles unauthenticated requests like login)
app.UseIpRateLimiting();

// ✅ swagger.json + UI (Development only)
app.UseNexPosSwagger();

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

// ✅ React SPA (client/, built into wwwroot/app): any client-side route under /app gets index.html so a reload or a
// deep link works. "nonfile" leaves paths with an extension (a missing /app/assets/x.js) to 404 instead of returning
// HTML, and /api, /swagger and the MVC routes are outside /app so they are never swallowed. Lowest priority, so it
// only applies when no controller route matched.
app.MapFallbackToFile("/app/{*path:nonfile}", "app/index.html");

// Visual Studio "Electron" launch profile: open the desktop shell against this (debuggable) run.
if (app.Environment.IsDevelopment() && Environment.GetEnvironmentVariable("ELECTRON_MODE") == "true")
{
    app.Lifetime.ApplicationStarted.Register(() => LaunchElectron(app));
}

// ✅ Application lifecycle in the trail. "Started" with no matching "Stopping" before it means the previous run ended
// abruptly (crash, power cut, or the desktop shell killing the process) - useful to know when chasing odd data.
app.Lifetime.ApplicationStarted.Register(() =>
{
    try
    {
        var license = app.Services.GetRequiredService<Application.IServices.Licensing.ILicenseService>().GetState();
        using var _ = auditContext.BeginSystemScope("Startup");
        auditTrail.LogSystem(AuditActions.AppStarted,
            $"Application started (build {Web.BuildInfo.BuildDate}, {app.Environment.EnvironmentName})",
            "Application",
            details: new
            {
                build = Web.BuildInfo.BuildDate,
                environment = app.Environment.EnvironmentName,
                machine = Environment.MachineName,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                processId = Environment.ProcessId,
                databaseFile = Path.GetFileName(AppDataPathProvider.GetDatabaseFilePath()),
                auditDatabaseFile = Path.GetFileName(AppDataPathProvider.GetAuditDatabaseFilePath()),
                license = new { status = license.Status.ToString(), customer = license.Customer, validUntil = license.ValidUntil?.ToString("yyyy-MM-dd") }
            });
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Could not record the application start in the audit trail");
    }
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    using var _ = auditContext.BeginSystemScope("Shutdown");
    auditTrail.LogSystem(AuditActions.AppStopping, "Application stopping (clean shutdown)", "Application");
});

app.Run();

static void LaunchElectron(WebApplication app)
{
    var repoRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, ".."));
    var electronExe = Path.Combine(repoRoot, "node_modules", "electron", "dist", "electron.exe");
    if (!File.Exists(electronExe))
    {
        app.Logger.LogWarning("Electron not found at {Path}. Run 'npm install' in {Root}.", electronExe, repoRoot);
        return;
    }

    var startInfo = new System.Diagnostics.ProcessStartInfo(electronExe, ".")
    {
        WorkingDirectory = repoRoot,
        UseShellExecute = false
    };
    // Tells electron-main.js to use this server instead of starting its own Web.exe,
    // and which process to watch so the window closes when debugging stops.
    startInfo.Environment["NEXPOS_EXTERNAL_SERVER"] = Environment.ProcessId.ToString();
    // Inherited from VS Code and some tools; it would make electron.exe run as plain Node.
    startInfo.Environment.Remove("ELECTRON_RUN_AS_NODE");

    var electron = System.Diagnostics.Process.Start(startInfo);
    if (electron == null) return;
    electron.EnableRaisingEvents = true;
    // Closing the Electron window ends the debug session.
    electron.Exited += (_, _) => app.Lifetime.StopApplication();
}

// Lets the API integration tests host the app in-process (WebApplicationFactory<Program>).
public partial class Program;
