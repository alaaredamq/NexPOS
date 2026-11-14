import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:nexpos_owner/api/owner_api.dart';
import 'package:nexpos_owner/data/shop_data.dart';
import 'package:nexpos_owner/widgets/common.dart' show isolateAmounts, money;
import 'package:nexpos_owner/screens/customer_screen.dart';
import 'package:nexpos_owner/screens/product_screen.dart';
import 'package:nexpos_owner/screens/sale_detail_screen.dart';

import 'app_test.dart' show startApp, tab, openManage;

String utc(DateTime d) => d.toUtc().toIso8601String().replaceFirst('T', ' ').replaceFirst('Z', '0000');

/// A small shop: two products (one low), a customer who owes money, and a sale on account with two items.
void seedShop(ShopData shop) {
  final today = DateTime.now();
  final when = DateTime(today.year, today.month, today.day, 0, 5);
  shop.seed('PaymentMethods', 1, {'Name': 'Cash', 'IsDefault': 1, 'IsDeleted': 0});
  shop.seed('Products', 1, {
    'Name': 'Fresh milk 1L', 'Barcode': '6290001', 'StockQuantity': '4', 'MinimumStock': '10', 'SellingPrice': '5', 'CostPrice': '3',
    'CurrencyId': 1, 'CategoryId': 1, 'SupplierId': 1, 'Unit': 'pc', 'IsActive': 1, 'IsDeleted': 0,
  });
  shop.seed('Products', 2, {'Name': 'Arabic bread', 'Barcode': '6290002', 'StockQuantity': '80', 'MinimumStock': '20', 'SellingPrice': '1.5', 'CurrencyId': 1, 'IsActive': 1, 'IsDeleted': 0});
  shop.seed('Customers', 7, {'FirstName': 'Mona', 'LastName': 'Haddad', 'PhoneNumber': '71 123 456', 'Balance': '842', 'IsDeleted': 0});
  shop.seed('CustomerDebtTransactions', 1, {'CustomerId': 7, 'Amount': '900', 'Type': 0, 'Note': '', 'CreatedAt': utc(DateTime.now().subtract(const Duration(days: 3))), 'IsDeleted': 0});
  shop.seed('CustomerDebtTransactions', 2, {'CustomerId': 7, 'Amount': '-58', 'Type': 1, 'Note': 'Cash', 'CreatedAt': utc(DateTime.now().subtract(const Duration(days: 1))), 'IsDeleted': 0});
  shop.seed('Receipts', 1, {
    'ReceiptNumber': 'RCP-1048', 'ReceiptDate': utc(when), 'TotalAmount': '40', 'SubTotal': '40', 'DiscountAmount': '0', 'TaxAmount': '0', 'AmountPaid': '10',
    'CurrencyId': 1, 'CustomerId': 7, 'CustomerName': 'Mona Haddad', 'PaymentMethodId': 1, 'IsVoid': 0, 'IsDeleted': 0,
  });
  shop.seed('ReceiptItems', 1, {'ReceiptId': 1, 'ProductId': 1, 'ProductName': 'Fresh milk 1L', 'Quantity': '4', 'Unit': 'pc', 'UnitPrice': '5', 'DiscountAmount': '0', 'LineTotal': '20', 'IsDeleted': 0});
  shop.seed('ReceiptItems', 2, {'ReceiptId': 1, 'ProductId': 2, 'ProductName': 'Arabic bread', 'Quantity': '10', 'Unit': '', 'UnitPrice': '2', 'DiscountAmount': '0', 'LineTotal': '20', 'IsDeleted': 0});
}

