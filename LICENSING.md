# Coduo POS licensing

Offline license keys, bound to one PC. Built 2026-10-17 (WORKLOG entry "L1"). Every client gets every feature; the key only decides whether the till may sell.

## How it behaves

Release builds need a key. Debug builds never ask for one (set `NEXPOS_LICENSE_ENFORCE=1` to test enforcement in Debug).

| Key kind | What the date means | What happens |
|---|---|---|
| **Full** | End of the *updates plan* | The till never expires. Builds compiled after the date refuse to run (nobody gets a surprise update: there is no auto-updater). |
| **Provisional** | Expiry date | For instalment sales. Issue a Full key after the last payment. |
| **Trial** | Expiry date | For evaluations. |

**Provisional and Trial timeline** (D = expiry date, the last valid day):

| When | State | What the user sees | Selling |
|---|---|---|---|
| Before D-7 | Active | nothing | works |
| D-7 up to and including D | Expiring soon | "Your license expires in N day(s), on D" | works |
| D+1 to D+7 | Grace | "Expired on D. Renew now: the POS and new sales stop after D+7 (N days left)" | works |
| After D+7 | Expired | "The POS and new sales are stopped. Your data is safe." | **stopped** |

When selling is stopped:
- The POS page redirects to the License page.
- `POST /POS/Checkout`, `HoldCart`, `ResumeHeldCart` are refused, and writes to `api/carts` get a 403.
- Checkout is also blocked inside `POSCheckoutService` and `OrderService`, so no other route can create an order.
- Products, orders, reports, backups, settings and the License page keep working. Data is never touched.

A key that is missing, damaged, or issued for another PC gives the same "selling stopped" state, with a matching message.

## Issuing a key

1. Install Coduo POS on the customer's PC and open the **License** page (sidebar, or `/en/License`; no login needed). Copy the **Machine ID** (`XXXXX-XXXXX-XXXXX-XXXXX`), or read it over AnyDesk.
2. On your PC, from the repo root:

   ```powershell
   dotnet run --project tools/LicenseIssuer -- issue --machine <ID> --customer "<name>" --kind full|provisional|trial --plan basic|pro
   ```

   | Option | Meaning |
   |---|---|
   | `--plan basic` or `--plan pro` | **Required.** The edition (see *Plans* below). |
   | `--days N` or `--until yyyy-MM-dd` | Length. Defaults: full 365 days, provisional 45, trial 14. |
   | `--out <file>` | Also save the key to a file. |
   | `--allow-past` | Allow an end date in the past (only for testing the expiry screens). |
   | `--key <path>` | Use a private key somewhere else. |

3. Send the printed key (`NXL1-...`) to the customer. They paste it on the License page and press **Activate**. No internet needed.

### Packages with several PCs (seats)

Pro only: a Basic key always covers exactly 1 PC (the issuer refuses `--plan basic --seats 2`, and the app treats any Basic key as 1 PC). A key can cover more than one PC: `--seats N` (1 to 255, default 1) is the package size, **the store server plus its tills**. The GUI keygen has a "PCs (package)" box for the same thing. The key is still bound to the **store server's** Machine ID; tills are browsers that open the server, so they show the server's ID and need no key of their own. The seat count is part of the signed key, so it cannot be raised by editing it.

```powershell
dotnet run --project tools/LicenseIssuer -- issue --machine <server ID> --customer "<name>" --kind full --plan pro --seats 3
```

