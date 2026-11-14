import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../data/drawer.dart';
import '../data/shop_data.dart';
import '../l10n.dart';
import '../theme.dart';
import '../widgets/common.dart';
import '../widgets/detail.dart';
import '../widgets/motion.dart';

/// The shop's cash drawer: what it should hold right now, how each past shift ended, and the money that was taken out.
/// Worked out on the phone from the till's drawer sessions, orders and cash drops, the way the till works it out.
class DrawerPage extends StatelessWidget {
  const DrawerPage({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(context.tr('Cash drawer', 'صندوق النقد'), style: const TextStyle(fontWeight: FontWeight.w700))),
      body: DataGate(builder: (context) => _content(context, drawerReport(context.watch<ShopData>()))),
    );
  }

  List<Widget> _content(BuildContext context, DrawerReport r) {
    final n = r.notation;
    final locale = Localizations.localeOf(context).languageCode;
    String when(DateTime? d, {bool date = true}) => d == null ? '' : DateFormat(date ? 'd MMM · HH:mm' : 'HH:mm', locale).format(d);
    final current = r.current;

    return [
      if (current != null) ...[
        FadeUp(
          child: StatCard(
            dark: true,
            label: context.tr('Should be in the drawer now', 'يجب أن يكون في الصندوق الآن'),
            value: money(current.expected, n),
            sub: context.tr('Open since ${when(current.openedAt, date: false)}${current.cashier.isEmpty ? '' : ' · ${current.cashier}'}',
                'مفتوح منذ ${when(current.openedAt, date: false)}${current.cashier.isEmpty ? '' : ' · ${current.cashier}'}'),
          ),
        ),
        const SizedBox(height: 10),
        RowGroup(children: [
          Fact(context.tr('Opening cash', 'النقد الافتتاحي'), money(current.opening, n)),
          Fact(context.tr('Sales (${current.salesCount})', 'المبيعات (${current.salesCount})'), '+${money(current.sales, n)}'),
          if (current.dropsTotal > 0) Fact(context.tr('Taken out', 'مسحوب'), '-${money(current.dropsTotal, n)}', color: Tokens.danger),
          Fact(context.tr('Expected', 'المتوقع'), money(current.expected, n), strong: true),
        ]),
        if (current.hasNonCash) ...[
          const SizedBox(height: 10),
          Notice(
            icon: Icons.info_outline,
            text: context.tr(
              'The till counts every sale as cash when it works out this figure, so card and other payments are inside it: ${current.byPayment.map((m) => '${m.notation} ${money(m.amount, n)}').join(', ')}.',
              'يحتسب الصندوق كل بيع كنقد عند حساب هذا الرقم، فالبطاقة وطرق الدفع الأخرى داخله: ${current.byPayment.map((m) => '${m.notation} ${money(m.amount, n)}').join('، ')}.',
            ),
          ),
        ],
        if (current.drops.isNotEmpty) ...[
          SectionTitle(context.tr('Cash taken out', 'النقد المسحوب')),
          RowGroup(children: [
            for (final d in current.drops)
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 11),
                child: Row(children: [
                  Expanded(
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Text(d.reason.isEmpty ? context.tr('Cash drop', 'سحب نقدي') : d.reason, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600)),
                      Text(when(d.when, date: false), style: const TextStyle(fontSize: 13, color: Tokens.muted)),
                    ]),
                  ),
                  Text('-${money(d.amount, n)}', style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700, color: Tokens.danger)),
                ]),
              ),
          ]),
        ],
      ] else
        FadeUp(
          child: Notice(
            icon: Icons.lock_outline,
            color: const Color(0xFFEEF1F5),
            textColor: const Color(0xFF44505F),
            text: context.tr('The drawer is closed. Nobody has opened a shift on the till.', 'الصندوق مغلق. لم يفتح أحد وردية على الصندوق.'),
          ),
        ),
      SectionTitle(context.tr('Past shifts', 'الورديات السابقة')),
      if (r.history.isNotEmpty)
        Padding(
          padding: const EdgeInsets.only(bottom: 10),
          child: Notice(
            icon: r.offCount == 0 ? Icons.check_circle_outline : Icons.warning_amber_rounded,
            color: r.offCount == 0 ? Tokens.successBg : (r.differenceTotal < 0 ? Tokens.dangerBg : Tokens.warningBg),
            textColor: r.offCount == 0 ? Tokens.success : (r.differenceTotal < 0 ? Tokens.danger : const Color(0xFF5E4000)),
            text: r.offCount == 0
                ? context.tr('The last ${r.history.length} shift(s) all balanced.', 'توازنت آخر ${r.history.length} وردية بالكامل.')
                : context.tr(
                    '${r.offCount} of the last ${r.history.length} shift(s) were off. Together: ${r.differenceTotal < 0 ? 'short' : 'over'} ${money(r.differenceTotal.abs(), n)}.',
                    '${r.offCount} من آخر ${r.history.length} وردية لم تتوازن. المجموع: ${r.differenceTotal < 0 ? 'عجز' : 'زيادة'} ${money(r.differenceTotal.abs(), n)}.',
                  ),
          ),
        ),
      RowGroup(
        emptyText: context.tr('No closed shifts yet.', 'لا توجد ورديات مغلقة بعد.'),
        children: [
          for (final s in r.history)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
              child: Row(children: [
                Expanded(
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text(when(s.openedAt), style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600)),
                    Text(
                      [if (s.cashier.isNotEmpty) s.cashier, context.tr('Expected ${money(s.expected, n)}', 'المتوقع ${money(s.expected, n)}'), if (s.counted != null) context.tr('Counted ${money(s.counted!, n)}', 'المعدود ${money(s.counted!, n)}')].join(' · '),
                      style: const TextStyle(fontSize: 13, color: Tokens.muted),
                    ),
                  ]),
                ),
                const SizedBox(width: 10),
                _DifferenceChip(difference: s.difference, notation: n),
              ]),
            ),
        ],
      ),
      if (r.otherCurrencySales > 0) ...[
        const SizedBox(height: 10),
        Notice(
          icon: Icons.currency_exchange,
          text: context.tr('${r.otherCurrencySales} sale(s) in another currency are not counted in these figures.', '${r.otherCurrencySales} عملية بعملة أخرى غير محسوبة في هذه الأرقام.'),
        ),
      ],
    ];
  }
}

/// "Balanced", or how much a shift was short or over by.
class _DifferenceChip extends StatelessWidget {
  const _DifferenceChip({required this.difference, required this.notation});
  final double? difference;
  final String notation;

  @override
  Widget build(BuildContext context) {
    final d = difference;
    if (d == null) return Chip2(context.tr('Not counted', 'لم يُعدّ'), bg: const Color(0xFFEEF1F5), fg: const Color(0xFF44505F));
    if (d.abs() <= 0.009) return Chip2(context.tr('Balanced', 'متوازن'), bg: Tokens.successBg, fg: Tokens.success);
    return d < 0
        ? Chip2(context.tr('Short ${money(-d, notation)}', 'عجز ${money(-d, notation)}'), bg: Tokens.dangerBg, fg: Tokens.danger)
        : Chip2(context.tr('Over ${money(d, notation)}', 'زيادة ${money(d, notation)}'), bg: Tokens.warningBg, fg: Tokens.warning);
  }
}
