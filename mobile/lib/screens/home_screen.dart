import 'dart:async';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../data/reports.dart';
import '../data/shop_data.dart';
import '../l10n.dart';
import '../state/app_lock.dart';
import '../state/home_tabs.dart';
import '../state/permissions.dart';
import '../state/notify_settings.dart';
import '../state/session.dart';
import '../theme.dart';
import '../widgets/motion.dart';
import 'changes_screen.dart';
import 'data_tabs.dart';
import 'drawer_screen.dart';
import 'forms/customer_form.dart';
import 'forms/product_form.dart';
import 'forms/supplier_form.dart';
import 'today_screen.dart';
import 'unlock_screen.dart';

/// The signed-in app: Today / Sales / Stock / Debts read the shop's (decrypted) copy; Manage adds things and follows them.
class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> with WidgetsBindingObserver {
  late final ChangesController _changes = context.read<ChangesController>();
  late final ShopData _data = context.read<ShopData>();
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _changes.start();
    WidgetsBinding.instance.addPostFrameCallback((_) => _data.refresh()); // not during build: refreshing notifies listeners
    // Quietly keep the figures current while the app is open; each refresh only fetches what changed.
    _timer = Timer.periodic(const Duration(seconds: 90), (_) => _data.refresh());
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    // Coming back to the app: show where things stand now, not where they stood when it was put away.
    if (state == AppLifecycleState.resumed) {
      _data.refresh();
      _changes.refresh();
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _timer?.cancel();
    _changes.stop();
    _changes.reset(); // the next person to sign in must not see this owner's list
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final tabs = context.watch<HomeTabs>();
    final changes = context.watch<ChangesController>();
    final badge = changes.pending + changes.needFixing;
    final shop = context.watch<ShopData>();
    final session = context.watch<Session>();
    final canStock = session.can(Perm.stock);
    final restock = canStock && shop.hasKey && shop.hasData ? stockItems(shop).where((s) => s.level != StockLevel.ok).length : 0;

    NavigationDestination dest(IconData icon, IconData selected, String label, {int count = 0}) => NavigationDestination(
          icon: Badge(isLabelVisible: count > 0, label: Text('$count'), child: Icon(icon)),
          selectedIcon: Badge(isLabelVisible: count > 0, label: Text('$count'), child: Icon(selected, color: Tokens.primary)),
          label: label,
        );

    // Only the tabs this login may use. Each keeps its fixed id (HomeTabs.sales ...) so "7 items are low" can still jump to Stock.
    final entries = <({int id, Widget page, NavigationDestination destination})>[
      (id: HomeTabs.today, page: const TodayScreen(), destination: dest(Icons.home_outlined, Icons.home, context.tr('Today', 'اليوم'))),
      if (session.can(Perm.sales))
        (id: HomeTabs.sales, page: const SalesScreen(), destination: dest(Icons.receipt_long_outlined, Icons.receipt_long, context.tr('Sales', 'المبيعات'))),
      if (canStock)
        (id: HomeTabs.stock, page: const StockScreen(), destination: dest(Icons.inventory_2_outlined, Icons.inventory_2, context.tr('Stock', 'المخزون'), count: restock)),
      if (session.can(Perm.debts))
        (id: HomeTabs.debts, page: const DebtsScreen(), destination: dest(Icons.payments_outlined, Icons.payments, context.tr('Debts', 'الديون'))),
      (id: HomeTabs.manage, page: const _ManageTab(), destination: dest(Icons.add_circle_outline, Icons.add_circle, context.tr('Manage', 'إدارة'), count: badge)),
    ];
    final selected = entries.indexWhere((e) => e.id == tabs.index).clamp(0, entries.length - 1);

    return Scaffold(
      body: SafeArea(child: TabStage(index: selected, children: [for (final e in entries) e.page])),
      bottomNavigationBar: NavigationBar(
        selectedIndex: selected,
        onDestinationSelected: (i) {
          if (i != selected) Haptics.tap();
          tabs.go(entries[i].id);
        },
        backgroundColor: Colors.white,
        indicatorColor: const Color(0xFFE1F0F1),
        destinations: [for (final e in entries) e.destination],
      ),
    );
  }
}

