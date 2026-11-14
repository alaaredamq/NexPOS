import 'dart:convert';

import 'package:flutter/widgets.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:workmanager/workmanager.dart';

import '../api/owner_api.dart';
import '../crypto/change_seal.dart';
import '../crypto/shop_key.dart';
import '../data/shop_data.dart';
import '../state/session.dart';

/// What the owner may be told about while the app is closed.
class WatchSettings {
  const WatchSettings._();
  static const onKey = 'notify_on';
  static const detailsKey = 'notify_details';

  static bool isOn(SharedPreferences p) => p.getBool(onKey) ?? false;

  /// Names and amounts in the notification text. Off by default: a notification can be read on the lock screen by anyone.
  static bool showDetails(SharedPreferences p) => p.getBool(detailsKey) ?? false;
}

class Alert {
  Alert(this.id, this.title, this.body);
  final int id;
  final String title;
  final String body;
}

/// Shows an alert. An interface so tests can collect them.
abstract class Notifier {
  Future<void> show(Alert alert);
}

/// Works out what is new at the shop since the last look: sales, products that just ran low, and the answers to changes the owner sent.
///
/// This is how the app tells the owner things while it is closed, without any server pushing to the phone: the phone itself checks every
/// so often (Android allows about every 15 minutes), reads the encrypted rows that changed since last time, opens them with the shop key
/// and decides. Nothing readable is stored: only the "where I got to" markers (a position in the cloud's list, product ids, change ids).
/// The cloud learns nothing it did not already know (that the phone asked), and no third-party push service is involved.
class ShopWatcher {
  ShopWatcher({required this.api, required this.key, required this.prefs, DateTime Function()? clock}) : _clock = clock ?? DateTime.now;

  final OwnerApi api;
  final ShopKey key;
  final SharedPreferences prefs;
  final DateTime Function() _clock;

  static const _tables = ['Receipts', 'Products'];
  static const _startedKey = 'watch_started';
  static const _lowKey = 'watch_low';
  static const _seenKey = 'watch_seen_changes';

  /// A sale older than this is not "new": it only reached the phone late (a shop PC that was off, or a fresh install).
  static const _freshSale = Duration(hours: 36);

  bool get _ar => prefs.getString('language') == 'ar';
  String _t(String en, String ar) => _ar ? ar : en;

