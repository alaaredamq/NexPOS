# NexPOS Online: platform design

Branch `online/platform`. Written 2026-11-05.

NexPOS stays an offline-first desktop product: the till PC runs its own server and its own SQLite database, and
selling never depends on the internet. **Online** is a separate, paid add-on that gives a client's owner a copy of their
shop in the cloud, for phones and browsers.

This document explains what exists, why it is shaped this way, how to run and host it, and what is deliberately not done.

## 1. Product shape (v1)

> **Online v1: cloud mirror.** The till is the source of truth. It pushes every change to a cloud copy of that one
> installation's database. The owner looks at it from anywhere. Nothing flows back down yet, and nobody sells remotely.

Why one-way first: two-way sync means conflict rules for stock, drawer and debt, which is the hard and risky part.
One-way gives the owner the value they ask for first (see sales, stock and debts from the phone) with none of that risk.

It is a **service**, not a locked feature: the till keeps every feature without it, which matches the no-feature-gating
rule. Online is priced as a subscription because it has running costs (hosting, backups, support).

## 2. Architecture

```
 Shop PC (offline-first, unchanged)                           Your server (new)
┌────────────────────────────────────┐                  ┌───────────────────────────────────────┐
│ Electron → Web (ASP.NET, :5000)    │                  │ Cloud (ASP.NET, behind Caddy/HTTPS)   │
│   SQLite  nexpos.db                │                  │                                       │
│   triggers → __SyncLog (addresses) │   HTTPS, device  │  /api/sync/push   ← device token      │
│   OnlineSyncService (background)   │ ───────────────► │  /api/sync/ping                       │
│        reads current rows, pushes  │   token, batches │  Admin portal  ← your login           │
│        batches, trims log on ack   │ ◄─────────────── │                                       │
└────────────────────────────────────┘      ack         │  control.db   clients, plans, tokens  │
                                                         │  sites/<guid>.db  one mirror per till │
                                                         └───────────────────────────────────────┘
```

| Part | Project | What it does |
|---|---|---|
| Sync library | `Sync/` | Change capture (triggers), the pusher, the mirror applier, wire contracts, the connection-link parser, the settings store, the connect/disconnect service. Shared by both sides. |
| Till side: engine | `Sync/OnlineSync.cs`, `Sync/OnlineConnection.cs`, 2 lines in `Web/Program.cs` | Background service. **Off unless the owner connects the till.** |
| Till side: API | `Web/Api/Online/` (`api/v1/online`: status, connect, disconnect, sync-now) | Needs `Settings.Manage`. |
| Till side: screen | `client/src/modules/settings/OnlinePage.tsx` (Settings > Online, EN/AR) | What the owner sees. |
| Cloud | `Cloud/` | Control database, enrolment and device-token APIs, public `/enroll/<code>` page, admin portal (Razor Pages, redesigned: left rail, a "Needs you" panel, a 24-hour contact strip per till, toasts and confirm dialogs, phone layout). |
| Upgrade safety | `Infrastructure/Data/PreUpgradeGuard.cs` | Copy before, check after, restore on failure or power cut (section 5a). |
| Tests | `Cloud.Tests/` (149), `Tests/Migrations/PreUpgradeGuardTests.cs` (13), `Tests/Api/Online/` (8), `client/.../online.test.tsx` (25) | Triggers, sync, enrolment, link parsing, the background service, API and portal security, the portal pages, the Online screen, upgrade safety. |

## 3. Decisions

