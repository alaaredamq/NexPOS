import 'package:nexpos_owner/state/app_lock.dart';
import 'package:nexpos_owner/state/notify_settings.dart';

/// Stands in for the fingerprint sensor: answers what the test says.
class FakeBiometrics implements Biometrics {
  bool supported = true;
  bool accept = true;
  int asked = 0;
  String? lastReason;

  @override
  Future<bool> isSupported() async => supported;

  @override
  Future<bool> authenticate(String reason) async {
    asked++;
    lastReason = reason;
    return accept;
  }
}

class FakePrivacy implements PrivacyScreen {
  bool hidden = false;

  @override
  Future<void> hide(bool hidden) async => this.hidden = hidden;
}

/// Stands in for the permission prompt and the background scheduler.
class FakeWatchControl implements WatchControl {
  bool allow = true;
  bool running = false;
  int enableCalls = 0;

  @override
  Future<bool> enable() async {
    enableCalls++;
    if (!allow) return false;
    running = true;
    return true;
  }

  @override
  Future<void> disable() async => running = false;
}
