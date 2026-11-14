import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:nexpos_owner/api/owner_api.dart';
import 'package:nexpos_owner/crypto/change_seal.dart';
import 'package:nexpos_owner/crypto/shop_key.dart';

import 'support/vectors.dart';

/// What the phone seals and what the shop PC seals back. The C# vector below was made by the shop PC's own code
/// (`CommandSeal.SealResult`; Cloud.Tests/EncryptedSyncTests.cs opens the phone's), so the two sides are proven to agree.
const _shopResultFor11111111 =
    'e1.yx96hWU8XA8EXXH+S6e+6JvSPBNyB2c0Z5NSPx0bfNnJpGg+5KGrKgvoGqlrH/5JbTMz3ltCqzvGYheai08/kJELheynpkRuOWIy3zh/l5rHiRQ+h7s1gQMqr6bqfyAEPmujP8ZCL0jPyuAiEtR7Hbw1MqFgSr2oJztR0GLn';

void main() {
  final key = ShopKey.parseRecoveryCode(shopRecoveryCode)!;
  // Same shape, different shop: the two codes in the vectors belong to different keys.
  final otherKey = ShopKey.fromBase64(base64.encode(List<int>.generate(32, (i) => i + 1)));

  group('ShopKey.seal / open', () {
    test('what is sealed opens again, and is not readable in between', () async {
      final sealed = await key.seal('command', 'customer.create', utf8.encode('{"name":"Nadine Fares"}'));

      expect(sealed, startsWith('e1.'));
      expect(sealed, isNot(contains('Nadine')));
      expect(utf8.decode(await key.open('command', 'customer.create', sealed)), '{"name":"Nadine Fares"}');
    });

    test('two seals of the same thing differ (fresh nonce), yet both open', () async {
      final a = await key.seal('command', 'x', utf8.encode('same'));
      final b = await key.seal('command', 'x', utf8.encode('same'));

      expect(a, isNot(b));
      expect(utf8.decode(await key.open('command', 'x', b)), 'same');
    });

    test('another purpose, another context or a flipped byte fails to open', () async {
      final sealed = await key.seal('command', 'stock.adjust', utf8.encode('{}'));

      await expectLater(key.open('result', 'stock.adjust', sealed), throwsA(isA<RowDecryptException>()));
      await expectLater(key.open('command', 'product.update', sealed), throwsA(isA<RowDecryptException>()));
      final bytes = base64.decode(sealed.substring(3));
      bytes[bytes.length ~/ 2] ^= 1;
      await expectLater(key.open('command', 'stock.adjust', 'e1.${base64.encode(bytes)}'), throwsA(isA<RowDecryptException>()));
      await expectLater(key.open('command', 'stock.adjust', 'plain text'), throwsA(isA<RowDecryptException>()));
    });

    test('the shop PC\'s own sealed answer opens here, to its command id only', () async {
      final plain = jsonDecode(utf8.decode(await key.open('result', '11111111-2222-3333-4444-555555555555', _shopResultFor11111111))) as Map<String, dynamic>;

      expect(plain['message'], 'Only 3 in stock, cannot remove 8.');
      expect((plain['data'] as Map)['errors'], {'quantity': ['Only 3 in stock']});
      await expectLater(key.open('result', '99999999-2222-3333-4444-555555555555', _shopResultFor11111111), throwsA(isA<RowDecryptException>()));
    });
  });

  group('ChangeSeal', () {
    Change sealedChange({required String type, required Map<String, dynamic> payload, Map<String, dynamic>? data, String? message, ChangeStatus status = ChangeStatus.queued}) =>
        Change(id: '11111111-2222-3333-4444-555555555555', type: type, status: status, createdAt: DateTime.now(), payload: payload, data: data, message: message);

    test('the body goes out sealed to its kind, carrying the idempotency key', () async {
      final wire = await ChangeSeal.seal(key, ChangeKind.stock, 'key-123456', {'productId': 7, 'type': 'add', 'quantity': 4});

      expect(wire.keys, ['enc']);
      final opened = jsonDecode(utf8.decode(await key.open('command', 'stock.adjust', wire['enc'] as String))) as Map<String, dynamic>;
      expect(opened['k'], 'key-123456');
      expect(opened['body'], {'productId': 7, 'type': 'add', 'quantity': 4});
    });

    test('a sealed change in the list is shown opened, with the shop\'s sealed reason', () async {
      final wire = await ChangeSeal.seal(key, ChangeKind.product, 'k-aaaaaaaa', {'name': 'Pita bread 1 kg'});
      final answer = await key.seal(
        'result',
        '11111111-2222-3333-4444-555555555555',
        utf8.encode(jsonEncode({
          'message': 'The shop did not accept some of the values.',
          'data': {
            'errors': {'barcode': ['Prod_BarcodeExists']},
          },
        })),
      );
      final change = sealedChange(type: 'product.create', payload: wire, status: ChangeStatus.rejected, data: {'enc': answer}, message: 'generic');

      final shown = await ChangeSeal.open(key, change);

      expect(shown.hidden, isFalse);
      expect(shown.title, 'Pita bread 1 kg');
      expect(shown.message, 'The shop did not accept some of the values.');
      expect(shown.fieldErrors, {
        'barcode': ['Prod_BarcodeExists'],
      });
    });

    test('a locked phone (or the wrong key) marks the change as hidden instead of showing blanks', () async {
      final wire = await ChangeSeal.seal(key, ChangeKind.customer, 'k-bbbbbbbb', {'firstName': 'Nadine'});
      final change = sealedChange(type: 'customer.create', payload: wire);

      final locked = await ChangeSeal.open(null, change);
      final wrong = await ChangeSeal.open(otherKey, change);

      expect(locked.hidden, isTrue);
      expect(wrong.hidden, isTrue);
      expect(locked.payload, isEmpty);
    });

    test('an old change that was never sealed is shown as it is', () async {
      final plain = sealedChange(type: 'customer.create', payload: {'firstName': 'Old', 'lastName': 'One'});

      expect((await ChangeSeal.open(key, plain)).title, 'Old One');
      expect((await ChangeSeal.open(null, plain)).hidden, isFalse);
    });
  });
}
