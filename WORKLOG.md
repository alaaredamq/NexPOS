# ERP / POS — Work Log

Single source of truth for ongoing work across sessions. Supersedes the old scattered
status files (`ALL_FIXES_COMPLETE.md`, `APPLY_THREE_FIXES.md`, etc. — deleted).

**How to use:** at the start of a session, read "Open Questions" and "Backlog".
At the end of a session, add a dated entry to "Session Log" and tick off backlog items.
Keep this file as the *only* status doc — don't create new ones.

---

## Project Snapshot

| | |
|---|---|
| Stack | .NET 9, ASP.NET MVC + Razor, EF Core 9, SQL Server, Electron wrapper |
| Architecture | Clean-ish: `Core` (entities/interfaces) → `Application` (services/DTOs) → `Infrastructure` (EF/repos) → `Web` (MVC) |
| Front-end | Razor + jQuery + Bootstrap + DataTables + Select2 + SweetAlert2. **No build step.** |
| Localization | English + Arabic (RTL) via `.resx`, ~60 view-level resource pairs |
| Deployment | Electron spawns `Web.exe`, serves on `http://localhost:5000` |
| Tests | `Tests/` (xUnit + Moq + SQLite in-memory) — 8 tests covering S2 repricing, S5 stock deduction, cash-drawer reconciliation. See A9. |
| Scale assumption | Single store. **Tills per store: UNCONFIRMED — see Open Questions** |

---

## Open Questions (blockers)

