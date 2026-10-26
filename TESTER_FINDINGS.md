# Tester Findings — 2026-10-26

Full-app smoke/functional test run by Claude while the owner was away, followed by a fix round.
(`WORKLOG.md` says to keep a single status doc; once the leftovers below are worked off, fold them into its
Backlog and delete this file.)

## How it was tested

- Built `Web` into a scratch folder and ran a **throwaway instance on :5099** with a scratch SQLite DB
  (`NEXPOS_DEMO=1`, `NEXPOS_SEED_TEST=1`). The live `Web.exe` on :5000 and real data were never touched.
- Playwright (headless Chromium) crawled ≈70 routes × `en`/`ar` (HTTP ≥ 400, JS errors, raw resource keys, overflow),
  drove real flows (sales, debt, void, drawer, CRUD, backup, import/export, settings, users/roles, PIN), sent tampered
  checkouts, drove the POS UI, and screenshotted EN/AR/tablet/phone.
- Server log and audit log were cross-checked after every run.
- **After each fix round: full rebuild, fresh DB, every suite re-run — plus (from round 4) a second crawl once the flows have created data, and a scan of every Arabic page's `<script>` for number-format bugs, because some bugs only render with real data. Unit tests 255 → 287 passing.**
- Not tested: real printer hardware, Electron shell, installer/updater (never run), restore-from-backup,
  multi-till concurrency, licensing activation happy path.

---

## FIXED (all uncommitted in the working tree)

### Round 1
| # | Problem | Fix |
|---|---|---|
| F1 | Arabic `Inventory/AdjustStock` JS was dead (`SyntaxError: Private field '#x66B'`): decimal stock written raw into JS, Arabic culture uses `٫`. | `AdjustStock.cshtml` — `InvariantCulture`. |
| F2 | Stock Value report threw "Canvas is already in use" — page's `initializeCharts` shadowed the one in `site.js`. | Renamed to `initStockValueCharts`. |
| F3 | Notifications "Recent activity" always 404 (called a non-existent action). | Uses `GetRecentNotifications`. |
| F4 | Arabic `Pin_UpdateYourLogin` contained a stray `Pin_SetNew`. | `SharedResource.ar.resx`. |
| F5 | Checkout accepted negative/zero quantity → a −$14 "sale". | `CartService` rejects qty ≤ 0 and weight/volume ≤ 0 (+5 tests). |
| F6 | Checkout with a non-existent customer leaked a raw EF error. | `POSCheckoutService` clear message. |
| F7 | Two products could share a barcode. | `ProductsController.Create/Edit` reject duplicates. |

### Round 2
| # | Was | What was done |
|---|---|---|
| F8 (O1) | Deleting a category / supplier / user with history showed raw EF errors. | Services turn the FK failure into *"…still used by products (incl. deleted ones kept for sales history)… Deactivate it instead."* Controllers pass it through. +3 tests (`DeleteGuardTests`). Unused items still delete. **Decision:** message + "deactivate" rather than soft-deleting categories/suppliers/users. |
| F9 (O2) | Warning that `AddSupplierDebtTracking`'s `PRAGMA foreign_keys=0` can't run in a transaction. | **Not a bug.** New `MigrationUpgradeTests` upgrades a *populated* DB across that migration: products, supplier link and FK integrity all survive. The migration was deliberately **not** edited (already applied on real DBs; splitting it would break them). The EF warning is cosmetic. |
| F10 (O3) | Stock Value charts built from empty arrays. | Controller computes top-8 categories / top-10 products by stock value; view serialises them. Verified both render with points (en + ar). |
| F11 (O4) | Server accepted `amountPaid` < total. | Rejected when a caller sends it (the POS UI never sends it, so the UI is unaffected; overpayment/change still fine). |
| F12 (O6) | `Alerts.Create/Update/Delete`, `Products.Read/Update` didn't exist → endpoints denied to everyone incl. admin. | Added `Alerts_Create/Update/Delete` (Admin gets all; Manager gets Update for acknowledge/resolve; SuperAdmin automatic). `MainScreenProducts` now uses `Products_View/Edit/Delete`. **Every** `RequirePermission("literal")` in the app replaced by a constant so a typo can no longer compile. |
| F13 | *New, found while verifying F12:* `PUT /api/Alerts/{id}/acknowledge` and `/resolve` trusted a client-supplied `userId` (500 on a bad one, and spoofable). | Always uses the signed-in user; body ignored/optional. |
| F14 (S1) | Debug endpoints in production controllers. | **Deleted** (no consumers anywhere): `Products/TestProducts`, Receipts `test-connection` (leaked stack traces), Backup `test-db`, anonymous `POS/PrintReceiptToUsbTest`. **Gated to `Settings_Manage`** (was `Orders_View`, i.e. cashiers): the 9 printer-diagnostic `Orders/Test*`-style POSTs. Backup page's own `TestConnections`/`DebugDirectories` left alone (the UI uses them). |
| F15 (O7) | "Notification Settings" link → 404. | Link removed (there are no notification settings to configure). |
| F16 (O8) | `POS/GetProducts` returned **15.4 MB** for 36 products. | `GetActiveProductsAsync` is now `AsNoTracking`, so EF no longer fills `Category.Products`/`Supplier.Products`: **132 KB (≈116× smaller)**, same JSON shape (still has supplier/category/currency), 69 ms. Both callers are read-only cache loaders. |
| F17 (O9) | POS unusable on phones (cart squeezed to 25 %). | Columns are `col-12 col-md-*` and the `<768px` CSS now stacks cart over catalog and lets the page scroll. Verified at 390/600/767 px, a full sale on a 390-px layout, and that 768/1024/1440 px are unchanged. |
| F18 (O11) | Dead `Home/Privacy` view. | Deleted. |
| F19 (I3) | 34 duplicate keys across the resx files (build warning MSB3568; first definition wins). | Removed the later copies (36 blocks). Verified every key resolves to **exactly** the value it had before; all files still parse; warnings gone. |
| F20 (L1) | 439 antiforgery "Cache-Control overridden" warnings per session. | That one logger category set to Error in Serilog config. |
| F21 (L3) | `DashboardService` compared local-midnight windows with UTC order dates (wrong 00:00–03:00 local). | Windows built in local time, boundaries converted to UTC. |
| F22 | *New:* if the dashboard threw, `DashboardController` showed **invented numbers** ($125,430.50 sales, "John Doe"…) — and the fake object lacked fields the view reads, so it would have crashed anyway; the exception wasn't logged. | Logs the error and shows empty DTOs (zeros). |

