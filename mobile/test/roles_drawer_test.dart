import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:nexpos_owner/data/drawer.dart';
import 'package:nexpos_owner/data/shop_data.dart';
import 'package:nexpos_owner/l10n.dart';
import 'package:nexpos_owner/state/permissions.dart';
import 'package:nexpos_owner/state/session.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'app_test.dart' show startApp, tab, openManage;
import 'pages_test.dart' show seedShop, utc;
import 'support/fake_api.dart';

const staff = Perm.sales | Perm.stock;
const manager = Perm.sales | Perm.stock | Perm.debts | Perm.add | Perm.edit;

Future<ShopData> shop() async {
  SharedPreferences.setMockInitialValues({});
  final session = Session(prefs: await SharedPreferences.getInstance(), tokens: MemoryTokenStore(), apiFactory: (_, _) => FakeApi());
  final data = ShopData(session, MemoryKeyStore());
  data.seed('Currencies', 1, {'Name': 'US Dollar', 'Code': 'USD', 'Notation': r'$', 'IsDefault': 1, 'IsActive': 1, 'IsDeleted': 0});
  data.seed('Currencies', 2, {'Name': 'Lira', 'Code': 'LBP', 'Notation': 'L.L.', 'IsDefault': 0, 'IsActive': 1, 'IsDeleted': 0});
  data.seed('PaymentMethods', 1, {'Name': 'Cash', 'IsDefault': 1, 'IsDeleted': 0});
  data.seed('PaymentMethods', 2, {'Name': 'Card', 'IsDefault': 0, 'IsDeleted': 0});
  data.seed('Users', 1, {'FirstName': 'Layla', 'LastName': 'Saab', 'Username': 'layla', 'IsDeleted': 0});
  return data;
}

/// An order in a drawer session with its receipt (which carries the payment method).
void sale(ShopData data, int id, {required int session, required double total, int status = 3, int method = 1, int currency = 1}) {
  data.seed('Orders', id, {'CashDrawerSessionId': session, 'Status': status, 'TotalAmount': '$total', 'CurrencyId': currency, 'IsDeleted': 0});
  data.seed('Receipts', id, {'OrderId': id, 'PaymentMethodId': method, 'IsVoid': 0, 'IsDeleted': 0, 'TotalAmount': '$total', 'CurrencyId': currency});
}

