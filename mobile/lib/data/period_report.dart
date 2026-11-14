import 'reports.dart';
import 'shop_data.dart';

int? _int(Object? v) => v == null ? null : (v is num ? v.toInt() : int.tryParse('$v'));

class TopProduct {
  TopProduct({required this.productId, required this.name, required this.quantity, required this.revenue});
  final int? productId;
  final String name;
  final double quantity;
  final double revenue;
}

/// How the shop did over the last [days] days (today included), in the shop's main currency. Other currencies are counted
/// but never added in; voided and deleted sales are left out.
class PeriodReport {
  PeriodReport({
    required this.days,
    required this.notation,
    required this.total,
    required this.orders,
    required this.previousTotal,
    required this.perDay,
    required this.best,
    required this.byPayment,
    required this.hourTotals,
    required this.estimatedProfit,
    required this.otherCurrencySales,
  });

  final int days;
  final String notation;
  final double total;
  final int orders;

  /// The same number of days just before this period, for "up or down on before".
  final double previousTotal;

  /// One total per day, oldest first, ending today.
  final List<({DateTime day, double total})> perDay;

  /// Top sellers by money taken.
  final List<TopProduct> best;
  final List<MoneyLine> byPayment;

  /// Money taken in each hour of the day (24 entries), summed over the period.
  final List<double> hourTotals;

  /// Sales less cost, from the products' current cost prices; null when no sold product has a cost recorded. An estimate.
  final double? estimatedProfit;

  /// Sales in the period made in another currency (not in the totals above).
  final int otherCurrencySales;

  double get average => orders == 0 ? 0 : total / orders;

  /// Percent change against the period before; null when there is nothing to compare with.
  double? get changePercent => previousTotal <= 0 ? null : (total - previousTotal) / previousTotal * 100;

  /// The busiest hours, most money first (at most three, none when nothing was sold).
  List<int> get busiestHours {
    final hours = [for (var h = 0; h < 24; h++) h]..removeWhere((h) => hourTotals[h] <= 0);
    hours.sort((a, b) => hourTotals[b].compareTo(hourTotals[a]));
    return hours.take(3).toList();
  }

  /// The day with the most sales, or null when nothing was sold.
  ({DateTime day, double total})? get bestDay {
    final sold = perDay.where((d) => d.total > 0).toList();
    if (sold.isEmpty) return null;
    return sold.reduce((a, b) => b.total > a.total ? b : a);
  }
}

PeriodReport periodReport(ShopData data, DateTime now, int days) {
  final currencies = Currencies(data);
  final main = currencies.main;
  DateTime day(DateTime d) => DateTime(d.year, d.month, d.day);
  final today = day(now);
  final start = DateTime(today.year, today.month, today.day - (days - 1));
  final previousStart = DateTime(start.year, start.month, start.day - days);

  final perDay = <DateTime, double>{};
  final hours = List<double>.filled(24, 0);
  final payments = <String, double>{};
  final inPeriod = <int>{}; // receipt ids counted in this period (main currency)
  var total = 0.0, previous = 0.0, orders = 0, other = 0;

  for (final e in data.live('Receipts')) {
    final r = e.value;
    if (flag(r['IsVoid'])) continue;
    final when = utcDate(r['ReceiptDate'])?.toLocal();
    if (when == null) continue;
    final d = day(when);
    final currencyId = _int(r['CurrencyId']);
    final isMain = currencyId == null || currencyId == main.id || main.id == null;
    final amount = toNum(r['TotalAmount']);

    if (!d.isBefore(start) && !d.isAfter(today)) {
      if (!isMain) {
        other++;
        continue;
      }
      total += amount;
      orders++;
      perDay[d] = (perDay[d] ?? 0) + amount;
      hours[when.hour] += amount;
      final method = text(data.row('PaymentMethods', _int(r['PaymentMethodId']))?['Name']);
      if (method.isNotEmpty) payments[method] = (payments[method] ?? 0) + amount;
      inPeriod.add(e.key);
    } else if (isMain && !d.isBefore(previousStart) && d.isBefore(start)) {
      previous += amount;
    }
  }

  final sold = <int, ({String name, double qty, double revenue})>{};
  var profit = 0.0, anyCost = false;
  for (final e in data.live('ReceiptItems')) {
    final receiptId = _int(e.value['ReceiptId']);
    if (receiptId == null || !inPeriod.contains(receiptId)) continue;
    final productId = _int(e.value['ProductId']) ?? -1;
    final qty = toNum(e.value['Quantity']);
    final line = toNum(e.value['LineTotal']);
    final name = text(e.value['ProductName']);
    final before = sold[productId];
    sold[productId] = (name: before?.name ?? name, qty: (before?.qty ?? 0) + qty, revenue: (before?.revenue ?? 0) + line);

    final cost = data.row('Products', productId)?['CostPrice'];
    if (cost != null && toNum(cost) > 0) {
      anyCost = true;
      profit += (toNum(e.value['UnitPrice']) * qty - toNum(e.value['DiscountAmount'])) - toNum(cost) * qty;
    }
  }

  final best = [
    for (final s in sold.entries) TopProduct(productId: s.key < 0 ? null : s.key, name: s.value.name, quantity: s.value.qty, revenue: s.value.revenue),
  ]..sort((a, b) => b.revenue.compareTo(a.revenue));

  return PeriodReport(
    days: days,
    notation: main.notation,
    total: total,
    orders: orders,
    previousTotal: previous,
    perDay: [
      for (var i = days - 1; i >= 0; i--)
        (day: DateTime(today.year, today.month, today.day - i), total: perDay[DateTime(today.year, today.month, today.day - i)] ?? 0),
    ],
    best: best.take(5).toList(),
    byPayment: (payments.entries.toList()..sort((a, b) => b.value.compareTo(a.value))).map((e) => MoneyLine(e.key, e.value)).toList(),
    hourTotals: hours,
    estimatedProfit: anyCost ? profit : null,
    otherCurrencySales: other,
  );
}
