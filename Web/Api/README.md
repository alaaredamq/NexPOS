# Coduo POS JSON API (`api/v1`)

The API of the React app (`client/`). It sits next to the Razor pages and shares their database, services, licence and
sign-in cookie. Foundation by track **0A** of `REACT_MIGRATION.md`; each module adds its folder under `Web/Api/<Module>/`.

The legacy `api/*` controllers (`CartsController`, `AuthController`, ...) are **not** part of this API. They keep
their own `{success, message}` shapes for the Razor pages' jQuery until cutover.

## Writing an endpoint

```csharp
[Route("api/v1/cash-drawer")]                      // kebab-case, always under api/v1/
public sealed class CashDrawerController : ApiControllerBase   // signed-in users only, JSON, problem+json errors
{
    private readonly ICashDrawerQueryService _drawer;           // controller -> service; never DbContext (Web/Requirements/rules.md)

    [HttpGet("sessions")]
    [RequirePermission(PermissionHelper.Permissions.Sales_View)]  // copy the old action's permission, action by action
    public async Task<PagedResult<DrawerSessionDto>> List([FromQuery] PagedQuery query) { ... }
}
```

* **Every action must say who may call it**: `[RequirePermission]` / `[RequireAnyPermission]` / `[RequireRole]`,
  `[AllowAnonymous]`, or `[AnySignedInUser]`. A test fails for an action that says nothing.
* **Success returns the DTO itself.** No `{ success: true }` wrapper. `204` for "done, nothing to return".
* **Contracts first.** Put DTOs in `Web/Api/<Module>/Contracts/` with XML comments (they become the generated TypeScript docs)
  and merge them before the implementation. Non-nullable `string` = required, `string?` = optional. DTO class names must be unique
  across the API (Swagger fails loudly on a clash): `ProductDto`, not `Dto`.
* **Services:** a class `Web.Api.X.FooService` with an interface `Web.Api.X.IFooService` is registered as *scoped* automatically,
  so `Program.cs` (frozen after Gate G0) is not touched. Anything else registers as before.
* **Services must not return** EF entities or `{success}` objects; map to DTOs.
* **Audit:** requests are audited by `AuditRequestMiddleware` as `POST Api.<Controller>/<Action>` (the `Api.` prefix tells them from
  the legacy controllers). Keep calling the same `AuditActions` as the old controller for the same business event.
* File uploads: add `[Consumes("multipart/form-data")]` on the action (the base class does not restrict it).

## Wire format

| | |
|---|---|
| Route | `api/v1/<module>[/...]`, kebab-case |
| Success | the DTO, camelCase JSON; timestamps are UTC with a `Z` |
| Lists | `GET ...?page=1&pageSize=25&sort=name&dir=asc&q=milk` -> `{ items, total, page, pageSize }`. Bind `[FromQuery] PagedQuery`; `pageSize` max 200; **whitelist `sort`** in the endpoint |
| Errors | RFC 7807 `application/problem+json`: `{ title, status, detail?, instance, code, traceId, errors? }` |
| `code` | stable machine string the UI branches on (below). Modules add their own: `drawer_not_open`, `insufficient_stock`, ... |
| `title` / `detail` | already localized; show `detail ?? title`. `errors` is `{ "fieldName": ["message"] }`, camelCase like the request body |
| `traceId` | equals the `X-Request-Id` response header and the audit rows of the request: quote it to support |
| Language | send `Accept-Language: en` or `ar`; messages and nav labels come back in that language. The API never changes the Razor pages' language cookie |
| CSRF | every non-GET request must send `X-NexPOS-Client: web`, else `403 client_header_required`. Antiforgery tokens are not used |
| Auth | HttpOnly cookie from `POST /api/v1/auth/login`; no tokens in the browser |

### Codes of the whole API

| Status | `code` | When |
|---|---|---|
| 400 | `validation_failed` | DTO attributes / model binding failed, or `ValidationFailure(field, msg)`; has `errors` |
| 400 | `bad_request` | anything else malformed |
| 401 | `unauthenticated` | no / expired cookie, or the account was disabled. The UI goes to the login page and returns to the route afterwards |
| 401 | `invalid_credentials`, `account_disabled` | `POST auth/login` only (a 401 *on the login page itself* must not trigger the redirect loop) |
| 403 | `forbidden` | signed in but missing the permission |
| 403 | `client_header_required` | mutating call without `X-NexPOS-Client: web` |
| 403 | `license_sales_stopped` | licence does not allow selling (`[RequireSalesLicense]`, or a `LicenseRequiredException` from a service) |
| 404 / 405 | `not_found`, `method_not_allowed` | unknown route / wrong verb, or `return NotFound()` |
| 409 | your own (`ConflictFailure("drawer_not_open", msg)`) | a business rule says no because of current state |
| 429 | `rate_limited` | sign-in throttle (10 / minute / IP) or the general limit |
| 500 | `internal_error` | unexpected; detail only in Development, full exception in the log |

