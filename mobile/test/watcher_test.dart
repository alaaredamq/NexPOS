import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:nexpos_owner/api/owner_api.dart';
import 'package:nexpos_owner/background/watcher.dart';
import 'package:nexpos_owner/crypto/change_seal.dart';
import 'package:nexpos_owner/crypto/shop_key.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fake_api.dart';
import 'support/vectors.dart';

String utc(DateTime d) => d.toUtc().toIso8601String().replaceFirst('T', ' ').replaceFirst('Z', '0000');

void main() {
  final key = ShopKey.parseRecoveryCode(shopRecoveryCode)!;
  late FakeApi api;
  late SharedPreferences prefs;
  late DateTime now;
  var nextId = 1;

  // A row as the till would send it: sealed to its table and id.
  Future<void> put(String table, int id, Map<String, dynamic> row) async =>
      (api.cloudRows[table] ??= []).add((id: id, payload: await key.seal(table, '$id', utf8.encode(jsonEncode(row)))));

  Future<void> sale(double total, {DateTime? at, bool isVoid = false, bool deleted = false}) =>
      put('Receipts', nextId++, {'TotalAmount': '$total', 'ReceiptDate': utc(at ?? now.subtract(const Duration(minutes: 5))), 'IsVoid': isVoid ? 1 : 0, 'IsDeleted': deleted ? 1 : 0});

  Future<void> product(int id, String name, double qty, {double min = 10, bool active = true}) =>
      put('Products', id, {'Name': name, 'StockQuantity': '$qty', 'MinimumStock': '$min', 'IsActive': active ? 1 : 0, 'IsDeleted': 0});

  ShopWatcher watcher() => ShopWatcher(api: api, key: key, prefs: prefs, clock: () => now);

  Future<void> change(String id, ChangeKind kind, ChangeStatus status, Map<String, dynamic> body) async {
    api.stored.add(Change(
      id: id,
      type: kind.type,
      status: status,
      createdAt: now,
      payload: await ChangeSeal.seal(key, kind, 'k-$id-aaaa', body),
    ));
  }

  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    prefs = await SharedPreferences.getInstance();
    api = FakeApi();
    now = DateTime(2026, 11, 12, 15);
    nextId = 1;
    await put('Currencies', 1, {'Name': 'US Dollar', 'Code': 'USD', 'Notation': r'$', 'IsDefault': 1, 'IsActive': 1, 'IsDeleted': 0});
  });

  test('the first look only takes note of where things stand: what is already there is history, not news', () async {
    await sale(40);
    await product(1, 'Milk', 2);
    await change('c1', ChangeKind.customer, ChangeStatus.applied, {'firstName': 'Old'});

    expect(await watcher().check(), isEmpty);

    expect(await watcher().check(), isEmpty, reason: 'and the same history is still not news the second time');
  });

  test('a sale that arrives afterwards is announced once, with only the count by default', () async {
    await watcher().check();
    await sale(40);
    await sale(25.5);

    final alerts = await watcher().check();

    expect(alerts, hasLength(1));
    expect(alerts.single.title, 'New sales');
    expect(alerts.single.body, '2 new sale(s) at your shop.');
    expect(alerts.single.body, isNot(contains('65')));
    expect(await watcher().check(), isEmpty);
  });

  test('with names and amounts allowed, the alert carries the total in the shop\'s currency', () async {
    await prefs.setBool(WatchSettings.detailsKey, true);
    await watcher().check();
    await sale(40);
    await sale(25.5);

    expect((await watcher().check()).single.body, r'2 new sale(s), $65.50');
  });

  test('voided, deleted and long-ago sales are not news', () async {
    await watcher().check();
    await sale(10, isVoid: true);
    await sale(10, deleted: true);
    await sale(10, at: now.subtract(const Duration(days: 4))); // reached the phone late

    expect(await watcher().check(), isEmpty);
  });

  test('a product that runs low is announced once, again only after it was restocked and ran low again', () async {
    await product(1, 'Milk', 50);
    await watcher().check();

    await product(1, 'Milk', 4);
    final first = (await watcher().check()).single;
    expect(first.title, 'Low stock');
    expect(first.body, '1 product(s) need restocking.');

    await product(1, 'Milk', 3); // sold another, still low
    expect(await watcher().check(), isEmpty);

    await product(1, 'Milk', 60); // restocked
    expect(await watcher().check(), isEmpty);
    await product(1, 'Milk', 0); // ran out
    expect(await watcher().check(), hasLength(1));
  });

  test('products that were already low at the first look do not alert, and the names show only when allowed', () async {
    await product(1, 'Milk', 1);
    await watcher().check();
    expect(await watcher().check(), isEmpty);

    await prefs.setBool(WatchSettings.detailsKey, true);
    await product(2, 'Olive oil', 0);
    await product(3, 'Bread', 2);
    await product(4, 'Inactive thing', 0, active: false);

    final alert = (await watcher().check()).single;
    expect(alert.body, 'Olive oil, Bread');
  });

  test('the shop\'s answer to a change the owner sent is announced once', () async {
    await watcher().check();
    await change('c1', ChangeKind.stock, ChangeStatus.applied, {'productId': 1, 'type': 'add', 'quantity': 5});
    await change('c2', ChangeKind.productEdit, ChangeStatus.rejected, {'productId': 1, 'sellingPrice': 0});
    await change('c3', ChangeKind.customer, ChangeStatus.queued, {'firstName': 'Waiting'});

    final alerts = await watcher().check();

    expect(alerts.map((a) => a.title), unorderedEquals(['Change applied', 'A change needs a fix']));
    expect(alerts.map((a) => a.body).join(' '), isNot(contains('Waiting')));
    expect(await watcher().check(), isEmpty);

    api.stored.firstWhere((c) => c.id == 'c3').let((c) => api.stored[api.stored.indexOf(c)] = Change(id: 'c3', type: c.type, status: ChangeStatus.applied, createdAt: now, payload: c.payload));
    expect((await watcher().check()).single.title, 'Change applied');
  });

  test('a customer\'s name is in the alert only when names are allowed', () async {
    await prefs.setBool(WatchSettings.detailsKey, true);
    await watcher().check();
    await change('c1', ChangeKind.customer, ChangeStatus.applied, {'firstName': 'Nadine', 'lastName': 'Fares'});

    expect((await watcher().check()).single.body, 'Nadine Fares');
  });

  test('alerts follow the app\'s language', () async {
    await prefs.setString('language', 'ar');
    await watcher().check();
    await sale(10);

    final alert = (await watcher().check()).single;

    expect(alert.title, 'مبيعات جديدة');
    expect(alert.body, contains('عملية جديدة'));
  });

  test('forgetting where we got to makes the next look a first look again', () async {
    await watcher().check();
    await sale(10);
    await ShopWatcher.reset(prefs);

    expect(await watcher().check(), isEmpty);
  });
}

extension _Let<T> on T {
  R let<R>(R Function(T it) f) => f(this);
}