  /// Looks at what changed and returns what to tell the owner. Moves the markers on, so the same thing is never told twice.
  Future<List<Alert>> check() async {
    final firstLook = prefs.getString(_startedKey) == null;
    final details = WatchSettings.showDetails(prefs);
    final alerts = <Alert>[];

    final notation = await _mainNotation();

    // ---- sales ----
    var sales = 0;
    var takings = 0.0;
    await _walk('Receipts', decrypt: !firstLook, (id, row) {
      if (flag(row['IsVoid']) || flag(row['IsDeleted'])) return;
      final when = utcDate(row['ReceiptDate']);
      if (when == null || _clock().toUtc().difference(when) > _freshSale) return;
      sales++;
      takings += toNum(row['TotalAmount']);
    });
    if (sales > 0) {
      alerts.add(Alert(
        1,
        _t('New sales', 'مبيعات جديدة'),
        details && notation != null
            ? _t('$sales new sale(s), ${_money(takings, notation)}', '$sales عملية جديدة، ${_money(takings, notation)}')
            : _t('$sales new sale(s) at your shop.', '$sales عملية جديدة في محلك.'),
      ));
    }

    // ---- stock ----
    final low = <int>{...(jsonDecode(prefs.getString(_lowKey) ?? '[]') as List).map((e) => (e as num).toInt())};
    final ranLow = <String>[];
    await _walk('Products', decrypt: true, (id, row) {
      final qty = toNum(row['StockQuantity']);
      final min = row['MinimumStock'] == null ? null : toNum(row['MinimumStock']);
      final isLow = flag(row['IsActive']) && !flag(row['IsDeleted']) && (qty <= 0 || (min != null && min > 0 && qty <= min));
      if (!isLow) {
        low.remove(id);
      } else if (low.add(id) && !firstLook) {
        ranLow.add(text(row['Name']));
      }
    });
    await prefs.setString(_lowKey, jsonEncode(low.toList()));
    if (ranLow.isNotEmpty) {
      alerts.add(Alert(
        2,
        _t('Low stock', 'مخزون منخفض'),
        details
            ? ranLow.take(3).join(_t(', ', '، ')) + (ranLow.length > 3 ? _t(' and ${ranLow.length - 3} more', ' و${ranLow.length - 3} أخرى') : '')
            : _t('${ranLow.length} product(s) need restocking.', '${ranLow.length} منتج يحتاج تعبئة.'),
      ));
    }

    // ---- answers to what the owner sent ----
    final seen = <String>{...(jsonDecode(prefs.getString(_seenKey) ?? '[]') as List).map((e) => '$e')};
    final changes = await api.changes();
    for (final raw in changes) {
      final done = raw.status == ChangeStatus.applied || raw.status == ChangeStatus.rejected;
      if (!done || !seen.add(raw.id)) continue;
      if (firstLook) continue; // what was already answered before the first look is not news
      final change = await ChangeSeal.open(key, raw);
      final ok = change.status == ChangeStatus.applied;
      final name = details && !change.hidden ? _changeName(change) : '';
      alerts.add(Alert(
        100 + (raw.id.hashCode & 0xFFFF),
        ok ? _t('Change applied', 'تم تطبيق التغيير') : _t('A change needs a fix', 'تغيير يحتاج تعديلًا'),
        ok
            ? (name.isEmpty ? _t('The shop PC applied what you sent.', 'طبّق جهاز المحل ما أرسلته.') : name)
            : (name.isEmpty ? _t('The shop PC could not apply what you sent. Open the app to see why.', 'تعذّر على جهاز المحل تطبيق ما أرسلته. افتح التطبيق لمعرفة السبب.') : name),
      ));
    }
    final keepSeen = seen.toList();
    await prefs.setString(_seenKey, jsonEncode(keepSeen.length > 200 ? keepSeen.sublist(keepSeen.length - 200) : keepSeen));

    await prefs.setString(_startedKey, _clock().toUtc().toIso8601String());
    return alerts;
  }

  String _changeName(Change c) => switch (c.kind) {
        ChangeKind.stock || ChangeKind.productEdit => '',
        _ => c.title,
      };

  String _money(double v, String notation) {
    final text = '$notation${v.toStringAsFixed(v == v.roundToDouble() ? 0 : 2)}';
    // In Arabic the amount sits after Arabic words, where a bare "$" would end up behind the digits.
    return _ar ? '\u2066$text\u2069' : text;
  }

  Future<String?> _mainNotation() async {
    final notations = <({String notation, bool isDefault})>[];
    String? cursor;
    while (true) {
      final page = await api.rows('Currencies', cursor: cursor);
      for (final r in page.rows) {
        final row = await key.decryptRow('Currencies', r.id, r.payload);
        if (flag(row['IsDeleted']) || !flag(row['IsActive'])) continue;
        final n = text(row['Notation']).isEmpty ? text(row['Code']) : text(row['Notation']);
        notations.add((notation: n, isDefault: flag(row['IsDefault'])));
      }
      cursor = page.nextCursor;
      if (!page.hasMore) break;
    }
    for (final n in notations) {
      if (n.isDefault) return n.notation;
    }
    return notations.isEmpty ? null : notations.first.notation;
  }

  /// Reads the rows of [table] that arrived since the last look, calling [each] with each one opened, then moves the marker on.
  /// With [decrypt] off the rows are only skipped over (the first look: everything already there is history, not news).
  Future<void> _walk(String table, void Function(int id, ShopRow row) each, {required bool decrypt}) async {
    final cursorKey = 'watch_cursor_$table';
    var cursor = prefs.getString(cursorKey);
    while (true) {
      final page = await api.rows(table, cursor: cursor, limit: 500);
      if (decrypt) {
        for (final r in page.rows) {
          each(r.id, await key.decryptRow(table, r.id, r.payload));
        }
      }
      cursor = page.nextCursor ?? cursor;
      if (!page.hasMore) break;
    }
    if (cursor != null) await prefs.setString(cursorKey, cursor);
  }

