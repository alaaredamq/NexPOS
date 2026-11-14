# NexPOS Owner (phone app)

The shop owner's phone app: see **Today, Sales (with Reports), Stock and Debts** from the shop's copy in NexPOS Online; tap a sale, a
customer or a product to open its page; **add products, customers and suppliers, edit a product, and adjust or count stock**, all sent to
the shop PC and applied there. Optional **fingerprint / face / PIN lock** and **alerts** for new sales, low stock and answers to changes.
Android and iOS from one Flutter codebase, English and Arabic (RTL).

## How it works

```
shop PC ──encrypts rows with the shop key──▶ cloud (ciphertext only) ──▶ phone ──decrypts with the same key──▶ screens
```

- The phone signs in with an **owner login** made in the cloud portal (Client page > Phone logins). Token in secure storage.
- The shop's data is **end-to-end encrypted** (`Sync/DataKey.cs`). The cloud only hands out ciphertext
  (`GET /owner/v1/rows`, `/row-ids`). The owner types the shop's **recovery code** once (shop PC: Settings > Online > Show recovery
  code); the phone keeps the key in secure storage and decrypts on the device. Sign-out or lock removes it.
- Decrypted rows live **in memory only**, and only the columns the screens use (`ShopData.kept`). Nothing readable is written to disk.
- Every screen says how old its figures are (when the shop PC last sent data). A switched-off PC leaves the copy behind.
- Changes are **sealed with the shop key before they leave the phone** (`lib/crypto/change_seal.dart`): the cloud queues ciphertext it
  cannot read or alter, the shop PC opens it, refuses anything unsealed, and seals its answer back, so product names, quantities and
  refusal reasons are unreadable in the cloud. Routes: `POST /owner/v1/{products|customers|suppliers|product-edits|stock-changes}` with an
  idempotency key. **Manage > Changes** follows each one until the shop PC applies or refuses it. Without the key (locked) nothing is sent.
- **Stock changes:** Add and Remove are relative; **Count** sets the stock to exactly what was counted (the shop works out the difference
  at that moment, so sales made meanwhile do not skew it). Every change is in the product history with the phone user's name.
- **Alerts** (`lib/background/watcher.dart`): about every 15 minutes in the background (Android WorkManager) the phone fetches what
  changed, decrypts it and posts a *local* notification. No server pushes to the phone and no Firebase is involved, so no third party sees
  anything; the price is that it is never instant. Counts only by default, names and amounts are opt-in (lock screens are public).

Layout: `lib/crypto` (key, AES-GCM, sealed changes), `lib/data` (sync into memory, reports, detail pages' data), `lib/api` (cloud client),
`lib/screens`, `lib/state` (session, app lock, alert settings), `lib/background` (alert watcher), `lib/widgets` (motion kit, shared parts).

## Set up this PC (Windows)

Already installed on the dev PC: Flutter at `C:\src\flutter` (on PATH), JDK 17 at `C:\src\jdk17`, Android SDK at `C:\src\android-sdk`
(command-line tools, platform-tools; Gradle fetches the platform, build-tools and NDK on the first build). No emulator is installed on
purpose: test on a real phone.

```powershell
$env:JAVA_HOME = 'C:\src\jdk17'; $env:ANDROID_HOME = 'C:\src\android-sdk'   # also saved as user variables
flutter doctor            # the Android toolchain line should be green
```

## Run on your phone

1. On the phone: Settings > About > tap *Build number* 7 times, then Developer options > turn on **USB debugging**. Plug it in and accept
   the prompt.
2. `C:\src\android-sdk\platform-tools\adb devices` shows the phone (or just `flutter devices`).
3. `cd mobile; flutter run` installs and starts it. Or install a built file:
   `flutter build apk --release` then `adb install -r build\app\outputs\flutter-apk\app-release.apk`.

Cloud address in the app: your real cloud (`https://...`). **Debug builds** also allow plain `http://<PC-LAN-IP>:<port>` for testing
against a cloud on this PC (`android/app/src/debug/AndroidManifest.xml`); **release builds are https-only**.

## Tests

```powershell
flutter analyze
flutter test        # crypto vectors from the C# code, sync, reports, and the screens against a fake cloud
```

`test/support/vectors.dart` holds a key, recovery code and encrypted row produced by the shop PC's own C# code, so the tests fail if the
phone and the till ever disagree on the format.

## Not done / know before shipping

- **Sealed changes need a matching till.** An updated till refuses unsealed changes by design, so an old phone build cannot send to it.
  Update both together.
- **Alerts are not instant push.** Android runs the background check when it likes (about every 15 minutes at best, less in battery
  saver). For instant alerts a Firebase project is needed (a content-free "something changed" ping, then the phone fetches); not built.
  iOS background checks are rarer still and untested.
- Rows are read again from the cloud at every app start (in memory only). Fine for thousands; for a shop with very many receipts
  consider an encrypted on-device cache.
- Release builds are signed with the debug key. Make a real keystore before publishing to a store.
- Arabic text has not been read by a native speaker. There is no offline view of the last figures after the app is closed.
- The app lock hides the app from screenshots and the recent-apps view while it is on (Android `FLAG_SECURE`), which also blocks the
  owner's own screenshots.
- iOS has not been built (needs a Mac).

## Roles and what each login can do

A phone login is **Owner**, **Manager** or **Staff** (the provider picks it in the portal, Client page > Phone logins, and can change it later).
The cloud enforces it: tables a login may not read are never sent, changes it may not make are never queued, and a limited login sees only its own
changes. The app hides whatever a login cannot use. Limits work per table (a row is one encrypted blob), so cost prices, which are inside the
products, are hidden in the app from Manager and Staff but not cryptographically; the cash drawer, orders and users tables are withheld.

| | Owner | Manager | Staff |
|---|---|---|---|
| Today, Sales and Reports (profit is hidden from Manager and Staff) | yes | yes | yes |
| Stock | yes | yes | yes (read only) |
| Customers and debts | yes | yes | no |
| Add products / customers / suppliers, edit products, adjust stock | yes | yes | no |
| Profit, cost prices, margins, **cash drawer** | yes | no | no |
