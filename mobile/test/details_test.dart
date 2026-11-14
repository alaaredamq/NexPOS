import 'package:flutter_test/flutter_test.dart';
import 'package:nexpos_owner/data/details.dart';
import 'package:nexpos_owner/data/period_report.dart';
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

String utc(DateTime local) => local.toUtc().toIso8601String().replaceFirst('T', ' ').replaceFirst('Z', '0000');

Map<String, dynamic> receipt(DateTime local, double total,
        {int currency = 1, int payment = 1, bool isVoid = false, String number = '1', int? customerId, String customerName = '', double paid = -1, String voidReason = ''}) =>
    {
      'ReceiptNumber': number,
      'ReceiptDate': utc(local),
      'TotalAmount': total.toStringAsFixed(2),
      'SubTotal': total.toStringAsFixed(2),
      'DiscountAmount': '0',
      'TaxAmount': '0',
      'AmountPaid': (paid < 0 ? total : paid).toStringAsFixed(2),
      'CurrencyId': currency,
      'CustomerId': customerId,
      'CustomerName': customerName,
      'PaymentMethodId': payment,
      'IsVoid': isVoid ? 1 : 0,
      'VoidReason': voidReason,
      'IsDeleted': 0,
    };

Map<String, dynamic> item(int receiptId, int productId, String name, double qty, double unitPrice, {double discount = 0}) => {
      'ReceiptId': receiptId,
      'ProductId': productId,
      'ProductName': name,
      'Quantity': '$qty',
      'Unit': '',
      'UnitPrice': '$unitPrice',
      'DiscountAmount': '$discount',
      'LineTotal': '${qty * unitPrice - discount}',
      'IsDeleted': 0,
    };

