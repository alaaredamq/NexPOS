// What the admin API (/api/admin/v1, docs/online/admin-api.md) sends back, as typed values. Every field a screen uses is here; the
// shapes were taken from the cloud's own responses (test/fixtures), so a renamed field fails the tests rather than a screen.

typedef Json = Map<String, dynamic>;

/// A UTC time from the cloud. Some arrive without a "Z" (they are still UTC), so one is added before parsing. Shown in local time.
DateTime? utc(Object? value) {
  if (value is! String || value.isEmpty) return null;
  final hasZone = value.endsWith('Z') || RegExp(r'[+-]\d\d:\d\d$').hasMatch(value);
  return DateTime.tryParse(hasZone ? value : '${value}Z')?.toLocal();
}

/// A day ("2026-11-24") in the provider's own calendar: no time, no zone.
DateTime? day(Object? value) => value is String && value.length >= 10 ? DateTime.tryParse(value.substring(0, 10)) : null;

double money(Object? value) => value is num ? value.toDouble() : 0;
double? moneyOrNull(Object? value) => value is num ? value.toDouble() : null;
List<Json> jsonList(Object? value) => value is List ? value.whereType<Map>().map((e) => e.cast<String, dynamic>()).toList() : const [];
List<String> stringList(Object? value) => value is List ? value.whereType<String>().toList() : const [];

/// What a client bought. One subscription (Online) covers the cloud copy, the phone app and the online menu.
enum ClientType {
  pos('pos', 'POS only', 'A licence, no Online'),
  online('online', 'POS + Online', 'A licence, plus cloud copy, phone app and online menu'),
  menu('menu', 'Online menu only', 'No POS, no licence: just their menu');

  const ClientType(this.code, this.label, this.help);
  final String code;
  final String label;
  final String help;

  static ClientType parse(Object? code) => values.firstWhere((t) => t.code == code, orElse: () => online);
}

/// `None`, `Active`, `Grace`, `Expired` or `Suspended`.
enum PlanState {
  none('None'),
  active('Active'),
  grace('Grace'),
  expired('Expired'),
  suspended('Suspended');

  const PlanState(this.code);
  final String code;

  static PlanState parse(Object? code) => values.firstWhere((s) => s.code == code, orElse: () => none);
}

class Me {
  Me({required this.username, required this.deviceName, required this.currency, required this.timeZone, required this.today});

  factory Me.fromJson(Json j) => Me(
        username: j['username'] as String? ?? '',
        deviceName: j['deviceName'] as String?,
        currency: j['currency'] as String? ?? r'$',
        timeZone: j['timeZone'] as String? ?? '',
        today: day(j['today']) ?? DateTime.now(),
      );

  final String username;
  final String? deviceName;
  final String currency;
  final String timeZone;
  final DateTime today;
}

class SessionInfo {
  SessionInfo({required this.id, this.deviceName, this.createdAt, this.lastUsedAt, this.expiresAt, required this.current});

  factory SessionInfo.fromJson(Json j) => SessionInfo(
        id: j['id'] as int,
        deviceName: j['deviceName'] as String?,
        createdAt: utc(j['createdAtUtc']),
        lastUsedAt: utc(j['lastUsedAtUtc']),
        expiresAt: utc(j['expiresAtUtc']),
        current: j['current'] == true,
      );

  final int id;
  final String? deviceName;
  final DateTime? createdAt;
  final DateTime? lastUsedAt;
  final DateTime? expiresAt;
  final bool current;
}

/// One hour of the 24-hour contact strip: how often a shop PC was heard from.
class PulseCell {
  PulseCell(this.hour, this.contacts);
  factory PulseCell.fromJson(Json j) => PulseCell(utc(j['hourUtc']), j['contacts'] as int? ?? 0);
  final DateTime? hour;
  final int contacts;
}

List<PulseCell> pulseOf(Object? value) => jsonList(value).map(PulseCell.fromJson).toList();

/// Something that needs the provider ("Online ended", "Link expired", "Licence ends in 5 days").
class Attention {
  Attention({required this.bad, required this.title, required this.reason, required this.action, this.clientId, this.portalPath});

  factory Attention.fromJson(Json j) => Attention(
        bad: j['severity'] == 'bad',
        title: j['title'] as String? ?? '',
        reason: j['reason'] as String? ?? '',
        action: j['action'] as String? ?? '',
        clientId: j['clientId'] as int?,
        portalPath: j['portalPath'] as String?,
      );

