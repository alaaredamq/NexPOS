import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../api/owner_api.dart';
import '../data/shop_data.dart';
import '../l10n.dart';
import '../screens/unlock_screen.dart';
import '../theme.dart';
import 'motion.dart';

final _plain = NumberFormat('#,##0.##', 'en');

/// Whether amounts are wrapped so they keep their order inside Arabic text. Set by the app from its language (see [isolateAmounts]).
bool _isolateAmounts = false;

/// In Arabic, digits that follow an Arabic letter are read as Arabic numbers and a "$" in front of them is left behind, so "$15.07" showed
/// as "15.07$". Wrapping the amount in an invisible left-to-right isolate keeps "$15.07" and "1,200 L.L." exactly as written.
void isolateAmounts(bool on) => _isolateAmounts = on;

/// "$1,284.50" for a symbol, "1,200,000 L.L." for a code or abbreviation. Digits stay Western in both languages, as on the till.
String money(double amount, String notation) {
  final text = _money(amount, notation);
  return _isolateAmounts ? '\u2066$text\u2069' : text;
}

String _money(double amount, String notation) {
  final number = _plain.format(amount.abs());
  final sign = amount < 0 ? '-' : '';
  final isSymbol = notation.length == 1 && !RegExp(r'[A-Za-z0-9]').hasMatch(notation);
  if (notation.isEmpty) return '$sign$number';
  return isSymbol ? '$sign$notation$number' : '$sign$number $notation';
}

String qty(double amount) => _plain.format(amount);

String ago(BuildContext context, DateTime when) {
  final d = DateTime.now().difference(when);
  if (d.inMinutes < 1) return context.tr('just now', 'الآن');
  if (d.inMinutes < 60) return context.tr('${d.inMinutes} min ago', 'منذ ${d.inMinutes} دقيقة');
  if (d.inHours < 24) return context.tr('${d.inHours} h ago', 'منذ ${d.inHours} ساعة');
  return context.tr('${d.inDays} d ago', 'منذ ${d.inDays} يوم');
}

/// A coloured one-line notice (warning, error, info).
class Notice extends StatelessWidget {
  const Notice({super.key, required this.text, this.icon = Icons.info_outline, this.color = Tokens.warningBg, this.textColor = const Color(0xFF5E4000)});

  final String text;
  final IconData icon;
  final Color color;
  final Color textColor;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(10)),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 18, color: textColor),
          const SizedBox(width: 10),
          Expanded(child: Text(text, style: TextStyle(color: textColor, fontSize: 13, height: 1.4))),
        ],
      ),
    );
  }
}

/// How old the figures are. The shop PC sends changes as they happen; a PC that is off leaves the copy behind, and an old number
/// must not look current.
class FreshnessNotice extends StatelessWidget {
  const FreshnessNotice({super.key});

  @override
  Widget build(BuildContext context) {
    final data = context.watch<ShopData>();
    final asOf = data.asOf;
    if (asOf == null) {
      return Notice(
        icon: Icons.schedule,
        text: context.tr('The shop PC has not sent anything yet. Figures appear after it connects.', 'لم يرسل جهاز المحل شيئًا بعد. ستظهر الأرقام بعد اتصاله.'),
      );
    }
    final age = DateTime.now().difference(asOf);
    if (age.inMinutes < 30) {
      return Row(
        children: [
          _FreshDot(key: ValueKey(asOf)), // a new "as of" time rebuilds the dot, so it pings once per fresh delivery
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              context.tr('Shop PC last sent data ${ago(context, asOf)}', 'آخر إرسال من جهاز المحل ${ago(context, asOf)}'),
              style: const TextStyle(fontSize: 13, color: Tokens.muted),
            ),
          ),
        ],
      );
    }
    return Notice(
      icon: Icons.warning_amber_rounded,
      text: context.tr(
        'The shop PC last sent data ${ago(context, asOf)}. These figures may be out of date. It may be switched off or offline.',
        'آخر إرسال من جهاز المحل ${ago(context, asOf)}. قد تكون الأرقام قديمة. ربما الجهاز مطفأ أو غير متصل.',
      ),
    );
  }
}

/// A small green dot that sends out one ring when it appears: "new data just arrived".
class _FreshDot extends StatelessWidget {
  const _FreshDot({super.key});

  @override
  Widget build(BuildContext context) {
    return TweenAnimationBuilder<double>(
      tween: Tween<double>(begin: 0, end: 1),
      duration: Motion.scaled(context, const Duration(milliseconds: 1200)),
      curve: Curves.easeOut,
      builder: (context, t, _) => SizedBox(
        width: 16,
        height: 16,
        child: Stack(
          alignment: Alignment.center,
          children: [
            Opacity(
              opacity: (1 - t) * 0.6,
              child: Container(
                width: 8 + 8 * t,
                height: 8 + 8 * t,
                decoration: const BoxDecoration(shape: BoxShape.circle, color: Tokens.success),
              ),
            ),
            Container(width: 8, height: 8, decoration: const BoxDecoration(shape: BoxShape.circle, color: Tokens.success)),
          ],
        ),
      ),
    );
  }
}

/// Grey shapes in the layout of the Today tab while the shop's data loads: it feels faster than a spinner and nothing jumps when the data lands.
class DataSkeleton extends StatelessWidget {
  const DataSkeleton({super.key});