- **Compatibility:** a 1-seat key is written exactly as before, so every installed build still accepts it. A multi-seat key (format version 2) is refused by builds that predate seats with "unsupported version", never read as a 1-PC key. Ship the update before issuing multi-seat keys to a customer.
- **How seats are enforced (built 2026-11-06):** the server PC is always the first seat. Every other PC is a *till* and must be paired once: on the License page (shown when the key covers more than 1 PC) an administrator presses **Add a till**, which reserves a seat and shows a one-time code (`XXXX-XXXX`, valid 15 minutes). On the till PC, open `/app/pair-till`, type the code, and the PC receives a device cookie (`nexpos_till`, HttpOnly, 5 years). After that it can sell. **Add a till** is refused once `Seats - 1` tills exist; **Remove till** frees the seat and cuts that PC off at its next request. The count is checked on the server from the signed key, **offline**, never by the cloud.
- **What is gated:** only the sale endpoints (`[RequireSalesLicense]`, and the legacy POS pages/checkout by URL). An unpaired PC can still sign in and read. Refusals are 403 `device_not_registered` or `seats_exceeded`. If a smaller package is installed later, the oldest tills keep their seats and the newest are refused until a seat is freed (the License page flags them "Over the limit").
- **Pairing is throttled** (5 wrong codes per address per 15 minutes) and only hashes are stored (code and token), in the `TillDevices` table, which is never synced to the cloud.
- **Not enforceable yet:** the app still listens on `localhost:5000` only, so no other PC can connect and every request counts as the server's own. The check starts to matter the day the server is opened to the shop network ("store server mode", `docs/online/STORE-SERVER.md`). It trusts only the socket address (never `X-Forwarded-For`), so **do not put a reverse proxy on the server PC in front of a multi-seat install** - every request would look local.
- **Backstop gap:** the sale services (`POSCheckoutService`, `OrderService`) still only repeat the licence check, not the till check, because they have no request to look at. The endpoints are the gate.
- `issued.csv` gets a `seats` column (last, so older rows still line up).

### Plans: Basic and Pro (built 2026-11-15)

The plan is part of the signed key, like the seats. `Common/Licensing/PlanRules.cs` is the one place that says what each plan includes.

| | Basic | Pro |
|---|---|---|
| POS, products, categories, customers and debts, receipts, cash drawer and Z-report, currencies, taxes, backups, reports, dashboard | yes | yes |
| Inventory (stock, low stock, stock value), Suppliers, Purchases, Audit log | locked | yes |
| PCs | 1 | the key's seats |
| Active users | 3 (the owner and 2 cashiers, any roles) | unlimited |
| Reports and the dashboard chart | up to 7 days (today, last 7 days, any 7-day range) | any range |

- **Old keys are Pro.** A Pro key is still written as format version 1 or 2, so every installed build accepts it, and every key issued before plans existed reads as Pro. A Basic key is format version 3; a build that predates plans refuses it ("unsupported version") instead of running it as Pro. Ship the update before issuing Basic keys.
- **Locked pages:** the controllers carry `[RequirePlan(ProFeature.X)]` (API: 403 `plan_upgrade_required` with `feature`; Razor: redirect to `/app/upgrade`). The menu still lists them with a lock, and opening one shows the upgrade screen. Nothing is deleted, so after an upgrade everything is there again.
- **User limit:** creating an active user, or switching one on, is refused (409 `plan_user_limit`) when 3 are already active. Users who were already active over the limit (after a downgrade) stay active.
- **No key / Debug:** with no key installed the plan is Basic (selling is stopped anyway). A Debug build without a key is Pro, so development sees every page; install a Basic key to try Basic.
- **Online** (cloud sync, owner portal, mobile app) is a separate subscription that works with either plan. The shop PC hands the cloud its **signed licence key** on its regular check-in (`X-Coduo-License` header on `GET api/sync/ping`: at once on connecting, then every 10 minutes; an older cloud ignores it). The cloud verifies the issuer's signature with the same public key the app embeds (`Cloud:LicensePublicKey`, defaults to `LicensePublicKey.SpkiBase64`) and keeps only what the key says: `Site.ShopPlan`, `ShopLicenseKind`, `ShopLicenseUntil`, `ShopLicenseSeats` (admin portal: branch page, "Licence plan" / "Licence key"). The key text and the customer name in it are not stored. A forged or edited key, or plain text like `Pro`, fails the signature and is ignored, so the plan cannot be claimed, only proven. The cloud then hands the plan and hands it to the phone and the owner website (`shopPlan` on `/owner/v1/me` and on every `/owner/v1/rows` page). On Basic they show a **Products** list with prices (no stock figures, low-stock alerts, Adjust stock, suppliers or "Add a supplier"); a new product goes under the default supplier (id 1). A shop whose key the cloud has not seen yet (a shop PC older than 2.5.1, or no key installed) is shown everything, as before. The real check stays on the shop PC: the phone's `stock.adjust` and `supplier.create` are refused there with `plan_upgrade_required`.
- **Reports on Basic (`ProFeature.Reports`):** `GET api/v1/reports/sales` (and `.csv` / `.pdf`) answer 403 `plan_upgrade_required` for a range over 7 days, and `api/v1/dashboard/sales-trend` for any period but `week`. The React pages show only Today among the tiles, the week chart and the quick picks that fit; the month / all-time tiles and the category donut are Pro. The old Razor report and dashboard-analytics endpoints are locked on Basic (no range limit there). The phone's reports stay on "last 7 days". Discounts need no limit: a discount is already just a name and a rate.
- **Products on Basic:** no stock column, tiles or filter on the list; no supplier, stock or stock value on the details; the form hides the supplier (it keeps the default one) and the stock fields.
- **POS on Basic:** no stock on the product keys, search results or price check, and no "selling past stock" toast (selling past stock was always allowed).
- `issued.csv` gets a `plan` column (last).

