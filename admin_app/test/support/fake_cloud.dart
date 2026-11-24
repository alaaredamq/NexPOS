import 'dart:convert';
import 'dart:io';

import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

/// A response the cloud really sent (captured from a running cloud into test/fixtures), so a renamed field fails here.
dynamic fixture(String name) => jsonDecode(File('test/fixtures/$name.json').readAsStringSync());

/// A stand-in for the cloud's admin API: answers each route with a fixture and remembers every request.
class FakeCloud {
  final List<http.Request> requests = [];

  /// Routes answered differently from the default, by "METHOD /path" (path after /api/admin/v1, without the query).
  final Map<String, http.Response Function(http.Request)> overrides = {};

  late final http.Client client = MockClient((request) async {
    requests.add(request);
    final path = request.url.path.replaceFirst('/api/admin/v1', '');
    final key = '${request.method} $path';
    if (overrides[key] case final handler?) return handler(request);
    if (path != '/auth/login' && request.headers['Authorization'] != 'Bearer nxa_test') {
      return _json({'code': 'unauthorized', 'message': 'Please sign in again.'}, 401);
    }
    return switch (key) {
      'POST /auth/login' => _login(request),
      'GET /me' => _json(fixture('me')),
      'GET /sessions' => _json(fixture('sessions')),
      'GET /overview' => _json(fixture('overview')),
      'GET /clients' => _json(fixture('clients')),
      'POST /clients' => _json(fixture(jsonDecode(request.body)['type'] == 'menu' ? 'client_create_menu' : 'client_create_online'), 201),
      'GET /finance/options' => _json(fixture('options')),
      'GET /finance/summary' => _json(fixture('summary')),
      'POST /finance/payments' => _json(fixture('payment'), 201),
      'GET /licenses' => _json(fixture('licenses')),
      'GET /licenses/signer' => _json(fixture('signer')),
      _ when RegExp(r'^GET /clients/\d+$').hasMatch(key) => _json(fixture('client_detail')),
      _ when RegExp(r'^GET /clients/\d+/licenses$').hasMatch(key) => _json(fixture('licenses')),
      _ when RegExp(r'^GET /clients/\d+/payments$').hasMatch(key) => _json(fixture('payments')),
      _ when RegExp(r'^GET /clients/\d+/events$').hasMatch(key) => _json(fixture('events')),
      _ when RegExp(r'^POST /clients/\d+/subscription/extend$').hasMatch(key) => _json(fixture('extend')),
      _ when key.startsWith('GET /sites/') => _json(fixture('site')),
      _ => _json({'code': 'not_found', 'message': 'No such route in the fake cloud: $key'}, 404),
    };
  });

  http.Response _login(http.Request request) {
    final body = jsonDecode(request.body) as Map;
    if (body['password'] != 'correct-horse-battery') {
      return _json({'code': 'invalid_credentials', 'message': 'The username or password is not right.'}, 401);
    }
    return _json({...fixture('login') as Map, 'token': 'nxa_test'});
  }

  /// The requests sent to "METHOD /path".
  List<http.Request> sent(String method, String path) =>
      requests.where((r) => r.method == method && r.url.path == '/api/admin/v1$path').toList();
}

http.Response _json(Object? body, [int status = 200]) =>
    http.Response.bytes(utf8.encode(jsonEncode(body)), status, headers: {'content-type': 'application/json; charset=utf-8'});
