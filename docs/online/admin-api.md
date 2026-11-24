# Admin API (`/api/admin/v1`)

Everything the provider portal at `/admin` can do, as JSON, for an admin app (phone or desktop).
The portal pages and this API call the same code (`AdminOperations`, `FinanceBook`, `OverviewBuilder`, `FinanceDashboard`),
so a rule changed in one place applies to both.

## Conventions

- **Auth:** `POST /auth/login` returns a token (`nxa_...`). Send it on every other call as `Authorization: Bearer <token>`.
  Tokens last 30 days. The portal's browser cookie is **not** accepted here.
- **Errors** are `{ "code": "...", "message": "..." }`. `code` is stable for the app to branch on; `message` is a sentence to show.
  - `401 unauthorized`: no token, or it expired or was signed out. Sign in again.
  - `400 <code>`: the request was refused (for example `name_required`, `bad_months`, `invalid_payment`, `sign_in_taken`).
  - `404 not_found`: the client, branch, login, payment or expense does not exist.
  - `429 rate_limited`: too many wrong passwords. Wait about 15 minutes.
- **Times** ending in `Utc` are UTC (`2026-11-24T10:00:00`). **Dates** (`date`, `from`, `to`) are `yyyy-MM-dd` in the provider's own
  calendar (time zone from `Cloud:TimeZone`). **Months** are `yyyy-MM`.
- **Amounts** are JSON numbers in one currency (`currency` in `/me`, default `$`), at most 2 decimals.
- Values marked "shown once" (connection links, device tokens, phone-login passwords) cannot be fetched again. Make a new one if lost.

## Sign-in and sessions

| Method | Path | Body / query | Returns |
|---|---|---|---|
| POST | `/auth/login` | `{ username, password, deviceName? }` | `{ token, expiresAtUtc, username, sessionId }` |
| POST | `/auth/logout` | | 204. This token stops working. |
| GET | `/me` | | `{ username, sessionId, deviceName, expiresAtUtc, currency, timeZone, today }` |
| GET | `/sessions` | | Signed-in devices: `[{ id, deviceName, createdAtUtc, lastUsedAtUtc, expiresAtUtc, current }]` |
| DELETE | `/sessions/{id}` | | 204. Signs that device out (for example a lost phone). |

## Overview

| Method | Path | Returns |
|---|---|---|
| GET | `/overview` | `{ summary, clients, connected, plans { active, endingSoon, ended }, productsHeld, ordersHeld, pulse[24], litHours, attention[], thisMonth { collected, spent, profit, balance } }` |

Each item in `attention` is `{ severity: "warn"|"bad", title, reason, action, portalPath, clientId }`.
Each item in `pulse` is `{ hourUtc, contacts }`, oldest first, ending with the current hour.

## Clients

| Method | Path | Body / query | Returns |
|---|---|---|---|
| GET | `/clients` | `?search=` (name, phone, email) `&filter=connected\|waiting\|attention` | Client rows |
| POST | `/clients` | `{ name, phone?, email?, notes?, months? }` | 201 `{ client, siteId, link { url, expiresAtUtc } }`. Creates the client, its "Main branch" and a connection link (shown once). |
| GET | `/clients/{id}` | | Client detail: fields, `subscription`, `sites[]`, `logins[]`, `payments { count, total, last12Months, last }`, `attention[]` |
| PUT | `/clients/{id}` | `{ name, phone?, email?, notes? }` | Client detail |
| POST | `/clients/{id}/activate` | | `{ id, isActive }` |
| POST | `/clients/{id}/deactivate` | | `{ id, isActive }`. All its branches are refused by the cloud; the shops keep selling. |
| DELETE | `/clients/{id}` | | 204. Removes branches, cloud data, subscription and history. **Payments stay** (detached, payer name kept). |
| GET | `/clients/{id}/events` | `?take=50` (max 500) | History, newest first: `[{ id, atUtc, text }]` |

### Online subscription

| Method | Path | Body | Returns |
|---|---|---|---|
| POST | `/clients/{id}/subscription/extend` | `{ months }` (1–60) | `{ state, startsAtUtc, expiresAtUtc, suspended }`. Early renewals keep the time left; late ones start now. Also lifts a suspension. |
| POST | `/clients/{id}/subscription/suspend` | | Same shape |
| POST | `/clients/{id}/subscription/resume` | | Same shape |

`state` is one of `None`, `Active`, `Grace`, `Expired`, `Suspended`.

## Branches (sites)

| Method | Path | Body | Returns |
|---|---|---|---|
| POST | `/clients/{id}/sites` | `{ name, machineId? }` | 201 site detail |
| GET | `/sites/{id}` | | `{ site, data { held, encrypted, counts }, tokens[], openLink, syncLog[] }` |
| PUT | `/sites/{id}` | `{ name, machineId? }` | Site detail |
| DELETE | `/sites/{id}` | | 204. Deletes the branch's cloud data. Nothing at the shop is touched. |
| POST | `/sites/{id}/link` | | 201 `{ url, code, expiresAtUtc, whatsAppUrl }` (shown once). Older unused links stop working. |
| POST | `/sites/{id}/disconnect` | | Site detail. Revokes the device token and any open link. |
| POST | `/sites/{id}/token` | | 201 `{ token }` (shown once). For support only; links are the normal way to connect. |
| POST | `/sites/{id}/resync` | | Site detail. The shop PC re-sends everything at its next contact. |
| POST | `/sites/{id}/enable` | | Site detail |
| POST | `/sites/{id}/disable` | | Site detail |

`site.connection` is one of `NotLinked`, `WaitingForShop`, `LinkExpired`, `Connected`, `Disconnected`.
`site.health` is one of `Never`, `Ok`, `Stale`, `Down`.

