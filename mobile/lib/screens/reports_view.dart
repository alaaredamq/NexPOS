import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../data/period_report.dart';
import '../data/shop_data.dart';
import '../l10n.dart';
import '../state/permissions.dart';
import '../state/session.dart';
import '../theme.dart';
import '../widgets/common.dart';
import '../widgets/detail.dart';
import '../widgets/motion.dart';
import 'open.dart';

/// The last 7 or 30 days: what came in, which days and hours were busy, what sold best, how it was paid, roughly what was earned.
/// Worked out on the phone from the shop's decrypted receipts and items.
class ReportsView extends StatelessWidget {
  const ReportsView({super.key, required this.days, required this.onDays});

  final int days;
  final ValueChanged<int> onDays;

  @override
  Widget build(BuildContext context) {
    final r = periodReport(context.watch<ShopData>(), DateTime.now(), days);
    final locale = Localizations.localeOf(context).languageCode;
    final n = r.notation;
    final change = r.changePercent;
    final dayLabels = [
      for (var i = 0; i < r.perDay.length; i++)
        days <= 7
            ? DateFormat('E', locale).format(r.perDay[i].day)
            : (i % 7 == 0 || i == r.perDay.length - 1 ? DateFormat('d/M', locale).format(r.perDay[i].day) : ''),
    ];
    final best = r.bestDay;

    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      Wrap(spacing: 8, children: [
        for (final d in const [7, 30])
          ChoiceChip(
            label: Text(context.tr('Last $d days', 'آخر $d يومًا')),
            selected: days == d,
            onSelected: (_) {
              Haptics.tap();
              onDays(d);
            },
          ),
      ]),
      const SizedBox(height: 12),
      FadeUp(
        child: Container(
          width: double.infinity,
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(color: Tokens.nav, borderRadius: BorderRadius.circular(14)),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(context.tr('Sales, last $days days', 'المبيعات، آخر $days يومًا'), style: const TextStyle(fontSize: 13, color: Color(0xFFA9BABC))),
            FittedBox(
              fit: BoxFit.scaleDown,
              alignment: AlignmentDirectional.centerStart,
              child: CountUp(
                value: r.total,
                format: (v) => money(v, n),
                style: const TextStyle(fontSize: 36, fontWeight: FontWeight.w700, color: Tokens.readout, height: 1.2),
              ),
            ),
            const SizedBox(height: 4),
            Wrap(spacing: 14, runSpacing: 2, children: [
              Text(context.tr('${r.orders} sale(s) · ${money(r.average, n)} avg', '${r.orders} عملية · متوسط ${money(r.average, n)}'),
                  style: const TextStyle(fontSize: 13, color: Color(0xFFA9BABC))),
              if (change != null)
                Text(
                  context.tr('${change.abs().round()}% ${change >= 0 ? 'up' : 'down'} on the $days days before', '${change.abs().round()}% ${change >= 0 ? 'أعلى' : 'أقل'} من $days يومًا قبلها'),
                  style: TextStyle(fontSize: 13, fontWeight: FontWeight.w600, color: change >= 0 ? const Color(0xFF74E8AB) : const Color(0xFFFF9AA5)),
                ),
            ]),
          ]),
        ),
      ),
      if (r.otherCurrencySales > 0) ...[
        const SizedBox(height: 8),
        Notice(
          icon: Icons.currency_exchange,
          text: context.tr('${r.otherCurrencySales} sale(s) in another currency are not counted here; they are listed under Sales.',
              '${r.otherCurrencySales} عملية بعملة أخرى غير محسوبة هنا؛ تجدها في تبويب المبيعات.'),
        ),
      ],
      if (r.orders == 0)
        Padding(
          padding: const EdgeInsets.symmetric(vertical: 36),
          child: Center(child: Text(context.tr('No sales in this period.', 'لا توجد مبيعات في هذه الفترة.'), style: const TextStyle(color: Tokens.muted))),
        )
      else ...[
        SectionTitle(context.tr('Day by day', 'يومًا بيوم')),
        FadeUp(
          index: 1,
          child: Container(
            padding: const EdgeInsets.fromLTRB(14, 16, 14, 12),
            decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12), border: Border.all(color: Tokens.border)),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              BarChart(
                key: ValueKey(days), // switching 7 / 30 draws the bars afresh
                values: [for (final d in r.perDay) d.total],
                labels: dayLabels,
                highlight: r.perDay.length - 1,
                semanticsLabel: context.tr('Sales per day for the last $days days', 'المبيعات اليومية لآخر $days يومًا'),
              ),
              if (best != null) ...[
                const SizedBox(height: 10),
                Text(
                  context.tr('Best day: ${DateFormat('EEEE d MMM', locale).format(best.day)}, ${money(best.total, n)}',
                      'أفضل يوم: ${DateFormat('EEEE d MMM', locale).format(best.day)}، ${money(best.total, n)}'),
                  style: const TextStyle(fontSize: 13, color: Tokens.muted),
                ),
              ],
            ]),
          ),
        ),
        if (r.best.isNotEmpty) ...[
          SectionTitle(context.tr('Best sellers', 'الأكثر مبيعًا')),
          RowGroup(children: [
            for (final (i, p) in r.best.indexed)
              _TopRow(
                rank: i + 1,
                name: p.name,
                detail: context.tr('${qty(p.quantity)} sold', 'بيع ${qty(p.quantity)}'),
                amount: money(p.revenue, n),
                onTap: p.productId == null || !context.read<Session>().can(Perm.stock) || context.read<ShopData>().row('Products', p.productId) == null ? null : () => openProduct(context, p.productId!),
              ),
          ]),
        ],
        if (r.estimatedProfit != null && context.read<Session>().can(Perm.financials)) ...[
          SectionTitle(context.tr('Estimated profit', 'الربح التقديري')),
          StatCard(
            label: context.tr('Sales less cost', 'المبيعات ناقص التكلفة'),
            value: money(r.estimatedProfit!, n),
            valueColor: r.estimatedProfit! >= 0 ? Tokens.success : Tokens.danger,
            sub: context.tr('An estimate: it uses each product\'s current cost price.', 'تقدير: يعتمد على سعر تكلفة كل منتج الحالي.'),
          ),
        ],
        if (r.byPayment.isNotEmpty) ...[
          SectionTitle(context.tr('How it was paid', 'طريقة الدفع')),
          RowGroup(children: [
            for (final p in r.byPayment)
              Fact(p.notation, '${money(p.amount, n)}  ·  ${(r.total <= 0 ? 0 : p.amount / r.total * 100).round()}%'),
          ]),
        ],
        if (r.busiestHours.isNotEmpty) ...[
          SectionTitle(context.tr('Busiest hours', 'أكثر الساعات ازدحامًا')),
          Container(
            padding: const EdgeInsets.fromLTRB(14, 16, 14, 12),
            decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12), border: Border.all(color: Tokens.border)),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(
                r.busiestHours.map((h) => '${h.toString().padLeft(2, '0')}:00').join('  ·  '),
                textDirection: TextDirection.ltr,
                style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700),
              ),
              const SizedBox(height: 12),
              BarChart(
                key: ValueKey('h$days'),
                values: r.hourTotals,
                labels: [for (var h = 0; h < 24; h++) h % 6 == 0 ? '$h' : ''],
                height: 56,
                color: Tokens.primary,
                semanticsLabel: context.tr('Money taken in each hour of the day', 'المبالغ المحصّلة في كل ساعة من اليوم'),
              ),
            ]),
          ),
        ],
      ],
      const SizedBox(height: 14),
      const FreshnessNotice(),
    ]);
  }
}

class _TopRow extends StatelessWidget {
  const _TopRow({required this.rank, required this.name, required this.detail, required this.amount, this.onTap});
  final int rank;
  final String name;
  final String detail;
  final String amount;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final body = Padding(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 11),
      child: Row(children: [
        CircleAvatar(radius: 13, backgroundColor: rank == 1 ? Tokens.primary : const Color(0xFFE1F0F1), foregroundColor: rank == 1 ? Colors.white : Tokens.primary, child: Text('$rank', style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w700))),
        const SizedBox(width: 12),
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600)),
            Text(detail, style: const TextStyle(fontSize: 13, color: Tokens.muted)),
          ]),
        ),
        const SizedBox(width: 10),
        Text(amount, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700)),
      ]),
    );
    return onTap == null ? body : TapRow(onTap: onTap!, child: body);
  }
}
