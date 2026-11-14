import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:nexpos_owner/api/owner_api.dart';
import 'package:nexpos_owner/crypto/shop_key.dart';
import 'package:nexpos_owner/data/shop_data.dart';
import 'package:nexpos_owner/main.dart';
import 'package:nexpos_owner/widgets/motion.dart';
import 'package:nexpos_owner/state/session.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:nexpos_owner/state/app_lock.dart';
import 'package:nexpos_owner/state/notify_settings.dart';

import 'support/fake_api.dart';
import 'support/fakes.dart';
import 'support/vectors.dart';

class App {
  App(this.session, this.api, this.shop, this.keys, {required this.lock, required this.bio, required this.notify, required this.watch});
  final Session session;
  final FakeApi api;
  final ShopData shop;
  final MemoryKeyStore keys;
  final AppLock lock;
  final FakeBiometrics bio;
  final NotifySettings notify;
  final FakeWatchControl watch;
}

/// The app-lock and alert services with fakes behind them; the lock is off unless a test turns it on.
({AppLock lock, FakeBiometrics bio, NotifySettings notify, FakeWatchControl watch}) services(SharedPreferences prefs, Session session) {
  final bio = FakeBiometrics();
  final watch = FakeWatchControl();
  return (
    lock: AppLock(prefs: prefs, biometrics: bio, privacy: FakePrivacy()),
    bio: bio,
    notify: NotifySettings(prefs: prefs, control: watch, session: session),
    watch: watch,
  );
}

/// Starts the app on a phone-sized screen. By default the owner is signed in and the shop's data is unlocked, with the shop's
/// categories, suppliers and currencies already on the phone.
Future<App> startApp(WidgetTester tester,
    {bool signedIn = true, bool unlocked = true, void Function(ShopData shop)? seed, String language = 'en', Size size = const Size(800, 2200), bool appLock = false, bool fingerprintWorks = true, int permissions = 63, String role = 'owner'}) async {
  // A tall phone by default, so every field of the longest form is on screen and a tap on it lands.
  tester.view.physicalSize = size;
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  SharedPreferences.setMockInitialValues({'cloud_url': 'https://cloud.test', 'language': language, if (appLock) 'app_lock': true, 'permissions': permissions, 'role': role});
  final api = FakeApi();
  final tokens = MemoryTokenStore();
  if (signedIn) tokens.value = 'nxo_test';
  final session = Session(
    prefs: await SharedPreferences.getInstance(),
    tokens: tokens,
    apiFactory: (url, token) => api,
  );
  await session.restore();
  final keys = MemoryKeyStore();
  if (unlocked) keys.value = ShopKey.parseRecoveryCode(shopRecoveryCode);
  final shop = ShopData(session, keys);
  await shop.restore();
  if (unlocked) {
    shop.seed('ProductCategories', 1, {'Name': 'Dairy', 'IsActive': 1});
    shop.seed('ProductCategories', 2, {'Name': 'Drinks', 'IsActive': 1});
    shop.seed('Suppliers', 1, {'Name': 'Lebanese Dairy Co.', 'IsActive': 1, 'IsDeleted': 0, 'Balance': 0});
    shop.seed('Currencies', 1, {'Name': 'US Dollar', 'Code': 'USD', 'Notation': r'$', 'IsDefault': 1, 'IsActive': 1, 'IsDeleted': 0});
    shop.seed('Currencies', 2, {'Name': 'Lira', 'Code': 'LBP', 'Notation': 'L.L.', 'IsDefault': 0, 'IsActive': 1, 'IsDeleted': 0});
    seed?.call(shop);
  }
  final extra = services(await SharedPreferences.getInstance(), session);
  extra.bio.accept = fingerprintWorks;
  await tester.pumpWidget(OwnerApp(session: session, shop: shop, lock: extra.lock, notify: extra.notify));
  await tester.pumpAndSettle();
  return App(session, api, shop, keys, lock: extra.lock, bio: extra.bio, notify: extra.notify, watch: extra.watch);
}

