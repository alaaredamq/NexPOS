import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../api/owner_api.dart';
import '../crypto/shop_key.dart';
import '../state/permissions.dart';
import '../state/session.dart';

/// A shop row as the phone keeps it: column name -> value, only the columns the screens use.
typedef ShopRow = Map<String, dynamic>;

/// Where the shop's key lives. The real one is the phone's secure storage (Keychain / Keystore); tests use memory.
abstract class KeyStore {
  Future<ShopKey?> read();
  Future<void> write(ShopKey? key);
}

class SecureKeyStore implements KeyStore {
  const SecureKeyStore();
  static const _name = 'shop_key';
  static const _storage = FlutterSecureStorage();

  @override
  Future<ShopKey?> read() async {
    final text = await _storage.read(key: _name);
    if (text == null) return null;
    try {
      return ShopKey.fromBase64(text);
    } catch (_) {
      return null; // damaged: ask for the code again rather than fail
    }
  }

  @override
  Future<void> write(ShopKey? key) =>
      key == null ? _storage.delete(key: _name) : _storage.write(key: _name, value: key.toBase64());
}

class MemoryKeyStore implements KeyStore {
  ShopKey? value;
  @override
  Future<ShopKey?> read() async => value;
  @override
  Future<void> write(ShopKey? key) async => value = key;
}

/// How typing the recovery code ended.
enum Unlock {
  /// The key opens this shop's data.
  ok,

  /// Not a recovery code: a typo or the wrong length. Nothing was tried against the data.
  invalid,

  /// A well-formed code, but it does not open this shop's data (the code of another shop, or an old one).
  wrongKey,

  /// A good code, but the shop has not sent anything yet, so there is nothing to check it against. It is kept.
  unverified,
}

/// The shop's copy on this phone. The cloud only ever hands over ciphertext; this class holds the key, decrypts, and keeps the
/// rows in memory for the screens. Nothing readable is written to disk.
///
/// Only the columns the screens use are kept, so a shop with tens of thousands of receipts stays small in memory.
class ShopData extends ChangeNotifier {
  ShopData(this._session, this._keys) {
    _wasSignedIn = _session.signedIn;
    _session.addListener(_onSession);
  }

  /// Table -> the columns worth keeping. Everything else in a row is dropped as soon as it is decrypted.
  static const Map<String, Set<String>> kept = {
    'Currencies': {'Name', 'Code', 'Notation', 'IsDefault', 'IsActive', 'IsDeleted'},
    'PaymentMethods': {'Name', 'IsDefault', 'IsDeleted'},
    'ProductCategories': {'Name', 'IsActive'},
    'Suppliers': {'Name', 'Phone', 'Balance', 'CurrencyId', 'IsActive', 'IsDeleted', 'OrderNumber'},
    'Customers': {'FirstName', 'LastName', 'PhoneNumber', 'Address', 'City', 'Balance', 'IsActive', 'IsDeleted'},
    'Products': {
      'Name', 'Barcode', 'StockQuantity', 'MinimumStock', 'SellingPrice', 'CostPrice', 'CurrencyId', 'CategoryId', 'SupplierId', 'Unit',
      'IsActive', 'IsDeleted',
    },
    // The ledger behind a customer's balance (sales on account, payments, corrections).
    'CustomerDebtTransactions': {'CustomerId', 'Amount', 'Type', 'Note', 'CreatedAt', 'IsDeleted'},
    // The cash drawer: who opened it, the money in and out, and what was counted at close.
    'Users': {'FirstName', 'LastName', 'Username', 'IsDeleted'},
    'CashDrawerSession': {
      'UserId', 'OpenedAt', 'ClosedAt', 'IsOpen', 'OpeningCash', 'ExpectedCash', 'ActualCash', 'OpeningNotes', 'ClosingNotes', 'IsDeleted',
    },
    'CashDrop': {'CashDrawerSessionId', 'Amount', 'DroppedAt', 'Reason', 'IsDeleted'},
    // Last and largest: the screens fill in table by table, small ones first.
    'Receipts': {
      'ReceiptNumber', 'ReceiptDate', 'TotalAmount', 'SubTotal', 'DiscountAmount', 'TaxAmount', 'AmountPaid', 'CurrencyId', 'CustomerId',
      'CustomerName', 'PaymentMethodId', 'OrderId', 'IsVoid', 'VoidReason', 'IsDeleted',
    },
    'ReceiptItems': {'ReceiptId', 'ProductId', 'ProductName', 'Quantity', 'Unit', 'UnitPrice', 'DiscountAmount', 'LineTotal', 'IsDeleted'},
    // Which drawer session each sale belongs to (only what the drawer needs).
    'Orders': {'CashDrawerSessionId', 'Status', 'TotalAmount', 'CurrencyId', 'IsDeleted'},
  };

