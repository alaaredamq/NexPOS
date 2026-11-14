import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../data/details.dart';
import '../data/shop_data.dart';
import '../l10n.dart';
import '../state/permissions.dart';
import '../state/session.dart';
import '../theme.dart';
import '../widgets/common.dart';
import '../widgets/detail.dart';
import '../widgets/motion.dart';
import 'open.dart';

/// One sale: what was sold, to whom, how it was paid. Items and customer open their own pages.
class SaleDetailPage extends StatelessWidget {
  const SaleDetailPage({super.key, required this.saleId});
  final int saleId;

  @override
  Widget build(BuildContext context) {
    final detail = saleDetail(context.watch<ShopData>(), saleId);
    final locale = Localizations.localeOf(context).languageCode;
    return Scaffold(
      appBar: AppBar(title: Text(detail == null ? context.tr('Sale', 'عملية بيع') : '#${detail.sale.number}', style: const TextStyle(fontWeight: FontWeight.w700))),
      body: detail == null ? const Gone() : _body(context, detail, locale),
    );
  }

  Widget _body(BuildContext context, SaleDetail d, String locale) {
    final sale = d.sale;
    final n = sale.notation;
    final when = sale.when == null ? '' : DateFormat('EEEE d MMMM · HH:mm', locale).format(sale.when!);
    final (statusText, statusBg, statusFg) = sale.isVoid
        ? (context.tr('Void', 'ملغاة'), Tokens.dangerBg, Tokens.danger)
        : d.unpaid > 0
            ? (context.tr('On account', 'على الحساب'), Tokens.warningBg, Tokens.warning)
            : (context.tr('Paid', 'مدفوعة'), Tokens.successBg, Tokens.success);

    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 28),
      children: [
        FadeUp(
          child: StatCard(
            dark: true,
            label: [when, if (sale.payment.isNotEmpty) sale.payment].where((s) => s.isNotEmpty).join(' · '),
            value: money(sale.total, n),
            sub: d.unpaid > 0 && !sale.isVoid ? context.tr('${money(d.unpaid, n)} still owed', 'متبقٍ ${money(d.unpaid, n)}') : null,
            valueColor: sale.isVoid ? const Color(0xFF8FA3A6) : null,
          ),
        ),
        const SizedBox(height: 10),
        FadeUp(index: 1, child: Align(alignment: AlignmentDirectional.centerStart, child: Chip2(statusText, bg: statusBg, fg: statusFg))),
        if (sale.isVoid && d.voidReason.isNotEmpty) ...[
          const SizedBox(height: 10),
          Notice(icon: Icons.block, color: Tokens.dangerBg, textColor: Tokens.danger, text: context.tr('Voided: ${d.voidReason}', 'ملغاة: ${d.voidReason}')),
        ],
        if (d.customerName.isNotEmpty || d.customerId != null) ...[
          SectionTitle(context.tr('Customer', 'الزبون')),
          FadeUp(
            index: 2,
            child: RowGroup(children: [
              if (d.customerId != null && context.read<Session>().can(Perm.debts))
                TapRow(
                  onTap: () => openCustomer(context, d.customerId!),
                  child: Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 14),
                    child: Row(children: [
                      const CircleAvatar(radius: 16, backgroundColor: Color(0xFFE1F0F1), foregroundColor: Tokens.primary, child: Icon(Icons.person_outline, size: 18)),
                      const SizedBox(width: 12),
                      Expanded(child: Text(d.customerName, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600))),
                      Icon(context.isArabic ? Icons.chevron_left : Icons.chevron_right, color: Tokens.muted),
                    ]),
                  ),
                )
              else
                Fact(context.tr('Name', 'الاسم'), d.customerName),
            ]),
          ),
        ],
        SectionTitle(context.tr('Items (${d.items.length})', 'المنتجات (${d.items.length})')),
        RowGroup(
          emptyText: context.tr('The item list has not reached the phone for this sale.', 'لم تصل قائمة المنتجات لهذه العملية إلى الهاتف.'),
          children: [for (final item in d.items) _ItemRow(item: item, notation: n)],
        ),
        SectionTitle(context.tr('Totals', 'المجاميع')),
        RowGroup(children: [
          if (d.subTotal > 0 && (d.discount > 0 || d.tax > 0)) Fact(context.tr('Subtotal', 'المجموع الفرعي'), money(d.subTotal, n)),
          if (d.discount > 0) Fact(context.tr('Discount', 'الخصم'), '-${money(d.discount, n)}', color: Tokens.success),
          if (d.tax > 0) Fact(context.tr('Tax', 'الضريبة'), money(d.tax, n)),
          Fact(context.tr('Total', 'الإجمالي'), money(sale.total, n), strong: true),
          if (d.paid > 0) Fact(context.tr('Paid', 'المدفوع'), money(d.paid, n)),
        ]),
        const SizedBox(height: 14),
        const FreshnessNotice(),
      ],
    );
  }
}

class _ItemRow extends StatelessWidget {
  const _ItemRow({required this.item, required this.notation});
  final SaleItemLine item;
  final String notation;

  @override
  Widget build(BuildContext context) {
    final known = item.productId != null && context.read<Session>().can(Perm.stock) && context.read<ShopData>().row('Products', item.productId) != null;
    final body = Padding(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 11),
      child: Row(children: [
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text(item.name, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600)),
            Text(
              '${qty(item.quantity)}${item.unit.isEmpty ? '' : ' ${unitShort(context, item.unit)}'} × ${money(item.unitPrice, notation)}',
              style: const TextStyle(fontSize: 13, color: Tokens.muted),
            ),
          ]),
        ),
        const SizedBox(width: 10),
        Text(money(item.total, notation), style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700)),
      ]),
    );
    return known ? TapRow(onTap: () => openProduct(context, item.productId!), child: body) : body;
  }
}
