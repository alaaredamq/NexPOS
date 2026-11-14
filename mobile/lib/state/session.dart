import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../api/owner_api.dart';
import '../crypto/change_seal.dart';
import '../crypto/shop_key.dart';
import 'permissions.dart';

/// Where the sign-in token lives. The real one is the phone's secure storage (Keychain / Keystore); tests use memory.
abstract class TokenStore {
  Future<String?> read();
  Future<void> write(String? token);
}

class SecureTokenStore implements TokenStore {
  const SecureTokenStore();
  static const _key = 'owner_token';
  static const _storage = FlutterSecureStorage();

  @override
  Future<String?> read() => _storage.read(key: _key);

  @override
  Future<void> write(String? token) =>
      token == null ? _storage.delete(key: _key) : _storage.write(key: _key, value: token);
}

class MemoryTokenStore implements TokenStore {
  String? value;
  @override
  Future<String?> read() async => value;
  @override
  Future<void> write(String? token) async => value = token;
}

typedef ApiFactory = OwnerApi Function(String baseUrl, String? token);

/// Who is signed in, to which cloud, in which language. Screens listen to it and rebuild.
class Session extends ChangeNotifier {
  Session({required this._prefs, required this._tokens, ApiFactory? apiFactory})
      : _apiFactory = apiFactory ?? ((url, token) => HttpOwnerApi(baseUrl: url, token: token));

  static const _urlKey = 'cloud_url';
  static const _langKey = 'language';
  static const _nameKey = 'display_name';
  static const _clientKey = 'client_name';
  static const _permKey = 'permissions';
  static const _roleKey = 'role';

  final SharedPreferences _prefs;
  final TokenStore _tokens;
  final ApiFactory _apiFactory;

  String? _token;
  OwnerApi? _api;

  String get cloudUrl => _prefs.getString(_urlKey) ?? '';
  String get displayName => _prefs.getString(_nameKey) ?? '';
  String get clientName => _prefs.getString(_clientKey) ?? '';

  /// What this login may do. A phone signed in before roles existed keeps everything (the cloud still decides what it will serve).
  int get permissions => _prefs.getInt(_permKey) ?? Perm.all;
  String get role => _prefs.getString(_roleKey) ?? 'owner';

  /// Whether this login may [permission] (a `Perm` bit).
  bool can(int permission) => permissions & permission == permission;
  bool get signedIn => _token != null && _api != null;

  /// The api to use while signed in. Only call after checking [signedIn].
  OwnerApi get api => _api!;

  Locale get locale => Locale(_prefs.getString(_langKey) ?? 'en');

  /// Picks up where the owner left off: a saved sign-in is used again without asking.
  Future<void> restore() async {
    _token = await _tokens.read();
    if (_token != null && cloudUrl.isNotEmpty) {
      _api = _apiFactory(cloudUrl, _token);
    } else {
      _token = null;
    }
    notifyListeners();
  }

  Future<void> signIn({required String cloudUrl, required String email, required String password}) async {
    final url = cloudUrl.trim();
    final result = await _apiFactory(url, null).signIn(email.trim(), password);
    await _tokens.write(result.token);
    await _prefs.setString(_urlKey, url);
    await _prefs.setString(_nameKey, result.displayName);
    await _prefs.setString(_clientKey, result.clientName);
    await _prefs.setInt(_permKey, result.permissions);
    await _prefs.setString(_roleKey, result.role);
    _token = result.token;
    _api = _apiFactory(url, result.token);
    notifyListeners();
  }

  Future<void> signOut() async {
    try {
      await _api?.signOut();
    } catch (_) {
      // Signing out must work with no signal; the token also expires by itself.
    }
    await _forget();
  }

  /// The cloud no longer accepts the token (signed out elsewhere, expired): go back to the sign-in screen.
  Future<void> handleUnauthorized() => _forget();

  Future<void> _forget() async {
    await _tokens.write(null);
    _token = null;
    _api = null;
    notifyListeners();
  }

  Future<void> setLanguage(String code) async {
    await _prefs.setString(_langKey, code);
    notifyListeners();
  }
}

/// The owner's changes and their progress. Polls while the app is open so "Waiting for shop" turns into "Done" by itself.
class ChangesController extends ChangeNotifier {
  ChangesController(this._session, {this.keyOf});

  /// Where to get the shop key from when a change comes back sealed. Null in places that never see sealed changes.
  final ShopKey? Function()? keyOf;

  final Session _session;
  Timer? _timer;
  List<Change> items = const [];
  bool loading = false;
  Object? error;

  int get pending => items.where((c) => c.status.isPending).length;
  int get needFixing => items.where((c) => c.status == ChangeStatus.rejected).length;

  void start() {
    _timer ??= Timer.periodic(const Duration(seconds: 6), (_) => refresh());
    refresh();
  }

  void stop() {
    _timer?.cancel();
    _timer = null;
  }

  void reset() {
    items = const [];
    error = null;
  }

  Future<void> refresh() async {
    if (!_session.signedIn || loading) return;
    loading = true;
    try {
      final fetched = await _session.api.changes();
      final key = keyOf?.call();
      items = [for (final c in fetched) await ChangeSeal.open(key, c)];
      error = null;
    } on ApiException catch (e) {
      error = e;
      if (e.isUnauthorized) await _session.handleUnauthorized();
    } catch (e) {
      error = e;
    } finally {
      loading = false;
      notifyListeners();
    }
  }

  Future<void> discard(Change change) async {
    try {
      await _session.api.discard(change.id);
    } finally {
      await refresh();
    }
  }

  @override
  void dispose() {
    stop();
    super.dispose();
  }
}