  final Session _session;
  final KeyStore _keys;

  ShopKey? _key;
  final Map<String, Map<int, ShopRow>> _rows = {for (final t in kept.keys) t: {}};
  final Map<String, String?> _cursors = {};

  bool _wasSignedIn = false;
  bool _loading = false;
  Object? _error;
  DateTime? _asOf;
  DateTime? _loadedAt;

  bool get hasKey => _key != null;

  /// The shop key, for sealing what the phone sends and opening what comes back. Null while locked.
  ShopKey? get key => _key;
  bool get loading => _loading;

  /// Why the last refresh failed, or null.
  Object? get error => _error;

  /// When the shop PC last sent anything to the cloud (not when this phone last asked).
  DateTime? get asOf => _asOf;

  /// Set once a refresh has fully completed on this phone.
  bool get hasData => _loadedAt != null;

  Future<void> restore() async {
    if (_session.signedIn) {
      _key = await _keys.read();
    } else {
      await _keys.write(null); // a key left behind by a sign-in that is gone belongs to nobody
    }
    notifyListeners();
  }

  /// Signing out (or an expired sign-in) takes the key and the shop's rows with it, so the next person on this phone starts clean.
  void _onSession() {
    final signedIn = _session.signedIn;
    if (_wasSignedIn && !signedIn) clear();
    _wasSignedIn = signedIn;
  }

  @override
  void dispose() {
    _session.removeListener(_onSession);
    super.dispose();
  }

  /// Checks [code] against the shop's own data, then keeps the key. A code that does not open the data is not kept.
  Future<Unlock> unlock(String code) async {
    final key = ShopKey.parseRecoveryCode(code);
    if (key == null) return Unlock.invalid;

    var checked = false;
    for (final table in ['Currencies', 'Products', 'Customers', 'Receipts'].where(_mayRead)) {
      final RowPage page;
      try {
        page = await _guarded(() => _session.api.rows(table, limit: 1));
      } on ApiException catch (e) {
        if (e.status == 403 && e.code == 'forbidden') continue; // this login may not read that table: check the key against another
        rethrow;
      }
      if (page.rows.isEmpty) continue;
      try {
        await key.decryptRow(table, page.rows.first.id, page.rows.first.payload);
        checked = true;
        break;
      } on RowDecryptException {
        return Unlock.wrongKey;
      }
    }

    await _keys.write(key);
    _key = key;
    _resetRows();
    notifyListeners();
    return checked ? Unlock.ok : Unlock.unverified;
  }

  /// Forget the key and everything read with it (sign-out, or the owner chose to lock).
  Future<void> clear() async {
    await _keys.write(null);
    _key = null;
    _resetRows();
    _error = null;
    notifyListeners();
  }

  void _resetRows() {
    for (final t in _rows.values) {
      t.clear();
    }
    _cursors.clear();
    _asOf = null;
    _loadedAt = null;
  }

  /// Brings the phone up to date with the cloud. With [prune], rows the shop has deleted are dropped too (a few cheap requests; the
  /// shop mostly marks things deleted, which arrives as an ordinary change).
  Future<void> refresh({bool prune = false}) async {
    final key = _key;
    if (key == null || _loading || !_session.signedIn) return;
    _loading = true;
    _error = null;
    notifyListeners();
    try {
      for (final table in kept.keys.where(_mayRead)) {
        await _pullTable(key, table);
        notifyListeners(); // small tables first, so the screens fill in as the big ones arrive
      }
      if (prune) {
        for (final table in kept.keys.where(_mayRead)) {
          try {
            final alive = (await _guarded(() => _session.api.rowIds(table))).toSet();
            _rows[table]!.removeWhere((id, _) => !alive.contains(id));
          } on ApiException catch (e) {
            if (e.status != 403) rethrow;
          }
        }
      }
      _loadedAt = DateTime.now();
    } on RowDecryptException {
      // The shop changed its key (or reset): what we hold is no longer readable. Ask for the new code.
      await clear();
    } catch (e) {
      _error = e;
    } finally {
      _loading = false;
      notifyListeners();
    }
  }

  /// Whether this login may read [table]. The cloud enforces the same rule; asking only for what is allowed saves refused requests.
  bool _mayRead(String table) => Perm.mayRead(_session.permissions, table);

