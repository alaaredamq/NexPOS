# NexPOS → React migration plan

> Written for: Claude Code sessions (and the one human) working on this repo in parallel.
> Each `##` section under **Phase 0**, **Tracks** and **Cutover** is self-contained: point a session at one
> section ID (e.g. "do track S2") and it has everything it needs. Read sections 1–4 first, once.

---

## 1. Goal

Replace the server-rendered UI (81 Razor views, 35 MVC controllers, 26 jQuery/Metronic page scripts, ~10k lines of JS)
with a single-page **React + TypeScript** app, modelled on BoussiCell (`C:\Users\Alaar\Source\repos\BoussiCell\client`),
talking to a JSON API over the **same** services, database, licensing and Electron shell.

**Why:** BoussiCell feels faster. Cause: one page load, then JSON only — no full-page navigation, no
Bootstrap/Metronic/jQuery weight, no server HTML rendering per click.

**Non-goals:** no database/schema change, no change to business rules, no new features, no change to licensing,
no change to the installer product. Behaviour parity first; improvements go on a later list (`## 11`).

## 2. Locked decisions

Change one only by editing this table and saying why in the status log.

| # | Decision | Why |
|---|----------|-----|
| D1 | Client lives in `ERP/client/` (single Vite app, not a workspace). | One app, one consumer. BoussiCell's `packages/shared` split isn't needed. |
| D2 | React 19, TypeScript (strict), Vite, `react-router-dom` 7, `zustand` (UI + cart state), **TanStack Query** (server state), `react-hook-form` + `zod` (forms), `react-i18next`, CSS Modules. | Same stack as BoussiCell, plus Query because ERP has ~25 list/detail screens that all need cache + invalidation. |
| D3 | **Same origin, cookie auth.** The SPA is served by the .NET host from `Web/wwwroot/app/`. No JWT in the browser. | Electron already loads `http://localhost:5000`. HttpOnly cookie, no token storage, existing role/permission refresh (`OnValidatePrincipal`) keeps working. |
| D4 | New JSON controllers under `Web/Api/<Module>/`, route `api/v1/<module>`. Existing MVC controllers are **not** edited to serve JSON; they are deleted at cutover. | Existing controllers mix views and ad-hoc `Json(new {success})`. A clean API is the contract; strangler-style removal. |
| D5 | API controllers stay thin and follow `Web/Requirements/rules.md`: **controller → service → repository, never `DbContext` in a controller.** Business logic stays in `Application/Services` and is shared with the old MVC controllers until removal. | Repo rule; also means old and new UI behave identically. |
| D6 | **Contract-first with generated types.** Swashbuckle (already referenced) emits `swagger.json`; `openapi-typescript` generates `client/src/api/schema.d.ts`. Backend and UI sessions of one module can work in parallel off the agreed DTOs. | The enabler for parallel work. |
| D7 | Strangler migration. SPA at **`/app/*`** while Razor pages live at their old URLs; both share the auth cookie. Migrated modules flip a one-line entry in `Web/Helpers/SpaMigration.cs` so the Razor sidebar links to `/app/...`. At cutover the SPA moves to `/`. | Ship and test module by module; `master` stays releasable. |
| D8 | Language is client state (`localStorage`) sent as `Accept-Language`; URLs have **no** `/{lang}/` prefix in the SPA. RTL is done with CSS logical properties + `<html dir>`. Translations are converted from the `.resx`/`locales/*.json` by a re-runnable script (0E), never retyped. | The old app keeps being edited during the migration, so the conversion must be repeatable. |
| D9 | Design tokens = BoussiCell's `tokens.css` verbatim at `client/src/styles/tokens.css`, plus NexPOS brand overrides (teal, not BoussiCell blue) in `client/src/styles/theme.css`, mirroring `Web/wwwroot/css/colors.css`. No per-page colours. | Visual parity with BoussiCell, one place to rebrand. |
| D10 | Money/stock/drawer/debt endpoints get xUnit tests **before** the UI is built. | These are the regressions that cost real money. |
| D11 | Receipts, PDFs and thermal printing stay **server-side** (`IThermalPrinterService`, `PrintDocument`). React only calls print/PDF endpoints. | No rewrite of the print path. |
| D12 | Route-level code splitting (`React.lazy`) is mandatory. Budgets: shell + login ≤ 250 KB gzip; POS route ≤ 350 KB gzip; route change < 100 ms on the counter PC. | The whole point is speed; budgets are checked in 0B and in Z. |

## 3. Target layout

