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
   dotnet run --project tools/LicenseIssuer -- issue --machine <ID> --customer "<name>" --kind full|provisional|trial
   ```

   | Option | Meaning |
   |---|---|
   | `--days N` or `--until yyyy-MM-dd` | Length. Defaults: full 365 days, provisional 45, trial 14. |
   | `--out <file>` | Also save the key to a file. |
   | `--allow-past` | Allow an end date in the past (only for testing the expiry screens). |
   | `--key <path>` | Use a private key somewhere else. |

3. Send the printed key (`NXL1-...`) to the customer. They paste it on the License page and press **Activate**. No internet needed.

### Packages with several PCs (seats)

A key can cover more than one PC: `--seats N` (1 to 255, default 1) is the package size, **the store server plus its tills**. The GUI keygen has a "PCs (package)" box for the same thing. The key is still bound to the **store server's** Machine ID; tills are browsers that open the server, so they show the server's ID and need no key of their own. The seat count is part of the signed key, so it cannot be raised by editing it.

```powershell
dotnet run --project tools/LicenseIssuer -- issue --machine <server ID> --customer "<name>" --kind full --seats 3
```

- **Compatibility:** a 1-seat key is written exactly as before, so every installed build still accepts it. A multi-seat key (format version 2) is refused by builds that predate seats with "unsupported version", never read as a 1-PC key. Ship the update before issuing multi-seat keys to a customer.
- **How seats are enforced (built 2026-11-06):** the server PC is always the first seat. Every other PC is a *till* and must be paired once: on the License page (shown when the key covers more than 1 PC) an administrator presses **Add a till**, which reserves a seat and shows a one-time code (`XXXX-XXXX`, valid 15 minutes). On the till PC, open `/app/pair-till`, type the code, and the PC receives a device cookie (`nexpos_till`, HttpOnly, 5 years). After that it can sell. **Add a till** is refused once `Seats - 1` tills exist; **Remove till** frees the seat and cuts that PC off at its next request. The count is checked on the server from the signed key, **offline**, never by the cloud.
- **What is gated:** only the sale endpoints (`[RequireSalesLicense]`, and the legacy POS pages/checkout by URL). An unpaired PC can still sign in and read. Refusals are 403 `device_not_registered` or `seats_exceeded`. If a smaller package is installed later, the oldest tills keep their seats and the newest are refused until a seat is freed (the License page flags them "Over the limit").
- **Pairing is throttled** (5 wrong codes per address per 15 minutes) and only hashes are stored (code and token), in the `TillDevices` table, which is never synced to the cloud.
- **Not enforceable yet:** the app still listens on `localhost:5000` only, so no other PC can connect and every request counts as the server's own. The check starts to matter the day the server is opened to the shop network ("store server mode", `docs/online/STORE-SERVER.md`). It trusts only the socket address (never `X-Forwarded-For`), so **do not put a reverse proxy on the server PC in front of a multi-seat install** - every request would look local.
- **Backstop gap:** the sale services (`POSCheckoutService`, `OrderService`) still only repeat the licence check, not the till check, because they have no request to look at. The endpoints are the gate.
- `issued.csv` gets a `seats` column (last, so older rows still line up).

Renewing is the same thing: issue a new key and paste it in. A newer key replaces an older one; an older key can never replace a newer one (same day: Full beats time-limited).

If a customer reinstalls Windows or swaps a disk, the key normally still works (2 of the PC's 4 hardware fingerprints must still match). For a new PC, issue a key for the new Machine ID and move their data with Backup/Restore.

Other tool commands:

| Command | Purpose |
|---|---|
| `keygen` | Create the signing key pair. Already done once; it refuses to overwrite. |
| `pubkey` | Print the public key (the one embedded in the app). |
| `inspect <key>` | Decode a key and verify it against the app's public key. |
| `protect` | Add a passphrase to the private key. The key does not change, so existing licenses stay valid. |

Every key you issue is appended to `issued.csv` next to the private key, so you can find renewals later.

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
