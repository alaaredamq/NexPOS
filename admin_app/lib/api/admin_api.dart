import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;

import 'models.dart';

/// The cloud said no. [code] is stable (`unauthorized`, `months_required`, `wrong_passphrase`...); [message] is a sentence to show.
class ApiException implements Exception {
  ApiException(this.status, this.code, this.message);

  final int status;
  final String code;
  final String message;

  bool get isUnauthorized => status == 401;

  @override
  String toString() => message;
}

/// Could not reach the cloud at all (no signal, wrong address, timeout).
class OfflineException implements Exception {
  OfflineException([this.detail]);
  final String? detail;

  @override
  String toString() => 'Could not reach the cloud. Check the connection and try again.';
}

/// Every call of the admin API (`/api/admin/v1`, docs/online/admin-api.md). Stateless apart from the token; one instance per sign-in.
class AdminApi {
  AdminApi(this.baseUrl, {http.Client? client, this.token, this.onUnauthorized}) : _http = client ?? http.Client();

  final String baseUrl;
  final http.Client _http;
  String? token;

  /// Called when the cloud no longer accepts the token (expired, or signed out from another device).
  void Function()? onUnauthorized;

  static const _timeout = Duration(seconds: 25);

  Uri _uri(String path, [Map<String, String?>? query]) {
    final q = {for (final e in (query ?? const {}).entries) if (e.value != null && e.value!.isNotEmpty) e.key: e.value!};
    return Uri.parse('$baseUrl/api/admin/v1$path').replace(queryParameters: q.isEmpty ? null : q);
  }

  Future<dynamic> _send(String method, String path, {Object? body, Map<String, String?>? query}) async {
    final request = http.Request(method, _uri(path, query));
    request.headers['Accept'] = 'application/json';
    if (token != null) request.headers['Authorization'] = 'Bearer $token';
    if (body != null) {
      request.headers['Content-Type'] = 'application/json';
      request.body = jsonEncode(body);
    }
    http.Response response;
    try {
      response = await http.Response.fromStream(await _http.send(request).timeout(_timeout));
    } on TimeoutException {
      throw OfflineException('timeout');
    } on http.ClientException catch (e) {
      throw OfflineException(e.message);
    }
    final text = utf8.decode(response.bodyBytes);
    if (response.statusCode >= 200 && response.statusCode < 300) return text.isEmpty ? null : jsonDecode(text);

    var code = 'error';
    var message = 'The cloud answered ${response.statusCode}.';
    try {
      final error = jsonDecode(text);
      if (error is Map) {
        code = error['code'] as String? ?? code;
        message = error['message'] as String? ?? message;
      }
    } catch (_) {/* not JSON: keep the generic message */}
    if (response.statusCode == 401 && path != '/auth/login') onUnauthorized?.call();
    throw ApiException(response.statusCode, code, message);
  }

  Future<Json> _object(String method, String path, {Object? body, Map<String, String?>? query}) async =>
      ((await _send(method, path, body: body, query: query)) as Map).cast<String, dynamic>();

  Future<List<Json>> _list(String path, {Map<String, String?>? query}) async => jsonList(await _send('GET', path, query: query));

  // ---- sign-in ----

  /// Signs in; the token is kept on this object and returned for safe keeping.
  Future<String> login(String username, String password, {String? deviceName}) async {
    final j = await _object('POST', '/auth/login', body: {'username': username, 'password': password, 'deviceName': deviceName});
    return token = j['token'] as String;
  }

  Future<void> logout() => _send('POST', '/auth/logout');
  Future<Me> me() async => Me.fromJson(await _object('GET', '/me'));
  Future<List<SessionInfo>> sessions() async => (await _list('/sessions')).map(SessionInfo.fromJson).toList();
  Future<void> revokeSession(int id) => _send('DELETE', '/sessions/$id');

  // ---- overview and clients ----

  Future<Overview> overview() async => Overview.fromJson(await _object('GET', '/overview'));

  /// [filter] is `connected`, `waiting`, `attention`, `offline` (POS only) or `menu` (online menu only).
  Future<List<ClientRow>> clients({String? search, String? filter}) async =>
      (await _list('/clients', query: {'search': search, 'filter': filter})).map(ClientRow.fromJson).toList();

  Future<NewClientResult> createClient({
    required String name,
    String? phone,
    String? email,
    String? notes,
    required ClientType type,
    int months = 0,
  }) async =>
      NewClientResult.fromJson(await _object('POST', '/clients',
          body: {'name': name, 'phone': phone, 'email': email, 'notes': notes, 'type': type.code, 'months': months}));

  Future<ClientDetail> client(int id) async => ClientDetail.fromJson(await _object('GET', '/clients/$id'));

