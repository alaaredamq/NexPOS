import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

/// The cloud said no. [code] is stable and machine-readable (`unauthorized`, `subscription_inactive`...), [message] is for a person.
class ApiException implements Exception {
  ApiException(this.status, this.code, this.message);

  final int status;
  final String code;
  final String message;

  bool get isUnauthorized => status == 401;

  @override
  String toString() => 'ApiException($status, $code, $message)';
}

/// Could not reach the cloud at all (no signal, wrong address, timeout).
class OfflineException implements Exception {
  OfflineException([this.detail]);
  final String? detail;
  @override
  String toString() => 'OfflineException($detail)';
}

/// The kinds of change the phone can ask the shop PC to make. [route] is the owner-API path; the cloud turns it into a command.
enum ChangeKind {
  customer('customers'),
  supplier('suppliers'),
  product('products');

  const ChangeKind(this.route);
  final String route;

  static ChangeKind? fromType(String type) {
    switch (type) {
      case 'customer.create':
        return customer;
      case 'supplier.create':
        return supplier;
      case 'product.create':
        return product;
    }
    return null;
  }
}

enum ChangeStatus {
  queued,
  delivered,
  applied,
  rejected,
  discarded;

  static ChangeStatus parse(String value) =>
      ChangeStatus.values.firstWhere((s) => s.name == value, orElse: () => ChangeStatus.queued);

  /// Still on its way to the shop; the owner can expect it to finish by itself.
  bool get isPending => this == queued || this == delivered;
}

/// One change the owner asked for and what became of it.
class Change {
  Change({
    required this.id,
    required this.type,
    required this.status,
    required this.createdAt,
    required this.payload,
    this.completedAt,
    this.code,
    this.message,
    this.data,
  });

  factory Change.fromJson(Map<String, dynamic> json) => Change(
        id: json['id'] as String,
        type: json['type'] as String,
        status: ChangeStatus.parse(json['status'] as String),
        createdAt: DateTime.parse(json['createdAtUtc'] as String),
        completedAt: json['completedAtUtc'] == null ? null : DateTime.parse(json['completedAtUtc'] as String),
        code: json['code'] as String?,
        message: json['message'] as String?,
        data: json['data'] as Map<String, dynamic>?,
        payload: (json['payload'] as Map<String, dynamic>?) ?? const {},
      );

  final String id;
  final String type;
  final ChangeStatus status;
  final DateTime createdAt;
  final DateTime? completedAt;
  final String? code;
  final String? message;
  final Map<String, dynamic>? data;
  final Map<String, dynamic> payload;

  ChangeKind? get kind => ChangeKind.fromType(type);

  /// What to call it in a list: the name the owner typed.
  String get title {
    final p = payload;
    final name = p['name'] ?? '${p['firstName'] ?? ''} ${p['lastName'] ?? ''}'.trim();
    return (name as Object?).toString();
  }

  /// Field -> raw messages, when the shop refused particular values.
  Map<String, List<String>> get fieldErrors {
    final errors = data?['errors'];
    if (errors is! Map) return const {};
    return {
      for (final e in errors.entries)
        e.key.toString(): [for (final m in (e.value as List)) m.toString()],
    };
  }
}

class Option {
  Option(this.id, this.name);
  final int id;
  final String name;
}

class CurrencyOption {
  CurrencyOption(this.id, this.code, this.notation, this.isDefault);
  final int id;
  final String code;
  final String notation;
  final bool isDefault;
}

/// The choices the forms need, from the cloud copy of the shop.
class Lookups {
  Lookups({required this.categories, required this.suppliers, required this.currencies, this.asOf});

  factory Lookups.fromJson(Map<String, dynamic> json) {
    final data = json['data'] as Map<String, dynamic>;
    List<Map<String, dynamic>> list(String key) => [for (final e in (data[key] as List)) e as Map<String, dynamic>];
    return Lookups(
      categories: [for (final c in list('categories')) Option(c['id'] as int, c['name'] as String)],
      suppliers: [for (final s in list('suppliers')) Option(s['id'] as int, s['name'] as String)],
      currencies: [
        for (final c in list('currencies'))
          CurrencyOption(c['id'] as int, c['code'] as String, c['notation'] as String, c['isDefault'] as bool),
      ],
      asOf: json['asOfUtc'] == null ? null : DateTime.parse(json['asOfUtc'] as String),
    );
  }

