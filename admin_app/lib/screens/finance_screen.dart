import 'package:flutter/material.dart';

import '../api/models.dart';
import '../theme.dart';
import '../util/format.dart';
import '../widgets/common.dart';
import 'client_screen.dart';
import 'payment_forms.dart';

/// The portal's Finance page: money in and out for a month (or year), the budget, the trend, renewals due and every line.
class FinanceScreen extends StatefulWidget {
  const FinanceScreen({super.key});

  @override
  State<FinanceScreen> createState() => _FinanceScreenState();
}

class _FinanceScreenState extends State<FinanceScreen> {
  final _page = GlobalKey<LoaderState<FinanceSummary>>();

  /// `m=2026-11` or `y=2026`; null is this month.
  String? _period;
  FinanceSummary? _shown;

  Future<void> _reload() async => _page.currentState?.reload();

  void _go(String? period) {
    setState(() => _period = period);
    _reload();
  }

  Future<void> _open(Widget screen) async {
    final saved = await Navigator.push<bool>(context, MaterialPageRoute(builder: (_) => screen));
    if (saved == true) await _reload();
  }

  Future<void> _add() async {
    final what = await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          ListTile(leading: const Icon(Icons.south_west, color: Tokens.ok), title: const Text('Payment (money in)'), onTap: () => Navigator.pop(context, 'payment')),
          ListTile(leading: const Icon(Icons.north_east, color: Tokens.bad), title: const Text('Expense (money out)'), onTap: () => Navigator.pop(context, 'expense')),
        ]),
      ),
    );
    if (what == 'payment') await _open(const PaymentFormScreen());
    if (what == 'expense') await _open(const ExpenseFormScreen());
  }

  String get _monthForBudget {
    final q = _shown?.query ?? '';
    if (q.startsWith('m=')) return q.substring(2);
    return monthKey(DateTime.now());
  }

  @override
  Widget build(BuildContext context) {
    final s = _shown;
    return Scaffold(
      appBar: AppBar(
        title: const Text('Finance'),
        actions: [
          IconButton(
            tooltip: 'Budget',
            icon: const Icon(Icons.savings_outlined),
            onPressed: () async {
              await Navigator.push(context, MaterialPageRoute<void>(builder: (_) => BudgetScreen(month: _monthForBudget)));
              await _reload();
            },
          ),
        ],
      ),
      floatingActionButton: FloatingActionButton.extended(heroTag: 'record-money', onPressed: _add, icon: const Icon(Icons.add), label: const Text('Record')),
      body: Column(children: [
        // The period: previous / next, and month or the whole year.
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 8),
          child: Row(children: [
            IconButton(tooltip: 'Earlier', onPressed: s?.previous == null ? null : () => _go(s!.previous), icon: const Icon(Icons.chevron_left)),
            Expanded(
              child: Text(s?.label ?? '', textAlign: TextAlign.center, style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700)),
            ),
            IconButton(tooltip: 'Later', onPressed: s?.next == null ? null : () => _go(s!.next), icon: const Icon(Icons.chevron_right)),
            TextButton(
              onPressed: s == null
                  ? null
                  : () => _go(s.isYear ? 'm=${monthKey(DateTime.now())}' : 'y=${(s.query.length >= 6 ? s.query.substring(2, 6) : '${DateTime.now().year}')}'),
              child: Text(s?.isYear == true ? 'Month' : 'Year'),
            ),
          ]),
        ),
        Expanded(
          child: Loader<FinanceSummary>(
            key: _page,
            load: (api) async {
              final summary = await api.financeSummary(_period);
              if (mounted) setState(() => _shown = summary);
              return summary;
            },
            builder: (context, s, reload) => PageList(children: [
              TileRow(
                StatTile(label: 'Collected', value: context.money(s.collected), color: Tokens.ok, note: _change(s.collectedChange)),
                StatTile(label: 'Spent', value: context.money(s.spent), note: _change(s.spentChange)),
              ),
              TileRow(
                StatTile(
                  label: 'Profit',
                  value: context.money(s.profit),
                  color: s.profit < 0 ? Tokens.bad : Tokens.ink,
                  note: s.marginPercent == null ? null : '${s.marginPercent}% of what came in',
                ),
                StatTile(label: 'Balance', value: context.money(s.balance), note: '${s.payingClients} paying client${s.payingClients == 1 ? '' : 's'}'),
              ),
              if (s.attention.isNotEmpty)
                SectionCard(title: 'Needs you', children: [
                  for (final a in s.attention)
                    AttentionTile(a, onTap: a.clientId == null ? null : () => _openClient(context, a.clientId!, reload)),
                ]),
              if (s.income != null || s.spending != null)
                SectionCard(title: 'Budget', children: [
                  if (s.income != null) _BudgetBar('Income target', s.income!, goodWhenFull: true),
                  if (s.spending != null) ...[const SizedBox(height: 12), _BudgetBar('Spending limit', s.spending!, goodWhenFull: false)],
                ]),
              if (s.trend.isNotEmpty) SectionCard(title: 'Last 12 months', children: [_Trend(s.trend)]),
              if (s.renewals.isNotEmpty)
                SectionCard(
                  title: 'Renewals due',
                  trailing: s.renewalsExpected > 0 ? Text('≈ ${context.money(s.renewalsExpected)}', style: const TextStyle(fontWeight: FontWeight.w700)) : null,
                  children: [
                    for (final r in s.renewals)
                      ListTile(
                        contentPadding: EdgeInsets.zero,
                        title: Text(r.name, style: const TextStyle(fontWeight: FontWeight.w600)),
                        subtitle: Text('${r.what} ${until(r.endsAt)} · ${formatDay(r.endsAt)}'),
                        trailing: r.lastPaid == null ? null : Text('last ${context.money(r.lastPaid!)}', style: const TextStyle(color: Tokens.muted)),
                        onTap: () => _openClient(context, r.clientId, reload),
                      ),
                  ],
                ),
              if (s.byPurpose.isNotEmpty) _ShareCard('Money in, by what it was for', s.byPurpose),
              if (s.byPayer.isNotEmpty) _ShareCard('Money in, by who paid', s.byPayer),
              if (s.byCategory.isNotEmpty) _ShareCard('Money out, by category', s.byCategory),
              SectionCard(title: 'Every payment and expense', children: [
                if (s.lines.isEmpty) const MutedText('Nothing in this period.'),
                for (final l in s.lines)
                  ListTile(
                    contentPadding: EdgeInsets.zero,
                    leading: CircleAvatar(
                      radius: 18,
                      backgroundColor: l.isPayment ? Tokens.okBg : Tokens.badBg,
                      foregroundColor: l.isPayment ? Tokens.ok : Tokens.bad,
                      child: Icon(l.isPayment ? Icons.south_west : Icons.north_east, size: 18),
                    ),
                    title: Text(l.who, style: const TextStyle(fontWeight: FontWeight.w600)),
                    subtitle: Text([formatDay(l.date), if (l.details.isNotEmpty) l.details, ?l.note].join(' · ')),
                    trailing: Text('${l.isPayment ? '+' : '-'}${context.money(l.amount)}',
                        style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700, color: l.isPayment ? Tokens.ok : Tokens.ink)),
                    onTap: () => _open(l.isPayment ? PaymentFormScreen(paymentId: l.id, clientName: l.clientId == null ? null : l.who) : ExpenseFormScreen(expenseId: l.id)),
                  ),
              ]),
            ]),
          ),
        ),
      ]),
    );
  }

  static String? _change(String? change) => change == null ? null : '$change on the period before';

  Future<void> _openClient(BuildContext context, int id, Future<void> Function() reload) async {
    await Navigator.push(context, MaterialPageRoute<void>(builder: (_) => ClientScreen(clientId: id)));
    await reload();
  }
}

