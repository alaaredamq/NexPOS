# NexPOS licensing

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

1. Install NexPOS on the customer's PC and open the **License** page (sidebar, or `/en/License`; no login needed). Copy the **Machine ID** (`XXXXX-XXXXX-XXXXX-XXXXX`), or read it over AnyDesk.
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

- **Back up the signing key.** It is at `C:\Users\Alaar\.nexpos-license\private.pem` (outside the repo, git-ignored). Copy it to two offline places. If you lose it you cannot issue licenses; if it leaks anyone can. Never commit it.
- **License your own PC for Release builds.** Your Machine ID is `KRV52-6VOSC-BDDXQ-3OK2A`. Debug runs do not need a key.

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
| Tests (82) | `Tests/Licensing/` |
| License file on the customer's PC | `%LocalAppData%\NexPOS\license.nxl` (`NEXPOS_LICENSE_PATH` overrides it for testing) |

The key format is ECDSA P-256 over a small binary record, written as `NXL1-` plus Base32 text. The private key signs; the app only holds the public key, so it can verify a key offline but nobody can forge or edit one.