  final bool bad;
  final String title;
  final String reason;
  final String action;
  final int? clientId;
  final String? portalPath;

  /// A licence item links to the Licences page, not to a client page.
  bool get isLicence => portalPath?.startsWith('/admin/Licenses') ?? false;
}

class Overview {
  Overview({
    required this.summary,
    required this.clients,
    required this.connected,
    required this.plansActive,
    required this.plansEndingSoon,
    required this.plansEnded,
    required this.productsHeld,
    required this.ordersHeld,
    required this.pulse,
    required this.litHours,
    required this.attention,
    required this.collected,
    required this.spent,
    required this.profit,
    required this.balance,
  });

  factory Overview.fromJson(Json j) {
    final plans = (j['plans'] as Map?)?.cast<String, dynamic>() ?? const {};
    final month = (j['thisMonth'] as Map?)?.cast<String, dynamic>() ?? const {};
    return Overview(
      summary: j['summary'] as String? ?? '',
      clients: j['clients'] as int? ?? 0,
      connected: j['connected'] as int? ?? 0,
      plansActive: plans['active'] as int? ?? 0,
      plansEndingSoon: plans['endingSoon'] as int? ?? 0,
      plansEnded: plans['ended'] as int? ?? 0,
      productsHeld: j['productsHeld'] as int? ?? 0,
      ordersHeld: j['ordersHeld'] as int? ?? 0,
      pulse: pulseOf(j['pulse']),
      litHours: j['litHours'] as int? ?? 0,
      attention: jsonList(j['attention']).map(Attention.fromJson).toList(),
      collected: money(month['collected']),
      spent: money(month['spent']),
      profit: money(month['profit']),
      balance: money(month['balance']),
    );
  }

  final String summary;
  final int clients;
  final int connected;
  final int plansActive;
  final int plansEndingSoon;
  final int plansEnded;
  final int productsHeld;
  final int ordersHeld;
  final List<PulseCell> pulse;
  final int litHours;
  final List<Attention> attention;
  final double collected;
  final double spent;
  final double profit;
  final double balance;
}

/// A client as the list shows it.
class ClientRow {
  ClientRow({
    required this.id,
    required this.name,
    required this.isActive,
    required this.type,
    this.phone,
    this.email,
    required this.branches,
    required this.state,
    this.expiresAt,
    required this.suspended,
    required this.endingSoon,
    required this.planNote,
    this.lastSeenAt,
    required this.seen,
    required this.tags,
    this.products,
    this.orders,
    required this.pulse,
  });

  factory ClientRow.fromJson(Json j) {
    final sub = (j['subscription'] as Map?)?.cast<String, dynamic>() ?? const {};
    return ClientRow(
      id: j['id'] as int,
      name: j['name'] as String? ?? '',
      isActive: j['isActive'] != false,
      type: ClientType.parse(j['type']),
      phone: j['phone'] as String?,
      email: j['email'] as String?,
      branches: j['branches'] as int? ?? 0,
      state: PlanState.parse(sub['state']),
      expiresAt: utc(sub['expiresAtUtc']),
      suspended: sub['suspended'] == true,
      endingSoon: sub['endingSoon'] == true,
      planNote: sub['note'] as String? ?? '',
      lastSeenAt: utc(j['lastSeenAtUtc']),
      seen: j['seen'] as String? ?? '',
      tags: stringList(j['tags']),
      products: j['products'] as int?,
      orders: j['orders'] as int?,
      pulse: pulseOf(j['pulse']),
    );
  }

  final int id;
  final String name;
  final bool isActive;
  final ClientType type;
  final String? phone;
  final String? email;
  final int branches;
  final PlanState state;
  final DateTime? expiresAt;
  final bool suspended;
  final bool endingSoon;
  final String planNote;
  final DateTime? lastSeenAt;
  final String seen;
  final List<String> tags;
  final int? products;
  final int? orders;
  final List<PulseCell> pulse;
}

class Subscription {
  Subscription({required this.state, this.startsAt, this.expiresAt, required this.suspended, this.graceEndsAt});

  factory Subscription.fromJson(Json j) => Subscription(
        state: PlanState.parse(j['state']),
        startsAt: utc(j['startsAtUtc']),
        expiresAt: utc(j['expiresAtUtc']),
        suspended: j['suspended'] == true,
        graceEndsAt: utc(j['graceEndsAtUtc']),
      );

