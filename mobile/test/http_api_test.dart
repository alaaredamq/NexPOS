import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:nexpos_owner/api/owner_api.dart';

/// The real client against the JSON the cloud sends (shapes pinned by `Cloud.Tests/OwnerEncryptedReadTests.cs`).
void main() {
  late List<http.Request> seen;

  HttpOwnerApi apiAnswering(Object body, {int status = 200}) {
    seen = [];
    return HttpOwnerApi(
      baseUrl: 'https://cloud.test',
      token: 'nxo_abc',
      client: MockClient((request) async {
        seen.add(request);
        return http.Response(jsonEncode(body), status, headers: {'content-type': 'application/json'});
      }),
    );
  }

  test('a page of rows: path, query, bearer token, and the answer\'s shape', () async {
    final api = apiAnswering({
      'asOfUtc': '2026-11-12T09:30:00Z',
      'rows': [
        {'id': 7, 'payload': 'e1.AAAA'},
        {'id': 9, 'payload': 'e1.BBBB'},
      ],
      'nextCursor': '2026-11-12T09:29:59.1234567Z|9',
      'hasMore': true,
    });

    final page = await api.rows('Products', cursor: '2026-11-12T08:00:00.0000000Z|3', limit: 200);

    final request = seen.single;
    expect(request.method, 'GET');
    expect(request.url.path, '/owner/v1/rows');
    expect(request.url.queryParameters, {'table': 'Products', 'limit': '200', 'cursor': '2026-11-12T08:00:00.0000000Z|3'});
    expect(request.headers['Authorization'], 'Bearer nxo_abc');
    expect(page.rows.map((r) => r.id), [7, 9]);
    expect(page.rows.first.payload, 'e1.AAAA');
    expect(page.nextCursor, '2026-11-12T09:29:59.1234567Z|9');
    expect(page.hasMore, isTrue);
    expect(page.asOf, DateTime.utc(2026, 11, 12, 9, 30));
  });

  test('a shop that has never synced has no "as of" time', () async {
    final api = apiAnswering({'asOfUtc': null, 'rows': [], 'nextCursor': null, 'hasMore': false});

    final page = await api.rows('Products');

    expect(page.asOf, isNull);
    expect(page.rows, isEmpty);
    expect(page.nextCursor, isNull);
    expect(seen.single.url.queryParameters.containsKey('cursor'), isFalse, reason: 'no cursor on the first page');
  });

  test('row ids', () async {
    final api = apiAnswering({'asOfUtc': null, 'ids': [1, 2, 5]});

    expect(await api.rowIds('Customers'), [1, 2, 5]);
    expect(seen.single.url.path, '/owner/v1/row-ids');
    expect(seen.single.url.queryParameters, {'table': 'Customers'});
  });

  test('a refusal carries the cloud\'s code and message (lapsed plan)', () async {
    final api = apiAnswering({'code': 'subscription_inactive', 'message': 'The online plan is not active. Contact your provider.'}, status: 402);

    await expectLater(
      api.rows('Products'),
      throwsA(isA<ApiException>().having((e) => e.code, 'code', 'subscription_inactive').having((e) => e.status, 'status', 402)),
    );
  });

  test('an expired sign-in is recognised', () async {
    final api = apiAnswering({'code': 'unauthorized', 'message': 'Please sign in again.'}, status: 401);

    await expectLater(api.rows('Products'), throwsA(isA<ApiException>().having((e) => e.isUnauthorized, 'isUnauthorized', isTrue)));
  });

  test('no signal is reported as offline, not as a crash', () async {
    final api = HttpOwnerApi(baseUrl: 'https://cloud.test', token: 'x', client: MockClient((_) async => throw http.ClientException('no route')));

    await expectLater(api.rows('Products'), throwsA(isA<OfflineException>()));
  });
}
