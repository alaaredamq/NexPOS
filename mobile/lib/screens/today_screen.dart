import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../data/drawer.dart';
import '../data/reports.dart';
import '../data/shop_data.dart';
import '../l10n.dart';
import '../state/home_tabs.dart';
import '../state/permissions.dart';
import '../state/session.dart';
import '../theme.dart';
import '../widgets/common.dart';
import '../widgets/decor.dart';
import '../widgets/detail.dart';
import '../widgets/motion.dart';
import 'drawer_screen.dart';
import 'open.dart';

/// The day at a glance: what was sold, how it compares, and what needs the owner's attention.
class TodayScreen extends StatelessWidget {
  const TodayScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return DataGate(builder: (context) {
      final data = context.watch<ShopData>();
      final session = context.watch<Session>();
      final now = DateTime.now();
      final report = todayReport(data, now);
      final stock = stockItems(data);
      final needAttention = stock.where((s) => s.level != StockLevel.ok).length;
      final debts = debtsReport(data);
      final recent = recentSales(data, limit: 3);
      final drawer = session.can(Perm.financials) ? drawerReport(data, historyLimit: 1) : null;
      final tabs = context.read<HomeTabs>();
      final locale = Localizations.localeOf(context).languageCode;

      return [
        FadeUp(
          child: Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('${_greeting(context, now)} · ${DateFormat('EEEE, d MMMM', locale).format(now)}',
                        style: const TextStyle(fontSize: 13, color: Tokens.muted)),
                    Text(session.clientName, style: const TextStyle(fontSize: 22, fontWeight: FontWeight.w700)),
                  ],
                ),
              ),
              IconButton(
                tooltip: context.tr('Copy today\'s summary', 'نسخ ملخص اليوم'),
                icon: const Icon(Icons.ios_share_outlined),
                onPressed: () async {
                  final messenger = ScaffoldMessenger.of(context);
                  final copied = context.tr('Summary copied. Paste it into WhatsApp or a message.', 'نُسخ الملخص. الصقه في واتساب أو رسالة.');
                  await Clipboard.setData(ClipboardData(text: _summary(context, session, now, report, needAttention, debts)));
                  Haptics.success();
                  messenger.showSnackBar(SnackBar(content: Text(copied)));
                },
              ),
            ],
          ),
        ),
        const SizedBox(height: 14),
        FadeUp(index: 1, child: _Hero(report: report)),
        const SizedBox(height: 18),
        FadeUp(index: 2, child: Text(context.tr('Needs attention', 'يحتاج انتباهك'), style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700))),
        const SizedBox(height: 8),
        RowGroup(children: [
          if (session.can(Perm.stock))
          _AttentionRow(
            icon: Icons.inventory_2_outlined,
            tint: Tokens.warningBg,
            ink: Tokens.warning,
            title: context.tr('Low stock', 'مخزون منخفض'),
            subtitle: needAttention == 0
                ? context.tr('Everything is above its minimum', 'كل المنتجات فوق الحد الأدنى')
                : context.tr('$needAttention item(s) at or below their minimum', '$needAttention منتج عند الحد الأدنى أو أقل'),
            onTap: () => tabs.go(HomeTabs.stock),
          ),
          if (session.can(Perm.debts))
          _AttentionRow(
            icon: Icons.payments_outlined,
            tint: const Color(0xFFE3F6F7),
            ink: const Color(0xFF08636B),
            title: context.tr('Customer debts', 'ديون الزبائن'),
            subtitle: debts.owedToShop.isEmpty
                ? context.tr('Nobody owes the shop', 'لا أحد مدين للمحل')
                : context.tr(
                    '${debts.owedToShopTotals.map((m) => money(m.amount, m.notation)).join(' + ')} owed by ${debts.owedToShop.length} customer(s)',
                    '${debts.owedToShopTotals.map((m) => money(m.amount, m.notation)).join(' + ')} مستحقة على ${debts.owedToShop.length} زبون',
                  ),
            onTap: () => tabs.go(HomeTabs.debts),
          ),
          if (session.can(Perm.debts) && debts.shopOwes.isNotEmpty)
            _AttentionRow(
              icon: Icons.local_shipping_outlined,
              tint: const Color(0xFFEEF1F5),
              ink: const Color(0xFF454C5A),
              title: context.tr('Owed to suppliers', 'مستحق للموردين'),
              subtitle: context.tr(
                '${debts.shopOwesTotals.map((m) => money(m.amount, m.notation)).join(' + ')} to ${debts.shopOwes.length} supplier(s)',
                '${debts.shopOwesTotals.map((m) => money(m.amount, m.notation)).join(' + ')} لـ ${debts.shopOwes.length} مورّد',
              ),
              onTap: () => tabs.go(HomeTabs.debts),
            ),
          if (session.can(Perm.financials) && drawer != null)
            _AttentionRow(
              icon: Icons.point_of_sale_outlined,
              tint: const Color(0xFFE6F2E9),
              ink: Tokens.success,
              title: context.tr('Cash drawer', 'صندوق النقد'),
              subtitle: drawer.current != null
                  ? context.tr('Open · should hold ${money(drawer.current!.expected, drawer.notation)}', 'مفتوح · يجب أن يحتوي ${money(drawer.current!.expected, drawer.notation)}')
                  : drawer.history.isEmpty
                      ? context.tr('Closed', 'مغلق')
                      : context.tr('Closed · last shift ${_drawerOutcome(context, drawer.history.first, drawer.notation)}', 'مغلق · آخر وردية ${_drawerOutcome(context, drawer.history.first, drawer.notation)}'),
              onTap: () => Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => const DrawerPage())),
            ),
        ]),
        if (report.byPayment.isNotEmpty) ...[
          const SizedBox(height: 18),
          Text(context.tr('Today by payment', 'اليوم حسب الدفع'), style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700)),
          const SizedBox(height: 8),
          RowGroup(children: [
            for (final p in report.byPayment)
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 14),
                child: Row(children: [
                  Expanded(child: Text(p.notation, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600))),
                  Text(money(p.amount, report.main.notation), style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700)),
                ]),
              ),
          ]),
        ],
        const SizedBox(height: 18),
        Row(children: [
          Expanded(child: Text(context.tr('Recent sales', 'آخر المبيعات'), style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700))),
          TextButton(onPressed: () => tabs.go(HomeTabs.sales), child: Text(context.tr('See all', 'عرض الكل'))),
        ]),
        RowGroup(
          emptyText: context.tr('No sales yet.', 'لا توجد مبيعات بعد.'),
          children: [for (final s in recent) SaleTile(sale: s)],
        ),
      ];
    });
  }
}

