import 'package:flutter_test/flutter_test.dart';
import 'package:nexpos_owner/crypto/shop_key.dart';

import 'support/vectors.dart';

void main() {
  group('recovery code', () {
    test('the code printed by the shop PC gives its key', () {
      final key = ShopKey.parseRecoveryCode(shopRecoveryCode);
      expect(key, isNotNull);
      expect(key!.toBase64(), shopKeyBase64);
    });

    test('it is accepted however a person types it', () {
      for (final typed in [
        shopRecoveryCode.toLowerCase(),
        shopRecoveryCode.replaceAll('-', ''),
        ' ${shopRecoveryCode.replaceAll('-', ' ')}\n',
      ]) {
        expect(ShopKey.parseRecoveryCode(typed)?.toBase64(), shopKeyBase64, reason: typed);
      }
    });

    test('0, 1 and 8 can only be a mistyped O, I or B, so they are read that way', () {
      final key = ShopKey.parseRecoveryCode(otherShopRecoveryCode)!;
      final lookAlike = otherShopRecoveryCode.replaceAll('O', '0').replaceAll('I', '1').replaceAll('B', '8');
      expect(ShopKey.parseRecoveryCode(lookAlike)?.toBase64(), key.toBase64());
    });

    test('every single-character typo is refused, never turned into a different key', () {
      final clean = shopRecoveryCode.replaceAll('-', '');
      for (var i = 0; i < clean.length; i++) {
        final wrong = clean[i] == 'A' ? 'C' : 'A';
        expect(ShopKey.parseRecoveryCode(clean.substring(0, i) + wrong + clean.substring(i + 1)), isNull, reason: 'typo at $i was accepted');
      }
    });

    test('wrong lengths and junk are refused', () {
      final clean = shopRecoveryCode.replaceAll('-', '');
      expect(ShopKey.parseRecoveryCode(null), isNull);
      expect(ShopKey.parseRecoveryCode(''), isNull);
      expect(ShopKey.parseRecoveryCode(clean.substring(0, clean.length - 1)), isNull);
      expect(ShopKey.parseRecoveryCode('${clean}A'), isNull);
      expect(ShopKey.parseRecoveryCode('hello world'), isNull);
    });
  });

  group('rows from the shop PC', () {
    final key = ShopKey.parseRecoveryCode(shopRecoveryCode)!;

    test('a row encrypted by the shop PC opens, including Arabic text, decimals, null and booleans', () async {
      final row = await key.decryptRow(vectorTable, vectorId, vectorPayload);
      expect(row['Name'], vectorName);
      expect(row['SellingPrice'], 2.5);
      expect(row['StockQuantity'], 12);
      expect(row['Note'], isNull);
      expect(row['IsActive'], true);
    });

    test('the wrong key does not open it', () async {
      final other = ShopKey.parseRecoveryCode(otherShopRecoveryCode)!;
      await expectLater(other.decryptRow(vectorTable, vectorId, vectorPayload), throwsA(isA<RowDecryptException>()));
    });

    test('a row moved to another id or table is refused', () async {
      await expectLater(key.decryptRow(vectorTable, vectorId + 1, vectorPayload), throwsA(isA<RowDecryptException>()));
      await expectLater(key.decryptRow('Customers', vectorId, vectorPayload), throwsA(isA<RowDecryptException>()));
    });

    test('an altered row is refused', () async {
      final flipped = '${vectorPayload.substring(0, 20)}${vectorPayload[20] == 'A' ? 'B' : 'A'}${vectorPayload.substring(21)}';
      await expectLater(key.decryptRow(vectorTable, vectorId, flipped), throwsA(isA<RowDecryptException>()));
    });

    test('anything that is not an encrypted row is refused, not crashed on', () async {
      for (final junk in ['', 'plain text', 'e1.', 'e1.%%%', 'e1.AAAA']) {
        await expectLater(key.decryptRow(vectorTable, vectorId, junk), throwsA(isA<RowDecryptException>()), reason: junk);
      }
    });
  });
}
