import 'package:flutter_test/flutter_test.dart';
import 'package:nexpos_owner/data/reports.dart';
import 'package:nexpos_owner/data/shop_data.dart';
import 'package:nexpos_owner/state/session.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fake_api.dart';

Future<ShopData> shop() async {
  SharedPreferences.setMockInitialValues({});
  final session = Session(prefs: await SharedPreferences.getInstance(), tokens: MemoryTokenStore(), apiFactory: (_, _) => FakeApi());
  final data = ShopData(session, MemoryKeyStore());
  data.seed('Currencies', 1, {'Name': 'US Dollar', 'Code': 'USD', 'Notation': r'$', 'IsDefault': 1, 'IsActive': 1, 'IsDeleted': 0});
  data.seed('Currencies', 2, {'Name': 'Lira', 'Code': 'LBP', 'Notation': 'L.L.', 'IsDefault': 0, 'IsActive': 1, 'IsDeleted': 0});
  data.seed('PaymentMethods', 1, {'Name': 'Cash', 'IsDefault': 1, 'IsDeleted': 0});
  data.seed('PaymentMethods', 2, {'Name': 'Card', 'IsDefault': 0, 'IsDeleted': 0});
  return data;
}

/// A receipt as the till stores it: UTC text date, decimals as text, flags as 0/1.
Map<String, dynamic> receipt(DateTime local, double total,
        {int currency = 1, int payment = 1, bool isVoid = false, bool deleted = false, String number = '1', String customer = ''}) =>
    {
      'ReceiptNumber': number,
      'ReceiptDate': local.toUtc().toIso8601String().replaceFirst('T', ' ').replaceFirst('Z', '0000'),
      'TotalAmount': total.toStringAsFixed(2),
      'CurrencyId': currency,
      'CustomerName': customer,
      'PaymentMethodId': payment,
      'IsVoid': isVoid ? 1 : 0,
      'IsDeleted': deleted ? 1 : 0,
    };