String _greeting(BuildContext context, DateTime now) {
  if (now.hour < 12) return context.tr('Good morning', 'صباح الخير');
  if (now.hour < 18) return context.tr('Good afternoon', 'نهارك سعيد');
  return context.tr('Good evening', 'مساء الخير');
}

/// Plain text for the owner to paste into WhatsApp: the day in a few lines.
String _summary(BuildContext context, Session session, DateTime now, TodayReport report, int needAttention, DebtsReport debts) {
  final shop = session.clientName;
  final locale = Localizations.localeOf(context).languageCode;
  final change = report.versusLastWeekPercent;
  return [
    [if (shop.isNotEmpty) shop, DateFormat('EEEE, d MMMM', locale).format(now)].join(' · '),
    context.tr('Sales today: ${money(report.today, report.main.notation)} (${report.orders} sales)',
        'مبيعات اليوم: ${money(report.today, report.main.notation)} (${report.orders} عملية)'),
    for (final other in report.otherCurrencies) '+ ${money(other.amount, other.notation)}',
    if (change != null)
      context.tr('${change.abs().round()}% ${change >= 0 ? 'up' : 'down'} on the same day last week',
          '${change.abs().round()}% ${change >= 0 ? 'أعلى' : 'أقل'} من نفس اليوم في الأسبوع الماضي'),
    if (session.can(Perm.stock)) context.tr('Items to restock: $needAttention', 'منتجات تحتاج تعبئة: $needAttention'),
    if (session.can(Perm.debts) && debts.owedToShop.isNotEmpty)
      context.tr('Owed by customers: ${debts.owedToShopTotals.map((m) => money(m.amount, m.notation)).join(' + ')}',
          'مستحق على الزبائن: ${debts.owedToShopTotals.map((m) => money(m.amount, m.notation)).join(' + ')}'),
  ].join('\n');
}