  Future<ClientDetail> updateClient(int id, {required String name, String? phone, String? email, String? notes}) async =>
      ClientDetail.fromJson(await _object('PUT', '/clients/$id', body: {'name': name, 'phone': phone, 'email': email, 'notes': notes}));

  Future<void> setClientActive(int id, bool active) => _send('POST', '/clients/$id/${active ? 'activate' : 'deactivate'}');
  Future<void> removeClient(int id) => _send('DELETE', '/clients/$id');

  /// Online on gives a client with no branch its first one, and that branch's link (shown once).
  Future<LinkResult?> setOnline(int id, bool online) async {
    final j = await _object('POST', '/clients/$id/online/${online ? 'enable' : 'disable'}');
    return j['link'] is Map ? LinkResult.fromJson((j['link'] as Map).cast<String, dynamic>()) : null;
  }

  /// An online-menu-only client bought the POS: POS + Online, with a branch and its link (shown once).
  Future<LinkResult?> addPos(int id) async {
    final j = await _object('POST', '/clients/$id/pos/enable');
    return j['link'] is Map ? LinkResult.fromJson((j['link'] as Map).cast<String, dynamic>()) : null;
  }

  Future<List<ClientEvent>> events(int id, {int take = 50}) async =>
      (await _list('/clients/$id/events', query: {'take': '$take'})).map(ClientEvent.fromJson).toList();

  // ---- the Online subscription ----

  Future<Subscription> extend(int id, int months) async =>
      Subscription.fromJson(await _object('POST', '/clients/$id/subscription/extend', body: {'months': months}));

  Future<Subscription> setSuspended(int id, bool suspended) async =>
      Subscription.fromJson(await _object('POST', '/clients/$id/subscription/${suspended ? 'suspend' : 'resume'}'));

  // ---- branches ----

  Future<SiteDetail> addSite(int clientId, String name, {String? machineId}) async =>
      SiteDetail.fromJson(await _object('POST', '/clients/$clientId/sites', body: {'name': name, 'machineId': machineId}));

  Future<SiteDetail> site(String id) async => SiteDetail.fromJson(await _object('GET', '/sites/$id'));

  Future<SiteDetail> updateSite(String id, String name, {String? machineId}) async =>
      SiteDetail.fromJson(await _object('PUT', '/sites/$id', body: {'name': name, 'machineId': machineId}));

  Future<void> removeSite(String id) => _send('DELETE', '/sites/$id');
  Future<LinkResult> newLink(String id) async => LinkResult.fromJson(await _object('POST', '/sites/$id/link'));
  Future<SiteDetail> disconnect(String id) async => SiteDetail.fromJson(await _object('POST', '/sites/$id/disconnect'));
  Future<SiteDetail> resync(String id) async => SiteDetail.fromJson(await _object('POST', '/sites/$id/resync'));

  Future<SiteDetail> setSiteDisabled(String id, bool disabled) async =>
      SiteDetail.fromJson(await _object('POST', '/sites/$id/${disabled ? 'disable' : 'enable'}'));

  // ---- phone logins ----

  Future<List<OwnerLogin>> logins(int clientId) async => (await _list('/clients/$clientId/logins')).map(OwnerLogin.fromJson).toList();

  Future<LoginHandOver> addLogin(int clientId, {required String signIn, String? displayName, String? password, String role = 'owner'}) async =>
      LoginHandOver.fromJson(await _object('POST', '/clients/$clientId/logins',
          body: {'signIn': signIn, 'displayName': displayName, 'password': password, 'role': role}));

  Future<OwnerLogin> updateLogin(int clientId, int loginId, {String? role, bool? isActive}) async =>
      OwnerLogin.fromJson(await _object('PUT', '/clients/$clientId/logins/$loginId', body: {'role': role, 'isActive': isActive}));

  Future<LoginHandOver> resetPassword(int clientId, int loginId) async =>
      LoginHandOver.fromJson(await _object('POST', '/clients/$clientId/logins/$loginId/reset-password'));

  Future<void> removeLogin(int clientId, int loginId) => _send('DELETE', '/clients/$clientId/logins/$loginId');

  // ---- finance ----

  Future<FinanceOptions> financeOptions() async => FinanceOptions.fromJson(await _object('GET', '/finance/options'));

  /// [period] is `m=2026-11` or `y=2026`; null for this month.
  Future<FinanceSummary> financeSummary([String? period]) async {
    final parts = (period ?? '').split('=');
    return FinanceSummary.fromJson(await _object('GET', '/finance/summary', query: parts.length == 2 ? {parts[0]: parts[1]} : null));
  }

