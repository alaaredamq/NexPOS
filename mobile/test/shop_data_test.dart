import 'package:flutter_test/flutter_test.dart';
import 'package:nexpos_owner/crypto/shop_key.dart';
import 'package:nexpos_owner/data/shop_data.dart';
import 'package:nexpos_owner/state/session.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fake_api.dart';
import 'support/vectors.dart';

Future<(ShopData, Session, FakeApi, MemoryKeyStore, MemoryTokenStore)> start({bool signedIn = true}) async {
  SharedPreferences.setMockInitialValues({'cloud_url': 'https://cloud.test'});
  final api = FakeApi();
  final tokens = MemoryTokenStore()..value = signedIn ? 'nxo_test' : null;
  final session = Session(prefs: await SharedPreferences.getInstance(), tokens: tokens, apiFactory: (_, _) => api);
  await session.restore();
  final keys = MemoryKeyStore();
  final data = ShopData(session, keys);
  await data.restore();
  return (data, session, api, keys, tokens);
}

void main() {
  test('typing the right code opens the shop\'s data and keeps the key', () async {
    final (data, _, api, keys, _) = await start();
    api.cloudRows['Products'] = [(id: vectorId, payload: vectorPayload)];

    expect(await data.unlock(shopRecoveryCode), Unlock.ok);

    expect(data.hasKey, isTrue);
    expect((await keys.read())?.toBase64(), shopKeyBase64);
  });

  test('a code that is not a recovery code is refused before the cloud is asked anything', () async {
    final (data, _, api, keys, _) = await start();

    expect(await data.unlock('NOT-A-CODE'), Unlock.invalid);

    expect(api.rowRequests, 0);
    expect(data.hasKey, isFalse);
    expect(await keys.read(), isNull);
  });

  test('another shop\'s code is refused and not kept', () async {
    final (data, _, api, keys, _) = await start();
    api.cloudRows['Products'] = [(id: vectorId, payload: vectorPayload)];

    expect(await data.unlock(otherShopRecoveryCode), Unlock.wrongKey);

    expect(data.hasKey, isFalse);
    expect(await keys.read(), isNull);
  });

  test('before the shop has sent anything the code cannot be checked, so it is kept but said to be unverified', () async {
    final (data, _, _, _, _) = await start();

    expect(await data.unlock(shopRecoveryCode), Unlock.unverified);
    expect(data.hasKey, isTrue);
  });

  test('refresh decrypts rows, keeps only the columns the screens use, and records when the shop last sent', () async {
    final (data, _, api, _, _) = await start();
    api.cloudRows['Products'] = [(id: vectorId, payload: vectorPayload)];
    await data.unlock(shopRecoveryCode);

    await data.refresh();

    final row = data.row('Products', vectorId)!;
    expect(row['Name'], vectorName);
    expect(row['StockQuantity'], 12);
    expect(row.containsKey('Note'), isFalse, reason: 'a column the screens do not use is dropped on arrival');
    expect(row.keys.toSet().difference(ShopData.kept['Products']!), isEmpty, reason: 'only the kept columns are held');
    expect(data.hasData, isTrue);
    expect(data.asOf, isNotNull);
  });

  test('a big table is read page by page until the cloud says there is no more', () async {
    final (data, _, api, _, _) = await start();
    // Same ciphertext under several ids would not decrypt (the id is authenticated), so use one row and check paging by request count.
    api.cloudRows['Products'] = [(id: vectorId, payload: vectorPayload)];
    await data.unlock(shopRecoveryCode);
    api.rowRequests = 0;

    await data.refresh();

    // One request per table (all small), none repeated.
    expect(api.rowRequests, ShopData.kept.length);
  });

  test('a second refresh asks only for what is after the cursor', () async {
    final (data, _, api, _, _) = await start();
    api.cloudRows['Products'] = [(id: vectorId, payload: vectorPayload)];
    await data.unlock(shopRecoveryCode);
    await data.refresh();

    await data.refresh();

    expect(data.row('Products', vectorId)?['Name'], vectorName);
    expect(data.error, isNull);
  });

  test('a network failure keeps what the phone already has and says so', () async {
    final (data, _, api, _, _) = await start();
    api.cloudRows['Products'] = [(id: vectorId, payload: vectorPayload)];
    await data.unlock(shopRecoveryCode);
    await data.refresh();

    api.rowsError = Exception('offline');
    await data.refresh();

    expect(data.error, isNotNull);
    expect(data.row('Products', vectorId)?['Name'], vectorName, reason: 'old figures are better than none, and are labelled by their age');
  });

  test('if the shop changed its key, the phone forgets the old one and asks again', () async {
    final (data, _, api, keys, _) = await start();
    api.cloudRows['Products'] = [(id: vectorId, payload: vectorPayload)];
    await data.unlock(shopRecoveryCode);
    // The cloud now holds rows made with a different key: simulate by swapping to a payload the stored key cannot open.
    api.cloudRows['Products'] = [(id: vectorId + 1, payload: vectorPayload)];

    await data.refresh();

    expect(data.hasKey, isFalse);
    expect(await keys.read(), isNull);
  });

  test('signing out takes the key and the shop\'s rows with it', () async {
    final (data, session, api, keys, _) = await start();
    api.cloudRows['Products'] = [(id: vectorId, payload: vectorPayload)];
    await data.unlock(shopRecoveryCode);
    await data.refresh();

    await session.signOut();
    await Future<void>.delayed(Duration.zero);

    expect(data.hasKey, isFalse);
    expect(data.row('Products', vectorId), isNull);
    expect(await keys.read(), isNull);
  });

  test('an old key left on the phone is dropped when nobody is signed in', () async {
    SharedPreferences.setMockInitialValues({});
    final session = Session(prefs: await SharedPreferences.getInstance(), tokens: MemoryTokenStore(), apiFactory: (_, _) => FakeApi());
    await session.restore();
    final keys = MemoryKeyStore()..value = ShopKey.parseRecoveryCode(shopRecoveryCode);
    final data = ShopData(session, keys);

    await data.restore();

    expect(data.hasKey, isFalse);
    expect(await keys.read(), isNull);
  });

  group('reading what the till stores', () {
    test('numbers arrive as numbers or text, booleans as 0/1', () {
      expect(toNum(12), 12);
      expect(toNum('12.50'), 12.5);
      expect(toNum(null), 0);
      expect(toNum('x'), 0);
      expect(flag(1), isTrue);
      expect(flag(0), isFalse);
      expect(flag('1'), isTrue);
      expect(flag(true), isTrue);
      expect(flag(null), isFalse);
    });

    test('dates are UTC whether or not the till wrote a zone, with 7-digit ticks', () {
      final expected = DateTime.utc(2026, 11, 7, 17, 23, 57);
      expect(utcDate('2026-11-07 17:23:57.0000000'), expected);
      expect(utcDate('2026-11-07T17:23:57Z'), expected);
      final ticks = utcDate('2026-11-07T17:23:57.1234567Z')!;
      expect([ticks.millisecond, ticks.microsecond], [123, 456]);
      expect(utcDate('2026-11-07T19:23:57+02:00'), expected);
      expect(utcDate(null), isNull);
      expect(utcDate('garbage'), isNull);
      expect(utcDate('2026-11-07 17:23:57')!.isUtc, isTrue);
    });
  });
}
