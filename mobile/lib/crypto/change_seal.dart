import 'dart:convert';

import '../api/owner_api.dart';
import 'shop_key.dart';

/// Sending and reading changes without the cloud being able to read them (`Sync/Commands.cs`, `CommandSeal`).
///
/// A change goes out as `{ "enc": "e1..." }`, the sealed form of `{ "k": idempotencyKey, "body": ... }`, bound to the kind of change.
/// The shop PC opens it with the same key, refuses anything unsealed, and refuses a replay of the same `k`. Its answer comes back sealed
/// to the change's id, so a product name or a refusal reason is readable only here.
class ChangeSeal {
  const ChangeSeal._();

  static Future<Map<String, dynamic>> seal(ShopKey key, ChangeKind kind, String idempotencyKey, Map<String, dynamic> body) async {
    final plain = utf8.encode(jsonEncode({'k': idempotencyKey, 'body': body}));
    return {'enc': await key.seal('command', kind.type, plain)};
  }

  /// [change] as the owner should see it: its request and the shop's answer opened. Without the key (or when something does not open)
  /// it is returned as it came, marked [Change.hidden], so the list can say "encrypted" instead of showing blanks.
  static Future<Change> open(ShopKey? key, Change change) async {
    final sealedRequest = change.payload['enc'] is String;
    final sealedAnswer = change.data?['enc'] is String;
    if (!sealedRequest && !sealedAnswer) return change;
    if (key == null) return change.withHidden();

    try {
      var payload = change.payload;
      var message = change.message;
      var data = change.data;
      if (sealedRequest) {
        final opened = jsonDecode(utf8.decode(await key.open('command', change.type, change.payload['enc'] as String)));
        payload = ((opened as Map<String, dynamic>)['body'] as Map?)?.cast<String, dynamic>() ?? const {};
      }
      if (sealedAnswer) {
        final opened = jsonDecode(utf8.decode(await key.open('result', change.id, change.data!['enc'] as String))) as Map<String, dynamic>;
        message = (opened['message'] as String?) ?? message;
        data = (opened['data'] as Map?)?.cast<String, dynamic>();
      }
      return change.copyWith(payload: payload, message: message, data: data);
    } on RowDecryptException {
      return change.withHidden();
    } on FormatException {
      return change.withHidden();
    }
  }
}
