/// The Coduo cloud the app always talks to. Owners never type or see it.
///
/// The live cloud is the default. To build against another cloud, pass it at build time (no code change):
///   test cloud:   flutter build apk --release --dart-define=CLOUD_URL=https://nexpos.tryasp.net
///   this PC:      flutter run --dart-define=CLOUD_URL=http://10.0.2.2:51158   (debug builds allow plain http)
/// To change the default for every build, edit the defaultValue below.
const cloudUrl = String.fromEnvironment('CLOUD_URL', defaultValue: 'https://coduos.com');