void main() {
  narrowTests();
  amountTests();
  group('tap through', () {
    testWidgets('a sale opens with its items, its customer and what is still owed; the customer and a product open from it', (tester) async {
      await startApp(tester, seed: seedShop);
      await tester.tap(tab('Sales'));
      await tester.pumpAndSettle();

      await tester.tap(find.text('#RCP-1048'));
      await tester.pumpAndSettle();

      expect(find.text('Items (2)'), findsOneWidget);
      expect(find.text('Fresh milk 1L'), findsOneWidget);
      expect(find.text('4 pc × \$5'), findsOneWidget);
      expect(find.text('\$30 still owed'), findsOneWidget);
      expect(find.text('On account'), findsOneWidget);

      await tester.tap(find.text('Mona Haddad'));
      await tester.pumpAndSettle();
      expect(find.text('Owes the shop'), findsOneWidget);
      expect(find.text('\$842'), findsWidgets);

      await tester.pageBack();
      await tester.pumpAndSettle();
      await tester.tap(find.text('Fresh milk 1L'));
      await tester.pumpAndSettle();
      expect(find.text('Adjust stock'), findsWidgets);
    });

    testWidgets('a debtor opens their account: balance, history in plain words, and their purchases', (tester) async {
      await startApp(tester, seed: seedShop);
      await tester.tap(tab('Debts'));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Mona Haddad').first);
      await tester.pumpAndSettle();

      expect(find.text('Owes the shop'), findsOneWidget);
      expect(find.text('Sale on account'), findsOneWidget);
      expect(find.text('Payment'), findsOneWidget);
      expect(find.text('+\$900'), findsOneWidget);
      expect(find.text('-\$58'), findsOneWidget);
      expect(find.text('71 123 456'), findsOneWidget);
      expect(find.text('Purchases (1)'), findsOneWidget);
    });

    testWidgets('a product opens with its stock, margin and sales, from the Stock tab', (tester) async {
      await startApp(tester, seed: seedShop);
      await tester.tap(tab('Stock'));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Fresh milk 1L'));
      await tester.pumpAndSettle();

      expect(find.text('Running low'), findsOneWidget);
      expect(find.text('4 pc'), findsWidgets);
      expect(find.text('40%'), findsOneWidget); // (5 - 3) / 5
      expect(find.text('#RCP-1048'), findsOneWidget);
    });
  });

  group('edit and adjust stock from the phone', () {
    Future<void> openProduct(WidgetTester tester) async {
      await tester.tap(tab('Stock'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Fresh milk 1L'));
      await tester.pumpAndSettle();
    }

    testWidgets('adding stock sends a sealed stock change that the shop PC can read, then closes with a confirmation', (tester) async {
      final app = await startApp(tester, seed: seedShop);
      await openProduct(tester);

      await tester.tap(find.widgetWithText(FilledButton, 'Adjust stock'));
      await tester.pumpAndSettle();
      await tester.enterText(find.widgetWithText(TextFormField, 'How many arrived'), '24');
      await tester.pumpAndSettle();
      expect(find.text('Afterwards: 28 pc'), findsOneWidget);
      await tester.enterText(find.widgetWithText(TextFormField, 'Reason (optional)'), 'Delivery');
      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));
      expect(find.text('Sent to the shop'), findsOneWidget);
      await tester.pumpAndSettle();

      final (kind, body, _) = app.api.sent.single;
      expect(kind, ChangeKind.stock);
      expect(body, {'productId': 1, 'type': 'add', 'quantity': 24, 'reason': 'Delivery'});
      expect(app.api.wire.single.keys, ['enc'], reason: 'the cloud only ever saw the sealed form');
      expect(app.api.wire.single.toString(), isNot(contains('Delivery')));
    });

    testWidgets('a count sets the stock to what was counted, and removing more than there is is stopped on the phone', (tester) async {
      final app = await startApp(tester, seed: seedShop);
      await openProduct(tester);
      await tester.tap(find.widgetWithText(FilledButton, 'Adjust stock'));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Remove'));
      await tester.pumpAndSettle();
      await tester.enterText(find.widgetWithText(TextFormField, 'How many to take out'), '9');
      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pumpAndSettle();
      expect(find.textContaining('Only 4 pc in stock'), findsOneWidget);
      expect(app.api.sent, isEmpty);

      await tester.tap(find.text('Count'));
      await tester.pumpAndSettle();
      await tester.enterText(find.widgetWithText(TextFormField, 'What you counted'), '12');
      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pumpAndSettle();

      final (_, body, _) = app.api.sent.single;
      expect(body['type'], 'set');
      expect(body['quantity'], 12);
    });

    testWidgets('editing sends only what changed, and saving with nothing changed says so instead of sending', (tester) async {
      final app = await startApp(tester, seed: seedShop);
      await openProduct(tester);
      await tester.tap(find.widgetWithText(OutlinedButton, 'Edit'));
      await tester.pumpAndSettle();

      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pumpAndSettle();
      expect(find.text('Nothing was changed.'), findsOneWidget);
      expect(app.api.sent, isEmpty);
      await tester.pump(const Duration(seconds: 5)); // let the message go, it sits over the Save button
      await tester.pumpAndSettle();

      await tester.enterText(find.widgetWithText(TextFormField, 'Selling price'), '5,5');
      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pumpAndSettle();

      final (kind, body, _) = app.api.sent.single;
      expect(kind, ChangeKind.productEdit);
      expect(body, {'productId': 1, 'sellingPrice': 5.5});
    });

    testWidgets('a price of zero is stopped on the phone', (tester) async {
      final app = await startApp(tester, seed: seedShop);
      await openProduct(tester);
      await tester.tap(find.widgetWithText(OutlinedButton, 'Edit'));
      await tester.pumpAndSettle();

      await tester.enterText(find.widgetWithText(TextFormField, 'Selling price'), '0');
      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pumpAndSettle();

      expect(find.text('Enter an amount above 0.'), findsOneWidget);
      expect(app.api.sent, isEmpty);
    });

    testWidgets('the Changes list names the product and says what was asked, and a refusal explains itself', (tester) async {
      final app = await startApp(tester, seed: seedShop);
      await openProduct(tester);
      await tester.tap(find.widgetWithText(FilledButton, 'Adjust stock'));
      await tester.pumpAndSettle();
      await tester.enterText(find.widgetWithText(TextFormField, 'How many arrived'), '5');
      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pumpAndSettle();
      expect(app.api.sent, hasLength(1));

      await tester.pageBack(); // the product page, then Manage
      await tester.pumpAndSettle();
      await openManage(tester);
      await tester.tap(find.text('Changes').first);
      await tester.pumpAndSettle();

      expect(find.text('Stock change'), findsOneWidget);
      expect(find.text('Fresh milk 1L'), findsOneWidget);
      expect(find.text('Add 5'), findsOneWidget);
    });

    testWidgets('without the shop key nothing is sent: changes are sealed with it', (tester) async {
      final app = await startApp(tester, unlocked: false);
      await openManage(tester);
      await tester.tap(find.text('Add a customer'));
      await tester.pumpAndSettle();
      await tester.enterText(find.widgetWithText(TextFormField, 'First name'), 'Nadine');
      await tester.enterText(find.widgetWithText(TextFormField, 'Last name'), 'Fares');

      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pumpAndSettle();

      expect(find.textContaining('Unlock the shop\'s data first'), findsOneWidget);
      expect(app.api.sent, isEmpty);
      expect(app.api.wire, isEmpty);
    });
  });

  group('reports', () {
    testWidgets('Sales has a Reports view with the totals, the best sellers and the way it was paid', (tester) async {
      await startApp(tester, seed: seedShop);
      await tester.tap(tab('Sales'));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Reports'));
      await tester.pumpAndSettle();

      expect(find.text('Sales, last 7 days'), findsOneWidget);
      expect(find.text('\$40'), findsWidgets);
      expect(find.text('Best sellers'), findsOneWidget);
      expect(find.text('Fresh milk 1L'), findsOneWidget);
      expect(find.text('Estimated profit'), findsOneWidget);
      expect(find.text('How it was paid'), findsOneWidget);

      await tester.tap(find.text('Last 30 days'));
      await tester.pumpAndSettle();
      expect(find.text('Sales, last 30 days'), findsOneWidget);
    });
  });

  group('app lock and alerts', () {
    testWidgets('with the lock on, the app opens behind a lock screen that a failed fingerprint does not open', (tester) async {
      final app = await startApp(tester, appLock: true, fingerprintWorks: false);

      expect(find.text('Coduo POS is locked'), findsOneWidget);
      expect(find.text('Today'), findsNothing, reason: 'nothing of the app is on screen behind the lock');
      expect(app.bio.asked, 1, reason: 'it asks straight away');

      app.bio.accept = true;
      await tester.tap(find.widgetWithText(FilledButton, 'Unlock'));
      await tester.pumpAndSettle();

      expect(find.text('Coduo POS is locked'), findsNothing);
      expect(tab('Today'), findsOneWidget);
    });

    testWidgets('with the lock on and the fingerprint working, the owner is simply let in', (tester) async {
      await startApp(tester, appLock: true);

      expect(find.text('Coduo POS is locked'), findsNothing);
      expect(tab('Today'), findsOneWidget);
    });

    testWidgets('the lock is switched on from Manage after the owner proves who they are', (tester) async {
      final app = await startApp(tester);
      await openManage(tester);

      await tester.scrollUntilVisible(find.text('Lock the app'), 200, scrollable: find.byType(Scrollable).first);
      await tester.tap(find.text('Lock the app'));
      await tester.pumpAndSettle();

      expect(app.bio.asked, 1);
      expect(app.lock.enabled, isTrue);
    });

    testWidgets('a phone with no screen lock is told to set one first', (tester) async {
      final app = await startApp(tester);
      app.bio.supported = false;
      await openManage(tester);

      await tester.scrollUntilVisible(find.text('Lock the app'), 200, scrollable: find.byType(Scrollable).first);
      await tester.tap(find.text('Lock the app'));
      await tester.pumpAndSettle();

      expect(find.textContaining('Set a screen lock'), findsOneWidget);
      expect(app.lock.enabled, isFalse);
    });

    testWidgets('alerts need permission: refused, they stay off and say why; allowed, the periodic check starts', (tester) async {
      final app = await startApp(tester);
      await openManage(tester);
      await tester.scrollUntilVisible(find.text('Alerts'), 200, scrollable: find.byType(Scrollable).first);

      app.watch.allow = false;
      await tester.tap(find.text('Alerts'));
      await tester.pumpAndSettle();
      expect(find.textContaining('Notifications are blocked'), findsOneWidget);
      expect(app.notify.on, isFalse);
      expect(app.watch.running, isFalse);

      app.watch.allow = true;
      await tester.tap(find.text('Alerts'));
      await tester.pumpAndSettle();
      expect(app.notify.on, isTrue);
      expect(app.watch.running, isTrue);
      expect(find.text('Show names and amounts'), findsOneWidget);
    });

    testWidgets('alerts cannot be switched on while the shop\'s data is locked, and signing out switches them off', (tester) async {
      final locked = await startApp(tester, unlocked: false);
      await openManage(tester);
      await tester.scrollUntilVisible(find.text('Alerts'), 200, scrollable: find.byType(Scrollable).first);
      await tester.tap(find.text('Alerts'));
      await tester.pumpAndSettle();
      expect(find.textContaining('Unlock the shop\'s data first'), findsOneWidget);
      expect(locked.watch.enableCalls, 0);
    });

    testWidgets('signing out turns the alerts off', (tester) async {
      final app = await startApp(tester);
      await app.notify.setOn(true);
      expect(app.watch.running, isTrue);

      await app.session.signOut();
      await tester.pumpAndSettle();

      expect(app.notify.on, isFalse);
      expect(app.watch.running, isFalse);
    });
  });
}