/// Changes as its own page, reached from Manage.
class ChangesPage extends StatelessWidget {
  const ChangesPage({super.key});

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: Text(context.tr('Changes', 'التغييرات'), style: const TextStyle(fontWeight: FontWeight.w700))),
        body: const ChangesScreen(),
      );
}

class _ManageTab extends StatelessWidget {
  const _ManageTab();

  @override
  Widget build(BuildContext context) {
    final session = context.watch<Session>();
    final shop = context.watch<ShopData>();
    final changes = context.watch<ChangesController>();
    final canAdd = session.can(Perm.add);
    final entries = <({IconData icon, String title, String subtitle, Widget Function() page})>[
      // The product form offers the shop's categories and suppliers, so it needs to be allowed to read both.
      if (canAdd && session.can(Perm.stock) && session.can(Perm.debts))
      (
        icon: Icons.inventory_2_outlined,
        title: context.tr('Add a product', 'إضافة منتج'),
        subtitle: context.tr('Name, barcode, prices and stock', 'الاسم والباركود والأسعار والمخزون'),
        page: () => const ProductFormScreen(),
      ),
      if (canAdd)
      (
        icon: Icons.person_add_alt_outlined,
        title: context.tr('Add a customer', 'إضافة زبون'),
        subtitle: context.tr('Name, phone and address', 'الاسم والهاتف والعنوان'),
        page: () => const CustomerFormScreen(),
      ),
      if (canAdd)
      (
        icon: Icons.local_shipping_outlined,
        title: context.tr('Add a supplier', 'إضافة مورّد'),
        subtitle: context.tr('Company, phone and currency', 'الشركة والهاتف والعملة'),
        page: () => const SupplierFormScreen(),
      ),
      if (session.can(Perm.financials))
      (
        icon: Icons.point_of_sale_outlined,
        title: context.tr('Cash drawer', 'صندوق النقد'),
        subtitle: context.tr('What it should hold now and how past shifts ended', 'ما يجب أن يحتويه الآن وكيف انتهت الورديات السابقة'),
        page: () => const DrawerPage(),
      ),
    ];

    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Row(
          children: [
            Container(
              width: 40,
              height: 40,
              decoration: BoxDecoration(color: Tokens.primary, borderRadius: BorderRadius.circular(10)),
              alignment: Alignment.center,
              child: const Text('N', style: TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.w700)),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(session.clientName, style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
                  Text('${session.displayName} · ${_roleName(context, session.role)}', style: const TextStyle(fontSize: 12, color: Tokens.muted)),
                ],
              ),
            ),
            IconButton(
              tooltip: context.tr('Language', 'اللغة'),
              onPressed: () => session.setLanguage(context.isArabic ? 'en' : 'ar'),
              icon: const Icon(Icons.translate),
            ),
            IconButton(
              tooltip: context.tr('Sign out', 'تسجيل الخروج'),
              onPressed: () => _confirmSignOut(context, session),
              icon: const Icon(Icons.logout),
            ),
          ],
        ),
        const SizedBox(height: 20),
        for (final (i, e) in entries.indexed) ...[
          FadeUp(
            index: i + 1,
            child: PressScale(
              scale: 0.985,
              child: Card(
            child: ListTile(
              contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
              leading: CircleAvatar(
                backgroundColor: const Color(0xFFE1F0F1),
                foregroundColor: Tokens.primary,
                child: Icon(e.icon),
              ),
              title: Text(e.title, style: const TextStyle(fontWeight: FontWeight.w700)),
              subtitle: Text(e.subtitle),
              trailing: Icon(context.isArabic ? Icons.chevron_left : Icons.chevron_right),
              onTap: () {
                Haptics.tap();
                Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => e.page()));
              },
            ),
          ),
            ),
          ),
          const SizedBox(height: 10),
        ],
        Card(
          child: ListTile(
            contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
            leading: CircleAvatar(
              backgroundColor: const Color(0xFFE1F0F1),
              foregroundColor: Tokens.primary,
              child: Badge(
                isLabelVisible: changes.pending + changes.needFixing > 0,
                label: Text('${changes.pending + changes.needFixing}'),
                child: const Icon(Icons.sync),
              ),
            ),
            title: Text(context.tr('Changes', 'التغييرات'), style: const TextStyle(fontWeight: FontWeight.w700)),
            subtitle: Text(context.tr('What you sent to the shop and what became of it', 'ما أرسلته إلى المحل وما حدث له')),
            trailing: Icon(context.isArabic ? Icons.chevron_left : Icons.chevron_right),
            onTap: () {
              changes.refresh(); // opening the list should show where things stand now, not 6 seconds ago
              Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => const ChangesPage()));
            },
          ),
        ),
        const SizedBox(height: 10),
        Card(
          child: ListTile(
            contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
            leading: CircleAvatar(
              backgroundColor: shop.hasKey ? Tokens.successBg : Tokens.warningBg,
              foregroundColor: shop.hasKey ? Tokens.success : Tokens.warning,
              child: Icon(shop.hasKey ? Icons.lock_open : Icons.lock_outline),
            ),
            title: Text(shop.hasKey ? context.tr('Shop data unlocked', 'بيانات المحل مفتوحة') : context.tr('Shop data locked', 'بيانات المحل مقفلة'),
                style: const TextStyle(fontWeight: FontWeight.w700)),
            subtitle: Text(shop.hasKey
                ? context.tr('Tap to lock it on this phone', 'اضغط لقفلها على هذا الهاتف')
                : context.tr('Enter the recovery code to see sales, stock and debts', 'أدخل رمز الاسترداد لترى المبيعات والمخزون والديون')),
            onTap: () async {
              if (!shop.hasKey) {
                await Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => const UnlockScreen()));
                return;
              }
              final yes = await showDialog<bool>(
                context: context,
                builder: (ctx) => AlertDialog(
                  title: Text(ctx.tr('Lock shop data?', 'قفل بيانات المحل؟')),
                  content: Text(ctx.tr('The phone forgets the recovery code. You will need to enter it again to see sales, stock and debts.',
                      'سينسى الهاتف رمز الاسترداد. ستحتاج لإدخاله مجددًا لرؤية المبيعات والمخزون والديون.')),
                  actions: [
                    TextButton(onPressed: () => Navigator.pop(ctx, false), child: Text(ctx.tr('Cancel', 'إلغاء'))),
                    TextButton(onPressed: () => Navigator.pop(ctx, true), child: Text(ctx.tr('Lock', 'قفل'))),
                  ],
                ),
              );
              if (yes == true) await shop.clear();
            },
          ),
        ),
        const SizedBox(height: 10),
        const _AppLockCard(),
        const SizedBox(height: 10),
        const _NotificationsCard(),
        const SizedBox(height: 10),
        Text(
          context.tr(
            'Changes are sent to the shop PC and applied there. You can follow them under Changes.',
            'تُرسل التغييرات إلى جهاز المحل وتُطبَّق هناك. يمكنك متابعتها من تبويب التغييرات.',
          ),
          style: const TextStyle(fontSize: 12, color: Tokens.muted, height: 1.5),
        ),
      ],
    );
  }

  Future<void> _confirmSignOut(BuildContext context, Session session) async {
    final yes = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text(ctx.tr('Sign out?', 'تسجيل الخروج؟')),
        content: Text(ctx.tr('Changes already sent to the shop are not affected.', 'التغييرات المرسلة إلى المحل لن تتأثر.')),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: Text(ctx.tr('Cancel', 'إلغاء'))),
          TextButton(onPressed: () => Navigator.pop(ctx, true), child: Text(ctx.tr('Sign out', 'تسجيل الخروج'))),
        ],
      ),
    );
    if (yes == true) await session.signOut();
  }
}

