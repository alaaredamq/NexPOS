import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../api/owner_api.dart';
import '../l10n.dart';
import '../state/session.dart';
import '../theme.dart';

/// Everything the owner has sent to the shop and what became of it. A refusal explains itself.
class ChangesScreen extends StatelessWidget {
  const ChangesScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final controller = context.watch<ChangesController>();
    final waiting = controller.pending;

    return RefreshIndicator(
      onRefresh: controller.refresh,
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Text(context.tr('Changes sent to shop', 'التغييرات المرسلة إلى المحل'),
              style: const TextStyle(fontSize: 22, fontWeight: FontWeight.w700)),
          const SizedBox(height: 12),
          if (waiting > 0)
            _Banner(
              color: Tokens.warningBg,
              textColor: const Color(0xFF5E4000),
              icon: Icons.schedule,
              text: context.tr(
                '$waiting change(s) are waiting for the shop PC. They are applied as soon as it is online. Nothing is lost if you close the app.',
                '$waiting تغيير بانتظار جهاز المحل. سيُطبَّق فور اتصاله بالإنترنت. لن يضيع شيء إذا أغلقت التطبيق.',
              ),
            ),
          if (controller.error != null && controller.items.isEmpty)
            _Banner(
              color: Tokens.dangerBg,
              textColor: Tokens.danger,
              icon: Icons.wifi_off,
              text: context.tr('Could not load your changes. Pull down to try again.', 'تعذّر تحميل التغييرات. اسحب للأسفل للمحاولة مجددًا.'),
            ),
          if (controller.items.isEmpty && controller.error == null && !controller.loading)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 48),
              child: Text(
                context.tr('Nothing sent yet. Add a product, customer or supplier from Manage.',
                    'لم يُرسل شيء بعد. أضف منتجًا أو زبونًا أو مورّدًا من تبويب إدارة.'),
                textAlign: TextAlign.center,
                style: const TextStyle(color: Tokens.muted),
              ),
            ),
          const SizedBox(height: 4),
          for (final change in controller.items) ...[
            _ChangeCard(change: change),
            const SizedBox(height: 10),
          ],
        ],
      ),
    );
  }
}

class _Banner extends StatelessWidget {
  const _Banner({required this.color, required this.textColor, required this.icon, required this.text});

  final Color color;
  final Color textColor;
  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) => Container(
        margin: const EdgeInsets.only(bottom: 12),
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(12)),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, color: textColor, size: 20),
            const SizedBox(width: 10),
            Expanded(child: Text(text, style: TextStyle(color: textColor, fontSize: 13, height: 1.45, fontWeight: FontWeight.w500))),
          ],
        ),
      );
}

class _ChangeCard extends StatelessWidget {
  const _ChangeCard({required this.change});

  final Change change;

  String _kind(BuildContext context) => switch (change.kind) {
        ChangeKind.customer => context.tr('New customer', 'زبون جديد'),
        ChangeKind.supplier => context.tr('New supplier', 'مورّد جديد'),
        ChangeKind.product => context.tr('New product', 'منتج جديد'),
        null => change.type,
      };

  (String, Color, Color) _status(BuildContext context) => switch (change.status) {
        ChangeStatus.queued || ChangeStatus.delivered =>
          (context.tr('Waiting for shop', 'بانتظار المحل'), Tokens.warningBg, Tokens.warning),
        ChangeStatus.applied => (context.tr('Done', 'تم'), Tokens.successBg, Tokens.success),
        ChangeStatus.rejected => (context.tr('Needs a fix', 'يحتاج تعديلًا'), Tokens.dangerBg, Tokens.danger),
        ChangeStatus.discarded => (context.tr('Withdrawn', 'تم السحب'), const Color(0xFFEEF1F5), const Color(0xFF44505F)),
      };

  @override
  Widget build(BuildContext context) {
    final (label, bg, fg) = _status(context);
    final when = DateFormat.MMMd(Localizations.localeOf(context).languageCode).add_Hm().format(change.createdAt.toLocal());
    final errors = change.fieldErrors;

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(_kind(context), style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: Tokens.muted)),
                      const SizedBox(height: 2),
                      Text(change.title, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700)),
                    ],
                  ),
                ),
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 3),
                  decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(999)),
                  child: Text(label, style: TextStyle(color: fg, fontSize: 11, fontWeight: FontWeight.w700)),
                ),
              ],
            ),
            const SizedBox(height: 6),
            Text(when, style: const TextStyle(fontSize: 12, color: Tokens.muted)),
            if (change.status == ChangeStatus.rejected) ...[
              const SizedBox(height: 8),
              if (errors.isEmpty && change.message != null)
                Text(change.message!, style: const TextStyle(fontSize: 13, color: Tokens.danger, height: 1.4)),
              for (final e in errors.entries)
                for (final m in e.value)
                  Padding(
                    padding: const EdgeInsets.only(top: 2),
                    child: Text('${friendlyField(context, e.key)}: ${friendlyError(context, m)}',
                        style: const TextStyle(fontSize: 13, color: Tokens.danger, height: 1.4)),
                  ),
            ],
            if (change.status == ChangeStatus.queued) ...[
              const SizedBox(height: 8),
              Align(
                alignment: AlignmentDirectional.centerStart,
                child: TextButton(
                  onPressed: () => context.read<ChangesController>().discard(change),
                  child: Text(context.tr('Withdraw', 'سحب')),
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }
}
