import 'shop_data.dart';

/// What the owner's screens show, worked out from the shop's rows on the phone. Pure functions of [ShopData] so they can be tested
/// without a screen. Receipts are the sales; a voided or deleted receipt is not a sale.

class CurrencyInfo {
  const CurrencyInfo(this.id, this.notation, this.isDefault);
  final int? id;
  final String notation;
  final bool isDefault;
}

/// Currency by id, and the shop's main one (the default, else the first active).
class Currencies {
  Currencies(ShopData data) {
    CurrencyInfo? defaultInfo;
    for (final e in data.live('Currencies')) {
      final notation = text(e.value['Notation']).isEmpty ? text(e.value['Code']) : text(e.value['Notation']);
      final info = CurrencyInfo(e.key, notation, flag(e.value['IsDefault']));
      _byId[e.key] = info;
      if (info.isDefault) defaultInfo = info;
    }
    main = defaultInfo ?? (_byId.values.isEmpty ? const CurrencyInfo(null, '', true) : _byId.values.first);
  }

  final Map<int, CurrencyInfo> _byId = {};
  late final CurrencyInfo main;

  String notation(int? id) => _byId[id]?.notation ?? main.notation;
}

class SaleLine {
  SaleLine({
    required this.id,
    required this.number,
    required this.when,
    required this.total,
    required this.currencyId,
    required this.notation,
    required this.customer,
    required this.payment,
    required this.isVoid,
  });
  final int id;
  final String number;
  final DateTime? when; // local time
  final double total;
  final int? currencyId;
  final String notation;
  final String customer;
  final String payment;
  final bool isVoid;
}

/// One receipt row as a [SaleLine].
SaleLine saleLineOf(ShopData data, Currencies currencies, int id, ShopRow r) => SaleLine(
      id: id,
      number: text(r['ReceiptNumber']),
      when: utcDate(r['ReceiptDate'])?.toLocal(),
      total: toNum(r['TotalAmount']),
      currencyId: _int(r['CurrencyId']),
      notation: currencies.notation(_int(r['CurrencyId'])),
      customer: text(r['CustomerName']),
      payment: text(data.row('PaymentMethods', _int(r['PaymentMethodId']))?['Name']),
      isVoid: flag(r['IsVoid']),
    );

/// Newest first. Voided receipts are listed (marked) so the owner can see them; they are left out of every total.
List<SaleLine> recentSales(ShopData data, {int limit = 200}) {
  final currencies = Currencies(data);
  final lines = <SaleLine>[
    for (final e in data.live('Receipts')) saleLineOf(data, currencies, e.key, e.value),
  ]..sort((a, b) => (b.when ?? DateTime(0)).compareTo(a.when ?? DateTime(0)));
  return lines.take(limit).toList();
}

class MoneyLine {
  const MoneyLine(this.notation, this.amount);
  final String notation;
  final double amount;
}

class TodayReport {
  TodayReport({
    required this.main,
    required this.today,
    required this.otherCurrencies,
    required this.orders,
    required this.yesterdayMain,
    required this.sameDayLastWeekMain,
    required this.hourly,
    required this.firstHour,
    required this.byPayment,
    required this.last7,
  });

  /// Today's sales in the shop's main currency.
  final MoneyLine main;
  final double today;

  /// Sales made today in any other currency, shown beside the main figure rather than silently added to it.
  final List<MoneyLine> otherCurrencies;
  final int orders;
  final double yesterdayMain;
  final double sameDayLastWeekMain;

  /// Main-currency sales per hour, from [firstHour] to the current hour.
  final List<double> hourly;
  final int firstHour;
  final List<MoneyLine> byPayment;

  /// Main-currency sales for the last 7 days, oldest first, ending today.
  final List<double> last7;

  double get average => orders == 0 ? 0 : today / orders;

  /// Percent change against the same weekday last week; null when there is nothing to compare with.
  double? get versusLastWeekPercent => sameDayLastWeekMain <= 0 ? null : (today - sameDayLastWeekMain) / sameDayLastWeekMain * 100;
}