class _Hero extends StatelessWidget {
  const _Hero({required this.report});
  final TodayReport report;

  @override
  Widget build(BuildContext context) {
    final change = report.versusLastWeekPercent;
    final maxHour = report.hourly.fold<double>(0, (a, b) => b > a ? b : a);
    final lastIndex = report.hourly.length - 1;
    return ClipRRect(
      borderRadius: BorderRadius.circular(16),
      child: DecoratedBox(
        decoration: const BoxDecoration(
          gradient: LinearGradient(begin: Alignment.topCenter, end: Alignment.bottomCenter, colors: [Color(0xFF16292C), Tokens.nav]),
        ),
        child: Stack(
          children: [
            Positioned.fill(child: IgnorePointer(child: CustomPaint(painter: OrbitPainter(alignEnd: !context.isArabic, strength: 0.1)))),
            Padding(padding: const EdgeInsets.fromLTRB(18, 18, 18, 14), child: _heroBody(context, change, maxHour, lastIndex)),
          ],
        ),
      ),
    );
  }

  Widget _heroBody(BuildContext context, double? change, double maxHour, int lastIndex) {
    return Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(context.tr('Today\'s sales', 'مبيعات اليوم'), style: const TextStyle(fontSize: 13, color: Color(0xFFA9BABC))),
          const SizedBox(height: 2),
          FittedBox(
            fit: BoxFit.scaleDown,
            alignment: AlignmentDirectional.centerStart,
            child: CountUp(
              value: report.today,
              format: (v) => money(v, report.main.notation),
              style: const TextStyle(fontSize: 42, fontWeight: FontWeight.w700, color: Tokens.readout, height: 1.15),
            ),
          ),
          for (final other in report.otherCurrencies)
            Text('+ ${money(other.amount, other.notation)}', style: const TextStyle(fontSize: 15, color: Tokens.readout, fontWeight: FontWeight.w600)),
          const SizedBox(height: 6),
          Wrap(
            alignment: WrapAlignment.spaceBetween,
            crossAxisAlignment: WrapCrossAlignment.center,
            spacing: 12,
            runSpacing: 4,
            children: [
              if (change != null)
                Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(change >= 0 ? Icons.trending_up : Icons.trending_down, size: 16, color: change >= 0 ? const Color(0xFF74E8AB) : const Color(0xFFFF9AA5)),
                    const SizedBox(width: 4),
                    Flexible(
                      child: Text(
                        context.tr('${change.abs().round()}% ${change >= 0 ? 'up' : 'down'} on last week', '${change.abs().round()}% ${change >= 0 ? 'أعلى' : 'أقل'} من الأسبوع الماضي'),
                        style: TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: change >= 0 ? const Color(0xFF74E8AB) : const Color(0xFFFF9AA5)),
                      ),
                    ),
                  ],
                )
              else
                const SizedBox.shrink(),
              Text(
                context.tr('${report.orders} sale(s) · ${money(report.average, report.main.notation)} avg', '${report.orders} عملية · متوسط ${money(report.average, report.main.notation)}'),
                style: const TextStyle(fontSize: 13, color: Color(0xFF8FA3A6)),
              ),
            ],
          ),
          if (report.hourly.length > 1 && maxHour > 0) ...[
            const SizedBox(height: 14),
            // The bars grow up from the baseline one after another, left to right.
            TweenAnimationBuilder<double>(
              tween: Tween<double>(begin: 0, end: 1),
              duration: Motion.scaled(context, const Duration(milliseconds: 900)),
              builder: (context, t, _) => SizedBox(
                height: 46,
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    for (var i = 0; i < report.hourly.length; i++) ...[
                      if (i > 0) const SizedBox(width: 4),
                      Expanded(
                        child: Container(
                          height: (report.hourly[i] / maxHour * 46).clamp(3.0, 46.0) *
                              Motion.enter.transform(((t - i * 0.03) / 0.55).clamp(0.0, 1.0)),
                          constraints: const BoxConstraints(minHeight: 2),
                          decoration: BoxDecoration(
                            color: i == lastIndex ? Tokens.readout : Tokens.readout.withValues(alpha: 0.3),
                            borderRadius: BorderRadius.circular(3),
                          ),
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ),
            const SizedBox(height: 5),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text('${report.firstHour}:00', style: const TextStyle(fontSize: 12, color: Color(0xFFA9BABC))),
                Text(context.tr('Now', 'الآن'), style: const TextStyle(fontSize: 12, color: Color(0xFFA9BABC))),
              ],
            ),
          ],
        ],
      );
  }
}

