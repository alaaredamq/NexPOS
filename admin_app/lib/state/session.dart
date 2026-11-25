import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;

import '../api/admin_api.dart';
import '../api/models.dart';
import '../config.dart';

/// Where the sign-in is kept between launches. The token opens everything, so it goes to the phone's secure storage
/// (Keychain / Android Keystore), never plain preferences.
abstract class TokenStore {
  Future<String?> read(String key);
  Future<void> write(String key, String? value);
}

class SecureTokenStore implements TokenStore {
  const SecureTokenStore();
  static const _storage = FlutterSecureStorage();

  @override
  Future<String?> read(String key) => _storage.read(key: key);

  @override
  Future<void> write(String key, String? value) => value == null ? _storage.delete(key: key) : _storage.write(key: key, value: value);
}

/// Kept in memory only (tests).
class MemoryTokenStore implements TokenStore {
  final Map<String, String> values = {};

  @override
  Future<String?> read(String key) async => values[key];

  @override
  Future<void> write(String key, String? value) async => value == null ? values.remove(key) : values[key] = value;
}

/// Who is signed in, and the API client that goes with it. A 401 from any call signs the app out (the token expired, or this
/// device was signed out from another one).
class Session extends ChangeNotifier {
  Session({required this.store, this.client});

  final TokenStore store;

  /// The HTTP client every call goes through (a fake one in tests).
  final http.Client? client;

  static const _tokenKey = 'admin_token';
  static const _cloudKey = 'admin_cloud';

  AdminApi? _api;
  Me? me;
  String cloudUrl = defaultCloudUrl;

  /// Why the app went back to the sign-in screen on its own, shown there once.
  String? signedOutReason;

  bool get signedIn => _api?.token != null;

  /// The signed-in API. Only screens behind the sign-in use it.
  AdminApi get api => _api!;

  String get currency => me?.currency ?? r'$';

  /// Goes up by one after every change made from this app (and when it comes back to the front). Every screen that shows cloud
  /// data listens and loads again, so a payment recorded on a client's page is already in Finance when you open it.
  /// Separate from [notifyListeners], which would rebuild the whole app.
  final ValueNotifier<int> dataVersion = ValueNotifier(0);

  void dataChanged() => dataVersion.value++;

  AdminApi _newApi(String? token) => AdminApi(cloudUrl, client: client, token: token, onUnauthorized: _expired, onChanged: dataChanged);

  /// Picks up the sign-in from the last launch. Offline is fine: the screens say so and retry.
  Future<void> restore() async {
    cloudUrl = await store.read(_cloudKey) ?? defaultCloudUrl;
    final token = await store.read(_tokenKey);
    if (token == null) return;
    _api = _newApi(token);
    try {
      me = await _api!.me();
    } on ApiException catch (e) {
      if (e.isUnauthorized) _api = null;
    } on OfflineException {
      // Keep the token; the first screen will retry.
    }
    notifyListeners();
  }

  Future<void> signIn(String username, String password, {String? deviceName, String? cloud}) async {
    if (cloud != null && cloud.trim().isNotEmpty) cloudUrl = cloud.trim().replaceAll(RegExp(r'/+$'), '');
    final api = _newApi(null);
    final token = await api.login(username.trim(), password, deviceName: deviceName);
    me = await api.me();
    _api = api;
    signedOutReason = null;
    await store.write(_tokenKey, token);
    await store.write(_cloudKey, cloudUrl == defaultCloudUrl ? null : cloudUrl);
    notifyListeners();
  }

  /// Signs this device out in the cloud too (best effort: offline, the token is forgotten here anyway).
  Future<void> signOut() async {
    try {
      await _api?.logout();
    } catch (_) {/* forget it locally all the same */}
    await _forget();
  }

  Future<void> _forget() async {
    _api = null;
    me = null;
    await store.write(_tokenKey, null);
    notifyListeners();
  }

  void _expired() {
    if (_api == null) return;
    signedOutReason = 'You were signed out. Sign in again.';
    _forget();
  }
}