TodayReport todayReport(ShopData data, DateTime now) {
  final currencies = Currencies(data);
  final main = currencies.main;
  DateTime day(DateTime d) => DateTime(d.year, d.month, d.day);
  final today = day(now);

  final perDay = <DateTime, double>{}; // main currency only
  final hours = <int, double>{};
  final others = <String, double>{};
  final payments = <String, double>{};
  var orders = 0, todayMain = 0.0;

  for (final e in data.live('Receipts')) {
    final r = e.value;
    if (flag(r['IsVoid'])) continue;
    final when = utcDate(r['ReceiptDate'])?.toLocal();
    if (when == null) continue;
    final total = toNum(r['TotalAmount']);
    final currencyId = _int(r['CurrencyId']);
    final isMain = currencyId == null || currencyId == main.id || main.id == null;
    final d = day(when);

    if (isMain) perDay[d] = (perDay[d] ?? 0) + total;
    if (d != today) continue;

    orders++;
    if (isMain) {
      todayMain += total;
      hours[when.hour] = (hours[when.hour] ?? 0) + total;
      final method = text(data.row('PaymentMethods', _int(r['PaymentMethodId']))?['Name']);
      if (method.isNotEmpty) payments[method] = (payments[method] ?? 0) + total;
    } else {
      final n = currencies.notation(currencyId);
      others[n] = (others[n] ?? 0) + total;
    }
  }

  final firstHour = hours.isEmpty ? now.hour : [8, now.hour, ...hours.keys].reduce((a, b) => a < b ? a : b);
  final lastHour = now.hour < firstHour ? firstHour : now.hour;
  return TodayReport(
    main: MoneyLine(main.notation, todayMain),
    today: todayMain,
    otherCurrencies: [for (final o in others.entries) MoneyLine(o.key, o.value)],
    orders: orders,
    yesterdayMain: perDay[today.subtract(const Duration(days: 1))] ?? 0,
    sameDayLastWeekMain: perDay[today.subtract(const Duration(days: 7))] ?? 0,
    hourly: [for (var h = firstHour; h <= lastHour; h++) hours[h] ?? 0],
    firstHour: firstHour,
    byPayment: (payments.entries.toList()..sort((a, b) => b.value.compareTo(a.value))).map((e) => MoneyLine(e.key, e.value)).toList(),
    last7: [for (var i = 6; i >= 0; i--) perDay[DateTime(today.year, today.month, today.day - i)] ?? 0],
  );
}

enum StockLevel { out, low, ok }

class StockItem {
  StockItem({required this.id, required this.name, required this.barcode, required this.quantity, required this.minimum, required this.unit, required this.level});
  final int id;
  final String name;
  final String barcode;
  final double quantity;
  final double? minimum;
  final String unit;
  final StockLevel level;
}

/// Active products, running out first. A product is low at or below its minimum; out at zero or less.
List<StockItem> stockItems(ShopData data) {
  final items = <StockItem>[];
  for (final e in data.live('Products')) {
    final p = e.value;
    if (!flag(p['IsActive'])) continue;
    final qty = toNum(p['StockQuantity']);
    final min = p['MinimumStock'] == null ? null : toNum(p['MinimumStock']);
    items.add(StockItem(
      id: e.key,
      name: text(p['Name']),
      barcode: text(p['Barcode']),
      quantity: qty,
      minimum: min,
      unit: text(p['Unit']),
      level: qty <= 0 ? StockLevel.out : (min != null && min > 0 && qty <= min ? StockLevel.low : StockLevel.ok),
    ));
  }
  items.sort((a, b) {
    final byLevel = a.level.index.compareTo(b.level.index);
    return byLevel != 0 ? byLevel : a.name.toLowerCase().compareTo(b.name.toLowerCase());
  });
  return items;
}

class DebtEntry {
  DebtEntry({required this.id, required this.name, required this.phone, required this.amount, required this.notation});
  final int id;
  final String name;
  final String phone;
  final double amount;
  final String notation;
}

class DebtsReport {
  DebtsReport({required this.owedToShop, required this.shopOwes});

  /// Customers who owe the shop (positive balance), largest first.
  final List<DebtEntry> owedToShop;

  /// Suppliers the shop owes (positive balance), largest first.
  final List<DebtEntry> shopOwes;

  /// Totals per currency, so different currencies are never added together.
  List<MoneyLine> get owedToShopTotals => _totals(owedToShop);
  List<MoneyLine> get shopOwesTotals => _totals(shopOwes);

  static List<MoneyLine> _totals(List<DebtEntry> entries) {
    final sums = <String, double>{};
    for (final e in entries) {
      sums[e.notation] = (sums[e.notation] ?? 0) + e.amount;
    }
    return [for (final s in sums.entries) MoneyLine(s.key, s.value)];
  }
}

DebtsReport debtsReport(ShopData data) {
  final currencies = Currencies(data);
  final customers = <DebtEntry>[
    for (final e in data.live('Customers'))
      if (toNum(e.value['Balance']) > 0)
        DebtEntry(
          id: e.key,
          name: '${text(e.value['FirstName'])} ${text(e.value['LastName'])}'.trim(),
          phone: text(e.value['PhoneNumber']),
          amount: toNum(e.value['Balance']),
          notation: currencies.main.notation,
        ),
  ]..sort((a, b) => b.amount.compareTo(a.amount));

  final suppliers = <DebtEntry>[
    for (final e in data.live('Suppliers'))
      if (toNum(e.value['Balance']) > 0)
        DebtEntry(
          id: e.key,
          name: text(e.value['Name']),
          phone: text(e.value['Phone']),
          amount: toNum(e.value['Balance']),
          notation: currencies.notation(_int(e.value['CurrencyId'])),
        ),
  ]..sort((a, b) => b.amount.compareTo(a.amount));

  return DebtsReport(owedToShop: customers, shopOwes: suppliers);
}

int? _int(Object? v) => v == null ? null : (v is num ? v.toInt() : int.tryParse('$v'));