  final List<Option> categories;
  final List<Option> suppliers;
  final List<CurrencyOption> currencies;
  final DateTime? asOf;

  int? get defaultCurrencyId {
    for (final c in currencies) {
      if (c.isDefault) return c.id;
    }
    return currencies.isEmpty ? null : currencies.first.id;
  }
}

class SignedIn {
  SignedIn({required this.token, required this.displayName, required this.clientName, this.siteName});
  final String token;
  final String displayName;
  final String clientName;
  final String? siteName;
}

/// Everything the app asks of the cloud. An interface so screens can be tested with a fake.
abstract class OwnerApi {
  Future<SignedIn> signIn(String email, String password);
  Future<void> signOut();
  Future<Lookups> lookups();

  /// Queues a change. [idempotencyKey] must be new for each change and reused when the same one is retried.
  Future<Change> submit(ChangeKind kind, Map<String, dynamic> body, String idempotencyKey);
  Future<List<Change>> changes();
  Future<Change> discard(String id);
}

class HttpOwnerApi implements OwnerApi {
  HttpOwnerApi({required String baseUrl, this.token, http.Client? client})
      : _base = Uri.parse(baseUrl.endsWith('/') ? baseUrl : '$baseUrl/'),
        _http = client ?? http.Client();

  final Uri _base;
  final http.Client _http;
  final String? token;

  static const _timeout = Duration(seconds: 20);

  Map<String, String> get _headers => {
        'Content-Type': 'application/json',
        'Accept': 'application/json',
        if (token != null) 'Authorization': 'Bearer $token',
      };

  Uri _uri(String path) => _base.resolve('owner/v1/$path');

  Future<dynamic> _send(Future<http.Response> Function() call) async {
    final http.Response response;
    try {
      response = await call().timeout(_timeout);
    } on TimeoutException {
      throw OfflineException('timeout');
    } on http.ClientException catch (e) {
      throw OfflineException(e.message);
    } on Exception catch (e) {
      // SocketException and TLS failures: nothing reached the cloud.
      throw OfflineException(e.toString());
    }

    final text = utf8.decode(response.bodyBytes);
    if (response.statusCode >= 200 && response.statusCode < 300) {
      return text.isEmpty ? null : jsonDecode(text);
    }
    var code = 'http_${response.statusCode}';
    var message = 'The cloud answered ${response.statusCode}.';
    try {
      final body = jsonDecode(text) as Map<String, dynamic>;
      code = (body['code'] as String?) ?? code;
      message = (body['message'] as String?) ?? message;
    } catch (_) {
      // not JSON; keep the generic text
    }
    throw ApiException(response.statusCode, code, message);
  }

  @override
  Future<SignedIn> signIn(String email, String password) async {
    final json = await _send(() => _http.post(_uri('auth/login'),
        headers: _headers, body: jsonEncode({'email': email, 'password': password}))) as Map<String, dynamic>;
    return SignedIn(
      token: json['token'] as String,
      displayName: json['displayName'] as String,
      clientName: json['clientName'] as String,
      siteName: json['siteName'] as String?,
    );
  }

  @override
  Future<void> signOut() async {
    await _send(() => _http.post(_uri('auth/logout'), headers: _headers));
  }

  @override
  Future<Lookups> lookups() async =>
      Lookups.fromJson(await _send(() => _http.get(_uri('lookups'), headers: _headers)) as Map<String, dynamic>);

  @override
  Future<Change> submit(ChangeKind kind, Map<String, dynamic> body, String idempotencyKey) async {
    final json = await _send(() => _http.post(_uri(kind.route),
        headers: {..._headers, 'Idempotency-Key': idempotencyKey}, body: jsonEncode(body))) as Map<String, dynamic>;
    return Change.fromJson(json);
  }

  @override
  Future<List<Change>> changes() async {
    final json = await _send(() => _http.get(_uri('commands?limit=50'), headers: _headers)) as List<dynamic>;
    return [for (final c in json) Change.fromJson(c as Map<String, dynamic>)];
  }

  @override
  Future<Change> discard(String id) async =>
      Change.fromJson(await _send(() => _http.delete(_uri('commands/$id'), headers: _headers)) as Map<String, dynamic>);
}