  final PlanState state;
  final DateTime? startsAt;
  final DateTime? expiresAt;
  final bool suspended;
  final DateTime? graceEndsAt;
}

/// A branch: one shop PC (with its own database) connected to the cloud.
class Site {
  Site({
    required this.id,
    required this.clientId,
    required this.name,
    this.machineId,
    required this.isDisabled,
    required this.connection,
    required this.connectionLabel,
    required this.connectionHelp,
    required this.health,
    this.lastSeenAt,
    this.lastSyncAt,
    required this.resyncRequested,
    this.shopPlan,
    this.licenseKind,
    this.licenseUntil,
    this.seats,
    required this.pulse,
  });

  factory Site.fromJson(Json j) {
    final shop = (j['shop'] as Map?)?.cast<String, dynamic>() ?? const {};
    return Site(
      id: j['id'] as String,
      clientId: j['clientId'] as int? ?? 0,
      name: j['name'] as String? ?? '',
      machineId: j['machineId'] as String?,
      isDisabled: j['isDisabled'] == true,
      connection: j['connection'] as String? ?? '',
      connectionLabel: j['connectionLabel'] as String? ?? '',
      connectionHelp: j['connectionHelp'] as String? ?? '',
      health: j['health'] as String? ?? 'Never',
      lastSeenAt: utc(j['lastSeenAtUtc']),
      lastSyncAt: utc(j['lastSyncAtUtc']),
      resyncRequested: j['resyncRequested'] == true,
      shopPlan: shop['plan'] as String?,
      licenseKind: shop['licenseKind'] as String?,
      licenseUntil: day(shop['licenseUntil']),
      seats: shop['seats'] as int?,
      pulse: pulseOf(j['pulse']),
    );
  }

  final String id;
  final int clientId;
  final String name;
  final String? machineId;
  final bool isDisabled;

  /// `NotLinked`, `WaitingForShop`, `LinkExpired`, `Connected` or `Disconnected`.
  final String connection;
  final String connectionLabel;
  final String connectionHelp;

  /// `Never`, `Ok`, `Stale` or `Down`.
  final String health;
  final DateTime? lastSeenAt;
  final DateTime? lastSyncAt;
  final bool resyncRequested;
  final String? shopPlan;
  final String? licenseKind;
  final DateTime? licenseUntil;
  final int? seats;
  final List<PulseCell> pulse;

  bool get connected => connection == 'Connected';
}

class SiteDetail {
  SiteDetail({required this.site, required this.held, required this.encrypted, required this.counts, required this.tokens, this.openLinkExpiresAt, required this.syncLog});

  factory SiteDetail.fromJson(Json j) {
    final data = (j['data'] as Map?)?.cast<String, dynamic>() ?? const {};
    final counts = (data['counts'] as Map?)?.cast<String, dynamic>() ?? const {};
    final link = (j['openLink'] as Map?)?.cast<String, dynamic>();
    return SiteDetail(
      site: Site.fromJson((j['site'] as Map).cast<String, dynamic>()),
      held: data['held'] == true,
      encrypted: data['encrypted'] == true,
      counts: {for (final e in counts.entries) e.key: (e.value as num?)?.toInt() ?? 0},
      tokens: jsonList(j['tokens']).map(DeviceToken.fromJson).toList(),
      openLinkExpiresAt: utc(link?['expiresAtUtc']),
      syncLog: jsonList(j['syncLog']).map(SyncLogLine.fromJson).toList(),
    );
  }

  final Site site;
  final bool held;
  final bool encrypted;
  final Map<String, int> counts;
  final List<DeviceToken> tokens;
  final DateTime? openLinkExpiresAt;
  final List<SyncLogLine> syncLog;
}

class DeviceToken {
  DeviceToken({required this.prefix, this.createdAt, this.lastUsedAt, this.revokedAt});

  factory DeviceToken.fromJson(Json j) => DeviceToken(
        prefix: j['prefix'] as String? ?? '',
        createdAt: utc(j['createdAtUtc']),
        lastUsedAt: utc(j['lastUsedAtUtc']),
        revokedAt: utc(j['revokedAtUtc']),
      );

  final String prefix;
  final DateTime? createdAt;
  final DateTime? lastUsedAt;
  final DateTime? revokedAt;
}