  Future<void> _pullTable(ShopKey key, String table) async {
    final keep = kept[table]!;
    while (true) {
      final RowPage page;
      try {
        page = await _guarded(() => _session.api.rows(table, cursor: _cursors[table]));
      } on ApiException catch (e) {
        if (e.status == 403 && e.code == 'forbidden') return; // this login lost access to the table (its role changed): skip it
        rethrow;
      }
      for (final row in page.rows) {
        final plain = await key.decryptRow(table, row.id, row.payload);
        _rows[table]![row.id] = {for (final c in keep) c: plain[c]};
      }
      _cursors[table] = page.nextCursor;
      if (page.asOf != null) _asOf = page.asOf;
      if (!page.hasMore) return;
    }
  }

  /// Runs a cloud call; an expired sign-in sends the owner back to the sign-in screen.
  Future<T> _guarded<T>(Future<T> Function() call) async {
    try {
      return await call();
    } on ApiException catch (e) {
      if (e.isUnauthorized) await _session.handleUnauthorized();
      rethrow;
    }
  }

  /// Puts a row in directly, as if it had arrived decrypted. For tests of what the screens compute.
  @visibleForTesting
  void seed(String table, int id, ShopRow row) {
    _rows[table]![id] = row;
    _loadedAt ??= DateTime.now();
  }

  // ---- reading ----

  /// Rows of [table] the shop has not deleted, by id.
  Iterable<MapEntry<int, ShopRow>> live(String table) =>
      _rows[table]!.entries.where((e) => !flag(e.value['IsDeleted']));

  ShopRow? row(String table, int? id) => id == null ? null : _rows[table]![id];

  /// The choices the add-forms offer, from the shop's own categories, suppliers and currencies.
  Lookups get lookups {
    final suppliers = live('Suppliers').where((e) => flag(e.value['IsActive'])).toList()
      ..sort((a, b) {
        final byOrder = (a.value['OrderNumber'] == null ? 1 : 0).compareTo(b.value['OrderNumber'] == null ? 1 : 0);
        if (byOrder != 0) return byOrder;
        final order = toNum(a.value['OrderNumber']).compareTo(toNum(b.value['OrderNumber']));
        return order != 0 ? order : text(a.value['Name']).compareTo(text(b.value['Name']));
      });
    final categories = _rows['ProductCategories']!.entries.where((e) => flag(e.value['IsActive'])).toList()
      ..sort((a, b) => text(a.value['Name']).compareTo(text(b.value['Name'])));
    final currencies = live('Currencies').where((e) => flag(e.value['IsActive'])).toList()
      ..sort((a, b) {
        final byDefault = (flag(b.value['IsDefault']) ? 1 : 0).compareTo(flag(a.value['IsDefault']) ? 1 : 0);
        return byDefault != 0 ? byDefault : text(a.value['Name']).compareTo(text(b.value['Name']));
      });
    return Lookups(
      categories: [for (final c in categories) Option(c.key, text(c.value['Name']))],
      suppliers: [for (final s in suppliers) Option(s.key, text(s.value['Name']))],
      currencies: [
        for (final c in currencies)
          CurrencyOption(c.key, text(c.value['Code']), text(c.value['Notation']), flag(c.value['IsDefault'])),
      ],
      asOf: _asOf,
    );
  }
}

// ---- reading what the till stores ----
// SQLite hands back numbers as numbers or as text, booleans as 0/1, dates as text. Be forgiving, never throw on a odd value.

double toNum(Object? v) => v is num ? v.toDouble() : (double.tryParse('${v ?? ''}') ?? 0);

bool flag(Object? v) => v == true || v == 1 || v == '1' || (v is String && v.toLowerCase() == 'true');

String text(Object? v) => v == null ? '' : '$v';

/// A date the till stored in UTC ("2026-11-07 17:23:57.1234567"), as a UTC DateTime. Null when it cannot be read.
DateTime? utcDate(Object? v) {
  if (v == null) return null;
  var s = '$v'.trim().replaceFirst(' ', 'T');
  if (s.isEmpty) return null;
  final hasZone = s.endsWith('Z') || RegExp(r'[+-]\d\d:?\d\d$').hasMatch(s.length > 10 ? s.substring(10) : '');
  // Dart reads up to microseconds; the till writes ticks (7 digits).
  s = s.replaceFirstMapped(RegExp(r'(\.\d{6})\d+'), (m) => m[1]!);
  return DateTime.tryParse(hasZone ? s : '${s}Z');
}