```
ERP/
  client/                          ← NEW  (D1)
    src/
      app/            routes.tsx (auto-discovers modules/*/routes.tsx), providers, shell wiring   [Phase 0 owns]
      api/            http client, error envelope, schema.d.ts (generated), query keys            [Phase 0 owns]
      core/           auth/session store, permissions (<Can>), license state, i18n, idle timeout   [Phase 0 owns]
      ui/             design system: Button, Modal, ConfirmDialog, DataTable, Form fields, Select,
                      PinPad, StatTile, StatusBadge, PageHeader, Toast, Loader …                    [Phase 0 owns]
      shell/          rail, top bar, command palette, language toggle, drawer chip, license banner  [0D owns]
      styles/         tokens.css, global.css                                                         [Phase 0 owns]
      locales/{en,ar}/<namespace>.json                                                               [one file per module]
      modules/<module>/   pages, components, hooks, store, routes.tsx, nav.ts                       [that track owns]
    e2e/<module>.spec.ts                                                                             [that track owns]
  Web/
    Api/
      Infrastructure/     envelope, filters, CSRF header check, permission attribute, paging         [0A owns]
      <Module>/           controllers + Contracts/ (DTOs)                                            [that track owns]
    wwwroot/app/          ← Vite build output (gitignored)
    Helpers/SpaMigration.cs   controller → /app path (one line per migrated module)
  Tests/Api/<Module>/     API tests                                                                  [that track owns]
  docs/react-migration/notes/<ID>.md   per-track handoff notes
```

## 4. Working in parallel — rules of engagement

1. **Branches.** Integration branch **`react/main`** off `master` (git cannot hold both `react` and `react/<ID>`, so it is not just `react`). Every section works on `react/<ID>` in its **own git worktree**
   (`git worktree add ../ERP-<ID> -b react/<ID> react/main`) so sessions never share a working tree. Merge to `react/main`
   with a normal PR/merge, small and often. Rebase on `react/main` before merging.
2. **Park current work first.** *(Done 2026-10-31: the restyle/notifications work was committed on `master` as `35061e1`; `react/main` branches from it.)*
3. **Ownership = folders.** A section edits only the paths listed under its **Owns**. Everything in `client/src/{app,api,core,ui,shell,styles}`
   and `Web/Api/Infrastructure/` and `Program.cs` is **frozen after Gate G0**. If you need a change there: make the smallest
   possible PR to `react`, merge it immediately, add a line to the Core change log (`## 10`). Don't wait, don't bundle.
4. **Missing shared component?** Build it inside `modules/<yours>/components/` first. Promotion to `ui/` happens in Cutover.
5. **No shared-file edits for registration.** Routes and nav are auto-discovered (`import.meta.glob`); locales are one JSON per module;
   API controllers register by convention. The only shared one-liner is your entry in `SpaMigration.cs` (append alphabetically).
6. **Generated files are never hand-merged.** On conflict in `schema.d.ts`, take either side and run `npm run api:types`.
7. **Contract first.** A module's first commit is its DTOs (`Web/Api/<Module>/Contracts`) with Swagger annotations, merged to `react`,
   so UI and API halves can run at once. Don't change a merged DTO silently — additive changes freely, breaking ones need a note in the other half's notes file.
8. **Definition of done (every module)** — all must be ticked in the Status board:
   - [ ] Every old action has an API equivalent (or is consciously dropped, listed in notes).
   - [ ] Every old permission check is reproduced on the API (`[RequirePermission]`) **and** in the UI (`<Can>` / route guard). Permission matrix tested with a limited role.
   - [ ] EN + AR, LTR + RTL screenshot-checked at 1366×768 and a tablet width.
   - [ ] Keyboard/scan flows work where the old page had them.
   - [ ] Audit-trail entries still written for the same actions.
   - [ ] Licence-stopped and idle-timeout behaviour correct.
   - [ ] xUnit API tests for money/stock/drawer/debt paths (D10); Vitest for stores/pure logic; one Playwright happy-path spec.
   - [ ] `SpaMigration.cs` entry added; old Razor page still works untouched (deleted only in Cutover).
   - [ ] Notes file written: what was dropped, what's odd, what to watch.
9. **Verification recipe:** use the isolated demo-DB + Playwright runbook (`nexpos-ui-audit-runbook` memory). Do **not** test against `nexpos.db`.
10. **Bash/Windows:** Visual Studio's debugger locks `Web\bin`; build a copy in the scratchpad if it's running.

## 5. Dependency map

```
0B scaffold ─┬─► 0A API foundation ─────────┐
             ├─► 0C design system ──────────┼─► 0D shell ─► ═══ GATE G0 ═══► all tracks (parallel)
             ├─► 0E i18n pipeline ──────────┤
             └─► 0F serving + Electron ─────┘
                                                        P1 ─► P2 ∥ P3 ∥ P4
 Tracks (any order, all parallel after G0):  P*  C1 C2 C3  S1 S2 S3  I1 I2 I3  A1 A2 A3 A4
                                                        └──────────────► Z cutover (needs every track done)
```

Soft dependencies between tracks (read the other track's notes, don't wait unless blocked):
- S1 (Orders/Receipts) ← P4 produces the receipt data shape; agree on `ReceiptDto` in P1.
- S3 (Customers) debt dialog is reused by P4 (checkout "on account"); P4 imports it from `modules/customers` once S3 merges, otherwise P4 stubs it.
- A3 (Money config) feeds P1/P4 (currencies, payment methods, taxes, discounts) — P1 reads the existing services directly, no dependency on A3's UI.