class _BudgetBar extends StatelessWidget {
  const _BudgetBar(this.label, this.use, {required this.goodWhenFull});
  final String label;
  final BudgetUse use;

  /// An income target is good when reached; a spending limit is bad when passed.
  final bool goodWhenFull;

  @override
  Widget build(BuildContext context) {
    final color = goodWhenFull ? (use.percent >= 100 ? Tokens.ok : Tokens.navy) : (use.over ? Tokens.bad : use.percent >= 85 ? Tokens.warn : Tokens.data);
    return Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      Row(children: [
        Expanded(child: Text(label, style: const TextStyle(fontWeight: FontWeight.w600))),
        Text('${context.money(use.actual)} of ${context.money(use.planned)}', style: const TextStyle(color: Tokens.muted)),
      ]),
      const SizedBox(height: 6),
      ClipRRect(
        borderRadius: BorderRadius.circular(99),
        child: LinearProgressIndicator(value: (use.percent / 100).clamp(0, 1).toDouble(), minHeight: 10, color: color, backgroundColor: Tokens.noneBg),
      ),
      const SizedBox(height: 4),
      Text(
        use.over ? '${context.money(-use.left)} over' : goodWhenFull ? '${context.money(use.left)} to go' : '${context.money(use.left)} left',
        style: TextStyle(color: use.over && !goodWhenFull ? Tokens.bad : Tokens.muted, fontSize: 12.5),
      ),
    ]);
  }
}