void main() {
  final now = DateTime(2026, 11, 11, 12, 0);

  group('one sale', () {
    test('lists its items, the customer it was on account for, and what is still owed', () async {
      final data = await shop();
      data.seed('Customers', 5, {'FirstName': 'Mona', 'LastName': 'Haddad', 'Balance': '40', 'IsDeleted': 0});
      data.seed('Receipts', 1, receipt(DateTime(2026, 11, 11, 9, 30), 40, number: 'R-1', customerId: 5, paid: 10));
      data.seed('ReceiptItems', 1, item(1, 7, 'Milk 1 L', 2, 5));
      data.seed('ReceiptItems', 2, item(1, 8, 'Bread', 6, 5));
      data.seed('ReceiptItems', 3, item(2, 8, 'Another sale\'s bread', 1, 5));

      final d = saleDetail(data, 1)!;

      expect(d.sale.number, 'R-1');
      expect(d.items.map((i) => i.name), ['Bread', 'Milk 1 L']);
      expect(d.items.fold<double>(0, (a, i) => a + i.total), 40);
      expect(d.customerId, 5);
      expect(d.customerName, 'Mona Haddad');
      expect(d.unpaid, 30);
    });

    test('a walk-in sale has no customer page, a void one carries its reason, and change is not debt', () async {
      final data = await shop();
      data.seed('Receipts', 1, receipt(DateTime(2026, 11, 11, 9), 9, customerName: 'Walk-in', paid: 20)); // paid more: change given back
      data.seed('Receipts', 2, receipt(DateTime(2026, 11, 11, 10), 5, isVoid: true, voidReason: 'Wrong item'));

      expect(saleDetail(data, 1)!.customerId, isNull);
      expect(saleDetail(data, 1)!.unpaid, 0);
      expect(saleDetail(data, 2)!.voidReason, 'Wrong item');
      expect(saleDetail(data, 99), isNull);
    });
  });

  group('one customer', () {
    test('shows the balance, the ledger newest first with its meaning, and only their sales', () async {
      final data = await shop();
      data.seed('Customers', 5, {'FirstName': 'Mona', 'LastName': 'Haddad', 'PhoneNumber': '71 123 456', 'Address': 'Main St', 'City': 'Saida', 'Balance': '842', 'IsDeleted': 0});
      data.seed('Customers', 6, {'FirstName': 'Karim', 'LastName': 'N', 'Balance': '0', 'IsDeleted': 0});
      data.seed('CustomerDebtTransactions', 1, {'CustomerId': 5, 'Amount': '900', 'Type': 0, 'Note': '', 'CreatedAt': utc(DateTime(2026, 11, 1, 10)), 'IsDeleted': 0});
      data.seed('CustomerDebtTransactions', 2, {'CustomerId': 5, 'Amount': '-58', 'Type': 1, 'Note': 'Cash', 'CreatedAt': utc(DateTime(2026, 11, 8, 10)), 'IsDeleted': 0});
      data.seed('CustomerDebtTransactions', 3, {'CustomerId': 6, 'Amount': '5', 'Type': 0, 'Note': '', 'CreatedAt': utc(DateTime(2026, 11, 8, 10)), 'IsDeleted': 0});
      data.seed('Receipts', 1, receipt(DateTime(2026, 11, 1, 10), 900, customerId: 5, paid: 0, number: 'A'));
      data.seed('Receipts', 2, receipt(DateTime(2026, 11, 3, 10), 100, customerId: 5, number: 'B'));
      data.seed('Receipts', 3, receipt(DateTime(2026, 11, 4, 10), 50, customerId: 5, isVoid: true, number: 'C'));
      data.seed('Receipts', 4, receipt(DateTime(2026, 11, 4, 11), 7, customerId: 6, number: 'D'));

      final d = customerDetail(data, 5)!;

      expect(d.name, 'Mona Haddad');
      expect(d.phone, '71 123 456');
      expect(d.address, 'Main St, Saida');
      expect(d.balance, 842);
      expect(d.ledger.map((l) => l.kind), [LedgerKind.payment, LedgerKind.sale]);
      expect(d.ledger.first.amount, -58);
      expect(d.purchases.map((s) => s.number), ['C', 'B', 'A']);
      expect(d.totalBought, 1000, reason: 'the void sale is not counted');
      expect(customerDetail(data, 99), isNull);
    });
  });

  group('one product', () {
    test('stock, margin and what sold in the last 30 days (voids and older sales left out of the totals)', () async {
      final data = await shop();
      data.seed('ProductCategories', 1, {'Name': 'Dairy', 'IsActive': 1});
      data.seed('Suppliers', 1, {'Name': 'Dairy Co', 'IsActive': 1, 'IsDeleted': 0, 'Balance': 0});
      data.seed('Products', 7, {
        'Name': 'Milk 1 L', 'Barcode': '123', 'StockQuantity': '4', 'MinimumStock': '10', 'SellingPrice': '5', 'CostPrice': '3',
        'CurrencyId': 1, 'CategoryId': 1, 'SupplierId': 1, 'Unit': 'pc', 'IsActive': 1, 'IsDeleted': 0,
      });
      data.seed('Receipts', 1, receipt(DateTime(2026, 11, 10, 9), 10, number: 'R1'));
      data.seed('Receipts', 2, receipt(DateTime(2026, 11, 9, 9), 25, number: 'R2', isVoid: true));
      data.seed('Receipts', 3, receipt(DateTime(2026, 8, 1, 9), 15, number: 'OLD'));
      data.seed('ReceiptItems', 1, item(1, 7, 'Milk 1 L', 2, 5));
      data.seed('ReceiptItems', 2, item(2, 7, 'Milk 1 L', 5, 5));
      data.seed('ReceiptItems', 3, item(3, 7, 'Milk 1 L', 3, 5));

      final d = productDetail(data, 7, now)!;

      expect(d.category, 'Dairy');
      expect(d.supplier, 'Dairy Co');
      expect(d.level, StockLevel.low);
      expect(d.marginPercent, closeTo(40, 0.001));
      expect(d.soldQuantity, 2);
      expect(d.soldRevenue, 10);
      expect(d.recent.map((s) => s.number), ['R1', 'OLD'], reason: 'recent sales exclude voids but are not limited to 30 days');
      expect(productDetail(data, 99, now), isNull);
    });

    test('a product with no cost has no margin, and a stock of zero is "out"', () async {
      final data = await shop();
      data.seed('Products', 1, {'Name': 'Free', 'StockQuantity': '0', 'SellingPrice': '5', 'CurrencyId': 1, 'IsActive': 1, 'IsDeleted': 0});

      final d = productDetail(data, 1, now)!;

      expect(d.marginPercent, isNull);
      expect(d.level, StockLevel.out);
    });
  });

  group('last 7 / 30 days', () {
    Future<ShopData> week() async {
      final data = await shop();
      data.seed('Products', 7, {'Name': 'Milk', 'StockQuantity': '9', 'SellingPrice': '5', 'CostPrice': '2', 'CurrencyId': 1, 'IsActive': 1, 'IsDeleted': 0});
      data.seed('Products', 8, {'Name': 'Bread', 'StockQuantity': '9', 'SellingPrice': '1', 'CurrencyId': 1, 'IsActive': 1, 'IsDeleted': 0});
      data.seed('Receipts', 1, receipt(DateTime(2026, 11, 11, 9, 5), 30, number: '1')); // today, 09:00 hour
      data.seed('Receipts', 2, receipt(DateTime(2026, 11, 10, 14, 5), 20, number: '2', payment: 2));
      data.seed('Receipts', 3, receipt(DateTime(2026, 11, 10, 14, 40), 10, number: '3'));
      data.seed('Receipts', 4, receipt(DateTime(2026, 11, 8, 18, 0), 40, number: '4'));
      data.seed('Receipts', 5, receipt(DateTime(2026, 11, 9, 18, 0), 99, number: '5', isVoid: true)); // void: nowhere
      data.seed('Receipts', 6, receipt(DateTime(2026, 11, 9, 11, 0), 5000, number: '6', currency: 2)); // other currency: counted, never added
      data.seed('Receipts', 7, receipt(DateTime(2026, 11, 2, 11, 0), 50, number: '7')); // the 7 days before
      data.seed('ReceiptItems', 1, item(1, 7, 'Milk', 4, 5, discount: 0)); // 20 revenue, cost 8
      data.seed('ReceiptItems', 2, item(1, 8, 'Bread', 10, 1)); // 10 revenue, no cost
      data.seed('ReceiptItems', 3, item(2, 7, 'Milk', 4, 5)); // 20
      data.seed('ReceiptItems', 4, item(4, 8, 'Bread', 40, 1)); // 40
      data.seed('ReceiptItems', 5, item(5, 7, 'Milk', 99, 1)); // void receipt: ignored
      return data;
    }

    test('adds up the days, compares with the days before, and never adds another currency in', () async {
      final r = periodReport(await week(), now, 7);

      expect(r.total, 100); // 30 + 20 + 10 + 40
      expect(r.orders, 4);
      expect(r.average, 25);
      expect(r.previousTotal, 50);
      expect(r.changePercent, 100);
      expect(r.otherCurrencySales, 1);
      expect(r.perDay.length, 7);
      expect(r.perDay.last.total, 30);
      expect(r.perDay[r.perDay.length - 2].total, 30); // the 10th: 20 + 10
      expect(r.bestDay!.total, 40);
    });

    test('best sellers by money, payment split and the busiest hours', () async {
      final r = periodReport(await week(), now, 7);

      expect(r.best.map((p) => p.name), ['Bread', 'Milk']); // Bread 50, Milk 40
      expect(r.best.first.quantity, 50);
      expect(r.best.last.revenue, 40);
      expect(r.byPayment.map((m) => '${m.notation}:${m.amount}'), ['Cash:80.0', 'Card:20.0']);
      expect(r.busiestHours.first, 18); // 40 at 18:00
      expect(r.busiestHours, containsAll([9, 14]));
    });

    test('profit is an estimate from cost prices, and absent when no sold product has a cost', () async {
      final withCost = periodReport(await week(), now, 7);
      // Milk: 8 sold at 5 less 2 each = 24; Bread has no cost and adds nothing.
      expect(withCost.estimatedProfit, 24);

      final data = await shop();
      data.seed('Products', 8, {'Name': 'Bread', 'StockQuantity': '9', 'SellingPrice': '1', 'CurrencyId': 1, 'IsActive': 1, 'IsDeleted': 0});
      data.seed('Receipts', 1, receipt(DateTime(2026, 11, 11, 9), 10));
      data.seed('ReceiptItems', 1, item(1, 8, 'Bread', 10, 1));
      expect(periodReport(data, now, 7).estimatedProfit, isNull);
    });

    test('30 days reaches back further; an empty shop reports zeros without failing', () async {
      final r30 = periodReport(await week(), now, 30);
      expect(r30.total, 150); // the receipt on the 2nd is inside 30 days
      expect(r30.perDay.length, 30);

      final empty = periodReport(await shop(), now, 7);
      expect(empty.total, 0);
      expect(empty.orders, 0);
      expect(empty.changePercent, isNull);
      expect(empty.bestDay, isNull);
      expect(empty.busiestHours, isEmpty);
      expect(empty.best, isEmpty);
    });
  });
}