/// Switch for the fingerprint / face / PIN lock.
class _AppLockCard extends StatelessWidget {
  const _AppLockCard();

  @override
  Widget build(BuildContext context) {
    final lock = context.watch<AppLock>();
    return Card(
      child: SwitchListTile(
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
        secondary: CircleAvatar(
          backgroundColor: lock.enabled ? Tokens.successBg : const Color(0xFFE1F0F1),
          foregroundColor: lock.enabled ? Tokens.success : Tokens.primary,
          child: const Icon(Icons.fingerprint),
        ),
        title: Text(context.tr('Lock the app', 'قفل التطبيق'), style: const TextStyle(fontWeight: FontWeight.w700)),
        subtitle: Text(context.tr(
          'Ask for your fingerprint, face or phone PIN when the app opens or after you have been away. It is also hidden in the recent-apps view.',
          'اطلب بصمتك أو وجهك أو رمز الهاتف عند فتح التطبيق أو بعد غيابك. ويُخفى أيضًا في عرض التطبيقات الأخيرة.',
        )),
        value: lock.enabled,
        onChanged: (on) async {
          final messenger = ScaffoldMessenger.of(context);
          final unsupported = context.tr('Set a screen lock (PIN, pattern or fingerprint) on this phone first.', 'فعّل قفل الشاشة (رمز أو نمط أو بصمة) على هذا الهاتف أولًا.');
          final result = await lock.setEnabled(on, reason: context.tr('Confirm it is you', 'أكّد أنك أنت'));
          if (result == SetLock.unsupported) messenger.showSnackBar(SnackBar(content: Text(unsupported)));
          if (result == SetLock.done) Haptics.success();
        },
      ),
    );
  }
}