class SyncLogLine {
  SyncLogLine({this.receivedAt, required this.changes, required this.outcome, this.error});

  factory SyncLogLine.fromJson(Json j) => SyncLogLine(
        receivedAt: utc(j['receivedAtUtc']),
        changes: j['changes'] as int? ?? 0,
        outcome: j['outcome'] as String? ?? '',
        error: j['error'] as String?,
      );

  final DateTime? receivedAt;
  final int changes;
  final String outcome;
  final String? error;
}

/// A phone login: what the shop owner (or their staff) types in the owner app or on the owner website.
class OwnerLogin {
  OwnerLogin({required this.id, required this.signIn, this.displayName, required this.role, required this.isActive, this.createdAt, this.lastSignInAt});

  factory OwnerLogin.fromJson(Json j) => OwnerLogin(
        id: j['id'] as int,
        signIn: j['signIn'] as String? ?? '',
        displayName: j['displayName'] as String?,
        role: j['role'] as String? ?? 'owner',
        isActive: j['isActive'] != false,
        createdAt: utc(j['createdAtUtc']),
        lastSignInAt: utc(j['lastSignInAtUtc']),
      );

  final int id;
  final String signIn;
  final String? displayName;

  /// `owner`, `manager` or `staff`.
  final String role;
  final bool isActive;
  final DateTime? createdAt;
  final DateTime? lastSignInAt;
}

class ClientDetail {
  ClientDetail({
    required this.id,
    required this.name,
    this.phone,
    this.email,
    this.notes,
    required this.isActive,
    this.createdAt,
    required this.type,
    required this.menuLive,
    this.subscription,
    required this.state,
    required this.sites,
    required this.logins,
    required this.paymentCount,
    required this.paymentsTotal,
    required this.paymentsLast12Months,
    this.lastPayment,
    required this.attention,
  });

  factory ClientDetail.fromJson(Json j) {
    final pay = (j['payments'] as Map?)?.cast<String, dynamic>() ?? const {};
    final sub = (j['subscription'] as Map?)?.cast<String, dynamic>();
    final last = (pay['last'] as Map?)?.cast<String, dynamic>();
    return ClientDetail(
      id: j['id'] as int,
      name: j['name'] as String? ?? '',
      phone: j['phone'] as String?,
      email: j['email'] as String?,
      notes: j['notes'] as String?,
      isActive: j['isActive'] != false,
      createdAt: utc(j['createdAtUtc']),
      type: ClientType.parse(j['type']),
      menuLive: j['menuLive'] == true,
      subscription: sub == null ? null : Subscription.fromJson(sub),
      state: PlanState.parse(j['subscriptionState']),
      sites: jsonList(j['sites']).map(Site.fromJson).toList(),
      logins: jsonList(j['logins']).map(OwnerLogin.fromJson).toList(),
      paymentCount: pay['count'] as int? ?? 0,
      paymentsTotal: money(pay['total']),
      paymentsLast12Months: money(pay['last12Months']),
      lastPayment: last == null ? null : Payment.fromJson(last),
      attention: jsonList(j['attention']).map(Attention.fromJson).toList(),
    );
  }

  final int id;
  final String name;
  final String? phone;
  final String? email;
  final String? notes;
  final bool isActive;
  final DateTime? createdAt;
  final ClientType type;
  final bool menuLive;
  final Subscription? subscription;
  final PlanState state;
  final List<Site> sites;
  final List<OwnerLogin> logins;
  final int paymentCount;
  final double paymentsTotal;
  final double paymentsLast12Months;
  final Payment? lastPayment;
  final List<Attention> attention;

  bool get hasOnline => type != ClientType.pos;
  bool get hasPos => type != ClientType.menu;
}

class ClientEvent {
  ClientEvent(this.at, this.text);
  factory ClientEvent.fromJson(Json j) => ClientEvent(utc(j['atUtc']), j['text'] as String? ?? '');
  final DateTime? at;
  final String text;
}

/// A link (or one-time password) the cloud shows only once: send it now, it cannot be fetched again.
class ShownOnce {
  ShownOnce({required this.title, required this.text, this.whatsAppUrl, this.expiresAt, this.note});

  final String title;
  final String text;
  final String? whatsAppUrl;
  final DateTime? expiresAt;
  final String? note;
}

class NewClientResult {
  NewClientResult({required this.client, this.siteId, this.link, this.linkExpiresAt});