---

## 6. Phase 0 — Foundation (≈1 session each, mostly parallel after 0B)

Gate **G0** = all of 0A–0F merged, and: log in as two roles in the SPA at `/app`, see a permission-filtered rail, switch EN/AR (RTL flips), open a placeholder page per module, sign out, idle timeout fires.

### 0B — Client scaffold  *(do first, short; unblocks everyone)*
**Owns:** `client/` (everything, initial commit), `.gitignore` additions.
- Vite + React 19 + TS strict + `oxlint` (same as BoussiCell) + Vitest + Testing Library + Playwright config.
- Folder skeleton from section 3, `app/routes.tsx` with `import.meta.glob('../modules/*/routes.tsx')`.
- npm scripts: `dev`, `build` (→ `Web/wwwroot/app`), `lint`, `test`, `e2e`, `api:types`.
- Vite dev server on `:5173` proxying `/api` → `http://localhost:5000`; `base: '/app/'`.
- Fonts: copy `Web/wwwroot/fonts/boussi/` (Public Sans, JetBrains Mono, Archivo) — self-hosted, no CDN.
- **Baseline measurements of the current app** (Playwright trace on the demo DB): POS first interactive, dashboard load, products list load, one in-app navigation. Save to `docs/react-migration/baseline.md`. This is the before/after for D12.
- Bundle-budget check script (fails build over D12 limits).
**Done when:** `npm run dev` shows an empty shell at `/app`; `npm run build` emits to `Web/wwwroot/app`.