void main() {
  // Noon, so "an hour ago" and "earlier today" are the same calendar day in any time zone the test runs in.
  final now = DateTime(2026, 11, 11, 12, 0);

  group('today', () {
    test('adds up today\'s sales, counts them, and leaves out voided and deleted receipts', () async {
      final data = await shop();
      data.seed('Receipts', 1, receipt(DateTime(2026, 11, 11, 9, 30), 10, number: '1'));
      data.seed('Receipts', 2, receipt(DateTime(2026, 11, 11, 11, 15), 25.5, number: '2', payment: 2));
      data.seed('Receipts', 3, receipt(DateTime(2026, 11, 11, 11, 20), 99, number: '3', isVoid: true));
      data.seed('Receipts', 4, receipt(DateTime(2026, 11, 11, 11, 25), 77, number: '4', deleted: true));

      final r = todayReport(data, now);

      expect(r.today, 35.5);
      expect(r.orders, 2);
      expect(r.average, 17.75);
      expect(r.main.notation, r'$');
      expect({for (final p in r.byPayment) p.notation: p.amount}, {'Cash': 10, 'Card': 25.5});
    });

    test('another currency is shown beside the main figure, never added into it', () async {
      final data = await shop();
      data.seed('Receipts', 1, receipt(DateTime(2026, 11, 11, 9, 0), 10));
      data.seed('Receipts', 2, receipt(DateTime(2026, 11, 11, 10, 0), 1500000, currency: 2));

      final r = todayReport(data, now);

      expect(r.today, 10);
      expect(r.otherCurrencies.single.notation, 'L.L.');
      expect(r.otherCurrencies.single.amount, 1500000);
      expect(r.orders, 2);
    });

    test('compares with the same weekday last week and gives the last seven days', () async {
      final data = await shop();
      data.seed('Receipts', 1, receipt(DateTime(2026, 11, 11, 9, 0), 150));
      data.seed('Receipts', 2, receipt(DateTime(2026, 11, 4, 9, 0), 100)); // a week ago
      data.seed('Receipts', 3, receipt(DateTime(2026, 11, 10, 9, 0), 40)); // yesterday

      final r = todayReport(data, now);

      expect(r.versusLastWeekPercent, 50);
      expect(r.yesterdayMain, 40);
      expect(r.last7, hasLength(7));
      expect(r.last7.last, 150);
      expect(r.last7[5], 40);
      expect(r.last7.first, 0); // Nov 5
    });

    test('with nothing last week there is no percentage to show', () async {
      final data = await shop();
      data.seed('Receipts', 1, receipt(DateTime(2026, 11, 11, 9, 0), 150));
      expect(todayReport(data, now).versusLastWeekPercent, isNull);
    });

    test('a quiet shop gives zeros, not errors', () async {
      final data = await shop();
      final r = todayReport(data, now);
      expect(r.today, 0);
      expect(r.orders, 0);
      expect(r.average, 0);
      expect(r.byPayment, isEmpty);
    });

    test('sales per hour start at 8 or the first sale, and end at the current hour', () async {
      final data = await shop();
      data.seed('Receipts', 1, receipt(DateTime(2026, 11, 11, 9, 5), 10));
      data.seed('Receipts', 2, receipt(DateTime(2026, 11, 11, 11, 45), 30));

      final r = todayReport(data, now);

      expect(r.firstHour, 8);
      expect(r.hourly, hasLength(5)); // 8, 9, 10, 11, 12
      expect(r.hourly[1], 10);
      expect(r.hourly[3], 30);
    });
  });

  test('sales list is newest first, marks voids, and names the payment method', () async {
    final data = await shop();
    data.seed('Receipts', 1, receipt(DateTime(2026, 11, 11, 9, 0), 10, number: 'A', customer: 'Abu Karim'));
    data.seed('Receipts', 2, receipt(DateTime(2026, 11, 11, 11, 0), 20, number: 'B', isVoid: true, payment: 2));
    data.seed('Receipts', 3, receipt(DateTime(2026, 11, 11, 10, 0), 30, number: 'C', deleted: true));

    final sales = recentSales(data);

    expect(sales.map((s) => s.number), ['B', 'A']);
    expect(sales.first.isVoid, isTrue);
    expect(sales.first.payment, 'Card');
    expect(sales.last.customer, 'Abu Karim');
  });

  group('stock', () {
    Map<String, dynamic> product(String name, num qty, num? min, {bool active = true, bool deleted = false}) => {
          'Name': name,
          'Barcode': '123',
          'StockQuantity': qty.toString(),
          'MinimumStock': min?.toString(),
          'IsActive': active ? 1 : 0,
          'IsDeleted': deleted ? 1 : 0,
        };

    test('out first, then low, then fine; inactive and deleted products are left out', () async {
      final data = await shop();
      data.seed('Products', 1, product('Fine', 50, 10));
      data.seed('Products', 2, product('Low', 5, 10));
      data.seed('Products', 3, product('Out', 0, 10));
      data.seed('Products', 4, product('Exactly at minimum', 10, 10));
      data.seed('Products', 5, product('Retired', 0, 10, active: false));
      data.seed('Products', 6, product('Gone', 0, 10, deleted: true));
      data.seed('Products', 7, product('No minimum set', 3, null));

      final items = stockItems(data);

      expect(items.map((i) => i.name), ['Out', 'Exactly at minimum', 'Low', 'Fine', 'No minimum set']);
      expect(items.map((i) => i.level), [StockLevel.out, StockLevel.low, StockLevel.low, StockLevel.ok, StockLevel.ok]);
    });
  });

  test('debts: customers who owe, suppliers owed, largest first, nothing for settled or credit balances', () async {
    final data = await shop();
    data.seed('Customers', 1, {'FirstName': 'Abu', 'LastName': 'Karim', 'PhoneNumber': '70111222', 'Balance': '120.00', 'IsDeleted': 0});
    data.seed('Customers', 2, {'FirstName': 'Mona', 'LastName': 'Haddad', 'Balance': '300.50', 'IsDeleted': 0});
    data.seed('Customers', 3, {'FirstName': 'Paid', 'LastName': 'Up', 'Balance': '0', 'IsDeleted': 0});
    data.seed('Customers', 4, {'FirstName': 'Has', 'LastName': 'Credit', 'Balance': '-40', 'IsDeleted': 0});
    data.seed('Customers', 5, {'FirstName': 'Deleted', 'LastName': 'One', 'Balance': '900', 'IsDeleted': 1});
    data.seed('Suppliers', 1, {'Name': 'Dairy Co', 'Balance': '1000', 'CurrencyId': 1, 'IsDeleted': 0});
    data.seed('Suppliers', 2, {'Name': 'Owes Us', 'Balance': '-50', 'CurrencyId': 1, 'IsDeleted': 0});
    data.seed('Suppliers', 3, {'Name': 'Lira Supplier', 'Balance': '2000000', 'CurrencyId': 2, 'IsDeleted': 0});

    final r = debtsReport(data);

    expect(r.owedToShop.map((d) => d.name), ['Mona Haddad', 'Abu Karim']);
    expect(r.owedToShopTotals.single.amount, 420.5);
    expect(r.shopOwes.map((d) => d.name), ['Lira Supplier', 'Dairy Co']);
    expect({for (final t in r.shopOwesTotals) t.notation: t.amount}, {r'$': 1000, 'L.L.': 2000000});
  });

  test('the add-forms get their lists from the shop\'s own rows', () async {
    final data = await shop();
    data.seed('ProductCategories', 1, {'Name': 'Dairy', 'IsActive': 1});
    data.seed('ProductCategories', 2, {'Name': 'Retired', 'IsActive': 0});
    data.seed('Suppliers', 1, {'Name': 'B Supplier', 'IsActive': 1, 'IsDeleted': 0});
    data.seed('Suppliers', 2, {'Name': 'A Supplier', 'IsActive': 1, 'IsDeleted': 0});
    data.seed('Suppliers', 3, {'Name': 'Inactive', 'IsActive': 0, 'IsDeleted': 0});

    final l = data.lookups;

    expect(l.categories.map((c) => c.name), ['Dairy']);
    expect(l.suppliers.map((s) => s.name), ['A Supplier', 'B Supplier']);
    expect(l.currencies.first.code, 'USD'); // the default comes first
    expect(l.defaultCurrencyId, 1);
  });
}
