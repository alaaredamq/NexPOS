import 'reports.dart';
import 'shop_data.dart';

int? _int(Object? v) => v == null ? null : (v is num ? v.toInt() : int.tryParse('$v'));

class DrawerDrop {
  DrawerDrop({required this.when, required this.amount, required this.reason});
  final DateTime? when;
  final double amount;
  final String reason;
}

/// One shift of the cash drawer, worked out the way the till works it out (`CashDrawerService`):
/// expected = opening cash + sales rung up in the session - cash taken out. The till counts every sale as cash when it does this
/// (card and other payments are not separated yet), so [expected] includes them; [byPayment] shows what they were so the owner can see it.
class DrawerSession {
  DrawerSession({
    required this.id,
    required this.cashier,
    required this.openedAt,
    required this.closedAt,
    required this.isOpen,
    required this.opening,
    required this.sales,
    required this.salesCount,
    required this.byPayment,
    required this.drops,
    required this.expected,
    required this.counted,
    required this.openingNotes,
    required this.closingNotes,
  });

  final int id;
  final String cashier;
  final DateTime? openedAt;
  final DateTime? closedAt;
  final bool isOpen;
  final double opening;

  /// Sales rung up in this session (cancelled and returned ones left out), main currency.
  final double sales;
  final int salesCount;
  final List<MoneyLine> byPayment;
  final List<DrawerDrop> drops;

  /// What the drawer should hold. For a closed session this is the figure the till stored at close, so it matches what the cashier saw.
  final double expected;

  /// What was counted at close; null while the drawer is open.
  final double? counted;
  final String openingNotes;
  final String closingNotes;

  double get dropsTotal => drops.fold(0.0, (a, d) => a + d.amount);

  /// Counted less expected: negative is a shortage.
  double? get difference => counted == null ? null : counted! - expected;

  /// True when part of the sales were not cash, which the till still counts as cash in [expected].
  bool get hasNonCash => byPayment.length > 1 || (byPayment.isNotEmpty && !_looksLikeCash(byPayment.first.notation));
}

bool _looksLikeCash(String method) {
  final m = method.toLowerCase();
  return m.contains('cash') || method.contains('نقد');
}

class DrawerReport {
  DrawerReport({required this.notation, required this.current, required this.history, required this.otherCurrencySales});
  final String notation;

  /// The open drawer, or null when it is closed.
  final DrawerSession? current;

  /// Closed shifts, newest first.
  final List<DrawerSession> history;

  /// Sales in the shown sessions made in another currency (not in the figures).
  final int otherCurrencySales;

  /// Counted less expected over the closed shifts shown: the total shortage (negative) or surplus.
  double get differenceTotal => history.fold(0.0, (a, s) => a + (s.difference ?? 0));

  /// Shifts that ended off by more than a cent.
  int get offCount => history.where((s) => (s.difference ?? 0).abs() > 0.009).length;
}

DrawerReport drawerReport(ShopData data, {int historyLimit = 30}) {
  final currencies = Currencies(data);
  final main = currencies.main;

  // Sold orders by session, and the payment method of each (through its receipt).
  final methodOfOrder = <int, String>{};
  for (final e in data.live('Receipts')) {
    if (flag(e.value['IsVoid'])) continue;
    final orderId = _int(e.value['OrderId']);
    if (orderId == null) continue;
    final name = text(data.row('PaymentMethods', _int(e.value['PaymentMethodId']))?['Name']);
    methodOfOrder[orderId] = name.isEmpty ? '-' : name;
  }

  final salesBySession = <int, List<({int orderId, double total, bool isMain})>>{};
  for (final e in data.live('Orders')) {
    final sessionId = _int(e.value['CashDrawerSessionId']);
    final status = _int(e.value['Status']) ?? 0;
    if (sessionId == null || status == 4 || status == 5) continue; // cancelled and returned orders are not money in the drawer
    final currencyId = _int(e.value['CurrencyId']);
    (salesBySession[sessionId] ??= []).add((
      orderId: e.key,
      total: toNum(e.value['TotalAmount']),
      isMain: currencyId == null || currencyId == main.id || main.id == null,
    ));
  }

  final dropsBySession = <int, List<DrawerDrop>>{};
  for (final e in data.live('CashDrop')) {
    final sessionId = _int(e.value['CashDrawerSessionId']);
    if (sessionId == null) continue;
    (dropsBySession[sessionId] ??= []).add(DrawerDrop(when: utcDate(e.value['DroppedAt'])?.toLocal(), amount: toNum(e.value['Amount']), reason: text(e.value['Reason'])));
  }

  var other = 0;
  DrawerSession build(int id, ShopRow s) {
    final orders = salesBySession[id] ?? const [];
    final inMain = orders.where((o) => o.isMain).toList();
    other += orders.length - inMain.length;
    final payments = <String, double>{};
    for (final o in inMain) {
      final name = methodOfOrder[o.orderId] ?? '-';
      payments[name] = (payments[name] ?? 0) + o.total;
    }
    final sales = inMain.fold(0.0, (a, o) => a + o.total);
    final drops = List<DrawerDrop>.of(dropsBySession[id] ?? const <DrawerDrop>[])..sort((a, b) => (a.when ?? DateTime(0)).compareTo(b.when ?? DateTime(0)));
    final opening = toNum(s['OpeningCash']);
    final user = data.row('Users', _int(s['UserId']));
    final isOpen = flag(s['IsOpen']);
    return DrawerSession(
      id: id,
      cashier: _personName(user),
      openedAt: utcDate(s['OpenedAt'])?.toLocal(),
      closedAt: utcDate(s['ClosedAt'])?.toLocal(),
      isOpen: isOpen,
      opening: opening,
      sales: sales,
      salesCount: inMain.length,
      byPayment: (payments.entries.toList()..sort((a, b) => b.value.compareTo(a.value))).map((e) => MoneyLine(e.key, e.value)).toList(),
      drops: drops,
      // Closed: the figure the till stored. Open: the till's rule, applied to what has been sold so far.
      expected: !isOpen && s['ExpectedCash'] != null ? toNum(s['ExpectedCash']) : opening + sales - drops.fold(0.0, (a, d) => a + d.amount),
      counted: s['ActualCash'] == null ? null : toNum(s['ActualCash']),
      openingNotes: text(s['OpeningNotes']),
      closingNotes: text(s['ClosingNotes']),
    );
  }

  final sessions = [for (final e in data.live('CashDrawerSession')) (id: e.key, row: e.value)]
    ..sort((a, b) => (utcDate(b.row['OpenedAt']) ?? DateTime(0)).compareTo(utcDate(a.row['OpenedAt']) ?? DateTime(0)));

  DrawerSession? current;
  final history = <DrawerSession>[];
  for (final s in sessions) {
    if (flag(s.row['IsOpen'])) {
      current ??= build(s.id, s.row); // the newest open one; an older "open" row would be a session the till never closed
    } else if (history.length < historyLimit) {
      history.add(build(s.id, s.row));
    }
  }
  return DrawerReport(notation: main.notation, current: current, history: history, otherCurrencySales: other);
}

String _personName(ShopRow? user) {
  if (user == null) return '';
  final full = '${text(user['FirstName'])} ${text(user['LastName'])}'.trim();
  return full.isEmpty ? text(user['Username']) : full;
}