| # | Decision | Why |
|---|---|---|
| O1 | **One mirror database per installation**, a separate SQLite file named after a GUID the cloud generated. | You asked that no client's database interfere with another's. A separate file makes that physical: a bad query cannot cross clients because no two clients share a file. WORKLOG F1 already fixed "one till = one database", so one installation = one mirror. |
| O2 | The mirror uses **the same schema and migrations** as the till (`ErpDbContext`). | Zero new data model. The web and mobile views can later reuse existing services and repositories against the mirror. |
| O3 | **SQLite on the server for now**, behind a boundary that can change. | You have no host and a small client count. No database server to run, trivial backup (copy files). The control plane and applier are the only things that know the provider. Moving to Postgres later means swapping the applier's SQL and the migrations, not the design. See section 8. |
| O4 | **Change capture with SQLite triggers**, not an EF interceptor. | The app writes with `ExecuteUpdateAsync`, raw SQL and bulk import, none of which pass through `SaveChanges`. A trigger sees every write, inside the same transaction, so a sale cannot be committed yet missing from the log. |
| O5 | The log holds **addresses only** (table, row id, op). The pusher reads the current row when it sends. | 25 edits to one row cost one change. A batch is the row as it is now, so replaying a batch is harmless. |
| O6 | **State-based, acknowledged, idempotent** pushes. | The till trims its log only after the cloud acknowledges. A dropped connection or lost answer resends; every change is a full-row upsert. Tested (outage, lost acknowledgement). |
| O7 | **First batch is a full snapshot that wipes the mirror**, and the admin can request a **full resync** from the portal. | Guarantees the mirror equals the till rather than drifting. Resync is the recovery for a till that restored an old backup. |
| O8 | **Credentials never leave the shop.** `Users.PasswordHash` and `PinHash` are not sent; the mirror stores empty placeholders. The cloud ignores any column outside the whitelist. | Cloud sign-in for owners will use cloud accounts, not till PINs. A stolen mirror contains no till credentials. |
| O9 | **Device token per installation**, stored only as a SHA-256 hash, shown once. | A leaked control database cannot impersonate a till. Rotate or revoke from the portal. |
| O10 | Subscription gates **sync only**, with a 7-day grace, then HTTP 402. The till queues and keeps selling. | Same philosophy as the till licence: warn, never cut off abruptly, never touch data. Renewal releases the queue. |
| O11 | The **admin portal is server-rendered Razor Pages** inside the Cloud app. | It is an internal tool for you, not a product screen. No second build pipeline. The owner-facing web/mobile app is a separate, later piece. |
| O12 | The till writes **nothing extra and opens no network connection unless enrolled.** | A till that is not enrolled gets no extra tables or triggers (tested). Existing installs are unaffected by this branch. |
| O13 | **Enrolment is one pasted link**, not a token and a file. The portal makes `https://<cloud>/enroll/XXXX-XXXX-XXXX`; the owner pastes it in Settings > Online and presses Connect. | Installation had to be easy for a non-technical owner and for you on the phone. A link travels well on WhatsApp, and pasting the whole message works. |
| O14 | The link holds a **one-time code, not the credential**: 60 bits, stored hashed, works once, expires in 7 days, a new link cancels the old one, only unknown codes count toward the guess throttle. The device token is created when the till redeems it and is never shown to anyone. | A link sent over chat is a secret in transit. Making it single-use and short-lived limits the damage of a leak to "someone enrolled first", which the owner sees at once because their own attempt then fails with "already used". |
| O15 | **Connecting and disconnecting take effect at once, with no restart.** The background service re-reads the settings every cycle and can be woken instantly. Disconnecting removes the capture triggers; reconnecting starts from a full snapshot. | The owner should never be told to restart NexPOS, and a disconnected till must stop paying the (small) cost of change capture. |
| O16 | The till **refuses plain `http://` links** except to this computer. | The code buys a credential; it must not cross a network in clear text. Loopback http is allowed so the platform can be tried on one PC. |
| O17 | **Health is judged on contact, not on data.** A till is "in contact" if the cloud heard from it in the last 25 minutes, whether or not it had data to send. Each contact is counted per hour (kept 7 days) and drawn as a 24-hour strip. | A closed shop sends no data overnight; judging on data would show every quiet shop as down. A connected PC checks in about every 10 minutes, so silence is a real signal, and the strip shows exactly when a PC lost power or internet. |
| O18 | The portal **leads with what needs you** (ended or ending subscriptions, silent tills, unused links) and shows times in the provider's zone (`Cloud__TimeZone`, default Asia/Beirut). Anything that cuts a shop off asks first and says the shop keeps selling. | A provider with dozens of clients should not have to scan a list to find the three that matter. |

