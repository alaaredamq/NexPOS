/// The Coduo cloud the admin app talks to: the same cloud as the portal at /admin.
///
/// The live cloud is the default. To build against another one, pass it at build time (no code change):
///   test cloud:   flutter build apk --release --dart-define=CLOUD_URL=https://nexpos.tryasp.net
///   this PC:      flutter run --dart-define=CLOUD_URL=http://10.0.2.2:51158   (debug builds allow plain http)
/// The sign-in screen can also switch cloud (under "Cloud address"), which is kept on the phone.
const defaultCloudUrl = String.fromEnvironment('CLOUD_URL', defaultValue: 'https://coduos.com');