Renewing is the same thing: issue a new key and paste it in. A newer key replaces an older one; an older key can never replace a newer one (same day: Full beats time-limited).

If a customer reinstalls Windows or swaps a disk, the key normally still works (2 of the PC's 4 hardware fingerprints must still match). For a new PC, issue a key for the new Machine ID and move their data with Backup/Restore.

Other tool commands:

| Command | Purpose |
|---|---|
| `keygen` | Create the signing key pair. Already done once; it refuses to overwrite. |
| `pubkey` | Print the public key (the one embedded in the app). |
| `inspect <key>` | Decode a key and verify it against the app's public key. |
| `protect` | Add a passphrase to the private key. The key does not change, so existing licenses stay valid. |
| `cloud-login --url <https://your-cloud> --user <admin>` | Connect to the cloud admin portal. Every `issue` is then also recorded there (Licences page), linked to its client. Only a token is kept, in `~/.nexpos-license/cloud.json`. |
| `cloud-import` | Send the whole `issued.csv` to the cloud once (keys from before connecting). Repeating it skips what is there. |
| `cloud-logout` | Forget the connection on this PC. |

Every key you issue is appended to `issued.csv` next to the private key, so you can find renewals later. Once connected to the cloud
(`cloud-login`), each key is also recorded there; add `--no-cloud` to `issue` to skip that once.

The Keygen app has the same thing: the **Cloud** row on the Keys tab (**Connect...**, then **Send issued.csv**).

## Licences in the cloud (built 2026-11-24)

The admin portal's **Licences** page (and `/api/admin/v1/licenses`, see `docs/online/admin-api.md`) keeps every key with its client:

- **Where keys come from:** issued in the portal; reported by the desktop tools after each `issue` (once connected); pasted (the signature
  is checked); imported from `issued.csv` (details only, no key text); or seen at a connected shop PC's check-in (details only, as before).
- **Linking:** to the client picked, else the client whose branch has that Machine ID, else the client of an earlier key for that PC,
  else the one client with exactly the name in the key. The rest wait under **Not linked**.
- **Following up:** time-limited keys ending within 14 days, in their grace week or expired, and keys not linked to a client, show in
  **Needs you**. Those and Full keys whose updates plan ends within 30 days are listed as renewals in Finance.
- **POS-only clients:** a client can be **POS only** (no Online): no branch or link, no Online or connection warnings. Its licences and
  payments are what the portal follows. Online can be turned on later from the client page.
- **Three kinds of client, one subscription.** New client asks what they bought: **POS only** (a licence key, no Online), **POS +
  Online** (a licence key and Online months) or **Online menu only** (Online months, no POS: no branch, no link, no licence key; the
  owner edits the menu on the owner website with a phone login). Online is the only subscription: its months cover the cloud copy,
  the phone app and the online menu. A menu-only client must be given at least 1 month (the form has no "0" for it). A menu-only
  client that later buys the POS becomes POS + Online from its page ("They bought the POS"), keeping its months.
- **The online menu needs Online time.** When a client's months end, the menu keeps working through the 7-day grace week, then the
  public page, its order button and its sitemap entry answer "not found" until months are added (adding them brings it straight back;
  nothing is deleted, and the owner can still sign in and edit). This applies to every client, so a POS + Online client with 0 months
  has no menu either. The owner's menu editor says why the menu is offline; **Needs you** lists published menus that are offline or
  in their grace week.

### Issuing keys online

Off until you set it up. The signing key then lives on the server, so it is protected the same way as on your PC:

1. Make sure your `private.pem` has a passphrase (`LicenseIssuer protect`). **The server refuses a key without one.**
2. Copy that file to the server, **outside the site folder** (for example next to `cloud.settings.json`, never under `wwwroot`).
3. Set `"LicenseSigningKeyPath": "<full path>"` in the `Cloud` section of `cloud.settings.json` and restart.
4. The Licences page now shows **Issue a key**. Each issue asks for the passphrase; the key is unlocked for that one signature and the
   passphrase is never stored. Five wrong passphrases lock issuing for 15 minutes.

What this protects against, and what it does not:

- **Someone who copies the server's files or database** gets the encrypted key file only. Without the passphrase they cannot sign.
- **Someone who takes over the running server** could capture the passphrase the next time you type it, and then sign keys. That is the
  price of issuing online. If you suspect it: stop online issuing (empty the setting), change the passphrase on your PC, and keep
  issuing from your PC. Changing the key pair itself would invalidate every key already sold, because shop PCs only trust one public key.
- Every key issued here is recorded and cannot be deleted from the portal, so the Licences page is also the audit trail of what the
  server signed.
- A key that does not match the public key the app trusts is refused before anything is handed out.

## Do these now

- **Back up the signing key.** It is at `%USERPROFILE%\.nexpos-license\private.pem` (outside the repo, git-ignored). Copy it to two offline places. If you lose it you cannot issue licenses; if it leaks anyone can. Never commit it.
- **Issuing from several PCs: use the same `private.pem` on all of them.** Copy the file (USB stick or password manager, never git) to the same place on each PC. Then check that `dotnet run --project tools/LicenseIssuer -- pubkey` prints the same text as `SpkiBase64` in `Common/Licensing/LicensePublicKey.cs`. A PC with a different key pair signs keys that every existing build rejects. `issued.csv` is per PC, so merge them now and then.
- **License your own PC for Release builds.** Run the License page on your PC to get its Machine ID. Debug runs do not need a key.

## Limits

- **Casual sharing only.** This stops "one purchase installed in five shops". A developer could decompile the app and patch out the check, and offline keys cannot be revoked.
- **No clock-rollback detection.** A "last seen date" check would lock a shop's till after a single bad clock jump (for example a dead BIOS battery), which seemed worse than the bypass it prevents.
- **Grace week is a choice.** The week after expiry still allows selling. To stop the POS at expiry instead, set `GraceDays` to 0 in `LicenseOptions` (`Application/Services/Licensing/LicenseOptions.cs`, wired in `Web/Program.cs`).

## Still missing before you sell

- **Bulk product import** (Excel or CSV). Entering items by hand is the biggest onboarding cost.
- **Cash-drawer kick pulse.** The app's "cash drawer" is only cash-session accounting. A physical drawer will not pop open on a cash sale yet.

## Where things live

| Part | Location |
|---|---|
| Key format, signing, Machine ID, public key | `Common/Licensing/` |
| Rules (warn, grace, stop), activation | `Application/Services/Licensing/LicenseService.cs` |
| Reading the PC's hardware identity | `Infrastructure/Licensing/WindowsMachineIdProvider.cs` |
| Blocking the POS and sales | `Web/Middlewares/LicenseEnforcementMiddleware.cs` |
| License page and activation | `Web/Controllers/LicenseController.cs`, `Web/Views/License/` |
| Warning banner | `Web/Views/Shared/_LicenseBanner.cshtml` |
| Texts (English and Arabic) | `Web/Resources/SharedResource.{en,ar}.resx`, keys `License_*` |
| Issuer tool (never shipped) | `tools/LicenseIssuer/` |
| Seats: till registry, pairing, seat cap | `Application/Services/Licensing/TillDeviceService.cs`, `Core/Entities/Licensing/TillDevice.cs` (table `TillDevices`) |
| Seats: who may sell (server PC vs till) | `Web/Api/Infrastructure/TillGate.cs`, `RequireSalesLicenseAttribute`, `LicenseEnforcementMiddleware` |
| Seats: API and screens | `Web/Api/Devices/`, `client/src/modules/license/TillsSection.tsx`, `client/src/shell/pages/PairTillPage.tsx` |
| Tests | `Tests/Licensing/` (service, codec), `Tests/Api/License/` (HTTP), `client/src/modules/license/*.test.tsx` |
| License file on the customer's PC | `%LocalAppData%\NexPOS\license.nxl` (`NEXPOS_LICENSE_PATH` overrides it for testing) |

The key format is ECDSA P-256 over a small binary record, written as `NXL1-` plus Base32 text. The private key signs; the app only holds the public key, so it can verify a key offline but nobody can forge or edit one.