/// Long names and huge numbers on a 360-px phone, English and Arabic: no page may overflow.
void narrowTests() {
  for (final language in ['en', 'ar']) {
    testWidgets('every new page fits a narrow phone with long names and big numbers ($language)', (tester) async {
      final long = 'Extra long product name that keeps going and going ' * 2;
      await startApp(tester, language: language, size: const Size(360, 900), seed: (shop) {
        seedShop(shop);
        shop.seed('Products', 1, {
          'Name': long, 'Barcode': '6290001', 'StockQuantity': '123456789', 'MinimumStock': '10', 'SellingPrice': '99999999', 'CostPrice': '3',
          'CurrencyId': 1, 'CategoryId': 1, 'SupplierId': 1, 'Unit': 'kg', 'IsActive': 1, 'IsDeleted': 0,
        });
        shop.seed('Customers', 7, {'FirstName': long, 'LastName': long, 'PhoneNumber': '+961 70 123 456', 'Address': long, 'Balance': '123456789.5', 'IsDeleted': 0});
        shop.seed('Receipts', 1, {
          'ReceiptNumber': 'RCP-20261112-0048-VERY-LONG', 'ReceiptDate': utc(DateTime.now().subtract(const Duration(minutes: 5))), 'TotalAmount': '987654321', 'SubTotal': '987654321',
          'DiscountAmount': '5', 'TaxAmount': '7', 'AmountPaid': '10', 'CurrencyId': 1, 'CustomerId': 7, 'CustomerName': long, 'PaymentMethodId': 1, 'IsVoid': 0, 'IsDeleted': 0,
        });
        shop.seed('ReceiptItems', 1, {'ReceiptId': 1, 'ProductId': 1, 'ProductName': long, 'Quantity': '1234567', 'Unit': 'kg', 'UnitPrice': '99999999', 'DiscountAmount': '0', 'LineTotal': '987654321', 'IsDeleted': 0});
      });

      void noProblem(String where) {
        final problem = tester.takeException();
        expect(problem is FlutterError ? problem.toStringDeep() : problem, isNull, reason: where);
      }

      // Reports
      await tester.tap(find.descendant(of: find.byType(NavigationBar), matching: find.byType(NavigationDestination).at(1)));
      await tester.pumpAndSettle();
      await tester.tap(find.byIcon(Icons.insights_outlined));
      await tester.pumpAndSettle();
      noProblem('reports');
      await tester.tap(find.byType(ChoiceChip).last);
      await tester.pumpAndSettle();
      noProblem('reports 30 days');

      // Each page, pushed as the app would push it.
      final pages = <String, Widget>{
        'sale': const SaleDetailPage(saleId: 1),
        'customer': const CustomerPage(customerId: 7),
        'product': const ProductPage(productId: 1),
        'edit': const ProductEditScreen(productId: 1),
        'stock': const StockAdjustScreen(productId: 1),
      };
      for (final entry in pages.entries) {
        final navigator = tester.state<NavigatorState>(find.byType(Navigator).first);
        unawaited(navigator.push(MaterialPageRoute<void>(builder: (_) => entry.value)));
        await tester.pumpAndSettle();
        noProblem(entry.key);
        navigator.pop();
        await tester.pumpAndSettle();
      }
    });
  }
}