  /// Forget where we got to (sign-out, or the key changed): the next look starts from "now" again.
  static Future<void> reset(SharedPreferences prefs) async {
    for (final k in [_startedKey, _lowKey, _seenKey, for (final t in _tables) 'watch_cursor_$t']) {
      await prefs.remove(k);
    }
  }
}

// ---- running in the background ----

/// The Android notification channel. "Private" visibility: on a locked screen Android shows "contents hidden" unless the owner allows more.
const _channel = AndroidNotificationDetails(
  'shop_activity',
  'Shop activity',
  channelDescription: 'New sales, products running low, and answers to changes you sent',
  importance: Importance.defaultImportance,
  priority: Priority.defaultPriority,
  visibility: NotificationVisibility.private,
);

class LocalNotifier implements Notifier {
  LocalNotifier();
  static final _plugin = FlutterLocalNotificationsPlugin();
  static bool _ready = false;

  static Future<void> init() async {
    if (_ready) return;
    await _plugin.initialize(settings: const InitializationSettings(android: AndroidInitializationSettings('@mipmap/ic_launcher')));
    _ready = true;
  }

  /// Asks Android 13+ for permission to show notifications. True when allowed.
  static Future<bool> requestPermission() async {
    await init();
    final android = _plugin.resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>();
    return await android?.requestNotificationsPermission() ?? true;
  }

  @override
  Future<void> show(Alert alert) async {
    await init();
    await _plugin.show(id: alert.id, title: alert.title, body: alert.body, notificationDetails: const NotificationDetails(android: _channel));
  }
}

const watchTask = 'shop-watch';

/// What WorkManager runs about every 15 minutes while the app is closed. Returns false to be retried soon (no signal), true otherwise.
Future<bool> runWatch({Notifier? notifier, SharedPreferences? preferences}) async {
  final prefs = preferences ?? await SharedPreferences.getInstance();
  if (!WatchSettings.isOn(prefs)) return true;
  final token = await const SecureTokenStore().read();
  final key = await const SecureKeyStore().read();
  final url = prefs.getString('cloud_url') ?? '';
  if (token == null || key == null || url.isEmpty) return true; // signed out or locked: nothing to look at

  try {
    final watcher = ShopWatcher(api: HttpOwnerApi(baseUrl: url, token: token), key: key, prefs: prefs);
    final out = notifier ?? LocalNotifier();
    for (final alert in await watcher.check()) {
      await out.show(alert);
    }
    return true;
  } on ApiException catch (e) {
    return e.isUnauthorized || e.status < 500; // an expired sign-in will not fix itself; a server hiccup will
  } on OfflineException {
    return false;
  } on RowDecryptException {
    return true; // the shop's key changed; the app asks for the new code when opened
  }
}

/// Top-level because the background isolate starts here.
@pragma('vm:entry-point')
void watchDispatcher() {
  Workmanager().executeTask((task, input) async {
    WidgetsFlutterBinding.ensureInitialized();
    return runWatch();
  });
}

/// Switches the periodic check on or off.
class WatchScheduler {
  const WatchScheduler();

  Future<void> start() async {
    await Workmanager().initialize(watchDispatcher);
    await Workmanager().registerPeriodicTask(
      watchTask,
      watchTask,
      frequency: const Duration(minutes: 15),
      constraints: Constraints(networkType: NetworkType.connected),
      existingWorkPolicy: ExistingPeriodicWorkPolicy.update,
    );
  }

  Future<void> stop() async {
    await Workmanager().initialize(watchDispatcher);
    await Workmanager().cancelByUniqueName(watchTask);
  }
}