/// Twelve months of money in (green) and out (grey), side by side.
class _Trend extends StatelessWidget {
  const _Trend(this.months);
  final List<TrendMonth> months;

  @override
  Widget build(BuildContext context) {
    final most = months.fold<double>(1, (m, t) => [m, t.collected, t.spent].reduce((a, b) => a > b ? a : b));
    return Column(children: [
      SizedBox(
        height: 120,
        child: Row(crossAxisAlignment: CrossAxisAlignment.end, children: [
          for (final t in months)
            Expanded(
              child: Tooltip(
                message: '${monthLabel(t.month)}: in ${context.money(t.collected)}, out ${context.money(t.spent)}',
                child: Row(crossAxisAlignment: CrossAxisAlignment.end, children: [
                  Expanded(child: _bar(t.collected / most, Tokens.data)),
                  const SizedBox(width: 1),
                  Expanded(child: _bar(t.spent / most, Tokens.line)),
                  const SizedBox(width: 3),
                ]),
              ),
            ),
        ]),
      ),
      const SizedBox(height: 4),
      Row(children: [
        for (final t in months)
          Expanded(child: Text(monthLabel(t.month).substring(0, 1), textAlign: TextAlign.center, style: const TextStyle(color: Tokens.muted, fontSize: 11))),
      ]),
      const SizedBox(height: 8),
      const Row(children: [
        _Key(Tokens.data, 'In'),
        SizedBox(width: 14),
        _Key(Tokens.line, 'Out'),
      ]),
    ]);
  }

  Widget _bar(double share, Color color) => Container(
        height: share <= 0 ? 2 : 2 + 118 * share.clamp(0, 1),
        decoration: BoxDecoration(color: color, borderRadius: const BorderRadius.vertical(top: Radius.circular(3))),
      );
}

class _Key extends StatelessWidget {
  const _Key(this.color, this.label);
  final Color color;
  final String label;

  @override
  Widget build(BuildContext context) => Row(mainAxisSize: MainAxisSize.min, children: [
        Container(width: 10, height: 10, decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(2))),
        const SizedBox(width: 5),
        Text(label, style: const TextStyle(color: Tokens.muted, fontSize: 12.5)),
      ]);
}

class _ShareCard extends StatelessWidget {
  const _ShareCard(this.title, this.rows);
  final String title;
  final List<ShareRow> rows;

  @override
  Widget build(BuildContext context) {
    final total = rows.fold<double>(0, (t, r) => t + r.amount);
    return SectionCard(title: title, children: [
      for (final r in rows.take(8))
        Padding(
          padding: const EdgeInsets.symmetric(vertical: 5),
          child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            Row(children: [
              Expanded(child: Text(r.name, style: const TextStyle(fontWeight: FontWeight.w500))),
              Text(context.money(r.amount), style: const TextStyle(fontWeight: FontWeight.w700)),
            ]),
            const SizedBox(height: 4),
            ClipRRect(
              borderRadius: BorderRadius.circular(99),
              child: LinearProgressIndicator(value: total == 0 ? 0 : r.amount / total, minHeight: 6, color: Tokens.data, backgroundColor: Tokens.noneBg),
            ),
          ]),
        ),
    ]);
  }
}
