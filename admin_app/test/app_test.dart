import 'dart:convert';
import 'package:http/http.dart' as http;

import 'package:coduo_admin/main.dart';
import 'package:coduo_admin/state/session.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fake_cloud.dart';

/// The page's own vertical list (text fields have scrollables of their own).
final page = find.byWidgetPredicate((w) => w is Scrollable && w.axisDirection == AxisDirection.down).first;

/// The app against a fake cloud that answers with the real cloud's responses.
void main() {
  late FakeCloud cloud;
  late Session session;

  setUp(() {
    cloud = FakeCloud();
    session = Session(store: MemoryTokenStore(), client: cloud.client);
  });

  Future<void> start(WidgetTester tester) async {
    tester.view.physicalSize = const Size(1170, 2532);
    tester.view.devicePixelRatio = 3;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(AdminApp(session: session));
    await tester.pumpAndSettle();
  }

  Future<void> signIn(WidgetTester tester) async {
    await tester.enterText(find.widgetWithText(TextField, 'Username'), 'owner');
    await tester.enterText(find.widgetWithText(TextField, 'Password'), 'correct-horse-battery');
    await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
    await tester.pumpAndSettle();
  }

  testWidgets('a wrong password says so; the right one opens the overview', (tester) async {
    await start(tester);
    await tester.enterText(find.widgetWithText(TextField, 'Username'), 'owner');
    await tester.enterText(find.widgetWithText(TextField, 'Password'), 'nope');
    await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
    await tester.pumpAndSettle();
    expect(find.text('The username or password is not right.'), findsOneWidget);

    await signIn(tester);
    expect(find.text('Needs you'), findsOneWidget);
    expect(find.text('Collected this month'), findsOneWidget);
    expect(await session.store.read('admin_token'), 'nxa_test');
  });

  testWidgets('clients list, then a client page with every section', (tester) async {
    await start(tester);
    await signIn(tester);
    await tester.tap(find.text('Clients').last);
    await tester.pumpAndSettle();
    expect(find.text('Cedar Bakery'), findsOneWidget);
    expect(find.text('Juice Corner'), findsOneWidget);
    expect(find.text('Offline Grocer'), findsOneWidget);

    // The "Menu only" filter asks the cloud for it.
    await tester.ensureVisible(find.widgetWithText(ChoiceChip, 'Menu only'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(ChoiceChip, 'Menu only'));
    await tester.pumpAndSettle();
    expect(cloud.requests.last.url.queryParameters['filter'], 'menu');

    await tester.tap(find.text('Cedar Bakery'));
    await tester.pumpAndSettle();
    expect(find.text('Online subscription'), findsWidgets);
    expect(find.text('+12 mo'), findsOneWidget);
    for (final section in ['Branches', 'POS licences', 'Payments', 'Phone logins', 'History']) {
      await tester.scrollUntilVisible(find.text(section), 200, scrollable: page);
      expect(find.text(section), findsOneWidget, reason: section);
    }
  });

  testWidgets('adding months asks first, then calls the cloud', (tester) async {
    await start(tester);
    await signIn(tester);
    await tester.tap(find.text('Clients').last);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Cedar Bakery'));
    await tester.pumpAndSettle();

    await tester.tap(find.text('+12 mo'));
    await tester.pumpAndSettle();
    expect(find.text('Add 12 months?'), findsOneWidget);
    await tester.tap(find.widgetWithText(FilledButton, 'Add 12'));
    await tester.pumpAndSettle();
    expect(jsonDecode(cloud.sent('POST', '/clients/1/subscription/extend').single.body), {'months': 12});
  });

  testWidgets('a new online-menu-only client cannot have "None yet" and gets 12 months', (tester) async {
    await start(tester);
    await signIn(tester);
    await tester.tap(find.text('Clients').last);
    await tester.pumpAndSettle();
    await tester.tap(find.text('New client'));
    await tester.pumpAndSettle();

    expect(find.text('None yet'), findsOneWidget); // POS + Online may start with none
    await tester.enterText(find.widgetWithText(TextField, 'Shop name'), 'Juice Corner');
    await tester.tap(find.text('Online menu only'));
    await tester.pumpAndSettle();
    expect(find.text('None yet'), findsNothing);

    await tester.scrollUntilVisible(find.text('Create client'), 200, scrollable: page);
    await tester.tap(find.text('Create client'));
    await tester.pumpAndSettle();
    final body = jsonDecode(cloud.sent('POST', '/clients').single.body) as Map;
    expect(body['type'], 'menu');
    expect(body['months'], 12);
    expect(body['name'], 'Juice Corner');
  });

  testWidgets('a payment can be for several things', (tester) async {
    await start(tester);
    await signIn(tester);
    await tester.tap(find.text('Finance').last);
    await tester.pumpAndSettle();
    expect(find.text('Collected'), findsOneWidget);

    await tester.tap(find.text('Record'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Payment (money in)'));
    await tester.pumpAndSettle();
    await tester.enterText(find.widgetWithText(TextField, 'Where it came from'), 'Owner capital');
    await tester.enterText(find.widgetWithText(TextField, r'Amount ($)'), '1,200');
    await tester.tap(find.widgetWithText(FilterChip, 'Set-up / installation'));
    await tester.tap(find.widgetWithText(FilterChip, 'POS licence'));
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(find.text('Record payment'), 200, scrollable: page);
    await tester.ensureVisible(find.text('Record payment'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Record payment'));
    await tester.pumpAndSettle();

    final body = jsonDecode(cloud.sent('POST', '/finance/payments').single.body) as Map;
    expect(body['amount'], 1200);
    expect(body['payer'], 'Owner capital');
    // In the form's order, whatever order they were ticked in.
    expect(body['purposes'], ['Online subscription', 'POS licence', 'Set-up / installation']);
  });

  testWidgets('a payment recorded on a client page is in Finance without pulling to refresh', (tester) async {
    await start(tester);
    await signIn(tester);
    await tester.tap(find.text('Finance').last);
    await tester.pumpAndSettle();
    final summaries = cloud.sent('GET', '/finance/summary').length;
    final overviews = cloud.sent('GET', '/overview').length;

    await tester.tap(find.text('Clients').last);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Cedar Bakery'));
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(find.text('Record'), 200, scrollable: page);
    await tester.ensureVisible(find.text('Record'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Record'));
    await tester.pumpAndSettle();
    await tester.enterText(find.widgetWithText(TextField, r'Amount ($)'), '120');
    // Done typing: the keyboard closes (a focused field would pull the list back to itself).
    await tester.testTextInput.receiveAction(TextInputAction.done);
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(find.text('Record payment'), 200, scrollable: page);
    await tester.ensureVisible(find.text('Record payment'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Record payment'));
    await tester.pumpAndSettle();
    expect(cloud.sent('POST', '/finance/payments'), hasLength(1));

    // Every tab already asked the cloud again: Finance and the Overview show the payment when opened.
    expect(cloud.sent('GET', '/finance/summary').length, greaterThan(summaries));
    expect(cloud.sent('GET', '/overview').length, greaterThan(overviews));
    // ...and the client page it was recorded on reloaded its payments.
    expect(cloud.sent('GET', '/clients/1/payments').length, greaterThan(1));
  });

  testWidgets('coming back to the app loads every screen again', (tester) async {
    await start(tester);
    await signIn(tester);
    final before = cloud.sent('GET', '/overview').length;
    // The way a phone goes to the background and back.
    for (final state in [AppLifecycleState.inactive, AppLifecycleState.hidden, AppLifecycleState.paused, AppLifecycleState.hidden, AppLifecycleState.inactive, AppLifecycleState.resumed]) {
      tester.binding.handleAppLifecycleStateChanged(state);
    }
    await tester.pumpAndSettle();
    expect(cloud.sent('GET', '/overview').length, greaterThan(before));
  });

  testWidgets('when the cloud stops accepting the token the app goes back to sign-in', (tester) async {
    await start(tester);
    await signIn(tester);
    cloud.overrides['GET /clients'] = (r) => http401();
    await tester.tap(find.text('Clients').last);
    await tester.pumpAndSettle();
    // The list loaded on sign-in already; pull to refresh asks again.
    await tester.fling(page, const Offset(0, 400), 1000);
    await tester.pumpAndSettle();
    expect(find.text('You were signed out. Sign in again.'), findsOneWidget);
    expect(await session.store.read('admin_token'), isNull);
  });
}

http.Response http401() => http.Response(jsonEncode({'code': 'unauthorized', 'message': 'Please sign in again.'}), 401);
