import 'package:coduo_admin/api/models.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/fake_cloud.dart';

Json obj(String name) => (fixture(name) as Map).cast<String, dynamic>();
List<Json> list(String name) => jsonList(fixture(name));

/// Every response the cloud sends parses into what the screens read.
void main() {
  test('times without a zone are read as UTC', () {
    expect(utc('2026-11-24T16:43:55.7719475'), DateTime.utc(2026, 11, 24, 16, 43, 55, 771, 947).toLocal());
    expect(utc('2026-11-24T16:43:55Z'), DateTime.utc(2026, 11, 24, 16, 43, 55).toLocal());
    expect(utc(null), isNull);
    expect(day('2026-11-24'), DateTime(2026, 11, 24));
  });

  test('overview', () {
    final o = Overview.fromJson(obj('overview'));
    expect(o.clients, 3);
    expect(o.pulse, hasLength(24));
    expect(o.collected, greaterThan(0));
  });

  test('client rows know the three kinds', () {
    final rows = list('clients').map(ClientRow.fromJson).toList();
    expect(rows.map((r) => r.type), [ClientType.online, ClientType.menu, ClientType.pos]);
    expect(rows.first.state, PlanState.active);
    expect(rows.first.expiresAt, isNotNull);
    expect(rows.first.tags, contains('waiting'));
  });

  test('client detail with its branch, subscription and menu', () {
    final c = ClientDetail.fromJson(obj('client_detail'));
    expect(c.name, 'Cedar Bakery');
    expect(c.type, ClientType.online);
    expect(c.menuLive, isTrue);
    expect(c.subscription!.state, PlanState.active);
    expect(c.sites.single.connection, isNotEmpty);
    expect(c.hasPos && c.hasOnline, isTrue);
  });

  test('a new menu-only client has no branch or link', () {
    final r = NewClientResult.fromJson(obj('client_create_menu'));
    expect(r.client.type, ClientType.menu);
    expect(r.client.hasPos, isFalse);
    expect(r.link, isNull);
    final online = NewClientResult.fromJson(obj('client_create_online'));
    expect(online.link, contains('/enroll/'));
  });

  test('branch detail', () {
    final d = SiteDetail.fromJson(obj('site'));
    expect(d.site.name, 'Main branch');
    expect(d.openLinkExpiresAt, isNotNull);
    expect(d.held, isFalse);
  });

  test('links and logins handed over once', () {
    final link = LinkResult.fromJson(obj('site_link'));
    expect(link.url, contains('/enroll/'));
    expect(link.whatsAppUrl, startsWith('https://wa.me/'));
    final h = LoginHandOver.fromJson(obj('login_add'));
    expect(h.login.role, 'manager');
    expect(h.password, isNotEmpty);
  });

  test('finance summary, payments with several purposes, expenses and budgets', () {
    final s = FinanceSummary.fromJson(obj('summary'));
    expect(s.label, isNotEmpty);
    expect(s.query, startsWith('m='));
    expect(s.trend, hasLength(12));
    expect(s.income!.planned, 2000);
    expect(s.lines, isNotEmpty);

    final p = Payment.fromJson(obj('payment'));
    expect(p.purposes, ['Online subscription', 'POS licence']);
    expect(p.purpose, 'Online subscription + POS licence');
    // An older cloud sends only the joined text.
    expect(Payment.fromJson({...obj('payment'), 'purposes': null}).purposes, ['Online subscription', 'POS licence']);

    expect(Expense.fromJson(obj('expense')).category, 'Hosting & servers');
    expect(list('budgets').map(Budget.fromJson).single.month, '2026-11');
    final options = FinanceOptions.fromJson(obj('options'));
    expect(options.purposes, contains('Online menu'));
  });

  test('licences and the signer', () {
    final l = list('licenses').map(Licence.fromJson).single;
    expect(l.plan, 'Pro');
    expect(l.seats, 2);
    expect(l.key, startsWith('NXL1-'));
    expect(l.timeLimited, isFalse);
    expect(Signer.fromJson(obj('signer')).ready, isTrue);
  });

  test('account and sessions', () {
    expect(Me.fromJson(obj('me')).currency, r'$');
    expect(list('sessions').map(SessionInfo.fromJson).single.current, isTrue);
    expect(Subscription.fromJson(obj('extend')).expiresAt, isNotNull);
    expect(list('events').map(ClientEvent.fromJson).first.text, isNotEmpty);
  });
}