### Round 3 — "I had an issue deleting a category" (owner report)
Reproduced in the browser, with screenshots, before fixing.

| # | Cause | Fix |
|---|---|---|
| F23 | **The list pages never showed any result for a delete.** Categories, Customers, Suppliers and Products each had a copy-pasted `showAlert()` that injects its message after a `.products-header` element — **no page contains one**, so every success *and* error was silently dropped (a refused delete looked like "nothing happened"). | One shared `showAlert(type, message)` in `site.js`: success → toast, anything else → a dialog you must dismiss. The four dead copies removed (the Products one also overwrote the global). Verified: success toast and error dialog now appear for categories, customers and suppliers. |
| F24 | The Categories **list** confirm said *"Products in this category will be moved to 'Uncategorized'."* — no such behaviour exists; the delete is refused. (The Details page said "will need to be reassigned".) | Both pages (EN + AR) now say what really happens: a category that still has products can't be deleted — deactivate it. **Decision:** keep refusing rather than silently moving products into a catch-all category (there is no "Uncategorized" category today). |
| F25 | **A customer who owes money (or is owed money) could be deleted**, and because deleted customers vanish from all lists the debt vanished with them. | `CustomerService.DeleteAsync` refuses while `Balance != 0` ("outstanding balance of 30.00 — settle it or deactivate"). Customers with a settled balance still delete (soft delete) even if they have sales. +3 tests. |
| F26 | Same exposure for suppliers (balance owed to a supplier). | `SupplierService.DeleteAsync` refuses while `Balance != 0`; FK message widened to "products or debt records". +1 test. |

### Round 4 — owner decisions: S2, S3, L2, L4 and "fix the local-vs-UTC issue (also on the Backup page)"

