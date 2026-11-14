import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../data/reports.dart';
import '../data/shop_data.dart';
import '../l10n.dart';
import '../theme.dart';
import '../widgets/common.dart';
import '../widgets/motion.dart';
import 'reports_view.dart';
import '../widgets/detail.dart';
import 'open.dart';
import 'today_screen.dart';

/// A search box that filters the list under it.
class _Search extends StatelessWidget {
  const _Search({required this.hint, required this.onChanged});
  final String hint;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 12),
        child: TextField(
          onChanged: onChanged,
          decoration: InputDecoration(prefixIcon: const Icon(Icons.search), hintText: hint, isDense: true),
        ),
      );
}

// ---- Sales ----

class SalesScreen extends StatefulWidget {
  const SalesScreen({super.key});

  @override
  State<SalesScreen> createState() => _SalesScreenState();
}

class _SalesScreenState extends State<SalesScreen> {
  String _query = '';
  bool _reports = false;
  int _days = 7;

  @override
  Widget build(BuildContext context) {
    return DataGate(builder: (context) {
      final switcher = SegmentedButton<bool>(
        showSelectedIcon: false,
        segments: [
          ButtonSegment(value: false, icon: const Icon(Icons.receipt_long_outlined, size: 18), label: Text(context.tr('Receipts', 'الإيصالات'))),
          ButtonSegment(value: true, icon: const Icon(Icons.insights_outlined, size: 18), label: Text(context.tr('Reports', 'التقارير'))),
        ],
        selected: {_reports},
        onSelectionChanged: (s) {
          Haptics.tap();
          setState(() => _reports = s.first);
        },
      );
      if (_reports) {
        return [
          ScreenTitle(context.tr('Sales', 'المبيعات')),
          switcher,
          const SizedBox(height: 14),
          ReportsView(days: _days, onDays: (d) => setState(() => _days = d)),
        ];
      }
      final all = recentSales(context.watch<ShopData>());
      final q = _query.trim().toLowerCase();
      final shown = q.isEmpty ? all : all.where((s) => s.number.toLowerCase().contains(q) || s.customer.toLowerCase().contains(q)).toList();
      return [
        ScreenTitle(context.tr('Sales', 'المبيعات')),
        switcher,
        const SizedBox(height: 12),
        _Search(hint: context.tr('Receipt number or customer', 'رقم الإيصال أو الزبون'), onChanged: (v) => setState(() => _query = v)),
        RowGroup(
          emptyText: q.isEmpty ? context.tr('No sales yet.', 'لا توجد مبيعات بعد.') : context.tr('No sale matches.', 'لا توجد مبيعات مطابقة.'),
          children: [for (final s in shown) SaleTile(sale: s)],
        ),
        if (all.length >= 200 && q.isEmpty)
          Padding(
            padding: const EdgeInsets.only(top: 8),
            child: Text(context.tr('Showing the latest 200 sales.', 'تُعرض آخر 200 عملية بيع.'), style: const TextStyle(fontSize: 12, color: Tokens.muted)),
          ),
      ];
    });
  }
}

// ---- Stock ----

class StockScreen extends StatefulWidget {
  const StockScreen({super.key});

  @override
  State<StockScreen> createState() => _StockScreenState();
}

class _StockScreenState extends State<StockScreen> {
  String _query = '';
  bool _onlyLow = true;

  @override
  Widget build(BuildContext context) {
    return DataGate(builder: (context) {
      final all = stockItems(context.watch<ShopData>());
      final needing = all.where((s) => s.level != StockLevel.ok).length;
      final q = _query.trim().toLowerCase();
      Iterable<StockItem> list = all;
      if (_onlyLow && q.isEmpty) list = list.where((s) => s.level != StockLevel.ok);
      if (q.isNotEmpty) list = list.where((s) => s.name.toLowerCase().contains(q) || s.barcode.toLowerCase().contains(q));
      final shown = list.take(300).toList();

      return [
        ScreenTitle(context.tr('Stock', 'المخزون')),
        _Search(hint: context.tr('Product name or barcode', 'اسم المنتج أو الباركود'), onChanged: (v) => setState(() => _query = v)),
        Wrap(spacing: 8, children: [
          ChoiceChip(
            label: Text(context.tr('Needs restocking ($needing)', 'يحتاج تعبئة ($needing)')),
            selected: _onlyLow,
            onSelected: (_) => setState(() => _onlyLow = true),
          ),
          ChoiceChip(
            label: Text(context.tr('All products (${all.length})', 'كل المنتجات (${all.length})')),
            selected: !_onlyLow,
            onSelected: (_) => setState(() => _onlyLow = false),
          ),
        ]),
        const SizedBox(height: 12),
        RowGroup(
          emptyText: q.isNotEmpty
              ? context.tr('No product matches.', 'لا توجد منتجات مطابقة.')
              : _onlyLow
                  ? context.tr('Everything is above its minimum.', 'كل المنتجات فوق الحد الأدنى.')
                  : context.tr('No products yet.', 'لا توجد منتجات بعد.'),
          children: [for (final s in shown) _StockTile(item: s)],
        ),
      ];
    });
  }
}

