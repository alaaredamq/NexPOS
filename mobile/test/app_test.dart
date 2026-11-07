import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:nexpos_owner/api/owner_api.dart';
import 'package:nexpos_owner/main.dart';
import 'package:nexpos_owner/state/session.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Stands in for the cloud: records what the app sends and answers like the real owner API would.
class FakeApi implements OwnerApi {
  final List<(ChangeKind, Map<String, dynamic>, String)> sent = [];
  final List<Change> stored = [];
  Object? submitError;
  bool signInFails = false;

  @override
  Future<SignedIn> signIn(String email, String password) async {
    if (signInFails) throw ApiException(401, 'invalid_credentials', 'The email or password is not right.');
    return SignedIn(token: 'nxo_test', displayName: 'Layla', clientName: 'Al-Amal Market', siteName: 'Main till');
  }

  @override
  Future<void> signOut() async {}

  @override
  Future<Lookups> lookups() async => Lookups(
        categories: [Option(1, 'Dairy'), Option(2, 'Drinks')],
        suppliers: [Option(1, 'Lebanese Dairy Co.')],
        currencies: [CurrencyOption(1, 'USD', r'$', true), CurrencyOption(2, 'LBP', 'L.L.', false)],
      );

  @override
  Future<Change> submit(ChangeKind kind, Map<String, dynamic> body, String idempotencyKey) async {
    if (submitError != null) throw submitError!;
    sent.add((kind, body, idempotencyKey));
    final change = Change(
      id: 'c${stored.length}',
      type: switch (kind) {
        ChangeKind.customer => 'customer.create',
        ChangeKind.supplier => 'supplier.create',
        ChangeKind.product => 'product.create',
      },
      status: ChangeStatus.queued,
      createdAt: DateTime.now(),
      payload: body,
    );
    stored.insert(0, change);
    return change;
  }

  @override
  Future<List<Change>> changes() async => List.of(stored);

  @override
  Future<Change> discard(String id) async => stored.firstWhere((c) => c.id == id);
}

Future<(Session, FakeApi)> startApp(WidgetTester tester, {bool signedIn = true}) async {
  // A tall phone, so every field of the longest form is on screen and a tap on it lands.
  tester.view.physicalSize = const Size(800, 2200);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
  SharedPreferences.setMockInitialValues({'cloud_url': 'https://cloud.test', 'language': 'en'});
  final api = FakeApi();
  final tokens = MemoryTokenStore();
  if (signedIn) tokens.value = 'nxo_test';
  final session = Session(
    prefs: await SharedPreferences.getInstance(),
    tokens: tokens,
    apiFactory: (url, token) => api,
  );
  await session.restore();
  await tester.pumpWidget(OwnerApp(session: session));
  await tester.pumpAndSettle();
  return (session, api);
}

void main() {
  testWidgets('signing in with the right details opens the app', (tester) async {
    await startApp(tester, signedIn: false);
    expect(find.text('Sign in'), findsWidgets);

    await tester.enterText(find.widgetWithText(TextFormField, 'Email'), 'layla@shop.test');
    await tester.enterText(find.widgetWithText(TextFormField, 'Password'), 'secret');
    await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
    await tester.pumpAndSettle();

    expect(find.text('Al-Amal Market'), findsOneWidget);
    expect(find.text('Add a product'), findsOneWidget);
  });

  testWidgets('a wrong password shows the cloud\'s message and stays on the sign-in screen', (tester) async {
    tester.view.physicalSize = const Size(800, 2200);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    SharedPreferences.setMockInitialValues({'cloud_url': 'https://cloud.test'});
    final api = FakeApi()..signInFails = true;
    final session = Session(prefs: await SharedPreferences.getInstance(), tokens: MemoryTokenStore(), apiFactory: (_, _) => api);
    await tester.pumpWidget(OwnerApp(session: session));
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
    expect(find.text('Add a customer'), findsOneWidget);
    expect(find.widgetWithText(FilledButton, 'Sign in'), findsNothing);
  });

  testWidgets('adding a customer sends only what was typed and shows it under Changes', (tester) async {
    final (_, api) = await startApp(tester);

    await tester.tap(find.text('Add a customer'));
    await tester.pumpAndSettle();
    await tester.enterText(find.widgetWithText(TextFormField, 'First name'), 'Nadine');
    await tester.enterText(find.widgetWithText(TextFormField, 'Last name'), 'Fares');
    await tester.enterText(find.widgetWithText(TextFormField, 'Phone (used for reminders)'), '71234567');
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();

    expect(api.sent, hasLength(1));
    final (kind, body, key) = api.sent.single;
    expect(kind, ChangeKind.customer);
    expect(body, {'firstName': 'Nadine', 'lastName': 'Fares', 'phoneNumber': '71234567'}); // no empty email/address sent
    expect(key, isNotEmpty);
    expect(find.text('Add a customer'), findsOneWidget); // back on Manage

    await tester.tap(find.text('Changes'));
    await tester.pumpAndSettle();
    expect(find.text('Nadine Fares'), findsOneWidget);
    expect(find.text('Waiting for shop'), findsOneWidget);
  });

  testWidgets('a customer with a one-letter name is stopped on the phone and nothing is sent', (tester) async {
    final (_, api) = await startApp(tester);
    await tester.tap(find.text('Add a customer'));
    await tester.pumpAndSettle();

    await tester.enterText(find.widgetWithText(TextFormField, 'First name'), 'N');
    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();

    expect(find.text('At least 2 characters.'), findsWidgets);
    expect(api.sent, isEmpty);
  });

  testWidgets('with no connection nothing is lost: the form stays and says so', (tester) async {
    final (_, api) = await startApp(tester);
    api.submitError = OfflineException('no signal');
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
    final (_, api) = await startApp(tester);
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
    expect(api.sent, isEmpty);
    expect(find.text('Choose one.'), findsWidgets);

    await tester.tap(find.text('Category'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Dairy').last);
    await tester.pumpAndSettle();
    // The shop has one supplier, so the form picked it already.

    await tester.tap(find.widgetWithText(FilledButton, 'Save'));
    await tester.pumpAndSettle();

    final (kind, body, _) = api.sent.single;
    expect(kind, ChangeKind.product);
    expect(body['name'], 'Labneh 500 g');
    expect(body['categoryId'], 1);
    expect(body['supplierId'], 1);
    expect(body['sellingPrice'], 5.5);
    expect(body['purchasePrice'], 3.8);
    expect(body['stockQuantity'], 24);
  });

  testWidgets('a refusal from the shop is explained in plain words under Changes', (tester) async {
    final (_, api) = await startApp(tester);
    api.stored.add(Change(
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

    await tester.tap(find.text('Changes'));
    await tester.pumpAndSettle();

    expect(find.text('Needs a fix'), findsOneWidget);
    expect(find.text('Barcode: Another product already uses this barcode.'), findsOneWidget);
  });

  testWidgets('switching to Arabic mirrors the layout and translates the screen', (tester) async {
    await startApp(tester);
    await tester.tap(find.byTooltip('Language'));
    await tester.pumpAndSettle();

    expect(find.text('إضافة منتج'), findsOneWidget);
    expect(Directionality.of(tester.element(find.text('إضافة منتج'))), TextDirection.rtl);
  });
}