  @override
  Widget build(BuildContext context) {
    return ListView(
      physics: const NeverScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
      children: [
        const SkeletonBox(height: 14, width: 150),
        const SizedBox(height: 8),
        const SkeletonBox(height: 24, width: 220),
        const SizedBox(height: 16),
        const SkeletonBox(height: 168, radius: 16),
        const SizedBox(height: 22),
        const SkeletonBox(height: 14, width: 120),
        const SizedBox(height: 10),
        for (var i = 0; i < 4; i++) ...[
          const SkeletonBox(height: 56, radius: 12),
          const SizedBox(height: 8),
        ],
      ],
    );
  }
}

/// Wraps a data tab: asks for the recovery code when the phone has none, shows progress and failures, and pulls to refresh.
class DataGate extends StatelessWidget {
  const DataGate({super.key, required this.builder});

  /// Builds the tab's list content. Called only when there is data to show.
  final List<Widget> Function(BuildContext context) builder;

  @override
  Widget build(BuildContext context) {
    final data = context.watch<ShopData>();

    if (!data.hasKey) return const _LockedCard();

    if (!data.hasData) {
      if (data.error != null) return _LoadFailed(error: data.error!);
      return const DataSkeleton();
    }

    return RefreshIndicator(
      onRefresh: () => data.refresh(prune: true),
      child: ListView(
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
        children: [
          ...builder(context),
          const SizedBox(height: 12),
          const FreshnessNotice(),
          if (data.error != null) ...[
            const SizedBox(height: 8),
            Notice(
              icon: Icons.wifi_off,
              color: Tokens.dangerBg,
              textColor: Tokens.danger,
              text: context.tr('Could not refresh. Showing what the phone already has. Pull down to try again.',
                  'تعذّر التحديث. تُعرض البيانات الموجودة على الهاتف. اسحب للأسفل للمحاولة مجددًا.'),
            ),
          ],
        ],
      ),
    );
  }
}

class _LockedCard extends StatelessWidget {
  const _LockedCard();

  @override
  Widget build(BuildContext context) {
    return Center(
      child: FadeUp(
        child: Padding(
        padding: const EdgeInsets.all(28),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const CircleAvatar(radius: 32, backgroundColor: Color(0xFFE1F0F1), foregroundColor: Tokens.primary, child: Icon(Icons.lock_outline, size: 30)),
            const SizedBox(height: 16),
            Text(context.tr('Unlock your shop\'s data', 'افتح بيانات محلك'),
                style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w700), textAlign: TextAlign.center),
            const SizedBox(height: 8),
            Text(
              context.tr(
                'Your shop\'s data is encrypted so that nobody but you can read it. Enter the recovery code from the shop PC once to see sales, stock and debts here.',
                'بيانات محلك مشفّرة بحيث لا يقرأها غيرك. أدخل رمز الاسترداد من جهاز المحل مرة واحدة لترى المبيعات والمخزون والديون هنا.',
              ),
              textAlign: TextAlign.center,
              style: const TextStyle(color: Tokens.muted, height: 1.5),
            ),
            const SizedBox(height: 20),
            FilledButton(
              onPressed: () => Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => const UnlockScreen())),
              child: Text(context.tr('Enter recovery code', 'أدخل رمز الاسترداد')),
            ),
          ],
        ),
      ),
      ),
    );
  }
}

class _LoadFailed extends StatelessWidget {
  const _LoadFailed({required this.error});
  final Object error;

  @override
  Widget build(BuildContext context) {
    final data = context.read<ShopData>();
    final message = switch (error) {
      ApiException(code: 'subscription_inactive') =>
        context.tr('The online plan is not active. Contact your provider.', 'خطة الأونلاين غير فعّالة. تواصل مع المزوّد.'),
      ApiException(code: 'no_shop') =>
        context.tr('No shop PC is connected to this account yet.', 'لم يتصل جهاز محل بهذا الحساب بعد.'),
      ApiException e => e.message,
      _ => context.tr('Could not load the shop\'s data. Check your connection.', 'تعذّر تحميل بيانات المحل. تحقق من الاتصال.'),
    };
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(message, textAlign: TextAlign.center, style: const TextStyle(color: Tokens.danger)),
            const SizedBox(height: 12),
            OutlinedButton(onPressed: () => data.refresh(), child: Text(context.tr('Try again', 'حاول مجددًا'))),
          ],
        ),
      ),
    );
  }
}

/// A rounded white group of rows, like the design's lists.
class RowGroup extends StatelessWidget {
  const RowGroup({super.key, required this.children, this.emptyText});

  final List<Widget> children;
  final String? emptyText;

  @override
  Widget build(BuildContext context) {
    if (children.isEmpty) {
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: 28),
        child: Center(child: Text(emptyText ?? '', textAlign: TextAlign.center, style: const TextStyle(color: Tokens.muted))),
      );
    }
    return Container(
      decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12), border: Border.all(color: Tokens.border)),
      child: Column(
        children: [
          for (var i = 0; i < children.length; i++) ...[
            if (i > 0) const Divider(height: 1, color: Tokens.border),
            // Only the first rows are worth animating; a long list should not start hundreds of animations.
            FadeUp(index: i, animate: i < 10, child: children[i]),
          ],
        ],
      ),
    );
  }
}

class ScreenTitle extends StatelessWidget {
  const ScreenTitle(this.text, {super.key});
  final String text;
  @override
  Widget build(BuildContext context) =>
      Padding(padding: const EdgeInsets.only(bottom: 12), child: Text(text, style: const TextStyle(fontSize: 22, fontWeight: FontWeight.w700)));
}