/// Switches for alerts while the app is closed.
class _NotificationsCard extends StatelessWidget {
  const _NotificationsCard();

  @override
  Widget build(BuildContext context) {
    final notify = context.watch<NotifySettings>();
    return Card(
      child: Column(
        children: [
          SwitchListTile(
            contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
            secondary: CircleAvatar(
              backgroundColor: notify.on ? Tokens.successBg : const Color(0xFFE1F0F1),
              foregroundColor: notify.on ? Tokens.success : Tokens.primary,
              child: Icon(notify.on ? Icons.notifications_active_outlined : Icons.notifications_none),
            ),
            title: Text(context.tr('Alerts', 'التنبيهات'), style: const TextStyle(fontWeight: FontWeight.w700)),
            subtitle: Text(context.tr(
              'Tells you about new sales, products running low, and answers to changes you sent, even when the app is closed. The phone checks about every 15 minutes.',
              'تُعلمك بالمبيعات الجديدة والمنتجات المنخفضة وردود التغييرات التي أرسلتها حتى والتطبيق مغلق. يفحص الهاتف كل 15 دقيقة تقريبًا.',
            )),
            value: notify.on,
            onChanged: (on) async {
              final messenger = ScaffoldMessenger.of(context);
              final refused = context.tr('Notifications are blocked for this app. Allow them in the phone\'s settings.', 'التنبيهات محظورة لهذا التطبيق. اسمح بها من إعدادات الهاتف.');
              if (on && !context.read<ShopData>().hasKey) {
                messenger.showSnackBar(SnackBar(content: Text(context.tr('Unlock the shop\'s data first: alerts are worked out on this phone.', 'افتح بيانات المحل أولًا: تُحسب التنبيهات على هذا الهاتف.'))));
                return;
              }
              if (!await notify.setOn(on)) messenger.showSnackBar(SnackBar(content: Text(refused)));
            },
          ),
          if (notify.on)
            SwitchListTile(
              contentPadding: const EdgeInsetsDirectional.only(start: 72, end: 16),
              title: Text(context.tr('Show names and amounts', 'إظهار الأسماء والمبالغ')),
              subtitle: Text(context.tr('Off: alerts only say how many. Anyone who sees your lock screen could read more.', 'مطفأ: تذكر التنبيهات العدد فقط. من يرى شاشة القفل قد يقرأ أكثر.')),
              value: notify.details,
              onChanged: notify.setDetails,
            ),
        ],
      ),
    );
  }
}

String _roleName(BuildContext context, String role) => switch (role) {
      'manager' => context.tr('Manager', 'مدير'),
      'staff' => context.tr('Staff', 'موظف'),
      'custom' => context.tr('Custom access', 'صلاحيات مخصصة'),
      _ => context.tr('Owner', 'مالك'),
    };
