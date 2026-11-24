import 'dart:convert';

import 'package:coduo_admin/api/admin_api.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

import 'support/fake_cloud.dart';

void main() {
  test('signs in, then sends the token on every call', () async {
    final cloud = FakeCloud();
    final api = AdminApi('https://cloud.test', client: cloud.client);
    expect(await api.login('owner', 'correct-horse-battery', deviceName: 'Test'), 'nxa_test');
    final me = await api.me();
    expect(me.username, 'owner');
    expect(cloud.sent('GET', '/me').single.headers['Authorization'], 'Bearer nxa_test');
    expect(jsonDecode(cloud.sent('POST', '/auth/login').single.body)['deviceName'], 'Test');
  });

  test('a refusal comes back as its code and sentence', () async {
    final api = AdminApi('https://cloud.test',
        client: MockClient((_) async => http.Response(jsonEncode((fixture('error_passphrase') as List)[1]), 400)), token: 't');
    await expectLater(
      api.issueLicence(machineId: 'X', kind: 'full', plan: 'pro', passphrase: 'wrong'),
      throwsA(isA<ApiException>().having((e) => e.code, 'code', 'wrong_passphrase').having((e) => e.message, 'message', contains('passphrase'))),
    );
  });

  test('a 401 tells the session it was signed out', () async {
    var told = 0;
    final api = AdminApi('https://cloud.test', client: FakeCloud().client, token: 'old', onUnauthorized: () => told++);
    await expectLater(api.overview(), throwsA(isA<ApiException>().having((e) => e.isUnauthorized, 'unauthorized', isTrue)));
    expect(told, 1);
  });

  test('no network is an OfflineException, not a crash', () async {
    final api = AdminApi('https://cloud.test', client: MockClient((_) async => throw http.ClientException('no route')), token: 't');
    await expectLater(api.overview(), throwsA(isA<OfflineException>()));
  });

  test('a payment sends its purposes as a list and its day as yyyy-MM-dd', () async {
    final cloud = FakeCloud();
    final api = AdminApi('https://cloud.test', client: cloud.client, token: 'nxa_test');
    await api.savePayment(clientId: 1, amount: 650, date: DateTime(2026, 11, 3), method: 'Cash', purposes: ['Online subscription', 'POS licence']);
    final body = jsonDecode(cloud.sent('POST', '/finance/payments').single.body) as Map;
    expect(body['purposes'], ['Online subscription', 'POS licence']);
    expect(body['date'], '2026-11-03');
    expect(body['amount'], 650);
  });

  test('queries leave out what is empty', () async {
    final cloud = FakeCloud();
    final api = AdminApi('https://cloud.test', client: cloud.client, token: 'nxa_test');
    await api.clients(search: '', filter: 'menu');
    expect(cloud.sent('GET', '/clients').single.url.query, 'filter=menu');
    await api.financeSummary('y=2026');
    expect(cloud.sent('GET', '/finance/summary').single.url.query, 'y=2026');
  });
}
