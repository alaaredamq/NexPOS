import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';
import 'package:local_auth/local_auth.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Proving it is the owner holding the phone: fingerprint or face, or the phone's own PIN or pattern as the fallback.
/// An interface so tests can answer yes or no.
abstract class Biometrics {
  /// The phone has some screen lock to check against. Without one, "locking the app" would lock nothing.
  Future<bool> isSupported();

  /// Shows the system prompt. False when the owner cancels or fails.
  Future<bool> authenticate(String reason);
}

class DeviceBiometrics implements Biometrics {
  const DeviceBiometrics();
  static final _auth = LocalAuthentication();

  @override
  Future<bool> isSupported() async {
    try {
      return await _auth.isDeviceSupported();
    } on PlatformException {
      return false;
    }
  }

  @override
  Future<bool> authenticate(String reason) async {
    try {
      // The PIN fallback stays allowed: an owner whose fingerprint will not read must not be locked out of their own shop.
      return await _auth.authenticate(localizedReason: reason, persistAcrossBackgrounding: true);
    } on LocalAuthException {
      return false;
    } on PlatformException {
      return false;
    }
  }
}

/// Hides the app from the recent-apps thumbnail and from screenshots while the lock is on (Android; a no-op elsewhere).
abstract class PrivacyScreen {
  Future<void> hide(bool hidden);
}

class DevicePrivacyScreen implements PrivacyScreen {
  const DevicePrivacyScreen();
  static const _channel = MethodChannel('nexpos/privacy');

  @override
  Future<void> hide(bool hidden) async {
    try {
      await _channel.invokeMethod<void>('hideFromRecents', hidden);
    } on MissingPluginException {
      // not Android, or a test: nothing to hide
    } on PlatformException {
      // the thumbnail stays visible; the lock itself still works
    }
  }
}

/// The app lock. When it is on, the app asks for the owner's fingerprint (or phone PIN) when it opens and when it comes back after
/// being away for more than [grace]. The shop's key stays in the phone's secure storage; this keeps someone who picks up an unlocked
/// phone from reading the shop's figures.
class AppLock extends ChangeNotifier with WidgetsBindingObserver {
  AppLock({
    required this._prefs,
    required this._biometrics,
    this._privacy = const DevicePrivacyScreen(),
    this.grace = const Duration(seconds: 30),
    this._clock = DateTime.now,
  }) {
    _enabled = _prefs.getBool(_key) ?? false;
    _locked = _enabled; // a cold start always asks
    _privacy.hide(_enabled);
  }

  static const _key = 'app_lock';

  final SharedPreferences _prefs;
  final Biometrics _biometrics;
  final PrivacyScreen _privacy;
  final DateTime Function() _clock;

  /// How long the app may be out of sight before it asks again. Switching to a message and back should not cost a fingerprint.
  final Duration grace;

  late bool _enabled;
  late bool _locked;
  bool _asking = false;
  DateTime? _awaySince;

  bool get enabled => _enabled;
  bool get locked => _enabled && _locked;

  /// Starts watching the app going to the background. Call once, after the binding exists.
  void attach() => WidgetsBinding.instance.addObserver(this);

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (!_enabled) return;
    switch (state) {
      case AppLifecycleState.paused:
      case AppLifecycleState.hidden:
        _awaySince ??= _clock();
      case AppLifecycleState.resumed:
        // While the system prompt is up the app goes inactive, not paused, so asking never re-locks itself.
        final away = _awaySince;
        _awaySince = null;
        if (away != null && _clock().difference(away) >= grace && !_asking) {
          _locked = true;
          notifyListeners();
        }
      case AppLifecycleState.inactive:
      case AppLifecycleState.detached:
        break;
    }
  }

  /// The result of trying to switch the lock on or off.
  Future<SetLock> setEnabled(bool on, {required String reason}) async {
    if (on == _enabled) return SetLock.done;
    if (on && !await _biometrics.isSupported()) return SetLock.unsupported;
    // Either way, prove it is the owner: switching the lock off must not be possible for whoever holds an unlocked phone.
    if (!await _ask(reason)) return SetLock.cancelled;
    _enabled = on;
    _locked = false;
    await _prefs.setBool(_key, on);
    await _privacy.hide(on);
    notifyListeners();
    return SetLock.done;
  }

  /// Shows the prompt; the app opens when it succeeds.
  Future<bool> unlock(String reason) async {
    if (!locked) return true;
    final ok = await _ask(reason);
    if (ok) {
      _locked = false;
      notifyListeners();
    }
    return ok;
  }

  Future<bool> _ask(String reason) async {
    _asking = true;
    try {
      return await _biometrics.authenticate(reason);
    } finally {
      _asking = false;
      _awaySince = null;
    }
  }
}

enum SetLock { done, unsupported, cancelled }