void main() {
  group('cash drawer', () {
    test('an open shift: opening cash plus what was sold, less what was taken out, split by how it was paid', () async {
      final data = await shop();
      data.seed('CashDrawerSession', 1, {'UserId': 1, 'OpenedAt': utc(DateTime(2026, 11, 12, 8)), 'IsOpen': 1, 'OpeningCash': '100', 'IsDeleted': 0});
      sale(data, 1, session: 1, total: 40);
      sale(data, 2, session: 1, total: 25, method: 2);
      sale(data, 3, session: 1, total: 99, status: 4); // cancelled: never money in the drawer
      sale(data, 4, session: 1, total: 99, status: 5); // returned
      sale(data, 5, session: 1, total: 5000, currency: 2); // another currency: counted apart
      sale(data, 6, session: 2, total: 70); // another shift
      data.seed('CashDrop', 1, {'CashDrawerSessionId': 1, 'Amount': '30', 'DroppedAt': utc(DateTime(2026, 11, 12, 11)), 'Reason': 'To the safe', 'IsDeleted': 0});

      final r = drawerReport(data);

      final s = r.current!;
      expect(s.cashier, 'Layla Saab');
      expect(s.opening, 100);
      expect(s.sales, 65);
      expect(s.salesCount, 2);
      expect(s.dropsTotal, 30);
      expect(s.expected, 135); // 100 + 65 - 30
      expect(s.byPayment.map((m) => '${m.notation}:${m.amount}'), ['Cash:40.0', 'Card:25.0']);
      expect(s.hasNonCash, isTrue, reason: 'the till counts card sales as cash, so the owner is told');
      expect(s.drops.single.reason, 'To the safe');
      expect(s.counted, isNull);
      expect(r.history, isEmpty);
      expect(r.otherCurrencySales, 1);
    });

    test('a closed shift shows the figures the till stored, the difference, and the shifts are newest first', () async {
      final data = await shop();
      data.seed('CashDrawerSession', 1, {
        'UserId': 1, 'OpenedAt': utc(DateTime(2026, 11, 10, 8)), 'ClosedAt': utc(DateTime(2026, 11, 10, 20)), 'IsOpen': 0,
        'OpeningCash': '100', 'ExpectedCash': '250', 'ActualCash': '246.5', 'IsDeleted': 0,
      });
      data.seed('CashDrawerSession', 2, {
        'UserId': 1, 'OpenedAt': utc(DateTime(2026, 11, 11, 8)), 'ClosedAt': utc(DateTime(2026, 11, 11, 20)), 'IsOpen': 0,
        'OpeningCash': '100', 'ExpectedCash': '180', 'ActualCash': '180', 'IsDeleted': 0,
      });

      final r = drawerReport(data);

      expect(r.current, isNull);
      expect(r.history.map((s) => s.id), [2, 1]);
      expect(r.history[0].difference, 0);
      expect(r.history[1].difference, -3.5);
      expect(r.offCount, 1);
      expect(r.differenceTotal, -3.5);
    });

    test('a shift that was never counted has no difference, and an empty shop has an empty drawer', () async {
      final data = await shop();
      data.seed('CashDrawerSession', 1, {'UserId': 1, 'OpenedAt': utc(DateTime(2026, 11, 10, 8)), 'ClosedAt': utc(DateTime(2026, 11, 10, 20)), 'IsOpen': 0, 'OpeningCash': '10', 'ExpectedCash': '10', 'IsDeleted': 0});

      expect(drawerReport(data).history.single.difference, isNull);
      expect(drawerReport(await shop()).current, isNull);
      expect(drawerReport(await shop()).history, isEmpty);
    });

    testWidgets('the owner sees it on Today and opens the drawer page from there', (tester) async {
      await startApp(tester, seed: (data) {
        seedShop(data);
        data.seed('Users', 1, {'FirstName': 'Layla', 'LastName': 'Saab', 'Username': 'layla', 'IsDeleted': 0});
        data.seed('CashDrawerSession', 1, {'UserId': 1, 'OpenedAt': utc(DateTime.now().subtract(const Duration(hours: 3))), 'IsOpen': 1, 'OpeningCash': '100', 'IsDeleted': 0});
        sale(data, 1, session: 1, total: 40);
      });

      expect(find.text('Cash drawer'), findsOneWidget);
      expect(find.textContaining('should hold'), findsOneWidget);

      await tester.tap(find.text('Cash drawer'));
      await tester.pumpAndSettle();

      expect(find.text('Should be in the drawer now'), findsOneWidget);
      expect(find.text('Opening cash'), findsOneWidget);
      expect(find.text('Past shifts'), findsOneWidget);
    });
  });

  group('staff logins with limits', () {
    testWidgets('the cloud\'s answer at sign-in is remembered: a manager signing in is a manager', (tester) async {
      final app = await startApp(tester, signedIn: false, unlocked: false);
      app.api
        ..permissions = manager
        ..role = 'manager';

      await tester.enterText(find.widgetWithText(TextFormField, 'Email'), 'sam@shop.test');
      await tester.enterText(find.widgetWithText(TextFormField, 'Password'), 'secret');
      await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
      await tester.pumpAndSettle();

      expect(app.session.permissions, manager);
      expect(app.session.role, 'manager');
      expect(app.session.can(Perm.edit), isTrue);
      expect(app.session.can(Perm.financials), isFalse);
    });

    testWidgets('staff get Today, Sales, Stock and Manage only: no Debts tab, no drawer, nothing to add', (tester) async {
      final app = await startApp(tester, permissions: staff, role: 'staff', seed: seedShop);

      expect(tab('Today'), findsOneWidget);
      expect(tab('Sales'), findsOneWidget);
      expect(tab('Stock'), findsOneWidget);
      expect(tab('Debts'), findsNothing);
      expect(find.text('Cash drawer'), findsNothing);
      expect(find.text('Customer debts'), findsNothing);

      await openManage(tester);
      expect(find.textContaining('Staff'), findsWidgets);
      expect(find.text('Add a customer'), findsNothing);
      expect(find.text('Add a product'), findsNothing);
      expect(find.text('Cash drawer'), findsNothing);

      // The phone only asked the cloud for what staff may read.
      expect(app.api.tablesAsked.toSet().intersection({'Customers', 'Suppliers', 'CashDrawerSession', 'CashDrop', 'Orders', 'Users', 'CustomerDebtTransactions'}), isEmpty);
      expect(app.api.tablesAsked, containsAll(['Receipts', 'Products', 'Currencies']));
    });

    testWidgets('a manager can add and edit, sees debts, but has no cash drawer and no cost prices or profit', (tester) async {
      await startApp(tester, permissions: manager, role: 'manager', seed: seedShop);

      expect(tab('Debts'), findsOneWidget);
      expect(find.text('Cash drawer'), findsNothing);

      await tester.tap(tab('Stock'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Fresh milk 1L'));
      await tester.pumpAndSettle();
      expect(find.text('Adjust stock'), findsWidgets); // may edit
      expect(find.text('Selling price'), findsOneWidget);
      expect(find.text('Cost'), findsNothing);
      expect(find.text('Margin'), findsNothing);
    });

    testWidgets('staff can look at a product but not change it, and Reports leaves out profit', (tester) async {
      await startApp(tester, permissions: staff, role: 'staff', seed: seedShop);

      await tester.tap(tab('Stock'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Fresh milk 1L'));
      await tester.pumpAndSettle();
      expect(find.widgetWithText(FilledButton, 'Adjust stock'), findsNothing);
      expect(find.widgetWithText(OutlinedButton, 'Edit'), findsNothing);
      expect(find.text('Cost'), findsNothing);

      await tester.pageBack();
      await tester.pumpAndSettle();
      await tester.tap(tab('Sales'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Reports'));
      await tester.pumpAndSettle();
      expect(find.text('Best sellers'), findsOneWidget);
      expect(find.text('Estimated profit'), findsNothing);
    });

    testWidgets('a table the cloud refuses is skipped, not treated as a failure', (tester) async {
      final app = await startApp(tester, unlocked: false);
      app.api.forbiddenTables.add('Customers');
      app.api.cloudRows['Products'] = [];
      await app.shop.unlock('DLNE-EURX-WYBS-ECX5-TSMP-KPIZ-WQPA-3Y4Y-JS2J-4UYN-KN4P-LPPW-GIC7-JAXS');
      await app.shop.refresh(prune: true);

      expect(app.shop.error, isNull);
      expect(app.shop.hasData, isTrue);
      expect(app.api.tablesAsked, contains('Customers'));
    });

    test('the permission table in the app matches the one in the cloud', () {
      expect(Perm.mayRead(Perm.all, 'Anything'), isTrue);
      expect(Perm.mayRead(staff, 'Receipts'), isTrue);
      expect(Perm.mayRead(staff, 'Products'), isTrue);
      expect(Perm.mayRead(staff, 'Customers'), isFalse);
      expect(Perm.mayRead(staff, 'Suppliers'), isFalse);
      expect(Perm.mayRead(staff, 'CashDrawerSession'), isFalse);
      expect(Perm.mayRead(staff, 'Currencies'), isTrue);
      expect(Perm.mayRead(manager, 'CustomerDebtTransactions'), isTrue);
      expect(Perm.mayRead(manager, 'CashDrop'), isFalse);
      expect(Perm.mayRead(manager, 'SomethingNew'), isFalse);
    });
  });

  group('sold by', () {
    testWidgets('the add-product form offers the till\'s units in a list, and sends the one chosen', (tester) async {
      final app = await startApp(tester);
      await openManage(tester);
      await tester.tap(find.text('Add a product'));
      await tester.pumpAndSettle();

      Future<void> type(String label, String text) async {
        await tester.enterText(find.widgetWithText(TextFormField, label), text);
        await tester.pumpAndSettle();
      }

      await type('Name', 'Labneh 500 g');
      await type('Barcode', '6291003004512');
      await type('Selling price', '5.5');
      await type('Cost', '3.8');
      await tester.tap(find.text('Category'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Dairy').last);
      await tester.pumpAndSettle();

      await tester.scrollUntilVisible(find.text('Sold by (optional)'), 200, scrollable: find.byType(Scrollable).first);
      await tester.tap(find.text('Sold by (optional)'));
      await tester.pumpAndSettle();
      for (final label in ['Not set', 'Piece', 'Kilogram', 'Pound', 'Liter', 'Gallon', 'Box', 'Pack']) {
        expect(find.text(label), findsWidgets, reason: label);
      }
      await tester.tap(find.text('Kilogram').last);
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pumpAndSettle();

      final (_, body, _) = app.api.sent.single;
      expect(body['unit'], 'kg');
    });

    testWidgets('left alone, no unit is sent at all', (tester) async {
      final app = await startApp(tester);
      await openManage(tester);
      await tester.tap(find.text('Add a product'));
      await tester.pumpAndSettle();
      for (final (label, text) in [('Name', 'Plain item'), ('Barcode', '123456'), ('Selling price', '2'), ('Cost', '1')]) {
        await tester.enterText(find.widgetWithText(TextFormField, label), text);
        await tester.pumpAndSettle();
      }
      await tester.tap(find.text('Category'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Dairy').last);
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pumpAndSettle();

      expect(app.api.sent.single.$2.containsKey('unit'), isFalse);
    });

    testWidgets('units read in Arabic after a quantity, and a unit the shop typed itself is left as it is', (tester) async {
      late BuildContext inArabic;
      await tester.pumpWidget(MaterialApp(
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: const [GlobalMaterialLocalizations.delegate, GlobalWidgetsLocalizations.delegate, GlobalCupertinoLocalizations.delegate],
        home: Builder(builder: (context) {
          inArabic = context;
          return const SizedBox();
        }),
      ));

      expect(unitShort(inArabic, 'piece'), 'قطعة');
      expect(unitShort(inArabic, 'kg'), 'كغ');
      expect(unitShort(inArabic, 'crate'), 'crate');
      expect(unitShort(inArabic, ''), '');
      expect(unitName(inArabic, 'liter'), 'لتر');
      expect(unitName(inArabic, 'crate'), 'crate');
      expect(productUnits, ['piece', 'kg', 'lb', 'liter', 'gallon', 'box', 'pack']);
    });
  });
}
