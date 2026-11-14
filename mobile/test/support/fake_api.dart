import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:nexpos_owner/api/owner_api.dart';
import 'package:nexpos_owner/crypto/shop_key.dart';

import 'vectors.dart';

/// Stands in for the cloud: records what the app sends and answers like the real owner API would.
class FakeApi implements OwnerApi {
  /// What the shop PC read after opening each sealed change: kind, body, idempotency key.
  final List<(ChangeKind, Map<String, dynamic>, String)> sent = [];

  /// What actually crossed the wire (the sealed bodies), to prove the cloud never sees plain text.
  final List<Map<String, dynamic>> wire = [];
  final ShopKey shopKey = ShopKey.parseRecoveryCode(shopRecoveryCode)!;
  final List<Change> stored = [];
  Object? submitError;
  Object? rowsError;
  bool signInFails = false;

  /// What the cloud says this login may do at sign-in (63 = everything) and the tables it refuses with 403, like the real cloud.
  int permissions = 63;
  String role = 'owner';
  final Set<String> forbiddenTables = {};
  final List<String> tablesAsked = [];

  /// Ciphertext the "cloud" holds, by table. Served in pages so the app's paging is exercised.
  final Map<String, List<({int id, String payload})>> cloudRows = {};
  int rowRequests = 0;
  DateTime? asOf = DateTime.now();

  @override
  Future<SignedIn> signIn(String email, String password) async {
    if (signInFails) throw ApiException(401, 'invalid_credentials', 'The email or password is not right.');
    return SignedIn(token: 'nxo_test', displayName: 'Layla', clientName: 'Al-Amal Market', siteName: 'Main till', permissions: permissions, role: role);
  }

  @override
  Future<void> signOut() async {}

  @override
  Future<RowPage> rows(String table, {String? cursor, int limit = 500}) async {
    rowRequests++;
    tablesAsked.add(table);
    if (forbiddenTables.contains(table)) throw ApiException(403, 'forbidden', 'This login is not allowed to see that.');
    if (rowsError != null) throw rowsError!;
    final all = cloudRows[table] ?? const [];
    final from = cursor == null ? 0 : int.parse(cursor);
    final page = all.skip(from).take(limit).toList();
    final next = from + page.length;
    return RowPage(rows: page, nextCursor: '$next', hasMore: next < all.length, asOf: asOf);
  }

  @override
  Future<List<int>> rowIds(String table) async => [for (final r in cloudRows[table] ?? const []) r.id];

  @override
  Future<Change> submit(ChangeKind kind, Map<String, dynamic> body, String idempotencyKey) async {
    if (submitError != null) throw submitError!;
    wire.add(body);
    // What the shop PC does: open the sealed change with the shop key and check it carries the key it was sent under.
    final opened = jsonDecode(utf8.decode(await shopKey.open('command', kind.type, body['enc'] as String))) as Map<String, dynamic>;
    expect(opened['k'], idempotencyKey);
    sent.add((kind, (opened['body'] as Map).cast<String, dynamic>(), idempotencyKey));
    final change = Change(
      id: 'c${stored.length}',
      type: kind.type,
      status: ChangeStatus.queued,
      createdAt: DateTime.now(),
      payload: body,
    );
    stored.insert(0, change);
    return change;
  }

  @override
  Future<List<Change>> changes() async => List.of(stored);

  @override
  Future<Change> discard(String id) async => stored.firstWhere((c) => c.id == id);
}
