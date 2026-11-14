import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../data/details.dart';
import '../data/shop_data.dart';
import '../l10n.dart';
import '../theme.dart';
import '../widgets/common.dart';
import '../widgets/detail.dart';
import '../widgets/motion.dart';
import 'today_screen.dart';

/// One customer: contact, what they owe, how the account moved, and what they bought. Purchases open their sale.
class CustomerPage extends StatelessWidget {
  const CustomerPage({super.key, required this.customerId});
  final int customerId;

  @override
  Widget build(BuildContext context) {
    final d = customerDetail(context.watch<ShopData>(), customerId);
    return Scaffold(
      appBar: AppBar(title: Text(d?.name ?? context.tr('Customer', 'الزبون'), style: const TextStyle(fontWeight: FontWeight.w700))),
      body: d == null ? const Gone() : _body(context, d),
    );
  }

  String _kind(BuildContext context, LedgerKind k) => switch (k) {
        LedgerKind.sale => context.tr('Sale on account', 'بيع على الحساب'),
        LedgerKind.payment => context.tr('Payment', 'دفعة'),
        LedgerKind.adjustment => context.tr('Correction', 'تصحيح'),
        LedgerKind.reversal => context.tr('Sale cancelled', 'إلغاء بيع'),
        LedgerKind.transfer => context.tr('Moved between customers', 'نقل بين الزبائن'),
      };

  Widget _body(BuildContext context, CustomerDetail d) {
    final locale = Localizations.localeOf(context).languageCode;
    final owes = d.balance > 0.004;
    final credit = d.balance < -0.004;
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 28),
      children: [
        FadeUp(
          child: StatCard(
            label: owes ? context.tr('Owes the shop', 'مدين للمحل') : credit ? context.tr('The shop owes them', 'المحل مدين له') : context.tr('Account', 'الحساب'),
            value: owes || credit ? money(d.balance.abs(), d.notation) : context.tr('No debt', 'لا دين'),
            valueColor: owes ? Tokens.danger : credit ? Tokens.success : Tokens.ink,
            sub: d.totalBought > 0 ? context.tr('Bought ${money(d.totalBought, d.notation)} in total', 'اشترى بإجمالي ${money(d.totalBought, d.notation)}') : null,
          ),
        ),
        if (d.phone.isNotEmpty || d.address.isNotEmpty) ...[
          SectionTitle(context.tr('Contact', 'التواصل')),
          FadeUp(
            index: 1,
            child: RowGroup(children: [
              if (d.phone.isNotEmpty)
                TapRow(
                  onTap: () async {
                    final messenger = ScaffoldMessenger.of(context);
                    final copied = context.tr('Phone number copied.', 'نُسخ رقم الهاتف.');
                    await Clipboard.setData(ClipboardData(text: d.phone));
                    messenger.showSnackBar(SnackBar(content: Text(copied)));
                  },
                  child: Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                    child: Row(children: [
                      const Icon(Icons.phone_outlined, size: 20, color: Tokens.primary),
                      const SizedBox(width: 12),
                      Expanded(child: Text(d.phone, textDirection: TextDirection.ltr, textAlign: TextAlign.start, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600))),
                      Text(context.tr('Copy', 'نسخ'), style: const TextStyle(fontSize: 13, color: Tokens.primary, fontWeight: FontWeight.w600)),
                    ]),
                  ),
                ),
              if (d.address.isNotEmpty)
                Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
                  child: Row(children: [
                    const Icon(Icons.place_outlined, size: 20, color: Tokens.primary),
                    const SizedBox(width: 12),
                    Expanded(child: Text(d.address, style: const TextStyle(fontSize: 15))),
                  ]),
                ),
            ]),
          ),
        ],
        SectionTitle(context.tr('Account history', 'سجل الحساب')),
        RowGroup(
          emptyText: context.tr('No account entries yet.', 'لا توجد حركات على الحساب بعد.'),
          children: [
            for (final l in d.ledger.take(30))
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 11),
                child: Row(children: [
                  Expanded(
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Text(_kind(context, l.kind), style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600)),
                      Text(
                        [if (l.when != null) DateFormat('d MMM yyyy · HH:mm', locale).format(l.when!), if (l.note.isNotEmpty) l.note].join(' · '),
                        style: const TextStyle(fontSize: 13, color: Tokens.muted),
                      ),
                    ]),
                  ),
                  const SizedBox(width: 10),
                  // Positive added to what they owe (red); negative reduced it (green).
                  Text(
                    '${l.amount >= 0 ? '+' : '-'}${money(l.amount.abs(), d.notation)}',
                    style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700, color: l.amount > 0 ? Tokens.danger : Tokens.success),
                  ),
                ]),
              ),
          ],
        ),
        SectionTitle(context.tr('Purchases (${d.purchases.length})', 'المشتريات (${d.purchases.length})')),
        RowGroup(
          emptyText: context.tr('No sales to this customer yet.', 'لا توجد مبيعات لهذا الزبون بعد.'),
          children: [for (final s in d.purchases.take(30)) SaleTile(sale: s)],
        ),
        const SizedBox(height: 14),
        const FreshnessNotice(),
      ],
    );
  }
}