## 4. How a change travels

1. **Enrol.** In the portal you create a client: one form, which also creates the installation ("Main till") and shows
   the connection link with **Copy link** and **Send on WhatsApp**. The owner opens NexPOS > Settings > Online,
   pastes the link and presses **Connect**. The till redeems the one-time code for its device token, saves it beside
   its database (`online.json`) and starts syncing, with no restart.
2. **Start.** On connect (and on every start while connected, after migrations), the till installs one
   INSERT/UPDATE/DELETE trigger set per synced table. It queues every existing row once (the initial snapshot).
3. **Capture.** Every write appends `(table, row, op)` to `__SyncLog` in the same transaction.
4. **Push.** Every 30 s, the till takes the oldest entries, collapses repeats, reads each row's current values, and
   posts a batch. The cloud checks token, client, subscription and schema version, applies the batch in **one
   transaction** (all or nothing), and acknowledges.
5. **Trim.** Only after the acknowledgement does the till delete those log entries.
6. **Failure.** Network down, cloud down, subscription expired, token revoked: the till backs off (up to 5 minutes)
   and keeps queuing. Nothing is lost and the cashier sees nothing.

**What syncs.** Every table in the model except local-only state: sessions, carts, backups, notifications, alerts,
routes, `SystemSettings` and `AppSettings` (machine configuration). 32 tables today, derived from the EF model, so a
new entity is picked up automatically. The list is `SyncSchema.ExcludedTables`.

**Schema versions.** A till older than the cloud is accepted. A till *newer* than the cloud is refused (409
`schema_newer`) until the cloud is upgraded. Mirrors migrate lazily, on their next push after a cloud upgrade.

## 5. Security notes

- Device API: bearer token, hash lookup, per-client and per-installation kill switches, subscription check. Tested
  for missing, unknown, revoked and disabled credentials.
- Batches are validated against the whitelist: unknown tables are rejected (the whole batch), unknown columns are
  ignored, and no value is ever concatenated into SQL. Identifiers come only from the EF model.
- Admin portal: cookie sign-in, PBKDF2-SHA256 (210k iterations), antiforgery on every form, login throttle (5 failures
  per address or per username, 15 minutes), local-only `returnUrl`, `Secure` cookie unless in Development.
  There is **no default password**: the account comes from `Cloud__AdminUser` / `Cloud__AdminPassword`
  (at least 12 characters).
- Data-protection keys live on the data volume, so a redeploy does not sign you out.
- **Not done:** two-factor sign-in, audit trail of portal actions beyond the per-client history, token storage on the till is a
  plain JSON file. Shop data is now end-to-end encrypted before it leaves the till (section 5b), which supersedes encryption at rest
  for shop rows; the control database (clients, subscriptions) is still not encrypted at rest.

### 5a. Upgrade safety on the till (applies to every client PC, Online or not)

Packaged installs upgrade their own database at startup, which is the riskiest moment for a client's data. `PreUpgradeGuard`
(`Infrastructure/Data`, wired in `Web/Program.cs`) wraps it:

1. **Before:** when migrations are pending and the database already holds data, copy it (SQLite `VACUUM INTO`) to
   `pre-upgrade-backups/` next to the database, and prove the copy: integrity check plus identical row counts per table. If the
   copy cannot be made or does not verify, **the upgrade does not run** and the data is untouched. A marker file records that an
   upgrade is in progress.
2. **After:** integrity check, no new broken links between records (`PRAGMA foreign_key_check`), and no table has fewer rows.
   A migration that deletes rows on purpose must name its table (`allowShrinkingTables`), otherwise it is treated as a loss.