Finder tab(String label) => find.descendant(of: find.byType(NavigationBar), matching: find.text(label));

Future<void> openManage(WidgetTester tester) async {
  await tester.tap(tab('Manage'));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('signing in with the right details opens the app', (tester) async {
    await startApp(tester, signedIn: false, unlocked: false);
    expect(find.text('Sign in'), findsWidgets);

    await tester.enterText(find.widgetWithText(TextFormField, 'Email'), 'layla@shop.test');
    await tester.enterText(find.widgetWithText(TextFormField, 'Password'), 'secret');
    await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
    await tester.pumpAndSettle();

    expect(tab('Today'), findsOneWidget);
    expect(find.text('Unlock your shop\'s data'), findsOneWidget); // signed in, but the phone does not have the key yet
  });

  testWidgets('a wrong password shows the cloud\'s message and stays on the sign-in screen', (tester) async {
    tester.view.physicalSize = const Size(800, 2200);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    SharedPreferences.setMockInitialValues({'cloud_url': 'https://cloud.test'});
    final api = FakeApi()..signInFails = true;
    final session = Session(prefs: await SharedPreferences.getInstance(), tokens: MemoryTokenStore(), apiFactory: (_, _) => api);
    final extra = services(await SharedPreferences.getInstance(), session);
    await tester.pumpWidget(OwnerApp(session: session, shop: ShopData(session, MemoryKeyStore()), lock: extra.lock, notify: extra.notify));
    await tester.pumpAndSettle();

    await tester.enterText(find.widgetWithText(TextFormField, 'Email'), 'layla@shop.test');
    await tester.enterText(find.widgetWithText(TextFormField, 'Password'), 'wrong');
    await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
    await tester.pumpAndSettle();

    expect(find.text('The email or password is not right.'), findsOneWidget);
    expect(session.signedIn, isFalse);
  });

  testWidgets('a saved sign-in is used again without asking', (tester) async {
    await startApp(tester);
    await openManage(tester);
    expect(find.text('Add a customer'), findsOneWidget);
    expect(find.widgetWithText(FilledButton, 'Sign in'), findsNothing);
  });

  group('shop data', () {
    testWidgets('without the recovery code the data tabs ask for it instead of showing empty figures', (tester) async {
      await startApp(tester, unlocked: false);

      expect(find.text('Unlock your shop\'s data'), findsOneWidget);
      for (final label in ['Sales', 'Stock', 'Debts']) {
        await tester.tap(tab(label));
        await tester.pumpAndSettle();
        expect(find.text('Unlock your shop\'s data'), findsOneWidget, reason: label);
      }
    });

    testWidgets('typing the right code unlocks and shows the shop\'s figures', (tester) async {
      final app = await startApp(tester, unlocked: false);
      app.api.cloudRows['Products'] = [(id: vectorId, payload: vectorPayload)];

      await tester.tap(find.text('Enter recovery code'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextField), shopRecoveryCode);
      await tester.tap(find.widgetWithText(FilledButton, 'Unlock'));
      await tester.pumpAndSettle();

      expect(app.shop.hasKey, isTrue);
      expect(find.text('Unlock your shop\'s data'), findsNothing);

      // The product the shop PC encrypted is on the Stock tab, readable (12 of it; no minimum set, so it is not "low").
      await tester.tap(tab('Stock'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('All products (1)'));
      await tester.pumpAndSettle();
      expect(find.text(vectorName), findsOneWidget);
    });

    testWidgets('a mistyped code says so and keeps nothing', (tester) async {
      final app = await startApp(tester, unlocked: false);
      await tester.tap(find.text('Enter recovery code'));
      await tester.pumpAndSettle();

      await tester.enterText(find.byType(TextField), 'DLNE-EURX-WYBS');
      await tester.tap(find.widgetWithText(FilledButton, 'Unlock'));
      await tester.pumpAndSettle();

      expect(find.textContaining('not a valid recovery code'), findsOneWidget);
      expect(app.shop.hasKey, isFalse);
    });

    testWidgets('another shop\'s code is refused with an explanation', (tester) async {
      final app = await startApp(tester, unlocked: false);
      app.api.cloudRows['Products'] = [(id: vectorId, payload: vectorPayload)];
      await tester.tap(find.text('Enter recovery code'));
      await tester.pumpAndSettle();

      await tester.enterText(find.byType(TextField), otherShopRecoveryCode);
      await tester.tap(find.widgetWithText(FilledButton, 'Unlock'));
      await tester.pumpAndSettle();

      expect(find.textContaining('does not open this shop'), findsOneWidget);
      expect(app.shop.hasKey, isFalse);
    });

    testWidgets('Today shows the day\'s sales, low stock and who owes the shop', (tester) async {
      final now = DateTime.now();
      String utc(DateTime d) => d.toUtc().toIso8601String().replaceFirst('T', ' ').replaceFirst('Z', '0000');
      // Placed 5 minutes into today's local day plus an hour, so it is "today" however the test machine's clock sits.
      final sold = DateTime(now.year, now.month, now.day, 0, 5);
      await startApp(tester, seed: (shop) {
        shop.seed('PaymentMethods', 1, {'Name': 'Cash', 'IsDefault': 1, 'IsDeleted': 0});
        shop.seed('Receipts', 1, {
          'ReceiptNumber': '1048', 'ReceiptDate': utc(sold), 'TotalAmount': '1284.50', 'CurrencyId': 1,
          'CustomerName': '', 'PaymentMethodId': 1, 'IsVoid': 0, 'IsDeleted': 0,
        });
        shop.seed('Products', 1, {'Name': 'Milk', 'Barcode': '1', 'StockQuantity': '2', 'MinimumStock': '10', 'IsActive': 1, 'IsDeleted': 0});
        shop.seed('Customers', 1, {'FirstName': 'Mona', 'LastName': 'Haddad', 'Balance': '842', 'IsDeleted': 0});
      });

      expect(find.text(r'$1,284.5'), findsWidgets);
      expect(find.textContaining('1 item(s) at or below their minimum'), findsOneWidget);
      expect(find.textContaining(r'$842 owed by 1 customer(s)'), findsOneWidget);
    });

    testWidgets('a switched-off shop PC is called out so old figures do not look current', (tester) async {
      final app = await startApp(tester, unlocked: false);
      app.api.asOf = DateTime.now().subtract(const Duration(hours: 5));
      app.api.cloudRows['Products'] = [(id: vectorId, payload: vectorPayload)];
      await app.shop.unlock(shopRecoveryCode);
      await app.shop.refresh();
      await tester.pumpAndSettle();

      expect(find.textContaining('may be out of date'), findsOneWidget);
    });

    testWidgets('locking from Manage makes the phone forget the code', (tester) async {
      final app = await startApp(tester);
      await openManage(tester);

      await tester.tap(find.text('Shop data unlocked'));
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(TextButton, 'Lock'));
      await tester.pumpAndSettle();

      expect(app.shop.hasKey, isFalse);
      expect(await app.keys.read(), isNull);
      expect(find.text('Shop data locked'), findsOneWidget);
    });

    testWidgets('Stock lists what needs restocking first and can search every product', (tester) async {
      await startApp(tester, seed: (shop) {
        shop.seed('Products', 1, {'Name': 'Milk', 'Barcode': '111', 'StockQuantity': '0', 'MinimumStock': '10', 'IsActive': 1, 'IsDeleted': 0});
        shop.seed('Products', 2, {'Name': 'Rice', 'Barcode': '222', 'StockQuantity': '80', 'MinimumStock': '10', 'IsActive': 1, 'IsDeleted': 0});
      });
      await tester.tap(tab('Stock'));
      await tester.pumpAndSettle();

      expect(find.text('Milk'), findsOneWidget);
      expect(find.text('Out'), findsOneWidget);
      expect(find.text('Rice'), findsNothing); // fine, so hidden by default

      await tester.enterText(find.byType(TextField), '222');
      await tester.pumpAndSettle();
      expect(find.text('Rice'), findsOneWidget);
      expect(find.text('Milk'), findsNothing);
    });

    testWidgets('Debts totals what customers owe and what the shop owes', (tester) async {
      await startApp(tester, seed: (shop) {
        shop.seed('Customers', 1, {'FirstName': 'Mona', 'LastName': 'Haddad', 'PhoneNumber': '70', 'Balance': '300', 'IsDeleted': 0});
        shop.seed('Customers', 2, {'FirstName': 'Abu', 'LastName': 'Karim', 'Balance': '120', 'IsDeleted': 0});
        shop.seed('Suppliers', 2, {'Name': 'Dairy Co', 'IsActive': 1, 'IsDeleted': 0, 'Balance': '1000', 'CurrencyId': 1});
      });
      await tester.tap(tab('Debts'));
      await tester.pumpAndSettle();

      expect(find.text(r'$420'), findsOneWidget);
      expect(find.text('Mona Haddad'), findsOneWidget);
      expect(find.text('Dairy Co'), findsOneWidget);
      expect(find.text(r'$1,000'), findsWidgets);
    });
  });

  group('manage', () {
    testWidgets('adding a customer sends only what was typed and shows it under Changes', (tester) async {
      final app = await startApp(tester);
      await openManage(tester);

      await tester.tap(find.text('Add a customer'));
      await tester.pumpAndSettle();
      await tester.enterText(find.widgetWithText(TextFormField, 'First name'), 'Nadine');
      await tester.enterText(find.widgetWithText(TextFormField, 'Last name'), 'Fares');
      await tester.enterText(find.widgetWithText(TextFormField, 'Phone (used for reminders)'), '71234567');
      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pumpAndSettle();

      expect(app.api.sent, hasLength(1));
      final (kind, body, key) = app.api.sent.single;
      expect(kind, ChangeKind.customer);
      expect(body, {'firstName': 'Nadine', 'lastName': 'Fares', 'phoneNumber': '71234567'}); // no empty email/address sent
      expect(key, isNotEmpty);
      expect(find.text('Add a customer'), findsOneWidget); // back on Manage

      await tester.tap(find.text('Changes').first);
      await tester.pumpAndSettle();
      expect(find.text('Nadine Fares'), findsOneWidget);
      expect(find.text('Waiting for shop'), findsOneWidget);
    });

    testWidgets('a customer with a one-letter name is stopped on the phone and nothing is sent', (tester) async {
      final app = await startApp(tester);
      await openManage(tester);
      await tester.tap(find.text('Add a customer'));
      await tester.pumpAndSettle();

      await tester.enterText(find.widgetWithText(TextFormField, 'First name'), 'N');
      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pumpAndSettle();

      expect(find.text('At least 2 characters.'), findsWidgets);
      expect(app.api.sent, isEmpty);
    });

    testWidgets('with no connection nothing is lost: the form stays and says so', (tester) async {
      final app = await startApp(tester);
      await openManage(tester);
      app.api.submitError = OfflineException('no signal');
      await tester.tap(find.text('Add a customer'));
      await tester.pumpAndSettle();
      await tester.enterText(find.widgetWithText(TextFormField, 'First name'), 'Nadine');
      await tester.enterText(find.widgetWithText(TextFormField, 'Last name'), 'Fares');

      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pumpAndSettle();

      expect(find.textContaining('No connection'), findsOneWidget);
      expect(find.widgetWithText(TextFormField, 'First name'), findsOneWidget); // still on the form with the typed values
    });

    testWidgets('a product is sent with the chosen category, supplier and prices as numbers', (tester) async {
      final app = await startApp(tester);
      await openManage(tester);
      await tester.tap(find.text('Add a product'));
      await tester.pumpAndSettle();

      // Like a person: let the form settle between fields (the margin line appears and moves things down).
      Future<void> type(String label, String text) async {
        await tester.enterText(find.widgetWithText(TextFormField, label), text);
        await tester.pumpAndSettle();
      }

      await type('Name', 'Labneh 500 g');
      await type('Barcode', '6291003004512');
      await type('Selling price', '5,50'); // a comma works too
      await type('Cost', '3.8');
      await type('Stock on hand now', '24');

      // Category and supplier are required: saving without them is stopped.
      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pumpAndSettle();
      expect(app.api.sent, isEmpty);
      expect(find.text('Choose one.'), findsWidgets);

      await tester.tap(find.text('Category'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Dairy').last);
      await tester.pumpAndSettle();
      // The shop has one supplier, so the form picked it already.

      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pumpAndSettle();

      final (kind, body, _) = app.api.sent.single;
      expect(kind, ChangeKind.product);
      expect(body['name'], 'Labneh 500 g');
      expect(body['categoryId'], 1);
      expect(body['supplierId'], 1);
      expect(body['sellingPrice'], 5.5);
      expect(body['purchasePrice'], 3.8);
      expect(body['stockQuantity'], 24);
    });

    testWidgets('the product form tells a locked phone why it has no lists', (tester) async {
      await startApp(tester, unlocked: false);
      await openManage(tester);

      await tester.tap(find.text('Add a product'));
      await tester.pumpAndSettle();

      expect(find.textContaining('recovery code'), findsOneWidget);
    });

    testWidgets('a refusal from the shop is explained in plain words under Changes', (tester) async {
      final app = await startApp(tester);
      app.api.stored.add(Change(
        id: 'x',
        type: 'product.create',
        status: ChangeStatus.rejected,
        createdAt: DateTime.now(),
        payload: {'name': 'Pita bread 1 kg'},
        code: 'validation',
        message: 'The shop did not accept some of the values.',
        data: {
          'errors': {
            'barcode': ['Prod_BarcodeExists'],
          },
        },
      ));
      await openManage(tester);

      await tester.tap(find.text('Changes').first);
      await tester.pumpAndSettle();

      expect(find.text('Needs a fix'), findsOneWidget);
      expect(find.text('Barcode: Another product already uses this barcode.'), findsOneWidget);
    });

    testWidgets('signing out returns to the sign-in screen and forgets the shop\'s key', (tester) async {
      final app = await startApp(tester);
      await openManage(tester);

      await tester.tap(find.byTooltip('Sign out'));
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(TextButton, 'Sign out'));
      await tester.pumpAndSettle();

      expect(find.widgetWithText(FilledButton, 'Sign in'), findsOneWidget);
      expect(app.shop.hasKey, isFalse);
      expect(await app.keys.read(), isNull);
    });
  });

  for (final language in ['en', 'ar']) {
    testWidgets('every tab fits a narrow phone with long names and big numbers ($language)', (tester) async {
      String utc(DateTime d) => d.toUtc().toIso8601String().replaceFirst('T', ' ').replaceFirst('Z', '0000');
      final now = DateTime.now();
      final sold = DateTime(now.year, now.month, now.day, 0, 10);
      const long = 'مؤسسة الأمل للتجارة العامة والمواد الغذائية والمشروبات - Al-Amal General Trading & Foodstuff Company';
      await startApp(tester, language: language, size: const Size(360, 780), seed: (shop) {
        shop.seed('PaymentMethods', 1, {'Name': long, 'IsDefault': 1, 'IsDeleted': 0});
        shop.seed('Receipts', 1, {
          'ReceiptNumber': '20261112-000123', 'ReceiptDate': utc(sold), 'TotalAmount': '98765432.10', 'CurrencyId': 1,
          'CustomerName': long, 'PaymentMethodId': 1, 'IsVoid': 0, 'IsDeleted': 0,
        });
        shop.seed('Receipts', 2, {
          'ReceiptNumber': '2', 'ReceiptDate': utc(sold), 'TotalAmount': '1500000', 'CurrencyId': 2,
          'CustomerName': '', 'PaymentMethodId': 1, 'IsVoid': 1, 'IsDeleted': 0,
        });
        shop.seed('Products', 1, {'Name': long, 'Barcode': '6291003004512', 'StockQuantity': '0', 'MinimumStock': '1000000', 'Unit': 'kg', 'IsActive': 1, 'IsDeleted': 0});
        shop.seed('Customers', 1, {'FirstName': long, 'LastName': long, 'PhoneNumber': '+961 70 123 456', 'Balance': '123456789.5', 'IsDeleted': 0});
        shop.seed('Suppliers', 2, {'Name': long, 'Phone': '01 234 567', 'IsActive': 1, 'IsDeleted': 0, 'Balance': '99999999', 'CurrencyId': 2});
      });

      for (final index in [0, 1, 2, 3, 4]) {
        await tester.tap(find.descendant(of: find.byType(NavigationBar), matching: find.byType(NavigationDestination).at(index)));
        await tester.pumpAndSettle();
        final problem = tester.takeException();
        expect(problem is FlutterError ? problem.toStringDeep() : problem, isNull, reason: 'tab $index');
      }
    });
  }

  group('life and motion', () {
    testWidgets('the Stock tab carries a count of what needs restocking', (tester) async {
      await startApp(tester, seed: (shop) {
        shop.seed('Products', 1, {'Name': 'Milk', 'Barcode': '1', 'StockQuantity': '2', 'MinimumStock': '10', 'IsActive': 1, 'IsDeleted': 0});
        shop.seed('Products', 2, {'Name': 'Rice', 'Barcode': '2', 'StockQuantity': '90', 'MinimumStock': '10', 'IsActive': 1, 'IsDeleted': 0});
      });

      expect(find.descendant(of: find.byType(NavigationBar), matching: find.byType(Badge)), findsWidgets);
      expect(find.descendant(of: find.byType(NavigationBar), matching: find.text('1')), findsOneWidget);
    });

    testWidgets('the share button copies a plain-text summary of the day', (tester) async {
      String? copied;
      tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(SystemChannels.platform, (call) async {
        if (call.method == 'Clipboard.setData') copied = (call.arguments as Map)['text'] as String?;
        return null;
      });
      addTearDown(() => tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(SystemChannels.platform, null));
      final now = DateTime.now();
      final sold = DateTime(now.year, now.month, now.day, 0, 5).toUtc().toIso8601String().replaceFirst('T', ' ').replaceFirst('Z', '0000');
      await startApp(tester, seed: (shop) {
        shop.seed('PaymentMethods', 1, {'Name': 'Cash', 'IsDefault': 1, 'IsDeleted': 0});
        shop.seed('Receipts', 1, {
          'ReceiptNumber': '1', 'ReceiptDate': sold, 'TotalAmount': '1284.50', 'CurrencyId': 1,
          'CustomerName': '', 'PaymentMethodId': 1, 'IsVoid': 0, 'IsDeleted': 0,
        });
      });

      await tester.tap(find.byTooltip('Copy today\'s summary'));
      await tester.pumpAndSettle();

      expect(copied, isNot(startsWith(' ')), reason: 'no stray separator when the shop name is empty');
      expect(copied, contains(r'Sales today: $1,284.5 (1 sales)'));
      expect(find.textContaining('Summary copied'), findsOneWidget);
    });

    testWidgets('the password can be shown and hidden again', (tester) async {
      await startApp(tester, signedIn: false, unlocked: false);
      bool hidden() => tester.widget<EditableText>(find.descendant(of: find.widgetWithText(TextFormField, 'Password'), matching: find.byType(EditableText))).obscureText;

      expect(hidden(), isTrue);
      await tester.tap(find.byTooltip('Show password'));
      await tester.pumpAndSettle();
      expect(hidden(), isFalse);
      await tester.tap(find.byTooltip('Hide password'));
      await tester.pumpAndSettle();
      expect(hidden(), isTrue);
    });

    testWidgets('saving shows a confirmation, then closes the form', (tester) async {
      final app = await startApp(tester);
      await openManage(tester);
      await tester.tap(find.text('Add a customer'));
      await tester.pumpAndSettle();
      await tester.enterText(find.widgetWithText(TextFormField, 'First name'), 'Nadine');
      await tester.enterText(find.widgetWithText(TextFormField, 'Last name'), 'Fares');

      await tester.tap(find.widgetWithText(FilledButton, 'Save'));
      await tester.pump(); // the request resolves
      await tester.pump(const Duration(milliseconds: 400));
      expect(find.text('Sent to the shop'), findsOneWidget, reason: 'the check is on screen while it draws');
      expect(find.widgetWithText(TextFormField, 'First name'), findsOneWidget);

      await tester.pumpAndSettle();
      expect(find.widgetWithText(TextFormField, 'First name'), findsNothing);
      expect(app.api.sent, hasLength(1));
    });

    testWidgets('a failed sign-in shakes and shows the reason in a notice', (tester) async {
      tester.view.physicalSize = const Size(800, 2200);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      SharedPreferences.setMockInitialValues({'cloud_url': 'https://cloud.test'});
      final api = FakeApi()..signInFails = true;
      final session = Session(prefs: await SharedPreferences.getInstance(), tokens: MemoryTokenStore(), apiFactory: (_, _) => api);
      final extra = services(await SharedPreferences.getInstance(), session);
    await tester.pumpWidget(OwnerApp(session: session, shop: ShopData(session, MemoryKeyStore()), lock: extra.lock, notify: extra.notify));
      await tester.pumpAndSettle();
      await tester.enterText(find.widgetWithText(TextFormField, 'Email'), 'layla@shop.test');
      await tester.enterText(find.widgetWithText(TextFormField, 'Password'), 'wrong');

      await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 100));
      await tester.pumpAndSettle();

      expect(find.byIcon(Icons.error_outline), findsOneWidget);
      expect(find.text('The email or password is not right.'), findsOneWidget);
    });

    testWidgets('a figure counts up, unless the phone asks for no animation', (tester) async {
      Widget host(bool reduce) => MaterialApp(
            home: MediaQuery(
              data: MediaQueryData(disableAnimations: reduce),
              child: CountUp(value: 100, format: (v) => v.round().toString()),
            ),
          );

      await tester.pumpWidget(host(false));
      await tester.pump(const Duration(milliseconds: 200));
      final mid = int.parse((tester.widget<Text>(find.byType(Text)).data)!);
      expect(mid, greaterThan(0));
      expect(mid, lessThan(100), reason: 'partway through the count');
      await tester.pumpAndSettle();
      expect(find.text('100'), findsOneWidget);

      await tester.pumpWidget(host(true));
      await tester.pump();
      expect(find.text('100'), findsOneWidget, reason: 'with animations off the figure is simply there');
    });
  });

  testWidgets('switching to Arabic mirrors the layout and translates the screen', (tester) async {
    await startApp(tester);
    await openManage(tester);
    await tester.tap(find.byTooltip('Language'));
    await tester.pumpAndSettle();

    expect(find.text('إضافة منتج'), findsOneWidget);
    expect(Directionality.of(tester.element(find.text('إضافة منتج'))), TextDirection.rtl);
  });
}