### 0A — API foundation (backend)
**Owns:** `Web/Api/Infrastructure/**`, `Web/Api/Session/**`, `Web/Api/Auth/**`, `Tests/Api/Infrastructure/**`, the `Program.cs` edits listed below.
- Conventions (write them into `Web/Api/README.md`): route `api/v1/<module>`; success returns the DTO directly (no `{success:true}` wrapper); errors are RFC 7807 `ProblemDetails` + `code` (stable machine string, e.g. `license_sales_stopped`, `drawer_not_open`) + `errors{field:[msg]}` for validation; lists return `{items,total,page,pageSize}` and accept `page,pageSize,sort,dir,q`.
- **Auth for APIs**: cookie challenge on `/api/*` must return **401/403 JSON, not a redirect** (`OnRedirectToLogin`/`OnRedirectToAccessDenied`). Check that `RequirePermissionAttribute` / `RequireRoleAttribute` also answer JSON for `/api/*` (they currently target MVC) — add an API-aware path rather than a copy.
- **CSRF**: cookie is `SameSite=Lax`; additionally require header `X-NexPOS-Client: web` on every non-GET `/api/*` (custom header ⇒ cross-site forms can't send it). Tested. Antiforgery tokens are *not* used for the API.
- Culture: honour `Accept-Language` for API requests inside `LanguageMiddleware` so server messages (validation, errors) come back in the user's language.
- Make `LicenseEnforcementMiddleware`, `AuditRequestMiddleware` and `ExceptionHandlingMiddleware` behave correctly for `/api/*` (JSON error with `code`; audit row per request already works — verify action/controller naming is readable).
- `POST api/v1/auth/login` (user + PIN/password exactly as `AccountController` does today, incl. rate-limit and lockout), `POST auth/logout`, `GET api/v1/session` → bootstrap payload: user, role, **permissions[]**, nav pages (from `NavCatalog`, already permission-filtered, with SPA path from `SpaMigration`), licence state, branding (`BrandingService`), language, drawer status, unread notification count. One round trip = the whole shell.
- Swagger: publish `swagger.json`, tag per module, XML comments on; add a script that dumps it without starting the UI so `npm run api:types` is deterministic.
- Export permission keys (`PermissionHelper.Permissions`) into the bootstrap payload and a generated `permissions.ts`.
- `Web/Helpers/SpaMigration.cs` + `NavCatalog` link rewrite.
**Done when:** curl with and without cookie/permission gives the right 401/403/200 JSON; Swagger lists auth + session; tests green.

### 0C — Design system port
**Owns:** `client/src/ui/**`, `client/src/styles/**`.
- Copy `tokens.css` and `global.css` from BoussiCell; keep names identical so later restyles port 1:1.
- Port from `BoussiCell/client/packages/shared/src/components/common`: `Modal`, `ConfirmDialog`, `PinPad`, `ManagerPinModal`, `StatTile`, `StatusBadge`, `PageHeader`, `Loader`, `ScanInput`, `ChipGroup`, `IconButton`, `ContextMenu`, `Stepper`, `Icon`.
- Build what ERP needs that BoussiCell lacks (replacing the Razor ViewComponents `DataTable`, `Form`, `Modal`, `Pagination`, `Search`, `Select2`): **DataTable** (server paging/sort, column chooser optional, row actions, empty/loading states, virtualisation hook), **Pagination**, **SearchBox** (debounced), **Select/Combobox** (async options — replaces select2), **form fields** wired to react-hook-form (text, number, money, select, switch, date, textarea) with inline server-validation errors, **Toast** (replaces toastr, ~163 call sites), **Confirm** (replaces SweetAlert, ~66 call sites), **DateRange**, **Money/Qty formatters** (use shared helpers, respect Arabic digits setting if the old app has one — check `localization.js`).
- Everything uses logical CSS properties (`margin-inline-start`, …) so RTL needs no override sheet. Touch targets ≥ 44 px (POS is a touchscreen).
- A `/app/_kit` dev-only page showing every component in EN/AR and light/dark-rail. Vitest for behaviour; no snapshot tests of styles.
**Done when:** `_kit` renders in RTL without a single physical-direction property (add a lint/grep check for `margin-left|padding-right|text-align:\s*(left|right)|float:`).

### 0E — i18n pipeline
**Owns:** `client/src/core/i18n/**`, `client/scripts/export-locales.*`, `client/src/locales/**` (initial seed).
- Re-runnable script: reads `Web/Resources/SharedResource.{en,ar}.resx` (2.4k lines), `Web/Resources/Views/**.resx`, `Web/wwwroot/js/locales/{en,ar}.json` → `client/src/locales/{en,ar}/<namespace>.json`. Namespace = the view folder (`pos`, `orders`, `products`, …) + `common`. Keys preserved verbatim; report keys missing in one language.
- `react-i18next` setup, lazy-load a namespace with its route, language toggle writes `localStorage['nexpos.lang']`, sets `<html lang dir>`.
- Pluralisation and Arabic number formatting decision: mirror what the current app does (check `localization.js`); document it.
- Lint step: fail if `t('x')` key is missing from `en`.
**Done when:** a namespace added to `.resx` shows up after `npm run i18n:export`; AR/EN switch is instant and persists.

### 0F — Serving, build and Electron integration
**Owns:** `Web/Program.cs` (static + fallback lines only), `build-electron.ps1`, `electron-main.js` (only if needed), `Web.csproj` (build target), docs `BUILD_NEW_INSTALLER.md` addendum.
- Serve `Web/wwwroot/app/**` with hashed-asset immutable caching (the existing static-file block already does this for `?v=` — extend to `/app/assets/*`); `MapFallbackToFile("/app/{*path}", "app/index.html")` that excludes `/api`, `/swagger`, files with extensions.
- `Web.csproj`: optional MSBuild target that runs `npm ci && npm run build` for Release (skippable with a property for fast C# builds).
- `build-electron.ps1`: build client before `dotnet publish`; verify `publish/app/wwwroot/app/index.html` exists in the artefact.
- Electron: nothing changes while Razor exists (`APP_URL` stays `http://localhost:5000`); at Cutover the home redirect points to `/app`. Check `electron-preload.js` / `nexprint.js` for APIs the SPA must call (silent print, zoom) and expose them as typed `window.nexpos.*` in `client/src/core/electron.ts` with a browser no-op fallback. Preserve the POS zoom behaviour (see memory `pos-styles-and-electron-zoom`).
**Done when:** a Release publish contains the SPA, the Electron shell opens `/app` and loads it, dev proxy and prod serving both work.

### 0D — App shell  *(after 0A + 0C)*
**Owns:** `client/src/shell/**`, `client/src/app/**`, `client/src/core/{auth,permissions,license,idle}/**`.
- `GET /api/v1/session` on boot → Zustand session store; unauth → login page (PIN pad, design from `Login.cshtml` + `_PinPad`), 401 anywhere → back to login preserving the target route.
- Rail (collapsible, `nexpos.rail` localStorage key kept compatible), top bar, **EN|AR toggle**, **drawer chip** (poll 60 s + on focus), notification bell with unread count, user menu, licence banner, **command palette (Ctrl+K)** from the nav catalog, route guards + `<Can permission>`/`useCan()`, idle-timeout lock, error boundary, 403/404 pages, licence-stopped page.
- Module placeholder page per nav entry so the rail is complete before modules land. When a module registers its real route, it wins (placeholder has lowest priority).
- Unmigrated nav entries are plain anchors to the Razor URL (full navigation, same cookie).
**Done when:** Gate G0 checklist (top of this section).

---

## 7. Tracks (all parallel after G0)

Format per track: **Scope** (old source to read) · **API** · **UI** · **Owns** · **Notes/traps** · **Size** (S ≈ ½ session, M ≈ 1, L ≈ 2).
Every track also does the definition of done in section 4.8. Namespaces below are the locale/route/module names.

### P — Point of sale  *(the heart; do not let one session do all of it)*

Old sources: `Web/Controllers/POSController.cs` (1293 lines, 31 actions), `CartsController.cs`, `MainScreenProductsController.cs`,
`Web/wwwroot/js/Pages/POS/{POS.js (3376!), CartItem.js, PricePicker.js, UnitConverter.js}`, `Views/POS/Index.cshtml`,
`Views/Shared/Components/MainProductsItem*`, `Web/Components/POS`, `wwwroot/css/pages/pos.css`, `Application/Services/Sales/POSCheckoutService`,
`CheckoutIdempotencyGuard`, `Requirements/POS.md`. Owns folder `client/src/modules/pos/` split as below. POS is the only module that's a full-screen layout (no rail; drawer sidebar as today).

#### P1 — POS API + contract  *(first 1–2 hours unblock P2–P4)*  · Size L
**Owns:** `Web/Api/Pos/**`, `Web/Api/Carts/**`, `Tests/Api/Pos/**`.
- Day-one deliverable: the DTO set (product tile, search result, cart, cart line with unit/price tier/discount/tax, payment, checkout request/response, receipt) merged to `react`.
- Endpoints: catalog (main-screen products, by category, search incl. barcode/SKU, fast product search service), price tiers/units for a product, tax + discount resolution, held/parked carts (create, list, resume, delete — `Carts*`), customer lookup, currencies + exchange rates, payment methods, **checkout** (idempotency key preserved), drawer guard (`CashDrawer` open check), print receipt / PDF, last-sale lookup, reprint.
- Tests (D10): checkout totals with tax/discount/tier/unit conversion/multi-currency rounding; idempotent replay; checkout with drawer closed; stock decrement; debt/on-account sale.
- **Trap:** compute totals on the **server** and return them; the client shows what the server says and may preview locally, but money is never decided in the browser alone.

#### P2 — POS screen, catalog, search, scan  · Size L
**Owns:** `client/src/modules/pos/{PosPage.tsx, layout/, catalog/, search/, scan/}` · locale `pos`.
- Screen layout with named slots for cart and checkout (P3/P4 plug into them), category chips, **picture-free compact keycap tiles tinted per category** (`CategoryPalette` hash → 1 of 7 hues; same hue as the dashboard donut — port that function), product search with debounce, barcode scanner input (rapid keystroke detection like POS.js), keyboard shortcuts, virtualised grid for large catalogs (TanStack Virtual), RTL, touch-first.
- Fullscreen/zoom behaviour for Electron (see memory `pos-styles-and-electron-zoom`: 1500 px / 0.8 zoom, drawer sidebar).
- Define and commit `modules/pos/slots.ts` (cart/checkout slot contract) in the first 30 minutes.

#### P3 — Cart, pricing, units, discounts  · Size L
**Owns:** `client/src/modules/pos/{cart/, store/cartStore.ts}`.
- `cartStore` (Zustand): lines, qty, unit change (`UnitConverter`), price-tier picker (`PricePicker`), per-line and whole-cart discounts, tax display, currency toggle, hold/resume, undo last line. Public interface (`addProduct`, `setQty`, `setUnit`, `applyDiscount`, `clear`, `totals`) committed as a stub **first** so P4 can compile against it.
- Totals shown = server-validated totals from P1 (debounced "price" call), never silently different from checkout.
- Vitest: line math, unit conversion, rounding parity with the C# service (golden-file cases shared with P1's tests).

#### P4 — Checkout, payments, post-sale  · Size L
**Owns:** `client/src/modules/pos/{checkout/, receipt/, customer/}`.
- Payment dialog (multi-tender, change, multi-currency), customer attach / quick-add, on-account (debt) sale with credit-limit rules as today, manager-PIN overrides (`ManagerPinModal`), checkout with idempotency key, receipt print/PDF/reprint calls (D11), "sales stopped" licence screen, drawer-closed prompt that links to open drawer, success/failed states that never lose the cart on a network error.
- Reuses `modules/customers` debt UI once S3 lands (stub until then).

### C — Catalog

#### C1 — Products · Size L
Old: `ProductsController.cs` (755 lines, 14 actions), `Views/Products/{Index,Create,Edit,Details,_PriceTiersEditor}`, `js/Pages/Products/Index.js`, `js/sticker-label.js` (439), `Views/Shared/_StickerLabelScripts`, `css/pages/products.css`, `ProductImportExportService`, `ProductService`, `ProductPriceService`.
- API: list/search/filter, CRUD, units, price tiers, barcode/SKU uniqueness, image upload/serve, import (CSV/XLSX) with preview + errors, export, sticker/barcode labels data.
- UI: list (DataTable, filters, bulk actions), create/edit (react-hook-form, price-tier editor, unit table), details, import wizard, label printing (port `sticker-label.js` to a component; keep print path identical).
- Trap: product images — the POS no longer shows them, but the product forms still do.

#### C2 — Categories + display order + main-screen products · Size M
Old: `CategoriesController`, `ProductDisplayOrderController`, `MainScreenProductsController`, `Views/Categories/*`, `Views/ProductDisplayOrder/Index`, `js/Pages/Categories/Index.js`, `ProductDisplayOrderService`, `MainScreenProductService`.
- Category CRUD/details; drag-reorder of product display order (use a maintained DnD lib, keyboard-accessible); main-screen product pinning. Categories have **no stored colour** — keep it that way.

#### C3 — Inventory + Suppliers · Size M–L
Old: `InventoryController` (388), `SuppliersController` (377), `Views/Inventory/{Index,AdjustStock,LowStockReport,StockValueReport}`, `Views/Suppliers/*`, `js/Pages/Inventory/Index.js`, `js/Pages/Suppliers/Index.js`, `SupplierDebtService`, `StockAlertService`.
- Stock list, adjust stock (reason, audit), low-stock and stock-value reports (+export), supplier CRUD, supplier debt ledger (payments/charges).
- Tests (D10): stock adjustment, supplier debt balance.

### S — Sales back-office

#### S1 — Orders + Receipts · Size L
Old: `OrdersController` (1011 lines, 25 actions), `ReceiptsController` (494), `Views/Orders/{Index,Details,EditStatus,Receipt}`, `Views/Receipts/{Index,Details}`, `js/Pages/Orders/Index.js`, `js/Pages/Receipts/Index.js`, `css/receipts.css`, `ReceiptService`, `SaleReversalService`, `ExportService`.
- Orders list/filter/export, details, status edit, **returns/reversals** (`SaleReversalService`), receipt view/print/PDF (D11), receipts list/search/reprint.
- Tests (D10): reversal restores stock + drawer + debt correctly.
- **Trap:** `css/receipts.css` defines a global `.btn-primary` — don't port that.

#### S2 — Cash drawer · Size M
Old: `CashDrawerController` (365, 14 actions), `Views/CashDrawer/{Index,Open,Close,History,ZReport}`, `CashDrawerService`, `_DrawerStatus`.
- Open (opening float per currency), close (counted vs expected, variance), cash drops, history, **Z report** (print/PDF). Also publishes `useDrawerStatus()` consumed by the shell chip and POS — coordinate with 0D (it polls the same endpoint).
- Tests (D10): open/close totals, drops, variance, Z report numbers.

#### S3 — Customers + debt · Size M
Old: `CustomersController`, `Views/Customers/{Index,Create,Edit,Details}`, `js/Pages/Customers/{Index,DebtDialog}.js`, `CustomerService`, `CustomerDebtService`. (Customer search is lazy-loaded + paginated — keep that.)
- CRUD, details with purchase history, **debt ledger + payment dialog** (export it as `<CustomerDebtDialog>` for P4).
- Tests (D10): debt payment, balance, overpayment rules.

### I — Insights

#### I1 — Dashboard + Reports · Size M–L
Old: `DashboardController`, `ReportsController`, `Views/Dashboard/Index`, `Views/Reports/{Index,SalesReport}`, `js/Pages/Dashboard/Index.js` (4 Chart.js uses), `DashboardService`, `SalesReportService`, `ReportService`, `ExportService`.
- KPI tiles (`StatTile`), sales charts, **category donut using the shared `CategoryPalette` hue** (same function P2 ports — import from one place; whichever track lands first creates `client/src/modules/_shared/categoryPalette.ts`), sales report with filters + export (xlsx/pdf via existing service).
- Pick one light chart lib (recommend `uPlot` or `recharts`); no Chart.js + jQuery. Lazy-load it (D12). Use the `dataviz` skill.

#### I2 — Notifications + alerts · Size M
Old: `NotificationsController` (447, 22 actions), `AlertsController`, `Views/Notifications/Index`, `js/notifications.js` (253), `NotificationService`, `AlertService`, `StockAlertService`.
- Notification list/mark-read/archive, alert rules, bell badge feed (the shell's count comes from `/session`; live refresh via polling or SSE — decide in notes; no SignalR exists today).

#### I3 — Audit logs · Size S–M
Old: `AuditLogsController` (3 actions), `Views/AuditLogs/Index`, `js/Pages/AuditLogs/Index.js` (449), `AuditLogService`, `css/pages/auditlogs.css`.
- Server-paged table with filters (actor, action, outcome, severity, date), row detail drawer (JSON `details`), export. Very large tables → virtualise.

### A — Administration

#### A1 — Users, roles, account · Size L
Old: `UsersController` (581), `AccountController` (370), `Views/Users/Index`, `Views/Account/{Profile,ChangePin,ChangePassword,AccessDenied}`, `js/Pages/Users/Index.js`, `UserService`, `RoleAccessService`, `PermissionHelper`.
- Users CRUD, role assignment, **role editor with permission matrix** (the SPA's permission catalog comes from the same keys as 0A exports), per-user permission overrides if present, PIN/password change, profile.
- Tests (D10): permission changes take effect for signed-in users on next request (cookie revalidation path).
- Login/logout themselves belong to 0A/0D, not here.

#### A2 — Settings (store, branding, app settings, printers) · Size M
Old: `SettingsController` (418, 21 actions), `AppSettingsController`, `BrandingController`, `Views/Settings/{Index,StoreSettings,Branding,AppSettings}`, `StoreSettingsService`, `SystemSettingsService`, `BrandingService`, `ThermalPrinterService`.
- Store info, white-label logo/brand (the shell reads branding from `/session` — invalidate it on save), app settings, printer selection/test print, language defaults.

#### A3 — Money configuration · Size M
Old: `CurrencyController`, `ExchangeRatesController`, `TaxController`, `TaxesController`, `DiscountsController`, `PaymentMethodsController`, `CodeListsController`, `Views/{Currency,Tax}/Index`, `js/Pages/Currency/Index.js`, services: `CurrencyService`, `ExchangeRateService`, `TaxService`(×2 exist — see Core), `DiscountService`, `PaymentMethodService`, `CodeListService`.
- Currencies + rates, taxes, discounts, payment methods, code lists. Many are `[ApiController]` already — reuse their service calls; the existing JSON shapes are *not* the contract (D4).
- Tests (D10): rate conversion rounding.

#### A4 — Backup + licence · Size M
Old: `BackupController` (673, 37 actions), `LicenseController`, `Views/Backup/Index`, `Views/License/{Index,SalesStopped}`, `js/Pages/Backup/Index.js` (864), `BackupService`, `BackupSchedulingService`, `LicenseService`, `Common/Licensing`, `LICENSING.md`.
- Backup list/create/restore/download/schedule/retention, licence view/activate/renew. Restore restarts or locks the DB — keep the UI's confirmation + progress semantics identical.
- **Trap:** licence screens must stay reachable when sales are stopped (middleware lets them through today; 0A keeps that for the API).

---

## 8. Cutover (Z) — only after every track is Done

**Owns:** everything not owned elsewhere; one session, ordered checklist.
1. Full regression: Playwright smoke of every module in EN/AR as Admin and as a limited role; permission-matrix sweep; licence-stopped; idle timeout; backup/restore round trip on the demo DB.
2. Move SPA from `/app` to `/`; Razor `Home/Index` redirect removed; Electron `APP_URL` unchanged (still `localhost:5000`).
3. Delete: `Web/Views/**`, `Web/Components/**`, `Web/Resources/Views/**`, MVC controllers (keep API), `Web/wwwroot/{js,css,Metronics,lib}` and `libman.json`, `LanguageRouteHandler`, `ViewRenderService` (**verify first** whether it renders Razor for print/PDF — if yes, replace before deleting), session usage if only MVC used it. `AddControllersWithViews` → `AddControllers`. Remove the `{lang}` route and constraint.
4. Security tidy: drop CORS `AllowAnyOrigin` (same origin now); confirm CSRF header check is the only gate on mutating routes; run `/security-review`.
5. Promote any duplicated module components into `ui/`; dedupe `CategoryPalette`.
6. Budget report vs `baseline.md` (D12) into `docs/react-migration/result.md`: package size before/after, POS and navigation timings.
7. Docs: update `README.md`, `BUILD_NEW_INSTALLER.md`, `ELECTRON_SETUP_GUIDE.md`, `QUICK_START_ELECTRON.md`; build the installer and smoke-test it on a clean machine.

## 9. Cross-cutting checklists (apply in every section)

- **i18n:** no hard-coded strings; add keys to your module namespace; AR strings come from the existing `.resx` where they exist, otherwise add them to **both** languages in the same commit.
- **RTL:** logical properties only; icons that imply direction (arrows, chevrons) flip; numbers/currency render LTR inside RTL text (`<bdi>`/`dir="ltr"` spans).
- **Money:** one `formatMoney(amount, currency)` from `ui/`; never `toFixed` ad hoc. Decimal rounding mirrors the C# service.
- **Accessibility (WCAG 2.1, per `Web/Requirements/frontend.md`):** labels, focus rings, dialog focus-trap, `aria-live` for toasts, colour contrast against tokens.
- **Errors:** every mutation shows the server's localized `ProblemDetails` message; field errors land on the field.
- **Loading:** skeleton or inline loader per query; never a blank page; optimistic updates only for cart UX, never for money records.
- **Permissions:** hiding a button is not security; the API check is the real one (copy the old `[RequirePermission]` per action).
- **Audit:** keep the same `AuditActions` for the same business events.
- **Performance:** lazy route, no barrel imports of the whole `ui/`, no moment/lodash-whole.

## 10. Status board  *(each session edits only its own row; keep rows one line)*

| ID | Section | Depends on | Size | Status | Owner / branch | Notes file |
|----|---------|-----------|------|--------|----------------|-----------|
| 0B | Client scaffold + baseline | – | M | ☑ done | Claude · `react/0B` | [0B.md](docs/react-migration/notes/0B.md) |
| 0A | API foundation | 0B | L | ☑ done | Claude · `react/0A` | [0A.md](docs/react-migration/notes/0A.md) |
| 0C | Design system | 0B | L | ☑ done | Claude · `react/0C` | [0C.md](docs/react-migration/notes/0C.md) |
| 0E | i18n pipeline | 0B | M | ☑ done | Claude · `react/0E` | [0E.md](docs/react-migration/notes/0E.md) |
| 0F | Serving + build + Electron | 0B | M | ☑ done | Claude · `react/0F` | [0F.md](docs/react-migration/notes/0F.md) |
| 0D | App shell | 0A, 0C | L | ☑ done | Claude · `react/0D` | [0D.md](docs/react-migration/notes/0D.md) |
| **G0** | **Gate: foundation done** | 0A–0F | | ☑ 2026-10-31 | | |
| P1 | POS API + contract | G0 | L | ☑ done | Claude · `react/P1` | [P1.md](docs/react-migration/notes/P1.md) |
| P2 | POS screen / catalog / scan | G0, P1 DTOs | L | ☐ todo | | |
| P3 | POS cart / pricing | G0, P1 DTOs | L | ☐ todo | | |
| P4 | POS checkout / post-sale | G0, P1 DTOs, P3 stub | L | ☐ todo | | |
| C1 | Products | G0 | L | ☑ done | Claude · `react/C1` | [C1.md](docs/react-migration/notes/C1.md) |
| C2 | Categories / display order | G0 | M | ☑ done | Claude · `react/C2` | [C2.md](docs/react-migration/notes/C2.md) |
| C3 | Inventory + Suppliers | G0 | M–L | ◐ in progress | Claude · `react/C3` | |
| S1 | Orders + Receipts | G0 | L | ☐ todo | | |
| S2 | Cash drawer | G0 | M | ☐ todo | | |
| S3 | Customers + debt | G0 | M | ☐ todo | | |
| I1 | Dashboard + Reports | G0 | M–L | ☐ todo | | |
| I2 | Notifications + alerts | G0 | M | ☐ todo | | |
| I3 | Audit logs | G0 | S–M | ☐ todo | | |
| A1 | Users, roles, account | G0 | L | ☐ todo | | |
| A2 | Settings / branding / printers | G0 | M | ☐ todo | | |
| A3 | Money configuration | G0 | M | ☐ todo | | |
| A4 | Backup + licence | G0 | M | ☐ todo | | |
| Z | Cutover | everything | L | ☐ todo | | |

Status values: ☐ todo · ◐ in progress · ◑ in review · ☑ done (all of 4.8 ticked).

**Core change log** (post-G0 edits to frozen paths — one line each: date · ID · file · why):

- 2026-11-01 · C1 · `client/src/api/http.ts` · FormData bodies go out as multipart; added `http.download` / `http.downloadPost` (blob + file name) for CSV import/export and PDF labels. Additive.

## 11. Parking lot (explicitly *not* in scope — don't do during the migration)

- Offline/PWA mode, real-time push (SignalR), new reports, product-picture return to POS, dark theme, replacing SQLite.
- Anything discovered that deserves its own change goes here, one line, with the ID that found it.

## 12. Prompt template for a session

> Read `REACT_MIGRATION.md` sections 1–5 and section **\<ID\>**. Create worktree `../ERP-<ID>` on branch `react/<ID>` from `react/main`.
> Set my row on the status board to ◐ with this branch. Follow the repo rules in `Web/Requirements/rules.md`.
> Touch only the paths under **Owns**. Start by committing the DTO/contract (or the stub interface) so parallel sessions can rely on it.
> Finish the definition of done (4.8), write `docs/react-migration/notes/<ID>.md`, update my row to ◑, and tell me what to verify by hand.

## 13. Known unknowns to resolve early (cheap to check, expensive to discover late)

1. Does `ViewRenderService` render Razor to HTML for print/PDF? (Blocks deleting views in Z.) → check in 0A. **Resolved (0A): no - registered but never injected; safe to delete.**
2. What do `electron-preload.js` / `nexprint.js` expose (silent print, zoom)? → 0F.
3. ~~How does `localization.js` format numbers/dates for Arabic (Eastern digits?)~~ → **answered by 0E**: Western digits everywhere (`en-US` in the old JS; .NET never substitutes digits); no pluralisation. See `docs/react-migration/notes/0E.md`.
4. Two `TaxService`/`Tax` entities exist (`Core/Entities/Products/Tax.cs` and `Sales/Tax.cs`; `ITaxService` in both Products and Sales) — which is live? → A3, but P1 needs the answer on day one for pricing. **Answered by P1: neither affects the till; `POSCheckoutService` hard-codes `taxAmount = 0`. Which one A3 keeps is still open.**
5. Are there tests covering `POSCheckoutService` today? (`Tests/Services`) → P1 to extend, not rewrite. **Answered by P1: none (only `DiscountDisplayTests` touches the discount maths); `Tests/Api/Pos/` now covers checkout end to end.**