## Helpers (all in `Web/Api/Infrastructure/`)

| | |
|---|---|
| `ApiControllerBase` | base class: `[ApiController]`, `[Authorize]`, JSON; `CurrentUserId`, `CurrentLanguage`, `Failure(status, code, msg)`, `ConflictFailure(code, msg)`, `ValidationFailure(field, msg)` |
| `PagedQuery` / `PagedResult<T>` | list in / out. `PagedResult<T>.From(sequence, query)` pages an in-memory list; `Of(items, total, query)` for a paged service call |
| `[RequireSalesLicense]` | on checkout / hold / resume: 403 `license_sales_stopped` while the licence stops sales. Reads, reports, backup and the licence screens stay open. (The legacy `LicenseEnforcementMiddleware` matches URLs and deliberately ignores `api/v1`; the services also still check.) |
| `ApiCodes` | the shared codes above |
| `ApiProblem` | build / write problem responses from middleware or filters |

Existing `[RequirePermission]` / `[RequireRole]` attributes work unchanged: for any `/api/*` request they answer 401 / 403 JSON instead of
redirecting to the login / access-denied page. The cookie challenge does the same for `[Authorize]`.

## The point of sale (track P1)

`Web/Api/Pos/` (`api/v1/pos/*`) and `Web/Api/Carts/` (`api/v1/carts`). Business refusals are a `PosRuleException` (status + stable `code` + a `SharedResource` key), turned into problem JSON by
`PosControllerBase`: inherit it instead of `ApiControllerBase` when a service has rules to refuse with. Contract, rounding rules and error codes: `docs/react-migration/notes/P1.md`.

## Contract and generated types

`npm run api:types` (in `client/`; add `-- --no-build` to reuse the last build of `Web`) runs `Web.exe --dump-api-contract <tmp>`, which builds the
OpenAPI document and exits **before** any database or port is touched, and then writes the committed files
`client/src/api/schema.d.ts` (openapi-typescript) and `client/src/api/permissions.ts` (every key of `PermissionHelper.Permissions`).
Never hand-merge them: on a conflict take either side and run it again.
In Development the document is also served at `/swagger/v1/swagger.json` (UI at `/swagger`). Only `api/v1` is in it; one tag per module.

## The shell's two calls

* `POST /api/v1/auth/login` `{ username, pin, rememberMe }` -> `SessionDto` (same as `GET session`). Same rules as the Razor login: PIN of exactly 4 digits.
  The **sign-in throttle is the only brute-force protection** - there is no lockout in `AuthService` (the plan assumed one; see notes).
* `GET /api/v1/session` -> user, role, `permissions[]`, `nav` (sections -> items with `spaPath`, `legacyPath`, `migrated`, `href`), licence, branding,
  language/direction, drawer, unread notifications. Permissions are re-read from the database on every request, so a role edit shows up on the next call.
  `license` also carries the localized banner `title` / `message` / `severity` (same text as the Razor banner).
* `POST /api/v1/auth/logout` -> 204 (always). `GET /api/v1/auth/sign-in-options` (public) -> the name picker of the login screen, plus `branding` (app name / logo) for the screen itself.

Nav links: `Web/Helpers/SpaMigration.cs` maps a page to its SPA path. A module flips **one line** in `Migrated` (alphabetical) when its React page is done;
from then on the Razor rail, the Ctrl+K palette and `session.nav[].href` point at the SPA. Until then `href` is the Razor page (a full navigation; same cookie).
The SPA registers a page's route at `spaPath` (`/app/` + kebab-case of the controller, e.g. `/app/cash-drawer`) whether or not it is migrated.

## Testing

`Tests/Api/Infrastructure/` runs the whole app in-process (`ApiFactory`: throw-away demo database, users `layla`/1234 Admin and `sara`/1111 Cashier, no licence file so
selling is stopped). Copy the pattern for a module: an `[Collection(ApiCollection.Name)]` class, sign in with the helper, assert status + `code`. `FakeApiController` shows
one endpoint per convention. The convention tests (route prefix, access declaration, no `DbContext` in `Web.Api`) run over every controller automatically.

## Gotchas learned the hard way

* `[Produces("application/json")]` on a controller overwrites `ContentTypes` of every `ObjectResult`, including problems. `ApiProblemResultFilter` runs last (`Order = int.MaxValue`) to put `application/problem+json` back. Register global filters with an explicit `order:`.
* `UseDeveloperExceptionPage` / `UseExceptionHandler` swallow exceptions before `ExceptionHandlingMiddleware` sees them; `Program.cs` applies them to non-`api/v1` requests only.
* AspNetCoreRateLimit runs `string.Format` over `QuotaExceededResponse.Content`: literal `{` `}` in `appsettings.json` must be doubled.
* `Accept-Language` is honoured for `api/v1` only. The Razor pages' Chromium sends one on every XHR, which must not override the language they chose.