  factory NewClientResult.fromJson(Json j) {
    final link = (j['link'] as Map?)?.cast<String, dynamic>();
    return NewClientResult(
      client: ClientDetail.fromJson((j['client'] as Map).cast<String, dynamic>()),
      siteId: j['siteId'] as String?,
      link: link?['url'] as String?,
      linkExpiresAt: utc(link?['expiresAtUtc']),
    );
  }

  final ClientDetail client;
  final String? siteId;
  final String? link;
  final DateTime? linkExpiresAt;
}

class LinkResult {
  LinkResult({required this.url, this.code, this.expiresAt, this.whatsAppUrl});

  factory LinkResult.fromJson(Json j) =>
      LinkResult(url: j['url'] as String? ?? '', code: j['code'] as String?, expiresAt: utc(j['expiresAtUtc']), whatsAppUrl: j['whatsAppUrl'] as String?);

  final String url;
  final String? code;
  final DateTime? expiresAt;
  final String? whatsAppUrl;
}

class LoginHandOver {
  LoginHandOver({required this.login, required this.password, this.message, this.whatsAppUrl});

  factory LoginHandOver.fromJson(Json j) => LoginHandOver(
        login: OwnerLogin.fromJson((j['login'] as Map).cast<String, dynamic>()),
        password: j['password'] as String? ?? '',
        message: j['message'] as String?,
        whatsAppUrl: j['whatsAppUrl'] as String?,
      );

  final OwnerLogin login;
  final String password;
  final String? message;
  final String? whatsAppUrl;
}

// ---- finance ----

class FinanceOptions {
  FinanceOptions({required this.currency, required this.methods, required this.purposes, required this.expenseCategories, required this.maxAmount, required this.today});

  factory FinanceOptions.fromJson(Json j) => FinanceOptions(
        currency: j['currency'] as String? ?? r'$',
        methods: stringList(j['methods']),
        purposes: stringList(j['purposes']),
        expenseCategories: stringList(j['expenseCategories']),
        maxAmount: money(j['maxAmount']),
        today: day(j['today']) ?? DateTime.now(),
      );

  final String currency;
  final List<String> methods;
  final List<String> purposes;
  final List<String> expenseCategories;
  final double maxAmount;
  final DateTime today;
}

class Payment {
  Payment({
    required this.id,
    this.clientId,
    required this.payer,
    required this.amount,
    required this.date,
    required this.method,
    required this.purpose,
    required this.purposes,
    this.reference,
    this.note,
    this.createdBy,
  });

  factory Payment.fromJson(Json j) {
    final purpose = j['purpose'] as String? ?? '';
    final purposes = stringList(j['purposes']);
    return Payment(
      id: j['id'] as int,
      clientId: j['clientId'] as int?,
      payer: j['payer'] as String? ?? '',
      amount: money(j['amount']),
      date: day(j['date']) ?? DateTime.now(),
      method: j['method'] as String? ?? '',
      purpose: purpose,
      // Older clouds send only "purpose"; several are joined with " + ".
      purposes: purposes.isNotEmpty ? purposes : purpose.split(' + ').where((p) => p.trim().isNotEmpty).toList(),
      reference: j['reference'] as String?,
      note: j['note'] as String?,
      createdBy: j['createdBy'] as String?,
    );
  }

  final int id;
  final int? clientId;
  final String payer;
  final double amount;
  final DateTime date;
  final String method;
  final String purpose;
  final List<String> purposes;
  final String? reference;
  final String? note;
  final String? createdBy;
}

class Expense {
  Expense({required this.id, required this.amount, required this.date, required this.category, this.payee, this.note});

  factory Expense.fromJson(Json j) => Expense(
        id: j['id'] as int,
        amount: money(j['amount']),
        date: day(j['date']) ?? DateTime.now(),
        category: j['category'] as String? ?? '',
        payee: j['payee'] as String?,
        note: j['note'] as String?,
      );

  final int id;
  final double amount;
  final DateTime date;
  final String category;
  final String? payee;
  final String? note;
}

class Budget {
  Budget({required this.month, this.incomeTarget, this.spendingLimit});

  factory Budget.fromJson(Json j) =>
      Budget(month: j['month'] as String? ?? '', incomeTarget: moneyOrNull(j['incomeTarget']), spendingLimit: moneyOrNull(j['spendingLimit']));

