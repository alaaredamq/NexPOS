# Coduo Admin (the provider's phone app)

The admin portal (`/admin` on the cloud) on your phone: see and manage every client, their Online plan, branches, phone logins,
POS licences and your books. Android and iOS from one Flutter codebase. English. Separate from the shop owner's app (`mobile/`).

It talks to the cloud's **admin API** (`/api/admin/v1`, see `docs/online/admin-api.md`), which calls the same code as the portal
pages, so a rule changed in the cloud applies to both.

## What it does

| Tab | What you see | What you can do |
|---|---|---|
| **Overview** | What needs you, this month's money in / out / profit / balance, Online plans (active, ending, ended), shops in contact over 24 hours | Tap a "needs you" item to open its client |
| **Clients** | Every client with its plan and contact; search; filters (needs you, connected, waiting, POS only, menu only) | **New client** (POS only, POS + Online, or Online menu only; menu-only always gets months, 12 by default). Open a client: |
| | Client page: Online subscription (ends, grace, whether their menu is on), branches, licences, payments, phone logins, history | Add 1/3/6/12 months, suspend/resume; add a branch; issue a key; record a payment; add / reset / change role / disable / remove phone logins; edit details; turn Online on/off; "They bought the POS"; deactivate; remove. Call or WhatsApp the client |
| | Branch page: connection, last contact, 24-hour strip, the shop PC's licence, data held, recent syncs | New connection link (copy or WhatsApp), re-send everything, disconnect, disable, rename / Machine ID, remove |
| **Finance** | A month or a year: collected, spent, profit, balance, budget bars, 12-month trend, renewals due, money by purpose / payer / category, every line | Record a payment (several purposes at once) or an expense; tap a line to correct or delete it; set the monthly budget |
| **Licences** | Every POS licence key (in force, needs you, not linked, all); search | **Issue a key** (signed on the server with the passphrase, optional payment), record a key issued elsewhere, link to a client, note, renew, copy or WhatsApp the key |
| **More** | Who is signed in, the devices signed in to your admin account | Sign a lost phone out, open the portal in the browser, sign out |

Links, keys and passwords the cloud shows only once open in a sheet with **Copy** and **WhatsApp**.

## How it works

- Sign in with the **same username and password as the portal**. The app gets a token (30 days) and keeps it in the phone's secure
  storage (Keychain / Android Keystore). When the cloud stops accepting it (expired, or that device was signed out under More), the
  app goes back to the sign-in screen.
- Nothing is cached: every screen asks the cloud when it opens. After **any change you make in the app** (a payment, months, a key...),
  every screen loads again in the background, so Finance, the Overview and the lists already show it; the same happens when you
  come back to the app. Changes made elsewhere (the portal, another phone) show then too, or pull down to refresh. Offline, a screen
  says so and offers a retry.
- The key passphrase for **Issue a key** is sent for that one key and never kept, exactly as in the portal.
- The online **menu editor** opens in the browser (it needs the portal's sign-in there).

Layout: `lib/api` (the API client and its models), `lib/state/session.dart` (sign-in), `lib/screens`, `lib/widgets/common.dart`
(cards, pills, the loader, confirm, the "shown once" sheet), `lib/theme.dart` (the portal's colours).

## Run and build

Flutter 3.47 or newer (Dart 3.13), the same setup as the owner app (`mobile/README.md`).

```powershell
cd admin_app
flutter run                                     # on a plugged-in phone
flutter build apk --release                     # build\app\outputs\flutter-apk\app-release.apk
```

The app talks to **`https://coduos.com`** by default (`lib/config.dart`). For another cloud, pass it at build time, or open
**Cloud address** under the sign-in form (kept on the phone):

```powershell
flutter build apk --release --dart-define=CLOUD_URL=https://nexpos.tryasp.net   # the test cloud
flutter run --dart-define=CLOUD_URL=http://10.0.2.2:51158                       # a cloud on this PC (emulator)
```

Debug builds may use plain http (`android/app/src/debug/AndroidManifest.xml`); release builds are https-only.
Android id `com.coduo.admin`, iOS bundle `com.coduo.admin`; the icon is the Coduo Cloud mark (`coduo-cloud-brand-kit`).

## Tests

```powershell
flutter analyze
flutter test
```

`test/fixtures/*.json` are responses captured from a running cloud (every admin API call the app makes), so the tests fail if the
cloud renames a field. `test/support/fake_cloud.dart` serves them; the tests sign in, open every tab and the client page, add months,
create a menu-only client and record a payment for several purposes, and check what reaches the cloud.

To refresh the fixtures after an API change: run the cloud locally, call the endpoints (the list is in `fake_cloud.dart`) and save
each response under `test/fixtures/` with the same name.

## Not done / know before shipping

- **No push alerts.** Renewals and "needs you" show when you open the app; nothing is pushed.
- **The menu editor is web only** (opens the browser; sign in to the portal there).
- **Importing `issued.csv`** is in the portal only (it needs a file).
- **Release signing:** `android/app/build.gradle.kts` still signs release builds with the debug key (Flutter's default). Set up an
  upload key before publishing to the Play Store.