class _AttentionRow extends StatelessWidget {
  const _AttentionRow({required this.icon, required this.tint, required this.ink, required this.title, required this.subtitle, required this.onTap});

  final IconData icon;
  final Color tint;
  final Color ink;
  final String title;
  final String subtitle;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return PressScale(
      scale: 0.985,
      child: InkWell(
      onTap: () {
        Haptics.tap();
        onTap();
      },
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
        child: Row(children: [
          Container(
            width: 36,
            height: 36,
            decoration: BoxDecoration(color: tint, borderRadius: BorderRadius.circular(10)),
            child: Icon(icon, size: 20, color: ink),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(title, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600)),
              Text(subtitle, style: const TextStyle(fontSize: 13, color: Tokens.muted)),
            ]),
          ),
          Icon(context.isArabic ? Icons.chevron_left : Icons.chevron_right, color: Tokens.muted),
        ]),
      ),
      ),
    );
  }
}

/// One sale in a list: number, time, customer, payment and total. A voided sale is struck through and marked.
class SaleTile extends StatelessWidget {
  const SaleTile({super.key, required this.sale});
  final SaleLine sale;

  @override
  Widget build(BuildContext context) {
    final when = sale.when;
    final sameDay = when != null && DateUtils.isSameDay(when, DateTime.now());
    final timeText = when == null ? '' : (sameDay ? DateFormat.Hm().format(when) : DateFormat('d MMM · HH:mm', Localizations.localeOf(context).languageCode).format(when));
    final details = [
      timeText,
      if (sale.customer.isNotEmpty) sale.customer else context.tr('Walk-in', 'زبون عابر'),
      if (sale.payment.isNotEmpty) sale.payment,
    ].where((e) => e.isNotEmpty).join(' · ');
    return TapRow(
      onTap: () => openSale(context, sale.id),
      child: Padding(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
      child: Row(children: [
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              Flexible(
                child: Text('#${sale.number}',
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700, decoration: sale.isVoid ? TextDecoration.lineThrough : null)),
              ),
              if (sale.isVoid) ...[
                const SizedBox(width: 8),
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 1),
                  decoration: BoxDecoration(color: Tokens.dangerBg, borderRadius: BorderRadius.circular(4)),
                  child: Text(context.tr('Void', 'ملغاة'), style: const TextStyle(fontSize: 11, color: Tokens.danger, fontWeight: FontWeight.w700)),
                ),
              ],
            ]),
            Text(details, style: const TextStyle(fontSize: 13, color: Tokens.muted)),
          ]),
        ),
        // A huge amount shrinks instead of squeezing the receipt number and the "Void" mark off the row.
        ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 130),
          child: FittedBox(
            fit: BoxFit.scaleDown,
            alignment: AlignmentDirectional.centerEnd,
            child: Text(money(sale.total, sale.notation),
                style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, color: sale.isVoid ? Tokens.muted : Tokens.ink, decoration: sale.isVoid ? TextDecoration.lineThrough : null)),
          ),
        ),
      ]),
      ),
    );
  }
}

/// How the last shift ended, in a few words ("balanced", "short $2").
String _drawerOutcome(BuildContext context, DrawerSession s, String notation) {
  final d = s.difference;
  if (d == null) return context.tr('not counted', 'لم تُعدّ');
  if (d.abs() <= 0.009) return context.tr('balanced', 'متوازنة');
  return d < 0 ? context.tr('short ${money(-d, notation)}', 'عجز ${money(-d, notation)}') : context.tr('over ${money(d, notation)}', 'زيادة ${money(d, notation)}');
}