  /// yyyy-MM: from this month on, until a later budget replaces it.
  final String month;
  final double? incomeTarget;
  final double? spendingLimit;
}

class BudgetUse {
  BudgetUse({required this.planned, required this.actual, required this.left, required this.percent, required this.over});

  factory BudgetUse.fromJson(Json j) => BudgetUse(
        planned: money(j['planned']),
        actual: money(j['actual']),
        left: money(j['left']),
        percent: (j['percent'] as num?)?.toInt() ?? 0,
        over: j['over'] == true,
      );

  final double planned;
  final double actual;
  final double left;
  final int percent;
  final bool over;
}

class TrendMonth {
  TrendMonth(this.month, this.collected, this.spent, this.profit);
  factory TrendMonth.fromJson(Json j) => TrendMonth(j['month'] as String? ?? '', money(j['collected']), money(j['spent']), money(j['profit']));
  final String month;
  final double collected;
  final double spent;
  final double profit;
}

class ShareRow {
  ShareRow(this.name, this.amount, this.count, this.clientId);
  factory ShareRow.fromJson(Json j) => ShareRow(j['name'] as String? ?? '', money(j['amount']), j['count'] as int? ?? 0, j['clientId'] as int?);
  final String name;
  final double amount;
  final int count;
  final int? clientId;
}

class Renewal {
  Renewal({required this.clientId, required this.name, required this.what, this.endsAt, this.lastPaid, this.licenseId});

  factory Renewal.fromJson(Json j) => Renewal(
        clientId: j['clientId'] as int,
        name: j['name'] as String? ?? '',
        what: j['what'] as String? ?? 'Online ends',
        endsAt: utc(j['endsAtUtc']),
        lastPaid: moneyOrNull(j['lastPaid']),
        licenseId: j['licenseId'] as int?,
      );

  final int clientId;
  final String name;
  final String what;
  final DateTime? endsAt;
  final double? lastPaid;
  final int? licenseId;
}

class FinanceLine {
  FinanceLine({required this.isPayment, required this.id, required this.date, required this.who, this.clientId, required this.details, this.note, required this.amount});

  factory FinanceLine.fromJson(Json j) => FinanceLine(
        isPayment: j['type'] == 'payment',
        id: j['id'] as int,
        date: day(j['date']) ?? DateTime.now(),
        who: j['who'] as String? ?? '',
        clientId: j['clientId'] as int?,
        details: j['details'] as String? ?? '',
        note: j['note'] as String?,
        amount: money(j['amount']),
      );

  final bool isPayment;
  final int id;
  final DateTime date;
  final String who;
  final int? clientId;
  final String details;
  final String? note;
  final double amount;
}

class FinanceSummary {
  FinanceSummary({
    required this.label,
    required this.query,
    this.previous,
    this.next,
    required this.isYear,
    required this.currency,
    required this.collected,
    required this.spent,
    required this.profit,
    this.marginPercent,
    required this.balance,
    this.collectedChange,
    this.spentChange,
    required this.payingClients,
    this.monthBudget,
    this.income,
    this.spending,
    required this.trend,
    required this.byPayer,
    required this.byPurpose,
    required this.byCategory,
    required this.renewals,
    required this.renewalsExpected,
    required this.attention,
    required this.lines,
  });

  factory FinanceSummary.fromJson(Json j) {
    final period = (j['period'] as Map?)?.cast<String, dynamic>() ?? const {};
    final change = (j['change'] as Map?)?.cast<String, dynamic>() ?? const {};
    final budget = (j['budget'] as Map?)?.cast<String, dynamic>() ?? const {};
    BudgetUse? use(Object? v) => v is Map ? BudgetUse.fromJson(v.cast<String, dynamic>()) : null;
    return FinanceSummary(
      label: period['label'] as String? ?? '',
      query: period['query'] as String? ?? '',
      previous: period['previous'] as String?,
      next: period['next'] as String?,
      isYear: period['kind'] == 'year',
      currency: j['currency'] as String? ?? r'$',
      collected: money(j['collected']),
      spent: money(j['spent']),
      profit: money(j['profit']),
      marginPercent: (j['marginPercent'] as num?)?.toInt(),
      balance: money(j['balance']),
      collectedChange: change['collected'] as String?,
      spentChange: change['spent'] as String?,
      payingClients: j['payingClients'] as int? ?? 0,
      monthBudget: budget['monthBudget'] is Map ? Budget.fromJson((budget['monthBudget'] as Map).cast<String, dynamic>()) : null,
      income: use(budget['income']),
      spending: use(budget['spending']),
      trend: jsonList(j['trend']).map(TrendMonth.fromJson).toList(),
      byPayer: jsonList(j['byPayer']).map(ShareRow.fromJson).toList(),
      byPurpose: jsonList(j['byPurpose']).map(ShareRow.fromJson).toList(),
      byCategory: jsonList(j['byCategory']).map(ShareRow.fromJson).toList(),
      renewals: jsonList(j['renewals']).map(Renewal.fromJson).toList(),
      renewalsExpected: money(j['renewalsExpected']),
      attention: jsonList(j['attention']).map(Attention.fromJson).toList(),
      lines: jsonList(j['lines']).map(FinanceLine.fromJson).toList(),
    );
  }