  Future<List<Payment>> clientPayments(int clientId) async => (await _list('/clients/$clientId/payments')).map(Payment.fromJson).toList();
  Future<Payment> payment(int id) async => Payment.fromJson(await _object('GET', '/finance/payments/$id'));

  Map<String, Object?> _paymentBody(int? clientId, String? payer, double amount, DateTime date, String method, List<String> purposes,
          String? reference, String? note) =>
      {
        'clientId': clientId,
        'payer': payer,
        'amount': amount,
        'date': isoDay(date),
        'method': method,
        'purposes': purposes,
        'reference': reference,
        'note': note,
      };

  Future<Payment> savePayment({
    int? id,
    int? clientId,
    String? payer,
    required double amount,
    required DateTime date,
    required String method,
    required List<String> purposes,
    String? reference,
    String? note,
  }) async {
    final body = _paymentBody(clientId, payer, amount, date, method, purposes, reference, note);
    return Payment.fromJson(id == null
        ? await _object('POST', '/finance/payments', body: body)
        : await _object('PUT', '/finance/payments/$id', body: body));
  }

  Future<void> deletePayment(int id) => _send('DELETE', '/finance/payments/$id');

  Future<Expense> expense(int id) async => Expense.fromJson(await _object('GET', '/finance/expenses/$id'));

  Future<Expense> saveExpense({int? id, required double amount, required DateTime date, required String category, String? payee, String? note}) async {
    final body = {'amount': amount, 'date': isoDay(date), 'category': category, 'payee': payee, 'note': note};
    return Expense.fromJson(id == null
        ? await _object('POST', '/finance/expenses', body: body)
        : await _object('PUT', '/finance/expenses/$id', body: body));
  }

  Future<void> deleteExpense(int id) => _send('DELETE', '/finance/expenses/$id');

  Future<List<Budget>> budgets() async => (await _list('/finance/budgets')).map(Budget.fromJson).toList();

  /// From [month] (yyyy-MM) on; a null amount means none from that month.
  Future<void> setBudget(String month, {double? incomeTarget, double? spendingLimit}) =>
      _send('PUT', '/finance/budgets/$month', body: {'incomeTarget': incomeTarget, 'spendingLimit': spendingLimit});

  Future<void> deleteBudget(String month) => _send('DELETE', '/finance/budgets/$month');

  // ---- licences ----

  /// [show] is `current` (default), `attention`, `unassigned` or `all`.
  Future<List<Licence>> licences({String show = 'current', String? search, int? clientId}) async =>
      (await _list('/licenses', query: {'show': show, 'search': search, 'clientId': clientId?.toString()})).map(Licence.fromJson).toList();

  Future<List<Licence>> clientLicences(int clientId) async => (await _list('/clients/$clientId/licenses')).map(Licence.fromJson).toList();
  Future<Licence> licence(int id) async => Licence.fromJson(await _object('GET', '/licenses/$id'));
  Future<Signer> signer() async => Signer.fromJson(await _object('GET', '/licenses/signer'));

  /// Signs a key on the server. The passphrase unlocks the signing key for this one call and is never stored.
  Future<IssuedLicence> issueLicence({
    int? clientId,
    required String machineId,
    String? customer,
    required String kind,
    required String plan,
    int seats = 1,
    int? days,
    DateTime? until,
    String? note,
    double? paymentAmount,
    String? paymentMethod,
    required String passphrase,
  }) async =>
      IssuedLicence.fromJson(await _object('POST', '/licenses/issue', body: {
        'clientId': clientId,
        'machineId': machineId,
        'customer': customer,
        'kind': kind,
        'plan': plan,
        'seats': seats,
        'days': days,
        'until': until == null ? null : isoDay(until),
        'note': note,
        'paymentAmount': paymentAmount,
        'paymentMethod': paymentMethod,
        'passphrase': passphrase,
      }));

  /// Records a key issued elsewhere (the signature is checked).
  Future<Licence> recordLicence(String key, {int? clientId, String? note}) async =>
      Licence.fromJson(await _object('POST', '/licenses/record', body: {'key': key, 'clientId': clientId, 'note': note}));

  /// Links the licence to [clientId] (null unlinks) and sets its note. Missing fields are kept by the cloud, so both are always sent.
  Future<Licence> updateLicence(int id, {int? clientId, String? note}) async =>
      Licence.fromJson(await _object('PUT', '/licenses/$id', body: {'clientId': clientId, 'note': note}));

  Future<void> deleteLicence(int id) => _send('DELETE', '/licenses/$id');
}

String isoDay(DateTime d) =>
    '${d.year.toString().padLeft(4, '0')}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';