| # | Question | Blocks | Status |
|---|---|---|---|
| Q1 | Do any clients run **more than one till against a shared database**? | Database choice (SQLite vs PostgreSQL) | **Answered: maybe — some clients may run multiple PCs against one shared DB.** Treat as "yes" for design purposes. Rules out plain SQLite (no real concurrent-writer story over a network share); F1 should target PostgreSQL, not SQLite, if pursued. Also means S6's checkout idempotency guard (in-process `ConcurrentDictionary`) is **not sufficient** for multi-till deployments and needs to move to the DB before this scenario is supported for real. |
| Q2 | PIN-only auth for all roles, or PIN for cashiers + password for SuperAdmin/settings/reports? | PIN auth design | **Answered: PIN for all roles**, including SuperAdmin/settings/reports. F2 should build one PIN-based login path, no separate password flow to maintain. |
| Q3 | Existing client installs with live data to migrate, or all deployments greenfield? | Migration strategy | **Answered: all test data, no live data to migrate.** No migration strategy needed for any schema/provider change (incl. F1's SQL Server → other-provider move) — safe to make breaking schema changes without a data-migration plan. |

---

## Backlog

Status key: `TODO` · `WIP` · `DONE` · `BLOCKED` · `WONTFIX`

### P0 — Security / data integrity (exploitable or corrupting today)

| ID | Item | Where | Status |
|---|---|---|---|
| S1 | ~~Passwords compared in plaintext.~~ `ValidatePasswordAsync` now calls `BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)`. Verified the only account-creation path (`UserService.CreateAsync`, used by `UsersController`) already hashed with BCrypt, so no plaintext rows existed to migrate — the seeded admin (already BCrypt-hashed) can now log in. | `Application/Services/Users/UserService.cs:69-73` | DONE |
| S2 | ~~Client sets the price.~~ Root cause was `CartService.AddItemToCartAsync` persisting whatever `unitPrice` the caller passed. It now ignores that parameter and always reprices from `Product.SellingPrice` (a private `AddItemToCartWithTrustedPriceAsync` keeps `AddItemToCartWithCurrencyAsync`'s currency-converted price working). Fixes all three call sites that fed it client input: `POSCheckoutService`'s "Items" checkout path, `POSController.HoldCart`, and `CartsController.AddItem`. `POSCheckoutService` also now bounds `request.TotalAmount` to within the greater of $1 or 2% of the server-computed total and throws otherwise — the "editable total" is a rounding tolerance, not a client-set price. **Found and fixed in consolidated review:** `AddItemToCartWithCurrencyAsync`'s claim of "keeps the currency-converted price working" was wrong as originally written — it called the *public* `AddItemToCartAsync(cartId, productId, quantity, productPrice)`, which (correctly, per S2) ignores its `unitPrice` argument and re-derives from `Product.SellingPrice` itself, silently discarding the currency conversion. Confirmed via `git diff` that this method was untouched by the S2 change, so it's a real regression, not pre-existing — and confirmed it's a live call site (`CartsController`'s currency-aware add-to-cart action). Fixed by routing it through `AddItemToCartWithTrustedPriceAsync` directly instead. **Not done:** the adjustment audit trail is still the pre-existing prose-in-`DiscountCode` hack (A7); making it a real reportable column is separate scope. Also see A12 — an adjacent, pre-existing (not caused by S2) dead-code bug found during the same review pass. | `Application/Services/Inventory/CartService.cs`, `Application/Services/Sales/POSCheckoutService.cs` | DONE |
| S3 | ~~Unauthenticated cashier fallback~~ — `GetCurrentUserId()` now throws `UnauthorizedAccessException` instead of defaulting to user 1 when the claim is missing/unparseable (controller is already `[Authorize]`-protected, so this should never trigger in practice; failing loudly beats silent misattribution). | `Web/Controllers/POSController.cs:239-243` | DONE |
| S4 | ~~No database transactions anywhere.~~ `POSCheckoutService.ProcessCheckoutAsync` now wraps everything in `_dbContext.Database.BeginTransactionAsync()`, injecting `ErpDbContext` directly (confirmed `CartService`/`OrderService`/`ReceiptService`/`CashDrawerService`/`ProductRepository` all resolve the same scoped instance via DI — only `CacheService` uses `IDbContextFactory` for a separate context, and it's not in this call path). Commits right after the order/receipt/stock-deduction work, before the unrelated printer I/O; an unhandled exception anywhere in between rolls everything back via the transaction's dispose. Receipt-creation failure is still deliberately swallowed and doesn't roll back the sale (pre-existing behavior, kept intentionally — a POS shouldn't undo a completed, paid sale over a receipt hiccup). | `Application/Services/Sales/POSCheckoutService.cs` | DONE |
| S5 | ~~Stock can go negative / races.~~ Replaced the log-only check with `IProductRepository.TryDeductStockAsync`, a single atomic `UPDATE ... WHERE StockQuantity >= quantity` (via EF Core `ExecuteUpdateAsync`) — closes the race without needing a concurrency-token column/migration. `OrderService` now throws `InvalidOperationException` and aborts the order when it returns false. Now that S4 wraps checkout in a transaction, a multi-item cart that fails partway through is fully rolled back instead of partially deducting stock. | `Application/Services/Sales/OrderService.cs`, `Infrastructure/Repositories/Products/ProductRepository.cs` | DONE |
| S6 | ~~No checkout idempotency.~~ Added `ICheckoutIdempotencyGuard` (new `CheckoutIdempotencyGuard`, singleton — must outlive the scoped request it's protecting), an in-process atomic claim (`ConcurrentDictionary.AddOrUpdate`) keyed on `CartId` when one exists, or a content signature (`UserId`+`CustomerId`+`PaymentMethodId`+`TotalAmount`+sorted items) for the ad-hoc "Items" checkout path that has no cart to key off. A duplicate submission within a 30s window is rejected outright; the claim is released on failure so a genuine retry (e.g. after a declined payment) isn't blocked for the rest of the window, but stays claimed after success. **Caveat:** this is in-process/single-instance by design, matching the stated single-store/single-process deployment (Q1 unresolved) — it would need to move to the database (or a shared cache) if multiple app instances ever share one DB. | `Application/Services/Sales/POSCheckoutService.cs`, `Application/Services/Sales/CheckoutIdempotencyGuard.cs` | DONE |
| S7 | ~~JWT secret committed in appsettings.json + hardcoded fallback key in code.~~ Both `Program.cs`'s `AddJwtBearer` and `JwtService`'s constructor fell back to the same hardcoded string when `Jwt:SecretKey` was missing — and that exact string was *also* the value committed in `appsettings.json`, so the fallback was already public. Removed the value from `appsettings.json` (kept the key with a comment explaining where it must come from); both places now throw `InvalidOperationException` at startup if `Jwt:SecretKey` is missing or under 32 chars, instead of silently signing/validating with a known key. Set a freshly-generated random secret via `dotnet user-secrets` for this dev checkout (added `<UserSecretsId>` to `Web.csproj` via `dotnet user-secrets init`) so local `dotnet run` keeps working. **For the user:** the old secret is permanently compromised (git history) — never reuse it anywhere, including other environments. Production/Electron packaging needs its own independently-generated value via the `Jwt__SecretKey` environment variable (`openssl rand -base64 48` or equivalent) before shipping. **Also worth knowing:** grepped for consumers — nothing in the app actually sends `Authorization: Bearer`, so this JWT scheme is currently dead infrastructure (Cookie auth is what gates everything); fixing it was still correct, but it's not blocking anything today. | `Web/Program.cs`, `Web/appsettings.json`, `Application/Services/Auth/JwtService.cs` | DONE |
| S8 | ~~7 controllers with no auth attribute at all~~: `AppSettings`, `Carts`, `CodeLists`, `Discounts`, `ExchangeRates`, `PaymentMethods`, `Taxes`. Added `[RequirePermission]` (class-level `View`, method-level `Manage`/`Create`/`Edit`/`Delete` on mutations, mirroring `ProductsController`'s convention). Added new `Discounts.View`/`Discounts.Manage` and `PaymentMethods.View`/`PaymentMethods.Manage` permissions (no existing const fit); reused `Settings.*`, `POS.Manage`, `Currency.*`, `Tax.*` for the rest. Granted the new/reused view perms to Manager too so back-office access isn't regressed. | `Web/Controllers/` | DONE |
| S9 | ~~Rate limiting fully configured in appsettings but never registered.~~ Wired up `AddInMemoryRateLimiting()` + `IRateLimitConfiguration` and `app.UseIpRateLimiting()`, placed after `UseRouting()` (needs endpoint metadata) and before `UseAuthentication()` (so login is throttled too). | `Web/Program.cs` | DONE |
| S10 | ~~Both exception middlewares were dead code~~, never registered, and both leaked `ex.Message` to clients even in their unused state. Deleted `GlobalExceptionMiddleware.cs` (redundant, wrong response shape). Hardened `ExceptionHandlingMiddleware`: registered first in the pipeline; AJAX/JSON requests get `{success:false, message}` with the real exception message only in Development; non-AJAX requests are rethrown so the existing dev/prod exception page handling still runs. | `Web/Middlewares/` | DONE |
| S11 | ~~Serilog imported but never configured.~~ Added `builder.Host.UseSerilog(...)` — console + rolling daily file under `Web/logs/`, 14-day retention, `Microsoft.AspNetCore` capped at Warning. All existing `ILogger<T>` calls now flow through it automatically (Serilog.AspNetCore replaces the default provider). | `Web/Program.cs:14` | DONE |
| S12 | ~~Permissions referenced but never seeded~~ — real bug was worse than described: runtime authorization doesn't read the DB `Permission`/`RolePermission` tables at all (`AuthService.HasPermissionAsync`/`GetUserPermissionsAsync` are dead code, never called). Claims are built at login from the static `PermissionHelper.RolePermissions` dict, which had **no entry for role "SuperAdmin"** — the seeded SuperAdmin user got zero permission claims and was locked out of every `[RequirePermission]` screen. Also `Alerts.Read`/`AuditLogs.Read` weren't defined/assigned to any role. | `Web/Helpers/PermissionHelper.cs` | DONE |

### P1 — The four requested features

| ID | Item | Effort | Status |
|---|---|---|---|
| **F3** | ~~Printer picker in Settings.~~ `IPrinterTransport`/`Win32PrinterTransport` gained `GetAvailablePrinters()` (reuses the enumeration `ListAvailablePrinters()` already did for console debug output). `ThermalPrinterService` now resolves the default printer from the `ReceiptPrinterName` system setting via a new `ResolveDefaultPrinterAsync()`, falling back to the old hardcoded name only if nothing's configured yet; renamed the const `FallbackPrinter` to make that explicit. Added `SettingsController.GetAvailablePrinters`/`TestPrinter` endpoints and wired `AppSettings.cshtml`'s free-text `ReceiptPrinterName` input into a `<select>` (AJAX-populated, keeps a "(not detected)" option if the saved printer isn't currently installed) plus a Test button. **Note:** `GetDefaultSettings()`'s POS category still has no `ReceiptPrinterName` entry, so it won't show up on the category-driven `Settings/Index` widget (only on `Settings/AppSettings`, which is where the field already lived) — left as-is since that page's generic `renderSettingInput()` switch has no "printer" case and adding one is separate scope. | ~0.5 day | DONE |
| **F2** | **PIN auth** ~~+ role/permission admin.~~ PIN login shipped; permission-matrix admin UI **deferred at the user's explicit choice** (see below) since it needs a separate, riskier change first. Added `User.PinHash`/`MustChangePin`; `IUserService.SetPinAsync`/`ValidatePinAsync` (BCrypt, mirrors the password methods); `IAuthService.LoginWithPinAsync`. `AccountController.Login` (the actual working web login) now authenticates by PIN via a numeric on-screen keypad instead of a password field; `AuthController`'s JSON API keeps the password path unchanged since it's already dead/unused infra (per S7's note — nothing sends `Authorization: Bearer`), not worth touching for this. A forced `ChangePin` screen appears right after login when `MustChangePin` is true (seeded admin, or any admin-reset user). Admin side: `UsersController.ResetPin` sets a new PIN + `MustChangePin=true` for a user, exposed as a "Reset PIN" button next to the existing (pre-existing, already-decorative/unwired) "Reset Password" button — matching that button's existing level of front-end wiring, not going further. **Found and fixed in passing:** `UserService.UpdateAsync` silently dropped `LastLoginAt` (never included in the field-copy list) — affected both the password and new PIN login paths identically; added it to the copy list. Also fixed a stray broken jQuery selector in `Login.cshtml`'s inline script (`button[type="submit").ToString()')` — dead/non-functional button-disable-on-submit logic, unrelated to PIN but in a file I was rewriting anyway. **Bigger finding, not fixed:** generating an EF migration for the two new columns swept in months of unrelated, already-existing-but-never-migrated schema drift (new `Receipts`/`CashDrawerSession`/`CashDrop`/`ReceiptItems`/`ReceiptPayments` tables, `Product.StockQuantity` int→decimal, `IsPack`/`PackSize`/`SingleProductId`, etc.) because `ErpDbContextModelSnapshot.cs` is far behind the actual `OnModelCreating` model. Removed that migration rather than apply it blindly. Instead added just the two columns via a minimal hand-run SQL script against the dev DB (`ALTER TABLE Users ADD PinHash/MustChangePin`, both additive/nullable-or-defaulted) — confirmed both columns were missing beforehand and seeded the existing admin row's PIN to "0000" (same BCrypt hash used in the C# seed, verified it actually validates) with `MustChangePin=1`. **Also discovered while doing that:** the real dev DB's admin user has `Id=2`, not `Id=1` like `ErpDbContext`'s `HasData` seed assumes — the seed-vs-reality mismatch is consistent with the same drift problem, so the update targeted `WHERE Username='admin'` instead. **This snapshot/drift problem is bigger than F2 and affects everyone** — the next person to run `dotnet ef migrations add` for anything (including F1's Postgres work) will hit the same bundling. Needs someone to deliberately reconcile the snapshot against reality before that happens, ideally reviewed against the real dev DB schema rather than assumed. | ~3-5 days (PIN done; permission admin deferred) | DONE (PIN) / TODO (permission admin, deferred) |
| **F4** | ~~Replace thermal receipt pipeline.~~ Deleted `ReceiptRenderer`/`IReceiptRenderer` (Puppeteer), `IThermalPrinterService.PrintHtmlAsync`, `EscPosBuilder.BuildImage`/`BuildFromHtmlImage`/`Resize`/`ToMonochrome` (all dead once the HTML path was gone), and `PuppeteerSharp` from both csproj files. `IEscPosBuilder.BuildReceipt(Receipt, StoreSettings)` is now the single entry point: it scans the store name/address/footer/every item name for Arabic and internally picks fast CP437 text (unchanged) or a new SkiaSharp-rendered image path — callers don't need to know which happened. The image path uses `SKFontManager.Default.MatchCharacter(0x0627)` to find whatever installed font actually has Arabic glyphs (don't hardcode a font name - varies by machine), `SkiaSharp.HarfBuzz`'s `SKShaper`/`DrawShapedText` for correct Arabic joining-form shaping, forces Arabic lines right-aligned regardless of their nominal alignment (RTL), and encodes the result via ESC/POS `GS v 0` raster (simpler than the old `ESC *` strip-based mode it replaces). **Verified by actually rendering a test receipt to PNG** (temporary debug hook + throwaway console harness referencing the real `Infrastructure`/`Core` projects, deleted after) with mixed Arabic/English content and looking at it — correct glyph joining, correct RTL alignment, bold total, clean separators, no clipping. Cleaned up `OrdersController`: deleted `TestThermalPrint`/`TestTextPrint`/`TestMinimalPrint` (pure debug scaffolding for probing the old HTML path, no external callers, fully superseded by F3's `TestPrintAsync`), simplified `PrintReceipt` to drop the now-meaningless `useHtmlView` parameter (always uses the direct/robust path via `PrintReceiptFallback`, which was already there as the fallback), and removed the now-unused `IViewRenderService` field/constructor param from that controller (left the service itself registered - it's a generic "render a view to string" utility with no other current caller, not exclusively a printing concern, so removing it project-wide is out of scope here). **Found and logged as A14, not fixed:** `ThermalPrinterService.BuildDirectOrderReceiptAsync` (used by `OrdersController`'s reprint/diagnostic actions, not the live checkout path) is a *second*, independent CP437-only text builder that duplicates `EscPosBuilder.BuildReceipt`'s job from `Order`/`Cart` data instead of a `Receipt` row, and it still can't do Arabic - out of scope for this pass since unifying it means touching a dozen-plus `OrdersController` actions each with their own formatting quirks. | ~3-4 days | DONE |
| **F1** | **Drop the SQL Server dependency.** Q1 answered "maybe" (multiple tills against a shared DB) — treat as yes. That rules out plain SQLite (no real multi-writer story over a network share); target **PostgreSQL** instead if this is pursued. Bulk of the work is **81 decimal aggregate calls** written for SQL Server semantics, not the provider swap itself. Q3 answered (all test data, no live migration needed) removes any data-migration constraint on this. Silver lining: `BackupService.cs` (~1500 lines of `BACKUP`/`RESTORE DATABASE`) collapses to `pg_dump`/file-level backup. S6's in-process idempotency guard would also need to move to the DB for a real multi-till deployment. | ~1-2 weeks | TODO (unblocked, needs replanning around PostgreSQL) |

### P2 — Architecture / maintainability

| ID | Item | Status |
|---|---|---|
| A1 | ~~`BaseRepository` rethrows `new Exception(e.Message)`.~~ Removed the pointless try/catch/rethrow wrapping in `SaveAsync`, both `DeleteAsync` overloads, `SoftDeleteAsync`, `DeleteRangeAsync`, `SoftDeleteRangeAsync`, `DeleteByIdAsync`, `DeleteRangeByIdsAsync` — each one only caught `Exception` and rethrew `new Exception(e.Message[, e])`, which preserved the message text but destroyed the real exception type and (for the 4 without `, e`) the stack trace too. The 280 existing `catch (Exception)` call sites are unaffected (still catch everything, same `Message`); the only change is that a caller that ever wants to catch a *specific* type (e.g. `DbUpdateException` for unique-constraint violations) now can, since the original exception now actually propagates. | DONE |
| A2 | ~~`Program.cs` manual DI — commented-out registrations, `IBackupService` twice.~~ Removed the duplicate `IBackupService` registration and the two commented-out "namespace issues" blocks. The `AppSetting`/`CodeList` stacks in those blocks weren't actually dead — `AppSettingsController` and `CodeListsController` constructor-inject `IAppSettingService`/`ICodeListService`/`ICodeListValueService`, so with the registrations commented out **both controllers threw `InvalidOperationException` at request time** (and S8 had since added `[RequirePermission]` to them assuming they worked). The namespaces in the commented lines all match reality and every dependency resolves through the same pattern as the other ~40 registrations, so they're now registered normally — verified by build (interface conformance is compile-checked by `AddScoped<TInterface,TImpl>`) and a live startup. **Found + fixed a real blocker while testing:** the app wouldn't start from `dotnet user-secrets` alone (only with a `Jwt__SecretKey` env var) — `Web.csproj` has `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>`, which also suppresses the `[assembly: UserSecretsId]` attribute the SDK emits from `<UserSecretsId>`, so `WebApplication.CreateBuilder` never located the secret store and S7's `Jwt:SecretKey` never loaded. Added `Web/Properties/AssemblyInfo.cs` declaring `[assembly: UserSecretsId("707edb39-…")]` by hand; `dotnet run` now boots on the user-secrets value with no env var. **Not done (deliberately):** did *not* delete the `UrlRoute`/`UrlHelperService` stack (`IUrlRouteService`/`UrlRouteService`/`IUrlRouteRepository`/`UrlRouteRepository`/`Web.Services.UrlHelperService`) — it is genuinely 100% dead (no controller, nothing injects `UrlHelperService`, only `UrlRoutes` *seed* data exists) but it's plausibly half-built localization-routing infra (there's `LanguageRouteConstraint`/`LanguageMiddleware`), so removing it is a product call, not a cleanup. Same for whether `AppSettingsController`/`CodeListsController` should exist at all — they have no views or JS callers today; registering their services was the low-risk fix, deleting the feature is a separate decision. Also left the DI block's overall order/sectioning mostly as-is — reordering 120 lines risks a silent drop for little gain. | DONE (dead UrlRoute stack + unused-controller question left as noted) |
| A3 | ~~Duplicates: `ErpDbContext`, `EntityMapper`, `General.cs`.~~ Deleted `Core/Data/ErpDbContext.cs` (9 lines, zero references anywhere — confirmed via `using Core.Data` grep returning nothing vs 46 files using the real `Infrastructure/Data/ErpDbContext.cs`), `Infrastructure/Common/Helpers/Mapper/EntityMapper.cs` (byte-identical to the actually-used `Common/Mapper/EntityMapper.cs`, its own namespace never imported anywhere), and `Common/Helpers/General.cs` (an empty `public class General {}` stub — the real extension methods everything actually calls, `IsNullOrEmpty`/`IsNullOrEmptyOrDefault`, live in the *different* file `Common/Helpers/Entensions.cs`'s `ObjectExtensions`, same namespace). **Bonus find while tracing this:** the whole `Infrastructure/Common/` folder was an orphaned second `Common.csproj` project — never referenced by any other `.csproj` or the `.sln` — whose `.cs` files were nonetheless being silently compiled *into* `Infrastructure.csproj` via its default recursive glob. Deleted the entire orphaned project (it had no other content once its two duplicate files above were pulled: just the stray `.csproj`, empty `bin`/`obj`, and an empty `Helpers/Specifications` folder). None of the four deleted files were even git-tracked except `Common/Helpers/General.cs` — the rest never made it into a commit. Full solution rebuild after: 0 errors, same warning set. **Not done, deliberately left alone — needs a product decision, not a delete:** `Tax` entity x2 / `TaxController` + `TaxesController` turned out to be far more than a simple duplicate. They're two complete, independently-wired feature stacks (entity → repo → service → controller) that route-collide at the same URL: `Core/Entities/Products/Tax.cs` (class `TaxRate` — rich model with `TaxCode`/`CategoryId`/`EffectiveFrom`/`EffectiveTo`) backs `TaxController` (`[HttpGet("api/taxes")]`, serves the actual live "Tax Management" UI page at `Views/Tax/Index.cshtml`, whose form fields are `TaxCode`/`EffectiveFrom`/`CategoryId` — confirming this is the one users see), while `Core/Entities/Sales/Tax.cs` (class `Tax` — simpler, `SupplierId`-linked) backs `TaxesController` (`[Route("api/[controller]")]` → `api/Taxes`) and is the one actually registered in `ErpDbContext` as `DbSet<Tax> Taxes` with seed data. ASP.NET routing is case-insensitive, so `api/taxes` and `api/Taxes` are the same route — both controllers' `GET` actions collide there (a real `AmbiguousMatchException` waiting for whichever request pattern triggers it; the ASP0023 build-time analyzer didn't flag it, apparently because it doesn't compare route templates across controllers case-insensitively). Worse: `TaxRate` (the one the live UI actually depends on) **isn't in the EF model at all** — no `DbSet<TaxRate>`, no `modelBuilder.Entity<TaxRate>()`, not reachable via any navigation from a tracked entity — so `TaxRepository`'s `_Context.Set<TaxRate>()` calls should throw `InvalidOperationException` at runtime if actually hit. Neither stack is even wired into real business logic: checkout (`POSCheckoutService`) hardcodes `TaxRate = 0` as a DTO field, not a service call (see A6) — so this has likely been silently broken with nobody noticing because nothing downstream depends on it working. Picking a survivor is a product call (which UI/shape to keep, and whether the seeded `Sales.Tax` data matters), not something to guess at silently — flagging for the user/next session rather than deleting either side. | `Core/Data/`, `Common/Mapper/`, `Common/Helpers/`, `Infrastructure/Common/` (removed) | DONE (partial — Tax duplication intentionally left, needs a decision) |
| A4 | `CartItem.Quantity` is `int` while `Product.StockQuantity` is `decimal` — root cause of the whole `WeightOrVolume` workaround. Code comments admit `TotalPrice` is wrong for weighed goods. Fix: make `Quantity` decimal, delete `WeightOrVolume`. | TODO |
| A5 | ~~Dashboard headline figures are hardcoded mock values.~~ `GetDashboardStatsAsync` now computes everything from real data: `TotalSales`/`TotalOrders`/`TodaySales`/`TodayOrders` via the existing `ISalesReportService` (this-month-to-date and today, respectively); `*Change` percentages compare against a like-for-like prior-period window (same number of days into last month); `Profit` sums `(UnitPrice - Product.CostPrice) * quantity` over items actually sold this month (via `IOrderService.GetByDateRangeAsync`, which already eager-loads `Cart.CartItems.Product`) — items whose product has no `CostPrice` set contribute 0 rather than a guessed margin. `TotalCustomersChange`/`TotalProductsChange` are left at `0`, not faked: neither `Customer` nor `Product` has a `CreatedAt` (they don't inherit `BaseEntity`), so there's no real data to compute a period-over-period count from without a schema change — that's out of A5's scope. **Found, not fixed — logged as A13:** every *other* method in `DashboardService.cs` (`GetTopProductsAsync`, `GetTopCustomersAsync`, `GetRecentOrdersAsync`, `GetRevenueTrendAsync`, `GetProfitAnalysisAsync`) is still fully or partially mocked; A5's backlog description only called out the headline stats, and fixing the rest is a comparably-sized job on its own. | DONE |
| A6 | ~~Any non-empty discount code grants a flat 10%.~~ Turned out to be one symptom of a bigger, actually-live bug chain in the discount feature — the flat-10% hack was close to unreachable in practice because discounts never reliably made it to the backend at all. Root causes found and fixed: **(1)** `POSController.HoldCart` set `cart.DiscountAmount`/`DiscountCode`/`DiscountPercentage` on its local in-memory `cart` object but never called `_cartRepository.SaveAsync(cart)` — `HoldCartAsync` (only called when `!IsForCheckout`) re-fetches its own copy from the DB and only touches `IsActive`, so the discount fields were silently dropped, and for the live "pay now" flow (`IsForCheckout: true`, which is what `POS.js`'s main `checkout()` always sends) *nothing* persisted them at all. **(2)** The same method's walk-in/notes handling did `cart.DiscountCode = notes`, unconditionally overwriting any discount label just set two lines above — now appends instead. **(3)** `POS.js`'s `checkout()` built the discount payload from `this.currentDiscount?.amount/.code/.percentage`, but `currentDiscount` is always shaped `{type, value, name}` — those properties don't exist, so the payload was always `null` regardless of what the cashier picked in the "Apply Discount" dialog (the *other* checkout path, `holdCart()`/the hold-then-resume flow, already derived it correctly from `.type`/`.value` — this one code path just never got the memo). **(4)** `POSCheckoutService.ProcessCheckoutAsync` only ever subtracted `cart.DiscountAmount` from the subtotal, so even a *correctly persisted* `DiscountPercentage` was silently ignored at the one place that actually charges the customer. **(5)** The flat-10%-for-any-code hack itself, in `CalculateTotalAsync` (an endpoint `POS.js` doesn't currently call, but fixed for correctness/next-caller safety) — replaced with the same amount/percentage logic as everywhere else; `discountCode` is now treated purely as a label, never a numeric lookup, since `Discount` entities are per-supplier rates with no customer-redeemable-code concept at all. **Interaction with S2 worth knowing:** before this fix, a discounted checkout would have started **hard-failing** under my S2 total-bound check (client sends a correctly-discounted `TotalAmount`, server computes a full-price `cartTotal` since the discount never persisted → bound exceeded → rejected) instead of the pre-S2 behavior of silently undercharging. Found this by tracing the interaction, not by a report — worth a live checkout-with-discount smoke test since I did not run the app for this (shared dev DB, several other sessions mid-edit — didn't want to risk it; see A3's note on the live `Tax` duplication for the same reason `taxAmount` is still hardcoded, deliberately not touched here). | `Web/Controllers/POSController.cs`, `Web/wwwroot/js/Pages/POS/POS.js`, `Application/Services/Sales/POSCheckoutService.cs`, `Application/IServices/Sales/IPOSCheckoutService.cs`, `Application/DTOs/Sales/CalculateTotalRequest.cs` | DONE (discount half only — tax half blocked on A3's Tax-duplication decision) |
| A7 | Manual price adjustments stored as prose appended to `cart.DiscountCode` ("Adjustment: +$3.00") — unreportable. Needs a real column. | TODO |
| A8 | `.gitignore` contains only `/.idea/` — **11,918 tracked `bin`/`obj`/`dist` files** including DLLs and Electron `win-unpacked` output. | TODO |
| A9 | ~~No tests anywhere.~~ Added `Tests/Tests.csproj` (xUnit + Moq + `Microsoft.EntityFrameworkCore.Sqlite`), covering the three priorities named in this row: **checkout pricing** (`CartServiceTests` — S2's `AddItemToCartAsync` ignores a caller-supplied price and always repriced from `Product.SellingPrice`, both for a new cart item and for accumulating an existing one), **stock deduction** (`ProductRepositoryTests` — S5's `TryDeductStockAsync` deducts and returns true with sufficient/exact stock, returns false and leaves stock untouched when insufficient), and **cash-drawer reconciliation** (`CashDrawerServiceTests` — `CalculateExpectedCashAsync` = opening cash + sales − drops, including the zero-activity and unknown-session cases). Used a real SQLite in-memory `ErpDbContext` (via `SqliteInMemoryDbContextFactory`, `EnsureCreated()` — not a migration) rather than EF's InMemory provider, since `TryDeductStockAsync` needs `ExecuteUpdateAsync` translated to real SQL, which InMemory doesn't support. **Caught one test-harness bug against itself while writing these:** re-reading a product via `context.Products.FindAsync` on the *same* `DbContext` that ran `ExecuteUpdateAsync` returned the stale tracked instance from the change tracker's identity map instead of the post-update DB value (`ExecuteUpdateAsync` bypasses change tracking entirely) — not a product bug, fixed by verifying through a fresh `DbContext` on the same underlying connection, which is also how real request-scoped contexts behave in production. | `Tests/` (new project) | DONE |
| A10 | ~~`async void` in `BackupSchedulingService.cs:48`.~~ The method body was already wrapped in try/catch/finally, but the `await _executionLock.WaitAsync(0)` call happened *before* that try block — an exception there (e.g. `ObjectDisposedException`) would have been unhandled in an `async void` context and crashed the process. Moved the wait into its own try/catch so nothing between method entry and the `finally` can escape. | DONE |
| A11 | Oversized units: `POS/Index.cshtml` 1,714 lines · `POS.js` 2,734 lines · `POSController.cs` 1,128 lines. | TODO |
| A12 | ~~Pre-existing: `UpdateCartItemQuantityWithCurrencyAsync` computed a converted price and never applied it.~~ Extracted a shared private `UpdateCartItemQuantityInternalAsync(cartId, productId, quantity, trustedUnitPrice, weightOrVolume)` — mirrors the `AddItemToCartWithTrustedPriceAsync` pattern S2 already established. The public `UpdateCartItemQuantityAsync` passes `trustedUnitPrice: null` (unchanged behavior: quantity-only updates don't touch price); `UpdateCartItemQuantityWithCurrencyAsync` now passes its computed `convertedUnitPrice` through instead of discarding it. Safe by construction: the price being converted (`cartItem.UnitPrice`) is itself always server-trusted post-S2, never client input. | `Application/Services/Inventory/CartService.cs` | DONE |
| A14 | Found during F4: `ThermalPrinterService.BuildDirectOrderReceiptAsync` (used only by `OrdersController`'s reprint/diagnostic actions, not the live checkout path) is a second, independent CP437-only text builder duplicating `EscPosBuilder.BuildReceipt`'s job from `Order`/`Cart` data instead of a `Receipt` row — also can't do Arabic. Unifying it means touching a dozen-plus `OrdersController` actions each with their own formatting quirks; out of F4's scope. | TODO |
| A13 | ~~Found during A5: `DashboardService.cs`'s other methods still mocked.~~ De-mocked all of them against real data via `_orderService.GetByDateRangeAsync` (already eager-loads `Cart.CartItems.Product`, `Customer`, `Currency`). **`GetTopProductsAsync`** — trailing 90 days, grouped by `ProductId`, ranked by real revenue (`Σ UnitPrice·qty`), `qty` = `WeightOrVolume` when set else `Quantity`. **`GetTopCustomersAsync`** — trailing 12 months grouped by `CustomerId` (walk-in / null-customer sales excluded), real `TotalSpent`/`TotalOrders`/`LastOrderDate`. **`GetRecentOrdersAsync`** — real recent orders; payment method resolved by mapping `Receipt.OrderId → PaymentMethod.Name` (injected `IReceiptService`, `Order` has no payment field), status from `(OrderStatus)o.Status`, `"Walk-in"` for null customer. **`GetSalesAnalyticsAsync`** — replaced the flat `Sales · 0.3` profit guess with real gross profit per bucket (`revenue − COGS` from `Product.CostPrice`). **`GetRevenueTrendAsync`/`GetProfitAnalysisAsync`** (interface-only, no controller/view/JS caller today, but de-mocked for honesty) — real per-period buckets; "expense"/"cost" = COGS since the system has no operating-expense data (documented in code). Shared helpers added: `EffectiveQuantity`, `LineCost`, `IsCompletedSale` (excludes Cancelled/Returned — POS writes orders as `Delivered`), `ResolvePeriodWindow`, `BuildTrendBuckets`. `GetCategoryAnalyticsAsync`/`GetInventoryStatusAsync`/`GetDashboardStatsAsync` (A5) already real — untouched. The Dashboard view renders none of the changed fields' new values incorrectly (it reads `Id`/`CustomerName`/`AmountFormatted`/`OrderDate`, and hardcodes the "Completed" badge — no string-matching on `Status`). **Verification:** `Application.csproj` builds clean (0 errors, 0 warnings in `DashboardService`); `Tests` 8/8. Full-solution build + live dashboard smoke-test **not done** — a Rider debug session (`Web.exe` live on :5000) held `Web/bin` DLLs locked; the change is `Application`-only and `IDashboardService` is unchanged, so Web needs no recompile for correctness, but a clean full build should be re-run once the debugger is stopped. | DONE (full build + smoke-test pending debugger release) |
| A15 | ~~**High priority — found during F2.** `ErpDbContextModelSnapshot.cs` far behind the actual EF model — a trivial `migrations add` scaffolded months of unrelated already-real drift.~~ Reconciled. A prior (unlogged) session had already done the bulk: replaced the single stale `20251005145244_AddCartSessionEntities` migration with one `20260905140238_InitialBaseline` (full-schema `CreateTable` for all 40 tables, generated from the current model), regenerated `ErpDbContextModelSnapshot.cs` (+819/−92) to match, and added `Infrastructure/Data/ErpDbContextFactory.cs` (`IDesignTimeDbContextFactory` — lets `dotnet ef` build the context directly without running `Program.cs`'s JWT-secret fail-fast et al). **This session verified + finished it:** (1) scaffolded a throwaway `migrations add` → **empty `Up()`/`Down()`**, proving model == snapshot == `InitialBaseline.Designer` with zero remaining drift (the only designer-vs-snapshot delta is the cosmetic `ToTable("X")` vs `ToTable("X", (string)null)` EF codegen quirk — functionally identical), then removed it. (2) Confirmed the dev DB (`POS`) had **no `__EFMigrationsHistory` at all** — it was built by `EnsureCreated()`/hand-run SQL, never EF migrations — and was missing `CartSessions`/`CartSessionItems` (the old migration was never applied here). (3) Applied `InitialBaseline` to a throwaway fresh DB: clean, 40 tables + history row + seed data. (4) Per the user's call (Q3: all test data), **dropped and recreated `POS` from `dotnet ef database update`** — now 41 tables, `__EFMigrationsHistory` = `InitialBaseline`, admin seeded at `Id=1` with working PIN hash (`MustChangePin=1`), all previously-missing tables present. Deleted the obsolete unreferenced `Infrastructure/Migrations/Script_BackupTables.sql` (hand-patch relic with a schema that didn't even match the model — `BackupType` vs `Type`). Full solution build + `Tests` (8/8) green after. **For anyone else with a pre-A15 `POS` DB:** same fix — drop it and run `dotnet ef database update` (nothing to preserve per Q3). Future `migrations add` now produces clean, drift-free diffs — F1's Postgres work included. | DONE |
| A17 | ~~Language switch took two clicks / one refresh to take effect.~~ `LanguageMiddleware` resolves the language (URL path → query → cookie → session) and set `IRequestCultureFeature` + the `.AspNetCore.Culture` response cookie, but the very next middleware `app.UseRequestLocalization()` ran its **default providers** (QueryString → Cookie → Accept-Language) and *rebuilt* the culture feature from scratch — `CookieRequestCultureProvider` read the **incoming request** cookie, still holding the pre-switch language, and won. So `GET /ar/…` rendered in English (wrong `<html lang>`/`dir`, English `.resx` strings, `rtl.css` disabled) and only picked up Arabic on the *following* request once the cookie caught up. Worse with the `lang-switch.js` partial-swap path: the first click changed the URL + the switcher's active state but swapped in an English fragment, so the page just looked stuck. Fixed by adding `CurrentLanguageItemRequestCultureProvider` (in `LanguageMiddleware.cs`) — reads `HttpContext.Items["CurrentLanguage"]` (which `LanguageMiddleware` already sets) — and `options.RequestCultureProviders.Insert(0, …)` in `Program.cs` so it outranks the default providers. Now the two middlewares agree on the same request. **Verified** with a headless-Chromium repro (login admin / driven UI) + raw-curl HTTP repro against a patched build on :5001: single click switches EN↔AR↔EN immediately, `<html lang/dir>`, title, `<h1>`, table headers and `rtl.css` all flip on the first click; unauthenticated `/ar/Account/Login` full-nav still correct. | DONE |
| A18 | ~~Found while testing A17.~~ **(1) Partial-swap re-init** — after a `lang-switch.js` shell swap, per-page `<script src=…>` in `@section Scripts` were never re-executed, so page JS stayed bound to the replaced DOM (chart period-buttons dead until a hard reload). Fixed: `reExecuteScripts` now re-runs scripts whose src is under `/js/pages/` (app page controllers — all written as re-runnable IIFE + `$(document).ready`), while libraries / CDN scripts still load once. `wwwroot/js/Pages/Backup/Index.js` had a script-scope `let operationId` (would be a re-declare `SyntaxError` on re-run) → changed to `var`. **(2) chart.js** — `dist/chart.min.js` was the **ESM** build served as a classic script (`Cannot use import statement outside a module` → `Chart is not defined` → Dashboard/Reports/StockValue charts never rendered, any language, even on full load). Replaced with `dist/chart.umd.min.js` (v4.4.0 UMD, defines the `Chart` global); updated `_Layout.cshtml` + `POS/Index.cshtml`; deleted the ESM file; removed the now-redundant per-page `https://cdn.jsdelivr.net/npm/chart.js` includes from `StockValueReport`/`Reports/Index`/`Reports/SalesReport` (the layout loads it globally, and the CDN ref broke offline); `libman.json` updated. **(3a) Shared `.resx` never resolved** — `SharedResource` lived in namespace `Web.Resources` while `options.ResourcesPath = "Resources"`, so the localizer factory looked for `Web.Resources.Resources.SharedResource` and **every `@SharedLocalizer[...]` returned its key** — the entire sidebar + topbar were untranslated in *both* languages (English only "looked" right; Arabic showed English). Moved the marker class to `Web/SharedResource.cs` (namespace `Web`); updated `Program.cs` + removed the dead `@using Web.Resources` from `_ViewImports`/`_Layout`/`Login`. **(3b)** Added missing `PointOfSale`/`AddProduct`/`ViewAll` to `Resources/Views/Dashboard/Index.{en,ar}.resx` (quick-action cards). **(3c) Font Awesome 404** — `_Layout` + `Orders/Receipt` loaded `/css/font-awesome.css`, whose `@font-face url(../webfonts/…)` resolved to `/webfonts/…` (nonexistent); repointed both to the libman copy `/lib/font-awesome/css/all.min.css` (fonts resolve) and deleted the stray `wwwroot/css/font-awesome.css`. **Verified** end-to-end on a patched :5001 build (headless Chromium + curl): charts render, single-click EN↔AR↔AR switch, chart period-button works after a switch, sidebar/topbar/quick-actions all localized, no `Chart`/import errors, no font 404s. **Still open:** the Dashboard clock only re-paints per-minute so a fast toggle *looks* frozen (it isn't); and 7 list views (`Products`/`Categories`/`Customers`/`Orders`/`Suppliers`/`Receipts`/`Inventory`) still 404 on `~/lib/datatables/js/jquery.dataTables.min.js` + `dataTables.bootstrap5.*` (files don't exist at that path; DataTables works from the `_Layout` copy — dead includes, pre-existing, left alone). | DONE (DataTables dead-include 404s noted, not fixed) |
| A16 | Found during F2: `UsersController.GetUserActionButtons`' "Edit"/"Reset Password"/"Disable" buttons (and `GetRoleActionButtons`'s "Edit"/"Permissions") are pure decoration — no `data-id`/`onclick` wiring in the emitted HTML at all, and `ResetPassword` is a stub that always returns success without touching the DB (`// Implementation for password reset would need to be added to IUserService`). Added a "Reset PIN" button in the same style for F2's admin-reset flow (backed by a real endpoint, `UsersController.ResetPin`) to match the existing convention, but didn't fix the pre-existing dead buttons around it — that's the whole Users admin table's interactivity, a separate and fairly large job. | TODO |

### P3 — Front-end direction

| ID | Item | Status |
|---|---|---|
| FE1 | **React for the POS screen only** (see Notes). Rest stays Razor. | PROPOSED |
| FE2 | Fix the actual POS latency causes before/independently of any rewrite: `CommandTimeout(10)`, Puppeteer blocking the print path, full product list loaded on page load, heavy theme assets. | TODO |

---

## Notes & Decisions

### F4 — why the receipt pipeline gets replaced, not patched

Three parallel print paths exist today. The HTML one is the problem:
`PrintHtmlAsync` → downloads & launches **headless Chromium** → screenshots at 272px → sends as ESC/POS raster.

- `BrowserFetcher().DownloadAsync()` = ~150-200MB download triggered on first print, at the till, possibly mid-sale; then 1-3s browser launch **per receipt**.
- **Width mismatch:** viewport 272px (72mm) vs CSS wrapper `80mm`/302px → cut-off and misaligned receipts.
- A **debug PNG is written to temp on every print** (`ReceiptRenderer.cs:86-96`), never cleaned up.
- `await Task.Delay(500)` hoping fonts loaded — a race, not a wait.
- Rasterized text thresholded at `gray < 160` is why output looks fuzzy vs the printer's built-in fonts.
- `ToMonochrome()` — 40 lines of `unsafe` pointer code — is **dead**, never called.
- Text fallback encodes as **codepage 437**, which cannot represent Arabic at all.

### FE1 — React scope decision

React for the POS terminal only (genuinely stateful, and already effectively an SPA —
`POSController` has 69 JSON endpoints and `POS.js` is 2,734 lines). Razor stays for admin CRUD,
where server-rendered tables + DataTables + `.resx` localization + permission-driven rendering
already work and a rewrite buys nothing.

Caveat recorded at proposal time: React will not fix the things that make the POS feel slow today
(see FE2). Those are server- and pipeline-side, not rendering-side.

---

## Session Log

### 2026-09-06 — A17 (language switch one-click fix) + A18 (all items fixed)

Reported symptom: "switching language has a bug." Reproduced it two ways against the live
app (headless Chromium driving the real UI after an `admin` login, plus a raw-curl HTTP
repro): clicking العربية changed the URL to `/ar/…` and marked the switcher active, but the
page stayed English + LTR until a **second** click or an F5.

Root cause in the middleware pipeline (`Program.cs` + `LanguageMiddleware.cs`): our custom
`LanguageMiddleware` picks the language from the URL path and sets the culture, then
`app.UseRequestLocalization()` immediately overwrites it using only the stock providers, and
`CookieRequestCultureProvider` reads the *pre-switch* request cookie. The URL path isn't a
provider and the switcher sends no `?culture=`, so nothing tells `RequestLocalizationMiddleware`
about the new language until the response cookie lands and the *next* request reads it.

Fix: `CurrentLanguageItemRequestCultureProvider` reading `HttpContext.Items["CurrentLanguage"]`
(already populated by `LanguageMiddleware`), inserted at index 0 of `RequestCultureProviders`.
Two files changed: `Web/Middlewares/LanguageMiddleware.cs`, `Web/Program.cs`. Verified on a
throwaway build on :5001 (couldn't rebuild `Web/bin` — Rider debugger holds `Web.exe` on :5000):
single click now flips EN↔AR↔EN immediately — `<html lang/dir>`, `<title>`, `<h1>`, table
headers, `rtl.css`. **The user must restart their Rider debug session to pick up the change.**

Then fixed A18 (see the backlog row for the full shape): partial-swap now re-runs `/js/pages/`
controllers on a shell swap (Backup's script-scope `let`→`var`); chart.js swapped ESM→UMD build
(+ dropped the redundant per-page CDN includes); `SharedResource` moved to namespace `Web` so
`@SharedLocalizer` actually resolves — the sidebar/topbar were untranslated in *both* languages,
not just missing two keys; missing Dashboard quick-action `.resx` keys added; Font Awesome
`_Layout`/`Receipt` links repointed to the libman copy so `../webfonts/` resolves. Left alone:
7 list views still 404 on non-existent `~/lib/datatables/js/*` includes (DataTables works from
the `_Layout` copy — dead includes, pre-existing).

Files: `Web/SharedResource.cs` (moved from `Resources/`), `Web/Program.cs`,
`Web/Views/_ViewImports.cshtml`, `Web/Views/Shared/_Layout.cshtml`, `Web/Views/POS/Index.cshtml`,
`Web/Views/Orders/Receipt.cshtml`, `Web/Views/Account/Login.cshtml`,
`Web/Views/{Inventory/StockValueReport,Reports/Index,Reports/SalesReport}.cshtml`,
`Web/wwwroot/js/lang-switch.js`, `Web/wwwroot/js/Pages/Backup/Index.js`,
`Web/wwwroot/lib/chart.js/dist/chart.umd.min.js` (new, `chart.min.js` deleted),
`Web/wwwroot/css/font-awesome.css` (deleted), `Web/libman.json`,
`Web/Resources/Views/Dashboard/Index.{en,ar}.resx`.

### 2026-09-05 — A13 (rest of DashboardService de-mocked)

Same session, continued after A2. See the A13 backlog row for the full shape.

- De-mocked `GetTopProductsAsync`, `GetTopCustomersAsync`, `GetRecentOrdersAsync`,
  `GetRevenueTrendAsync`, `GetProfitAnalysisAsync`, and the profit half of `GetSalesAnalyticsAsync`
  — all now computed from `_orderService.GetByDateRangeAsync` (+ `IReceiptService`, newly injected,
  for the order→payment-method map). Added shared helpers (`EffectiveQuantity`, `LineCost`,
  `IsCompletedSale`, `ResolvePeriodWindow`, `BuildTrendBuckets`).
- `Random.Shared` and the static Jan–Aug arrays are gone from the file.
- **Verification is partial:** `Application.csproj` compiles clean (0 errors, no warnings in the
  file), `Tests` 8/8. Could **not** run a full-solution build or smoke-test the live dashboard —
  the user has a Rider debug session running (`Web.exe` on :5000) that locks `Web/bin/*.dll`.
  A13 only touches `Application` and leaves `IDashboardService` unchanged, so this is a
  copy-step lock, not a compile problem — but flag for a clean rebuild once the debugger stops.
- Did **not** kill the user's debug session to force the build.

### 2026-09-05 — A2 (Program.cs DI cleanup) + user-secrets fix

Same session as A15 below, continued. See the A2 backlog row for the full shape.

- Removed the duplicate `IBackupService` registration and both commented-out "namespace issues"
  blocks. The `AppSetting`/`CodeList` service stacks in those blocks were **not dead** —
  `AppSettingsController`/`CodeListsController` inject them and were throwing at request time with
  the registrations commented out. Registered them properly (namespaces all matched; deps resolve
  identically to the other ~40 registrations).
- **Real blocker found while smoke-testing startup:** `dotnet run` failed with "Jwt:SecretKey is
  not configured" even though `dotnet user-secrets list` showed it set. Cause: `Web.csproj`'s
  `<GenerateAssemblyInfo>false</GenerateAssemblyInfo>` also suppresses the `[assembly: UserSecretsId]`
  attribute, so `CreateBuilder` never loaded the secret store — S7's whole `dotnet user-secrets`
  setup was inert, only the `Jwt__SecretKey` env var worked. Fixed with a hand-written
  `Web/Properties/AssemblyInfo.cs`. Confirmed the app now boots on the user-secrets value alone.
- Set a fresh random `Jwt:SecretKey` in this checkout's user-secrets (48 bytes base64) — the
  earlier value in the store had been overwritten with a weak test string during debugging.
- **Left alone (noted in the backlog row):** the fully-dead `UrlRoute`/`UrlHelperService` stack
  (removal is a product call — possible half-built localization infra), and whether the
  `AppSettings`/`CodeLists` API controllers should exist at all (no views/callers today).
- Full solution rebuild: 0 errors. `Tests`: 8/8.

### 2026-09-05 — A15 (migration snapshot reconciliation)

Single session (no peers active — checked). User picked A15 off the backlog. Found a prior
session had already done most of it but never logged it: an untracked `20260905140238_InitialBaseline`
migration (replacing the stale `20251005145244_AddCartSessionEntities`), a regenerated snapshot,
and a new `ErpDbContextFactory.cs` — all sitting uncommitted in the tree.

- **Verified the prior work is correct:** throwaway `migrations add` → empty `Up()`/`Down()`, so
  model == snapshot == `InitialBaseline.Designer`, no drift left. Removed the throwaway.
- **Dev DB:** `POS` had no `__EFMigrationsHistory` (built by `EnsureCreated`/hand-SQL, never EF)
  and was missing `CartSessions`/`CartSessionItems`. Test-applied `InitialBaseline` to a scratch
  DB first (clean), then — per the user's explicit choice, Q3 says all test data — **dropped and
  recreated `POS`** via `dotnet ef database update`. Now 41 tables, history row present, admin
  seeded at `Id=1` with a working PIN hash. The old drifted `POS` had admin at `Id=2`; that
  mismatch (A15's secondary finding) is resolved by the recreate.
- Deleted `Infrastructure/Migrations/Script_BackupTables.sql` — obsolete unreferenced hand-patch,
  schema didn't match the model.
- Full solution build: 0 errors, 0 warnings. `Tests`: 8/8.
- **Still uncommitted (pre-existing, not mine):** the whole day's S/F/A working tree, plus
  undocumented in-flight feature work (`StoreSettings` — DB table + service, DI-registered;
  `ProductUnit` entity + `Add_Product_Unit_Column.sql` + `UnitConverter.js`, *not* in the EF
  model yet; `CurrencyHelper`; Electron packaging + two new `*.md` files that break the
  "only WORKLOG" rule; a batch of new Arabic `.resx`). Flagged to the user — none of it is A15.
- **A15's payoff for F1:** `dotnet ef migrations add` now yields clean diffs. Whoever does F1's
  provider swap starts from a truthful snapshot.

### 2026-09-05 — A9 (test project)

Picked up after F2, confirmed with erp-94 (finishing A3) that it was independent of everything
in flight — a brand-new project, additive only, touches no file any other session had open.

- **A9** — see backlog row above for the full shape. `dotnet test Tests/Tests.csproj`: 8/8 pass.
- Added `Tests/Tests.csproj` to `ERP.sln` via `dotnet sln add`.
- Full solution rebuild (`ERP.sln`) after: 0 errors, same pre-existing warning set.
- **Not done:** the rest of A9's obvious next candidates — `POSCheckoutService`'s total-bound
  check (S2) and the full checkout transaction (S4) would need a much heavier harness (the real
  method pulls in `ICartService`/`IOrderService`/`IReceiptService`/`ICashDrawerService`/
  `ICheckoutIdempotencyGuard`/a DB transaction all at once) — left as a natural next test to add,
  not attempted here to keep this pass scoped to the three priorities the backlog row named.

### 2026-09-05 — F2 (PIN auth)

Claimed via cross-session message with erp-75 (who took F4 in the same exchange) once Q2 was
answered. Scope decision, confirmed with the user directly mid-task via a question: ship PIN
login now, defer the permission-matrix admin UI — it only means anything if live authorization
also gets rewired off the static `PermissionHelper` dict onto the DB `Permission`/`RolePermission`
tables (dead code today, per S12), and that rewire can lock out every user if the DB tables
aren't seeded to exactly match the static dict first. User chose "defer it."

- **F2 (PIN login)** — see backlog row above for the full shape of the change.
- **Found and fixed in passing:** `UserService.UpdateAsync` dropped `LastLoginAt` on every save
  (missing from its explicit field-copy list) — hit both the password and new PIN login paths
  identically. Added it. Also fixed a stray broken jQuery selector in `Login.cshtml`'s inline
  script left over from some earlier edit (`button[type="submit").ToString()')` — dead code,
  unrelated to PIN, but in a file I was already rewriting.
- **Real hazard caught before it shipped:** generating the EF migration for `PinHash`/
  `MustChangePin` bundled in months of unrelated, already-real schema drift the snapshot never
  captured (new `Receipts`/`CashDrawerSession` tables, `StockQuantity` int→decimal, etc. — logged
  as **A15**, flagged as affecting everyone, especially F1). Removed that migration rather than
  apply it. Confirmed with the user before proceeding, then hand-applied just the 2 needed columns
  via raw SQL against the dev DB (`sqlcmd`) — checked both columns were actually absent first,
  verified the BCrypt hash I was about to seed actually validates "0000" before writing it, used
  `Username='admin'` instead of the seed's assumed `Id=1` after discovering the real row is `Id=2`.
- **A16** — logged, not fixed: discovered the existing Users admin table's action buttons
  (Edit/Reset Password/Disable) are entirely unwired decoration, and `ResetPassword` is a stub
  that always reports success. Matched that existing convention for the new "Reset PIN" button
  (real backend, `UsersController.ResetPin`) rather than fixing the pre-existing dead ones too.
- Full solution rebuild at the end: 0 errors, no new warnings.
- **Not done:** the permission-matrix admin UI half of F2 (deferred per the user, see above);
  A15's bigger snapshot-reconciliation problem (out of scope for F2 itself, needs its own pass).

### 2026-09-05 — F4

Claimed via cross-session message before starting (erp-d0 took F2 in the same exchange, since
Q2 had just been answered and F2 was otherwise unclaimed). Hit one real snag mid-flight: this
SkiaSharp version (4.151.2, much newer than expected) hard-errors on `SKPaint.FakeBoldText` —
deprecated in favor of `SKFont.Embolden`. Confirmed the replacement via a scratch reflection
probe rather than guessing, which briefly left the shared build red; erp-d0 flagged it (blocking
their own `dotnet ef migrations add` for F2's PinHash column) and I fixed + confirmed green
within a few minutes.

- **F4** — see backlog row above for the full shape of the change.
- Before calling it done, actually rendered a test receipt (mixed Arabic/English, matching what
  a real Lebanese/Arabic-market store would put in `StoreSettings`) to a PNG via a temporary
  debug hook + throwaway console harness, and looked at it — this is exactly the kind of thing
  "it compiles" doesn't tell you, and there's no live printer or display in this environment to
  verify any other way. Confirmed correct Arabic glyph joining, correct RTL right-alignment,
  working bold (via `Embolden`), clean separators, no clipping. Reverted the debug hook and
  deleted the harness afterward — nothing test-only shipped.
- Logged A14 (pre-existing, separate duplicate order-based receipt text builder, also
  Arabic-incapable) rather than fixing it — genuinely out of scope, would touch a dozen-plus
  `OrdersController` actions.
- Full solution rebuild: 0 errors, 0 warnings (two transient failures during this session were
  build collisions with concurrent sessions' own `dotnet build` runs on the same `obj` folders,
  not code problems - resolved on retry both times).

### 2026-09-05 — A3

Picked A3 after coordinating over cross-session messaging with erp-75/erp-d0/erp-4a (asked what
each was on before starting — erp-75 had just finished A12 live, mid-edit, right as I was reading
the same file; erp-d0 was mid-A1/A10 already done, then moved to A5→F2; erp-4a had finished
S2/S3/S4/S5/S6/S8/S12 earlier and was idle). Confirmed A3 free before starting, announced it to
all three, got no objections.

- **A3** — see backlog row above for full detail. Verified each "duplicate" by grep before
  touching anything (usage counts, fully-qualified references, `.csproj` project-reference graph)
  rather than trusting the backlog's one-line description — this paid off twice: `General.cs`
  turned out not to be a clean duplicate at all (the real extension methods both files' callers
  actually use live in a third, differently-named file, `Entensions.cs`), and `Infrastructure/Common/`
  turned out to be an entire orphaned second project, not just two stray files.
- Deleted `Core/Data/ErpDbContext.cs`, `Infrastructure/Common/Helpers/Mapper/EntityMapper.cs`,
  `Common/Helpers/General.cs`, and the orphaned `Infrastructure/Common/` project as a whole.
  Full rebuild after: 0 errors, same warning set as before.
- **Deliberately did not touch the Tax duplication** (`Tax` entity x2 / `TaxController` +
  `TaxesController`) after investigating far enough to find it's not a simple duplicate: two
  fully independent stacks that route-collide at `api/taxes` (case-insensitive routing), one of
  which (`TaxRate`, the one the actual live UI page depends on) isn't even registered in the EF
  model and would throw at runtime if its repository were ever hit. See the A3 backlog row for
  the full trace. This needs a human product decision (which shape/UI survives, whether the
  seeded `Sales.Tax` data matters) — flagging rather than guessing and breaking someone's page.
- **Note on process:** at the start of this session, `WORKLOG.md` still said A12 was `TODO` while
  the fix was already sitting in the working tree (erp-75 hadn't logged it yet) — a reminder that
  this file can lag the actual code by a few minutes when sessions are running in parallel; worth
  a quick `git diff`/cross-session ping before trusting a `TODO` at face value, not just before
  starting big items.

### 2026-09-05 — A5 (+ A13 found)

Fourth pick for this session (after A1/A10 earlier). Coordinated with erp-75/erp-4a/erp-94 over
cross-session messaging first — all three confirmed `DashboardService.cs` was untouched and free.

- **A5** — see backlog row above. Rewired `GetDashboardStatsAsync` to real data via the
  already-existing `ISalesReportService`/`IOrderService`, instead of adding new data-access code.
  Deliberately left `TotalCustomersChange`/`TotalProductsChange` at `0` rather than inventing a
  number — `Customer`/`Product` have no `CreatedAt` to compute a real period-over-period count
  from (checked: neither inherits `BaseEntity`), and adding one is a schema change out of scope
  for a "wire up existing mock data" fix.
- **A13** — logged, not fixed: found while reading the rest of the file that every other method
  in `DashboardService.cs` is still mocked in the same way A5 was. Left as a separate backlog item
  since fixing it is roughly the same size of work as A5 itself, not something to fold in silently.
- Rebuilt `Application.csproj`: 0 errors, same warning set.
- Peers agreed on a division of remaining work: erp-75 takes F4 (receipt pipeline), I'm taking F2
  (PIN auth) next now that Q2 is answered; erp-94 is on A3; erp-4a is on A6. A2/A4/A7-A9/A11
  still open.

### 2026-09-05 — A12

Claimed via cross-session message to the two peers active at the time (erp-d0, erp-4a) before
starting; both confirmed no collision (erp-4a also confirmed the A12/S2 root cause independently —
it built `AddItemToCartWithTrustedPriceAsync` for exactly this purpose but forgot to rewire
`AddItemToCartWithCurrencyAsync` to call it). A fourth session (erp-94) joined mid-edit and asked
about this file; replied with current status once A12 was logged.

- **A12** — see backlog row above. Extracted `UpdateCartItemQuantityInternalAsync` so
  `UpdateCartItemQuantityWithCurrencyAsync` can actually apply its computed converted price
  instead of discarding it, mirroring the `AddItemToCartWithTrustedPriceAsync` pattern already
  established for S2. Public `UpdateCartItemQuantityAsync`'s behavior is unchanged (still passes
  `trustedUnitPrice: null`).
- Full solution rebuild: 0 errors.
- **Now four sessions total have touched this repo today** (this one, erp-d0, erp-4a, erp-94).
  Worth the user knowing the scale of concurrent activity, not just reading it in a log line.

### 2026-09-05 — Final consolidated review of S1-S12, F3, A1, A10

At the user's request, independently re-verified every DONE item from all three parallel
sessions by reading the actual current code (not just trusting each session's own log entry) -
S1-S12, F3, A1, A10. Also did a full merged-tree build.

- **Confirmed correct as described:** S1, S3, S4, S5, S7, S8, S9, S10, S11, S12, F3, A1, A10.
  For S4/S5 specifically, traced through that `ExecuteUpdateAsync` (S5's atomic stock deduction)
  and the ambient transaction (S4) actually compose correctly - both go through the same scoped
  `ErpDbContext` instance, so the atomic UPDATE genuinely participates in and rolls back with the
  surrounding transaction. For S3, confirmed the one call site is inside a try/catch that already
  returns clean JSON, so the middleware chain (S10) is never even involved for that path.
- **Found one real regression in S2** (not caught by the S2 session's own review) and fixed it —
  see the S2 backlog row above. `AddItemToCartWithCurrencyAsync` silently lost its currency
  conversion because it routed through the now-repricing-from-DB public `AddItemToCartAsync`
  instead of the new trusted-price path. Verified via `git diff` that this was a genuine
  regression (the method was untouched by S2's diff) and that it's a live, reachable call site,
  not dead code - `CartsController`'s currency-aware add-to-cart endpoint.
- **Found and logged one pre-existing, unrelated bug** as a byproduct of chasing the above:
  `UpdateCartItemQuantityWithCurrencyAsync` computes a converted price and then never applies it.
  Confirmed via `git diff` this predates all three sessions - logged as A12 rather than fixed
  inline, since it wasn't introduced by anything under review here and the correct fix involves
  a small design choice.
- **Reconfirmed a gap the sessions had already disclosed themselves, not something newly found:**
  S6's checkout idempotency guard is in-process (`ConcurrentDictionary`, one instance per running
  app process). Now that Q1 is answered "maybe/treat as yes" for multi-till, this guard will not
  catch a duplicate checkout submitted to a *different* till/process sharing the same database -
  each process has its own empty dictionary. Both the S6 backlog row and F1's row already say
  this; repeating it here because a consolidated review is exactly the moment this kind of
  "correct for the scope it was built for, insufficient for where the project is now headed" gap
  needs to be said out loud rather than left implicit in a footnote.
- One minor, harmless discrepancy: S8's backlog description says "granted the new/reused view
  perms to Manager too," but the actual grant gives Manager both `_View` and `_Manage` for
  Discounts/PaymentMethods, not just `_View`. Not a security issue (Manager already holds broad
  `_Manage` elsewhere), just the log undersold what the code actually does.
- Full solution rebuild after the S2 fix: 0 errors.

### 2026-09-05 — Open Questions answered (Q1, Q2, Q3)

User answered all three open questions:

- **Q1** — "maybe": some clients could run multiple PCs against a shared DB. Treated as "yes" for
  design purposes (safer default). This rules out plain SQLite for F1 (no real concurrent-writer
  story over a network share) — retargeted F1 at **PostgreSQL**. Also flags S6's in-process
  `ConcurrentDictionary`-based checkout idempotency guard as insufficient for a real multi-till
  deployment; it would need to move to the database.
- **Q2** — PIN auth for **all** roles, including SuperAdmin/settings/reports. F2 simplifies to a
  single PIN-based login path, no separate password flow to design/maintain.
- **Q3** — all existing data is test data, nothing live to migrate. No migration strategy needed
  for any schema or provider change, including F1's provider swap.
- Updated the Open Questions table and the F1/F2 backlog rows accordingly. F1 moves from
  BLOCKED (Q1) to TODO, but now needs replanning around PostgreSQL instead of SQLite before
  work starts.

### 2026-09-05 — S7

Coordinated with the third session (A1/A10) over cross-session messaging before starting, so it
knew to avoid `Web/Program.cs`/`Web/appsettings.json`; it picked A1/A10 instead and both logged
above once done.

- **S7** — see backlog row above for the full explanation. Short version: removed the committed
  secret and both hardcoded fallbacks, made both `Program.cs` and `JwtService` fail fast instead
  of silently signing with a known key, and set a fresh secret via `dotnet user-secrets` so this
  checkout keeps running locally without the old value anywhere.
- Two `dotnet build` runs collided transiently with the third session's own concurrent rebuild
  (`CS2012` file-in-use on `Infrastructure.dll`) — not a code problem, resolved on retry.
- Full solution rebuild after: 0 errors, same pre-existing warning set.
- **Not done:** F2, F4, F1 (blocked on Q1), and the remaining P2 items (A2-A9, A11) — all still
  open, none claimed by any session as of this entry.

### 2026-09-05 — Third parallel session: A1, A10

Joined as a third session alongside the S1/S9/S10/S11/F3 session and the S2/S3/S4/S5/S6/S8/S12
session (both already wrapped up by the time this session started — coordinated directly with
both over cross-session messaging rather than guessing from `git status`, since this repo isn't
using worktrees and all three sessions share one working copy). Picked P2 architecture items
confirmed by both peers to be untouched: `BaseRepository.cs`, `BackupSchedulingService.cs`.

- **A1** — see backlog row above. Deliberately scoped narrow: only deleted the try/catch/rethrow
  boilerplate, didn't touch the 280 call sites that catch it, and didn't add new exception types
  or retry logic — that's real design work (e.g. what should a unique-constraint violation surface
  as to the UI) better done as its own follow-up once something actually needs to catch a specific
  EF exception type.
- **A10** — see backlog row above. The method was already defensively coded (top-level try/catch/
  finally around almost everything); the gap was narrow — one `await` before the try — and the fix
  is equally narrow.
- Full solution rebuild after both changes: 0 errors, same pre-existing warning set as before.
- **Not done:** everything else in P2/P3 — A8 (`.gitignore`/11,918 tracked build files) is a
  reasonable next pick but is a big, visible diff (untracking thousands of files) worth flagging
  to the user before doing, rather than a quiet background change like A1/A10.

### 2026-09-05 — S2, S4, S6 (continuation of the S3/S5/S8/S12 session)

Continuation of the parallel session below, after the S1/S9/S10/S11/F3 session finished and
confirmed via timestamps (`Web/Program.cs` last written ~15 min prior, untouched since) that
it was safe to proceed into the checkout-flow items I'd deferred for explicit scoping.

- **S2** — traced the vulnerability to its actual root: `CartService.AddItemToCartAsync`
  persisted whatever price the caller passed, and three different call sites
  (`POSCheckoutService`'s direct-sale path, `POSController.HoldCart`, `CartsController.AddItem`)
  all forwarded client-supplied prices into it. Fixed at that single choke point instead of at
  each call site — it now always reprices from `Product.SellingPrice`, with a private
  `AddItemToCartWithTrustedPriceAsync` preserving `AddItemToCartWithCurrencyAsync`'s legitimate
  server-computed currency conversion. Bounded `POSCheckoutService`'s total-amount override to
  the greater of $1 or 2% of the recalculated total, rejecting anything past that.
- **S4** — wrapped `POSCheckoutService.ProcessCheckoutAsync` in a single
  `_dbContext.Database.BeginTransactionAsync()`. Verified first that every service touched in
  that method resolves the same scoped `ErpDbContext` (only `CacheService` uses a separate
  `IDbContextFactory` instance anywhere in the codebase, and it's not in this call path), so one
  ambient transaction actually covers all of it. Commits after the order/receipt/stock work,
  before the unrelated printer I/O. Receipt-creation failure still doesn't roll back the sale -
  kept that pre-existing behavior on purpose.
- **S6** — added a singleton `ICheckoutIdempotencyGuard` (`ConcurrentDictionary`-based atomic
  claim, 30s window) keyed on `CartId` when present, otherwise a signature of the checkout
  content. Rejects a duplicate submission outright; releases the claim on failure so a real
  retry isn't penalized. Flagged as in-process/single-instance by design — fine for the stated
  single-store deployment, but would need to move to the DB if Q1 ever comes back "yes, multiple
  tills share one database."
- Rebuilt the merged tree (both sessions' full set of changes together): 0 errors.
- **Still open:** S7 (JWT secret) — the other session flagged this as a deployment-process
  decision (user-secrets vs. env var vs. vault) rather than a pure code change, so left for the
  user to weigh in on directly.

### 2026-09-05 — S1, S9, S10, S11, F3

Ran in parallel with the S3/S5/S8/S12 session below — stayed on `Web/Program.cs`,
`Web/Middlewares/`, `Application/Services/Users/UserService.cs`, and the thermal-printer/F3
files, none of which that session's own note said it touched.

- **S1** — `ValidatePasswordAsync` now does a real `BCrypt.Verify`. Confirmed no plaintext rows
  could exist (the only creation path already hashed).
- **S9** — registered `AddInMemoryRateLimiting()` + `app.UseIpRateLimiting()`, so the
  already-configured `IpRateLimiting` appsettings section actually does something.
- **S10** — deleted the dead `GlobalExceptionMiddleware` (wrong response shape, never
  registered anyway); hardened and registered `ExceptionHandlingMiddleware` as the first
  middleware in the pipeline. AJAX/JSON gets `{success:false, message}`, message text only in
  Development; normal page requests rethrow to the existing dev/prod exception handling.
- **S11** — `builder.Host.UseSerilog(...)`: console + rolling daily file in `Web/logs/`.
- **F3** — see backlog row above for the shape of the change.
- Tried to seed a `Settings.Manage` DB `Permission`/`RolePermission` row (mirroring the pattern
  I'd assumed the app used) so the pre-existing `[RequirePermission("Settings.Manage")]` on
  `SettingsController` — which gates the new printer picker — would actually resolve for
  SuperAdmin. Got blocked generating the EF migration by a file lock from Rider's debugger;
  while waiting, the parallel session's S12 fix landed and revealed the DB `Permission`/
  `RolePermission` tables are dead — never read at runtime. Reverted the `ErpDbContext.cs`
  seed addition (no migration had been generated, nothing to clean up). `Permissions.All`
  (from the S12 fix) already covers `Settings.Manage` for SuperAdmin, so no further action
  was needed on my end.
- Rebuilt against the merged tree (both sessions' changes together): 0 errors.
- **Not done:** S2, S4, S6, S7 — left for explicit discussion (S2/S4/S6 are money/inventory
  correctness with real design choices; S7 needs a decision on secret storage e.g.
  user-secrets vs. environment variable vs. a vault, which is more a deployment-process
  question than a code change).

### 2026-09-05 — Parallel session: S3, S5, S8, S12

Ran in parallel with a session doing S1/S9/S10/S11/F3 — picked items touching none of
`Web/Program.cs`, `Web/Middlewares/`, `Application/Services/Users/UserService.cs`, or the
thermal-printer/F3 files, confirmed via `git diff --stat` before and after that those files
were untouched by this session.

- **S3** — `POSController.GetCurrentUserId()` now throws instead of defaulting to user 1.
- **S5** — atomic `TryDeductStockAsync` (EF Core `ExecuteUpdateAsync`) closes the stock race;
  `OrderService` aborts the order on insufficient stock instead of logging and continuing.
  Left a note under S4 that a multi-item cart can still partially deduct before a later item
  fails, since checkout still has no wrapping transaction (S4, untouched).
- **S8** — added `[RequirePermission]` to the 7 open controllers (`AppSettings`, `Carts`,
  `CodeLists`, `Discounts`, `ExchangeRates`, `PaymentMethods`, `Taxes`).
- **S12** — the real bug was worse than the backlog description: live authorization never
  reads the DB `Permission`/`RolePermission` tables (`AuthService.HasPermissionAsync` /
  `GetUserPermissionsAsync` are dead code — grepped, zero callers). Claims are built at login
  from the static `PermissionHelper.RolePermissions` dict, which had no `"SuperAdmin"` entry,
  so the seeded SuperAdmin user got **zero** permission claims and was locked out of every
  `[RequirePermission]` screen — a more severe version of the symptom S12 described. Fixed by
  adding `Roles.SuperAdmin` mapped to `Permissions.All` (reflection over every permission
  const, so it can't drift out of sync again). Left the dead DB permission tables alone —
  fixing inert seed data wouldn't have changed runtime behavior; that's F2 scope if the
  DB-driven `AuthService` path is ever wired up for real.
- Solution builds clean (0 errors, only pre-existing warnings).
- **Not done, deliberately deferred:** S2/S4/S6 (client-set pricing, checkout transactions,
  checkout idempotency) — all three are entangled in `POSCheckoutService.cs`/`OrderService.cs`,
  touch money/inventory correctness, and involve real design choices (e.g. how far a
  transaction should span across `OrderService`/`ReceiptService`/`CashDrawerService`, whether
  idempotency keys off cart state or a client-supplied key). Recommend planning those
  explicitly with the user rather than implementing silently.

### 2026-09-05 — Initial review

- Full read-through of the solution (~250 backend files, 45 controllers, 20k lines of Razor).
- Produced the P0/P2 findings above. **No code changed yet.**
- Scoped the four requested features (F1-F4); F1 blocked on Q1.
- Agreed direction on React: POS screen only, incremental (FE1).
- Created this file.
- **Correction to note:** an earlier claim that 11 controllers lacked `[Authorize]` was too broad —
  most carry `[RequirePermission]`, which does its own auth check. The real unprotected count is 7 (S8).