  final String label;

  /// `m=2026-11` or `y=2026`: what to ask for to get this period again.
  final String query;
  final String? previous;
  final String? next;
  final bool isYear;
  final String currency;
  final double collected;
  final double spent;
  final double profit;
  final int? marginPercent;
  final double balance;
  final String? collectedChange;
  final String? spentChange;
  final int payingClients;
  final Budget? monthBudget;
  final BudgetUse? income;
  final BudgetUse? spending;
  final List<TrendMonth> trend;
  final List<ShareRow> byPayer;
  final List<ShareRow> byPurpose;
  final List<ShareRow> byCategory;
  final List<Renewal> renewals;
  final double renewalsExpected;
  final List<Attention> attention;
  final List<FinanceLine> lines;
}

// ---- licences ----

class Licence {
  Licence({
    required this.id,
    this.clientId,
    this.clientName,
    this.siteId,
    required this.machineId,
    required this.customer,
    required this.kind,
    required this.plan,
    required this.seats,
    this.issuedOn,
    this.validUntil,
    required this.timeLimited,
    required this.status,
    required this.statusLabel,
    required this.serial,
    this.key,
    required this.source,
    this.paymentId,
    this.note,
    this.createdBy,
  });

  factory Licence.fromJson(Json j) => Licence(
        id: j['id'] as int,
        clientId: j['clientId'] as int?,
        clientName: j['clientName'] as String?,
        siteId: j['siteId'] as String?,
        machineId: j['machineId'] as String? ?? '',
        customer: j['customer'] as String? ?? '',
        kind: j['kind'] as String? ?? '',
        plan: j['plan'] as String? ?? '',
        seats: j['seats'] as int? ?? 1,
        issuedOn: day(j['issuedOn']),
        validUntil: day(j['validUntil']),
        timeLimited: j['timeLimited'] == true,
        status: j['status'] as String? ?? '',
        statusLabel: j['statusLabel'] as String? ?? '',
        serial: j['serial'] as String? ?? '',
        key: j['key'] as String?,
        source: j['source'] as String? ?? '',
        paymentId: j['paymentId'] as int?,
        note: j['note'] as String?,
        createdBy: j['createdBy'] as String?,
      );

  final int id;
  final int? clientId;
  final String? clientName;
  final String? siteId;
  final String machineId;
  final String customer;

  /// `Full`, `Provisional` or `Trial`.
  final String kind;

  /// `Basic` or `Pro`.
  final String plan;
  final int seats;
  final DateTime? issuedOn;
  final DateTime? validUntil;
  final bool timeLimited;

  /// `Active`, `EndingSoon`, `Grace`, `Expired`, `Updates`, `UpdatesEnding`, `UpdatesEnded` or `Superseded`.
  final String status;
  final String statusLabel;
  final String serial;
  final String? key;
  final String source;
  final int? paymentId;
  final String? note;
  final String? createdBy;
}

class Signer {
  Signer({required this.state, required this.ready, required this.message});
  factory Signer.fromJson(Json j) => Signer(state: j['state'] as String? ?? 'Off', ready: j['ready'] == true, message: j['message'] as String? ?? '');
  final String state;
  final bool ready;
  final String message;
}

class IssuedLicence {
  IssuedLicence(this.licence, this.whatsAppUrl);
  factory IssuedLicence.fromJson(Json j) => IssuedLicence(Licence.fromJson((j['license'] as Map).cast<String, dynamic>()), j['whatsAppUrl'] as String?);
  final Licence licence;
  final String? whatsAppUrl;
}
