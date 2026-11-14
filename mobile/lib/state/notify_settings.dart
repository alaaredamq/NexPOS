import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../background/watcher.dart';
import 'session.dart';

/// The platform side of notifications: asking permission and starting or stopping the periodic check. An interface so tests need no phone.
abstract class WatchControl {
  /// Asks to show notifications and starts the periodic check. False when the owner said no.
  Future<bool> enable();
  Future<void> disable();
}

class DeviceWatchControl implements WatchControl {
  const DeviceWatchControl();

  @override
  Future<bool> enable() async {
    if (!await LocalNotifier.requestPermission()) return false;
    await const WatchScheduler().start();
    return true;
  }

  @override
  Future<void> disable() => const WatchScheduler().stop();
}

/// Whether the owner wants to hear about the shop while the app is closed, and how much the notification may say.
class NotifySettings extends ChangeNotifier {
  NotifySettings({required this._prefs, required this._control, required this._session}) {
    _wasSignedIn = _session.signedIn;
    _session.addListener(_onSession);
  }

  final SharedPreferences _prefs;
  final WatchControl _control;
  final Session _session;
  bool _wasSignedIn = false;

  bool get on => WatchSettings.isOn(_prefs);
  bool get details => WatchSettings.showDetails(_prefs);

  /// Turns notifications on or off. Returns false when they could not be turned on (permission refused).
  Future<bool> setOn(bool value) async {
    if (value) {
      if (!await _control.enable()) return false;
      await ShopWatcher.reset(_prefs); // start from now: yesterday's sales are not news
    } else {
      await _control.disable();
    }
    await _prefs.setBool(WatchSettings.onKey, value);
    notifyListeners();
    return true;
  }

  Future<void> setDetails(bool value) async {
    await _prefs.setBool(WatchSettings.detailsKey, value);
    notifyListeners();
  }

  /// Signing out ends the checks: there is no longer anyone to tell.
  void _onSession() {
    final signedIn = _session.signedIn;
    if (_wasSignedIn && !signedIn && on) {
      _control.disable();
      _prefs.setBool(WatchSettings.onKey, false);
      ShopWatcher.reset(_prefs);
      notifyListeners();
    }
    _wasSignedIn = signedIn;
  }

  @override
  void dispose() {
    _session.removeListener(_onSession);
    super.dispose();
  }
}