class _StockTile extends StatelessWidget {
  const _StockTile({required this.item});
  final StockItem item;

  @override
  Widget build(BuildContext context) {
    final (label, bg, fg) = switch (item.level) {
      StockLevel.out => (context.tr('Out', 'نفد'), Tokens.dangerBg, Tokens.danger),
      StockLevel.low => (context.tr('Low', 'منخفض'), Tokens.warningBg, Tokens.warning),
      StockLevel.ok => ('', Colors.transparent, Tokens.muted),
    };
    return TapRow(
      onTap: () => openProduct(context, item.id),
      child: Padding(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
      child: Row(children: [
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(item.name, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600)),
            Text(
              [
                if (item.barcode.isNotEmpty) item.barcode,
                if (item.minimum != null) context.tr('min ${qty(item.minimum!)}', 'الحد الأدنى ${qty(item.minimum!)}'),
              ].join(' · '),
              style: const TextStyle(fontSize: 12, color: Tokens.muted),
            ),
          ]),
        ),
        if (label.isNotEmpty) ...[
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
            decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(5)),
            child: Text(label, style: TextStyle(fontSize: 11, color: fg, fontWeight: FontWeight.w700)),
          ),
          const SizedBox(width: 10),
        ],
        Text('${qty(item.quantity)}${item.unit.isEmpty ? '' : ' ${unitShort(context, item.unit)}'}', style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
      ]),
      ),
    );
  }
}

// ---- Debts ----

class DebtsScreen extends StatelessWidget {
  const DebtsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return DataGate(builder: (context) {
      final report = debtsReport(context.watch<ShopData>());
      return [
        ScreenTitle(context.tr('Debts', 'الديون')),
        _Total(
          label: context.tr('Owed to the shop', 'مستحق للمحل'),
          totals: report.owedToShopTotals,
          count: report.owedToShop.length,
          unit: context.tr('customer(s)', 'زبون'),
          color: Tokens.primary,
        ),
        const SizedBox(height: 10),
        RowGroup(
          emptyText: context.tr('Nobody owes the shop.', 'لا أحد مدين للمحل.'),
          children: [for (final d in report.owedToShop) _DebtTile(entry: d, onTap: () => openCustomer(context, d.id))],
        ),
        const SizedBox(height: 22),
        _Total(
          label: context.tr('The shop owes suppliers', 'المحل مدين للموردين'),
          totals: report.shopOwesTotals,
          count: report.shopOwes.length,
          unit: context.tr('supplier(s)', 'مورّد'),
          color: const Color(0xFF454C5A),
        ),
        const SizedBox(height: 10),
        RowGroup(
          emptyText: context.tr('The shop owes no supplier.', 'المحل غير مدين لأي مورّد.'),
          children: [for (final d in report.shopOwes) _DebtTile(entry: d)],
        ),
      ];
    });
  }
}

class _Total extends StatelessWidget {
  const _Total({required this.label, required this.totals, required this.count, required this.unit, required this.color});
  final String label;
  final List<MoneyLine> totals;
  final int count;
  final String unit;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(14)),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text(label, style: const TextStyle(color: Colors.white70, fontSize: 13)),
        const SizedBox(height: 2),
        Text(
          totals.isEmpty ? '0' : totals.map((m) => money(m.amount, m.notation)).join('  +  '),
          style: const TextStyle(color: Colors.white, fontSize: 28, fontWeight: FontWeight.w700),
        ),
        Text('$count $unit', style: const TextStyle(color: Colors.white70, fontSize: 13)),
      ]),
    );
  }
}

class _DebtTile extends StatelessWidget {
  const _DebtTile({required this.entry, this.onTap});
  final DebtEntry entry;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final row = Padding(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
      child: Row(children: [
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(entry.name.isEmpty ? '—' : entry.name, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600)),
            if (entry.phone.isNotEmpty)
              Text(entry.phone, textDirection: TextDirection.ltr, style: const TextStyle(fontSize: 13, color: Tokens.muted)),
          ]),
        ),
        Text(money(entry.amount, entry.notation), style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
      ]),
    );
    return onTap == null ? row : TapRow(onTap: onTap!, child: row);
  }
}