## Phone logins (the shop owner's sign-in to the phone app and owner website)

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/clients/{id}/logins` | | `[{ id, signIn, displayName, role, permissions, isActive, createdAtUtc, lastSignInAtUtc }]` |
| POST | `/clients/{id}/logins` | `{ signIn, displayName?, password?, role? }` | 201 `{ login, password, whatsAppUrl }`. With no password, one is made up. The password is shown once. |
| PUT | `/clients/{id}/logins/{loginId}` | `{ role?, isActive? }` | The login. A role change or disable signs its phones out. |
| POST | `/clients/{id}/logins/{loginId}/reset-password` | | `{ login, password, whatsAppUrl }` (shown once) |
| DELETE | `/clients/{id}/logins/{loginId}` | | 204 |

`signIn` is an email, a phone number or a username. `role` is `owner`, `manager` or `staff`.

## Finance

| Method | Path | Body / query | Returns |
|---|---|---|---|
| GET | `/finance/options` | | `{ currency, methods[], purposes[], expenseCategories[], maxAmount, today }`: what the forms offer |
| GET | `/finance/summary` | `?m=2026-11` or `?y=2026` (default: this month) | The whole dashboard (see below) |
| GET | `/finance/export` | `?m=` or `?y=` | CSV file. Money in is positive, money out negative. |

### Payments (money in)

| Method | Path | Body / query | Returns |
|---|---|---|---|
| GET | `/finance/payments` | `?from=&to=&clientId=` | `[payment]`, newest first |
| GET | `/clients/{id}/payments` | | That client's payments |
| GET | `/finance/payments/{id}` | | payment |
| POST | `/finance/payments` | `{ clientId?, payer?, amount, date?, method?, purpose?, reference?, note? }` | 201 payment |
| PUT | `/finance/payments/{id}` | same | payment |
| DELETE | `/finance/payments/{id}` | | 204 |

With no `clientId`, the payment is other income and `payer` says where it came from (for example "Owner capital").
`date` defaults to today; `method` defaults to Cash and `purpose` to Online subscription.
A payment is `{ id, clientId, payer, amount, date, method, purpose, reference, note, createdAtUtc, createdBy }`.

### Expenses (money out)

| Method | Path | Body / query | Returns |
|---|---|---|---|
| GET | `/finance/expenses` | `?from=&to=&category=` | `[expense]`, newest first |
| GET | `/finance/expenses/{id}` | | expense |
| POST | `/finance/expenses` | `{ amount, date?, category, payee?, note? }` | 201 expense |
| PUT | `/finance/expenses/{id}` | same | expense |
| DELETE | `/finance/expenses/{id}` | | 204 |

### Budgets

A budget applies from its month onward, until a later one replaces it. A year adds up each month's budget.

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/finance/budgets` | | Every budget set: `[{ month, incomeTarget, spendingLimit, updatedAtUtc }]` |
| GET | `/finance/budgets/{yyyy-MM}` | | The budget in force that month: `{ month, incomeTarget, spendingLimit, setIn }` |
| PUT | `/finance/budgets/{yyyy-MM}` | `{ incomeTarget?, spendingLimit? }` | The row. A null or missing amount means none from that month on. |
| DELETE | `/finance/budgets/{yyyy-MM}` | | 204. The earlier budget applies again. |

### `/finance/summary` response

```jsonc
{
  "period": { "kind": "month", "label": "November 2026", "from": "2026-11-01", "to": "2026-11-30",
              "query": "m=2026-11", "previous": "m=2026-10", "next": "m=2026-12" },
  "currency": "$",
  "collected": 365, "spent": 143, "profit": 222, "marginPercent": 61, "balance": 5586,
  "previous": { "collected": 250, "spent": 92, "profit": 158 },
  "change": { "collected": "+46%", "spent": "+55%" },   // null when the previous period had nothing
  "payingClients": 3,
  "budget": {
    "monthBudget": { "month": "2026-01", "incomeTarget": 600, "spendingLimit": 250, "updatedAtUtc": "..." },
    "income":   { "planned": 600, "actual": 365, "left": 235, "percent": 61, "over": false },
    "spending": { "planned": 250, "actual": 143, "left": 107, "percent": 57, "over": false }
  },
  "trend": [ { "month": "2025-12", "collected": 1590, "spent": 78, "profit": 1512 } /* 12 months, oldest first */ ],
  "averages": { "collected": 593.75, "spent": 128.25, "profit": 465.5 },
  "byPayer":    [ { "name": "Sunrise Pharmacy", "amount": 130, "count": 1, "clientId": 5 } ],
  "byPurpose":  [ { "name": "Online subscription", "amount": 365, "count": 3, "clientId": null } ],
  "byCategory": [ { "name": "Transport", "amount": 60, "count": 1, "clientId": null } ],
  "renewals": [ { "clientId": 3, "name": "Beirut Electronics", "endsAtUtc": "...", "lastPaid": 115 } ],
  "renewalsExpected": 115,
  "attention": [ { "severity": "warn", "title": "...", "reason": "...", "action": "...", "portalPath": "...", "clientId": 2 } ],
  "lines": [ { "type": "payment", "id": 41, "date": "2026-11-19", "who": "Cedar Bakery", "clientId": 2,
               "details": "Online subscription · OMT", "note": null, "amount": 120 } ]
}
```

## Online menu editor

The menu editor API (`/menu/v1/...`, used by `wwwroot/owner/menu.html`) also accepts the admin token. Add `?client={id}` to
every call to say which client's menu you are editing, as the portal does.