| # | Was | What was done |
|---|---|---|
| F27 (S2) | `/License` was anonymous (any cashier, or anyone at the keyboard, could open it and see the Machine ID / paste a key). | **Admin only.** New `License.Manage` permission (Admin; SuperAdmin automatically). Controller is `[Authorize]`; `Activate` needs the permission; sidebar link and the license-banner button are hidden from everyone else. A cashier bounced off the POS by an expired license (`?from=pos`) now sees a short *"ask your administrator"* screen (EN + AR) instead of a dead-end AccessDenied. An unlicensed install only blocks *selling*; sign-in/settings/backups keep working, so an admin can always reach the page. Verified: anonymous → login, cashier → AccessDenied, admin → 200. |
| F28 (S3) | Creating a user asked for a password **and** a PIN, though sign-in is PIN-only. | PIN is the only credential. `IUserService.CreateWithPinAsync` stores the PIN hash, sets "must change at first sign-in", and gives the (still required) password column a random unknowable value, so the old `api/Auth/login` password endpoint can't be used for new accounts. Password fields removed from the form/JS/DTO. +3 tests. |
| F29 | *Found while doing S3:* the admin form accepted a **4–6 digit** PIN but sign-in accepts **exactly 4**, so a user created with a 5- or 6-digit PIN could never log in. | Create and Reset-PIN now require exactly 4 digits (server + form). |
| F30 (L2) | Startup/EF warnings. | **Zero warnings at startup now.** (a) `CustomerCart` and `CustomerDebtTransaction` got the matching `!Customer.IsDeleted` query filter. (b) The multiple-collection-`Include` warning was traced to its real queries (by temporarily making EF throw) and fixed with `AsSplitQuery()` on: the **login query** (`UserRepository.GetByUsernameAsync`), `ReceiptRepository.GetById/GetByReceiptNumber/GetByOrderId`, `CashDrawerSessionRepository.GetOpenSessionByUserId/GetSessionWithDetails`. (c) The two `First()`-without-`OrderBy` calls (store-settings row, currency fallback) are now ordered. The temporary diagnostic was removed. |
| F31 (L4) | Backup #1 listed with no name; `ValidateBackup` returned an almost empty `backupInfo`. | A blank name now defaults to `Backup_yyyyMMdd_HHmmss` (cause: an API call with no body); existing nameless rows are listed by file name; `ValidateBackup` fills name/type/created/encryption/tables from the catalog record. |
| F32 | **Local vs UTC** (see below). | Fixed across display, JSON, reports, pickers, backups. |

**F32 in detail.** Everything stored is UTC (correct, unchanged); people read and pick *local* time. The bug was at the edges:
- **Backup page / all JS:** timestamps came back as `2026-10-26T12:59:13` (no zone), which browsers read as *local*, so every `new Date(x).toLocaleString()` showed the UTC clock (2–3 h early). The Backup list showed `createdAt` wrong while `validate` (file time, local) showed it right — two sources disagreeing. Now a JSON converter writes stored timestamps with `Z` (`Core.Time.UtcDateTimeJsonConverter`), so the browser converts them. Local dates (report period) are marked `Kind=Local` where produced.
- **Server-rendered text:** receipts, orders, customers/suppliers/categories/products details, Z-report, dashboard, the DataTables rows built in controllers (Orders, Receipts, Users), thermal/ESC-POS receipt, receipt HTML, report documents, CSV export, backup history text — ~60 places now show local time via one helper, `Core.Time.AppTime.UtcToLocal()`. (A receipt made at 15:11 showed 13:11.)
- **Backup schedules:** NextRun/LastRun were stored as *local* clock values — the opposite of every other timestamp. They are now UTC; the schedule's day/time rules still run on the local clock ("02:00" local = `00:00Z` in UTC+2). Verified end to end.
- **Picked date ranges** (report period, sales trend / top products / category sales, dashboard cards, daily-sales buckets, receipt stats, receipts-by-range, cash-drawer history, orders-by-range) are interpreted as **local calendar days** and converted to UTC bounds (`AppTime.LocalDayRangeToUtc`); daily buckets group by local day, not UTC day. The dashboard period windows (month/quarter/year) and `Reports/GetDashboardStats` use local boundaries.
- **Date-picker defaults** used `new Date().toISOString().split('T')[0]` = the UTC date, so between 00:00 and the UTC offset "today" was yesterday (Receipts, Cash-drawer history, Currency). New `localDateString()` in `site.js`.
- Order numbers (`ORD-…-yyyymmdd`) now use the local date like receipt numbers.
- DST-safe and testable: `AppTime` takes an explicit `TimeZoneInfo` in tests (Beirut UTC+2/+3, including the skipped spring-forward hour). **+17 tests** (`AppTimeTests`, `UtcDateTimeJsonConverterTests`).
- **Lesson baked in:** an early version treated "exactly midnight" as a date and left it without a `Z`; a 02:00 backup schedule is exactly `00:00Z`, so it was shown 2 h wrong. Caught by the end-to-end check and removed; there is a regression test.

Also fixed during this round:
- **`/ar/CashDrawer/Close` JS was dead in Arabic** (`expectedCash: 22٫6` → `SyntaxError`), so an Arabic-language cashier **could not close the drawer**. Same class as F1; found only with real data. A scan of all 72 Arabic pages' `<script>` blocks now finds no Arabic number separators.
- Extension methods don't bind on `dynamic` (Dashboard's `ViewBag` orders) → the Dashboard threw 500 once orders existed; the empty-DB crawl didn't show it. Fixed (static call). A second, data-rich crawl is now part of the routine.

Unit tests are now **287** passing; the full browser/API suites are unchanged and green (the only "failures" are the known wrong-URL guesses and expected refusals).