void amountTests() {
  tearDown(() => isolateAmounts(false));

  test('in Arabic an amount is wrapped so it keeps its order after Arabic words; in English it is left exactly as written', () {
    expect(money(15.07, r'$'), r'$15.07');
    expect(money(1200, 'L.L.'), '1,200 L.L.');

    isolateAmounts(true);

    expect(money(15.07, r'$'), '\u2066\$15.07\u2069');
    expect(money(-1200, 'L.L.'), '\u2066-1,200 L.L.\u2069');
    expect(money(15.07, r'$').replaceAll(RegExp('[\u2066\u2069]'), ''), r'$15.07', reason: 'only invisible marks were added');
  });

  testWidgets('the app turns the wrapping on in Arabic and off in English', (tester) async {
    await startApp(tester, language: 'ar', seed: seedShop);
    expect(money(1, r'$'), startsWith('\u2066'));

    await startApp(tester, language: 'en', seed: seedShop);
    expect(money(1, r'$'), r'$1');
  });

  testWidgets('Today in Arabic carries the wrapped amounts (so "\$15.07" does not turn into "15.07\$")', (tester) async {
    await startApp(tester, language: 'ar', seed: seedShop);

    expect(find.textContaining('\u2066\$40\u2069'), findsWidgets);
  });
}
