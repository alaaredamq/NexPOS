# NexPOS Owner (phone app)

See and manage your shop from your phone. Signs in to the NexPOS cloud (never to a till PIN); changes are queued in the
cloud and applied by the shop PC.

## Run it in Android Studio (against a cloud on this PC)

One-time setup

1. Install the Flutter SDK (this PC: `C:\src\flutter`) and put `C:\src\flutter\bin` on PATH.
2. Android Studio: **Settings > Plugins** > install **Flutter** (it pulls in Dart). Restart.
3. **Settings > Languages & Frameworks > Flutter** > SDK path = `C:\src\flutter`.
4. **Device Manager** > make sure an emulator has a system image (or plug in a phone with USB debugging).
5. In `mobile/` run `flutter pub get` (Android Studio offers it as "Pub get" on `pubspec.yaml`).

Every time

1. Start the cloud: `dotnet run --project Cloud` (it listens on `http://localhost:51158`).
2. Portal > open a client that has a branch and an active subscription > **Phone app sign-ins** > **Add a sign-in**.
3. In Android Studio pick the emulator, open `lib/main.dart`, press **Run**.
4. Sign in with that email and password. A debug build pre-fills the cloud address with `http://10.0.2.2:51158`
   (10.0.2.2 is how the emulator names the PC it runs on). On a real phone use the PC's LAN address instead and make
   sure the cloud listens on it.

The shop PC must be connected to that same cloud for the lookups (categories, suppliers, currencies) and for changes to
move from "Waiting for shop" to "Done".

Plain `http://` only works in debug builds (`android/app/src/debug/AndroidManifest.xml`). A release build needs `https://`.

## Tests

    flutter test
    flutter analyze