**O5 — intentional, keep (owner):** "Sell anyway" books revenue for goods not on hand and floors stock at 0, because not
every shop tracks quantity.

---

## STILL OPEN

- **O10 (M) — Fresh installs ship dev/personal data. Needs your decision.**
  `HasData` seeds customer **"Alaa Reda / alaa@example.com"** (`ErpDbContext.cs:406`) and the default store settings are
  placeholders that print on receipts until edited ("Your Store Name", "123 Main Street", "(123) 456-7890").
  *Why not fixed:* removing the seed needs a new EF migration; the installed `dotnet ef` is v10 vs the project's EF 9
  (rewrites the snapshot/designer with another version), and the alternative — deleting a customer row from clients'
  databases — shouldn't happen silently. *Options:* (a) you delete the customer in the UI before building the
  installer DB; (b) I hand-write a guarded migration (`DELETE … WHERE Id=1 AND email='alaa@example.com' AND no orders/debt`)
  and verify it with an upgrade test like `MigrationUpgradeTests`; (c) a first-run "store setup" screen for the store name/
  address/phone. `AppSettings.SiteName="My ERP System"` is unused by any code — harmless.
- **O12 (L)** — duplicate page headers (e.g. *Update Order Status* ×3). Already in the design-review notes.
- **I1 (L)** — `CashDrawer/ZReport.cshtml` is entirely hard-coded English even though `ZReport.*.resx` exists (needs a
  proper pass, not one label). "No reason given" is *stored* in the DB by `SaleReversalService` (so it shows English on
  Arabic pages) — store empty and localise at display time. Default payment methods ("Cash"/"Card") are seeded English-only.
- **I2 (L)** — controller/service validation and error strings are hard-coded English (`"Product not found."`, the new
  "A product with this barcode already exists.", and the new delete-refusal messages, which therefore appear in English
  inside an Arabic UI). Move to `SharedResource`.
- **Product deletes (unverified in the UI):** `Products/Index` has the `deleteProduct` handler but my run found no delete
  button on that list for a test product; worth a manual look now that `showAlert` works there.
- *(S2, S3, L2, L4 and the local-vs-UTC item are done — see Round 4.)*
- **Existing backup schedules (upgrade note, local-vs-UTC):** schedules created *before* this change have `NextRun`/`LastRun`
  stored as local clock values; the new code reads them as UTC. The first run after upgrading can fire up to the UTC offset
  (2–3 h) late once, and the old `LastRun` displays shifted until that run rewrites both. Self-healing; to avoid even that,
  open and re-save each schedule (or tell me and I'll add a one-time migration of those two columns).
- **Legacy password sign-in endpoint** (`POST api/Auth/login`) still exists for accounts that already have a password. New
  accounts can't use it (random password). Since login is PIN-only you may want to remove the endpoint entirely.
- **Date-only values in JSON:** every stored timestamp now gets a `Z`; a *date* (birth date, exchange-rate valid-from) is
  sent the same way. In Lebanon's zone that still shows the right day; a browser **west** of UTC would show the previous
  day. If that ever matters, mark such values `Kind=Local` where produced (as the sales report does) or use `DateOnly`.
- `Application/Services/Reports/ReportService.cs` (UTC-day sales/profit buckets) is registered but **nothing calls it** — dead
  code; delete or convert if it's ever wired up.
- **Unverified:** the direct-items checkout path's 30 s duplicate guard would block two identical single-item API sales
  within 30 s (the POS UI uses the cart path, so it isn't affected).
- **Repo hygiene (not from this test):** untracked `tools/nexpos.db` and `tools/log-*.txt` look like a real database and logs —
  don't `git add` them (and consider `.gitignore`).

---

## Checked and fine

- Role boundaries: a Cashier is redirected to AccessDenied on Users, Backup, Settings, AuditLogs, Reports, Receipts,
  Inventory, Suppliers, Currency, Tax, CashDrawer and on Backup/Users/Void POSTs (denials audited).
- Checkout rejects tampered prices/totals (server reprices; ±max($1, 2 %)), empty carts, unknown products, bad payment
  methods, walk-in "Add to debt", garbage bodies.
- Customer debt maths (manual +/−, debt sale, **void rolls the balance back**, stock restored).
- Cash drawer open → sales → drop → close; Z-report page + PDF. Hold/resume/delete carts. Barcode, `+`, card re-click in the UI.
- Product import preview/apply round-trip is idempotent; garbage file → clean error. Backup create → validate → download → delete.
  Store-settings save/reset. PIN validation. All dashboard/report/export endpoints and PDFs.
- 70+ pages in EN and AR render with no server errors; RTL layouts of Customers, Receipts, POS, Orders look right.
- Audit log: every non-success row during the runs was an intentional negative test.