3. **If either step fails:** the copy is put back and startup stops with a message that says the data was restored.
4. **Power cut or crash mid-upgrade:** the marker is still there at the next start, so `RecoverInterruptedUpgrade` puts the copy
   back *before* anything else, then the upgrade runs again from a clean state. (EF itself warns that a `PRAGMA foreign_keys`
   migration step cannot run inside a transaction, so a half-applied state is a real possibility.)
5. **After a good upgrade:** the marker is removed and only the newest 5 copies are kept.

What it does not do: protect against the disk failing or the copy folder being deleted (ordinary backups still matter), and it
trades availability for safety when the disk is full (the shop cannot start the new version until space is freed).
Tested with real databases on disk, including a real older-to-newer upgrade, a simulated bad upgrade, a failed copy, a damaged
copy, and a simulated power cut (`Tests/Migrations/PreUpgradeGuardTests.cs`); the real app was also started on a genuinely old
database and made the copy before upgrading. **Not yet tried on a client's PC.**

### 5b. End-to-end encryption of the shop's copy (added 2026-11-11, phase 1 of 3)

**Goal (owner's requirement):** the provider cannot read a client's shop data. The provider sees only what is needed to run the
service.

| The cloud/provider can see | The cloud/provider cannot see |
|---|---|
| Client and shop name, contact, subscription, last contact, versions, sync status | Product names, prices, barcodes, customers, debts, sales lines, staff |
| Which table a row belongs to, its id, whether it was deleted, when it arrived (so: **row counts per table**, storage size) | The contents of any row |

How it works (`Sync/DataKey.cs`):
- On first connect the till makes a **256-bit key** and keeps it in `online.json` (`DataKeyBase64`). It is never sent anywhere.
  It survives Disconnect on purpose, so a printed recovery code keeps opening the cloud's copy after a reconnect.
- Every changed row is encrypted on the till with **AES-256-GCM**, a fresh nonce each time, with the table and row id bound in as
  authenticated data (the cloud cannot swap rows between places undetected). The batch carries `Enc` instead of `Values`.
- The cloud stores the payload in a generic table, `__EncryptedRows(TableName, RowId, Payload)`, inside that client's own file. It
  never holds the key, so it cannot decrypt, and **cannot recover the data for the owner either**.
- **Recovery code:** the same key written as 56 base32 characters in groups of four with a 3-byte checksum, so a typo is refused instead
  of producing a wrong key (`GET /api/v1/online/recovery-code`, needs `Settings.Manage`). The owner must print or write it down.
  **If the PC is lost and the owner has neither the code nor a phone that already holds the key, the cloud copy is unreadable.** This is
  the price of the promise, not a bug. Decided with the owner: no escrow copy is kept by the provider.
- A till connected before this existed makes a key on its next cycle and re-sends the whole shop encrypted (the snapshot replaces the
  readable copy).
- `Cloud:RequireEncryption=true` makes the cloud refuse any readable row (deletions are always allowed).

**Verified:** `Cloud.Tests/EncryptedSyncTests.cs`: after a real sync, the raw bytes of the client's database file and WAL contain
none of the product names, barcodes, customer names or emails (and a control test proves the same scan *does* find them when sent
unencrypted); wrong key, moved row and flipped bit all fail; every single-character typo in a recovery code is caught.

**Phase 2, done (2026-11-12): the phone reads the copy.** `GET /owner/v1/rows?table=&cursor=&limit=` hands out one page of ciphertext (resumable cursor on arrival time); `GET /owner/v1/row-ids?table=` lets the phone drop rows the shop deleted. The Flutter app (`mobile/`) takes the recovery code once, keeps the key in secure storage, decrypts on the device (AES-GCM, tested against rows made by the C# code) and builds Today, Sales, Stock, Debts and the add-form pick-lists from the shop's own rows. `GET /lookups` still answers 409 `encrypted_copy` for an encrypted shop and is no longer used by the app. **`RequireEncryption` can now be turned on.** Settings > Online has a "Recovery code" card (show, copy, print, hide).

**Phase 3, done (2026-11-12): what the phone sends is encrypted too.** The phone seals every change (`{ "enc": ... }`, AES-GCM of `{ k, body }` bound to the kind of change) and the till opens it. With a key on the till, **an unsealed change is refused**, so the cloud can neither read nor forge a change; the same `k` is never run twice even if the cloud re-queues the bytes under a new id; the till's answer (names, quantities, refusal reasons) is sealed to the command id. See `Sync/Commands.cs` (`CommandSeal`), `OnlineCommandService`, `CommandLedger`. New change kinds that use it: `product.update`, `stock.adjust`.

**Not done yet:**
4. The key sits in `online.json` as plain text, like the device token. Protect it with Windows DPAPI (per-user) before real clients.
5. Customer names inside **audit/event text the portal writes itself** are not shop data, but check the portal never copies shop text
   into the control database.
6. The portal's per-table row counts still work (they count encrypted rows by table). Anything that wants totals (sales amounts) cannot
   be computed on the server by design; if you want billing by volume, send an explicit plain counter from the till.

## 6. Running it

**Visual walkthrough with screenshots (open in a browser, one file): `docs/online/HOW-TO-CONNECT.html`.** It covers connecting a shop, renewing, what each status means, and the other situations (new PC, second till, restored backup).

### Try it on your PC (no server needed)

```powershell
$env:Cloud__AdminUser = "owner"
$env:Cloud__AdminPassword = "choose-a-long-password"
$env:ASPNETCORE_ENVIRONMENT = "Development"      # lets the sign-in cookie work over plain http
dotnet run --project Cloud --urls http://localhost:5100
```

Open `http://localhost:5100`, sign in, **New client** (name, months of Online) and press the button. The next screen
shows the connection link. In NexPOS open **Settings > Online**, paste the link, press **Connect**. That is the whole
installation: the shop is uploaded within seconds and the installation's page in the portal shows **Connected**, the last
sync and row counts. (Plain `http://localhost` links are accepted for this local trial; anything else must be `https`.)

On a real server the link shows your public address. Set it once (`Cloud__PublicUrl`, already in the compose file),
otherwise the portal uses whatever address you opened it on.

**What the owner sees on the till** (Settings > Online, English and Arabic): before connecting, three plain steps and a
box; after connecting, the shop name, when data was last sent, how many changes are waiting, the subscription state, and
buttons **Send now** and **Disconnect**. Every failure is a sentence, not a code: link already used / expired / not
valid, cloud unreachable, subscription ended, till disconnected by you. Each one says that nothing is lost.

**Manual fallback** (rarely needed): the installation page has an *Advanced* section that issues a raw device token. Put
it in `%LocalAppData%\NexPOS\online.json` as `{ "Online": { "Enabled": true, "CloudUrl": "https://...", "DeviceToken": "nxp_..." } }`
and restart NexPOS. The same file is what the Connect button writes.

### Hosting (you have no host yet)

Any small Linux VPS works. Nothing here needs a specific provider. Needs: Docker, a DNS name pointing at the server,
ports 80 and 443 open. `Cloud/docker-compose.yml` runs the app behind Caddy, which obtains and renews the
certificate itself. The steps are in that file's header.

**The Dockerfile and compose file have not been run**: Docker was not running on the machine where this was built.
The same app was published with `dotnet publish` and smoke-tested (health, login, portal, database creation).
Build the image once on the server and check `https://<your-domain>/healthz` before relying on it.

Back up the `cloud-data` volume (`control.db`, `sites/*.db`, `keys/`). It is everything. Do it before you have a
paying client.

### Control database: SQLite or SQL Server

The control database (clients, subscriptions, tokens, accounts, heartbeats; **no shop data**) runs on SQLite by default
(`control.db` in `Cloud:DataRoot`), which needs nothing and is what tests, a local trial and a free test host use.
Set `Cloud:ControlConnectionString` (or `Cloud__ControlConnectionString`) to a SQL Server connection string and it runs
on SQL Server instead, creating its tables on first start. Recommended collation: `Arabic_CI_AS`. The two providers have
separate migration sets (`Cloud/Data/Migrations` for SQLite, `Cloud/Data/MigrationsSqlServer`), selected by the context type
(`ControlDbContext` / `SqlServerControlDbContext`). After changing the model, add a migration for **both**:
`dotnet ef migrations add <Name> --project Cloud --context ControlDbContext --output-dir Data/Migrations` and the same with
`--context SqlServerControlDbContext --output-dir Data/MigrationsSqlServer`. Shop mirrors stay SQLite files either way.
Verified against LocalDB (all 12 tables created, admin seeded, `/healthz` ok); the 172 Cloud tests run on SQLite only.
**Moving existing data between providers is not built**; do it before real clients exist.

## 7. Known limits

- **One-way only.** No remote edits, no remote selling.
- **No owner-facing web or mobile app yet.** Today the cloud holds the data and the admin portal shows row counts.
  Nothing lets an owner look at their sales. That is the next piece (section 9).
- **Disconnecting on the till does not tell the cloud.** The till forgets its credential and stops, but the cloud keeps
  the old token until you press **Disconnect** in the portal (or the next enrolment replaces it), and keeps its copy of
  the data. Removing a client's data from the cloud is a manual job today.
- **The Arabic texts on the Online screen and the WhatsApp message were written by me, not by a native reviewer.**
  Have someone read them before a client does.
- **The link is visible to anyone who sees the chat** until it is used or expires. The single-use rule means the owner's
  own attempt fails loudly if someone else got in first, but there is no confirmation step on your side.
- **Performance is not measured.** Tests use small databases. A large shop's first snapshot (tens of thousands of
  rows, product images stored in rows) has not been timed; batches are capped at 5000 changes and 64 MB.
- **Restoring a backup on the till** leaves the mirror ahead of it until you press **Request full resync**.
- **Multi-till clients** are out of scope (same as F1). Two tills would share integer ids; they would need separate
  installations, each with its own mirror and token.
- **Mirror integrity checks are light:** foreign keys are off while applying (batches arrive in log order), relying
  on the till having enforced them. A scheduled reconciliation (row counts / checksums per table) is not built.
- **No billing.** Subscriptions are extended by hand in the portal. No invoices, payment links or reminders.
- **Single admin account**, set by environment variable.

## 8. When SQLite on the server stops being enough

SQLite per site is a good start but not forever. Signs to move: many hundreds of clients (file handles, migration
time after upgrades), the need to query across clients (reporting for you, not them), or concurrent web writes
(two-way sync). The move is Postgres with one **schema or database per client** (keep the isolation), a Postgres
migration set for `ErpDbContext`, and a Postgres variant of `MirrorApplier`'s upsert. Everything else (tokens,
subscriptions, portal, till side) stays. This has not been scoped in detail.

## 9. Suggested next steps, in order

1. **Verify the container on a real host** and set up automated backups of the data volume.
2. **Owner portal (read-only):** cloud accounts for owners, bound to their client; dashboard, sales, stock, debts read
   from the mirror, mobile-first. Installable as a PWA (manifest and service worker). This is what clients pay for.
3. ~~Till-side Online settings page~~ Done (Settings > Online, section 6).
4. **Reconciliation:** nightly per-table row counts from the till compared with the mirror, flagged in the portal.
5. **Limited write-back** (prices, product edits) only after 1, 2 and 4, as a separate design with conflict rules.
6. Capacitor wrapper or store listing only if clients ask.

## 10. What drives your cost per client (for pricing)

No figures are given here because none were measured or quoted. The inputs are: server and bandwidth (small: batches
carry only changed rows), storage (one mirror per client, growing with their sales history), backup storage, your
support time, and a domain and certificate (free with Caddy). Work these out for your chosen host, then add your
margin. Charging per installation matches how the till itself is priced.
