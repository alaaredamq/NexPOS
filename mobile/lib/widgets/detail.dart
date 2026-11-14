import 'package:flutter/material.dart';

import '../l10n.dart';
import '../theme.dart';
import 'motion.dart';

/// A small heading above a group of rows.
class SectionTitle extends StatelessWidget {
  const SectionTitle(this.text, {super.key, this.trailing});
  final String text;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(top: 20, bottom: 8),
        child: Row(children: [
          Expanded(child: Text(text, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700))),
          ?trailing,
        ]),
      );
}

/// "Label ........ value" on one line; the value shrinks rather than overflow.
class Fact extends StatelessWidget {
  const Fact(this.label, this.value, {super.key, this.strong = false, this.color});
  final String label;
  final String value;
  final bool strong;
  final Color? color;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
        child: Row(children: [
          Expanded(child: Text(label, style: TextStyle(fontSize: strong ? 15 : 14, color: strong ? Tokens.ink : Tokens.muted, fontWeight: strong ? FontWeight.w700 : FontWeight.w400))),
          const SizedBox(width: 12),
          Flexible(
            child: FittedBox(
              fit: BoxFit.scaleDown,
              alignment: AlignmentDirectional.centerEnd,
              child: Text(value, style: TextStyle(fontSize: strong ? 17 : 15, fontWeight: FontWeight.w700, color: color ?? Tokens.ink)),
            ),
          ),
        ]),
      );
}

/// A tappable row (a sale, a product, a customer) with the press feedback every tappable thing in the app has.
class TapRow extends StatelessWidget {
  const TapRow({super.key, required this.onTap, required this.child});
  final VoidCallback onTap;
  final Widget child;

  @override
  Widget build(BuildContext context) => PressScale(
        scale: 0.985,
        child: InkWell(
          onTap: () {
            Haptics.tap();
            onTap();
          },
          child: child,
        ),
      );
}

/// A pill such as "Out", "Void", "Paid".
class Chip2 extends StatelessWidget {
  const Chip2(this.text, {super.key, required this.bg, required this.fg});
  final String text;
  final Color bg;
  final Color fg;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 3),
        decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(999)),
        child: Text(text, style: TextStyle(fontSize: 12, color: fg, fontWeight: FontWeight.w700)),
      );
}

/// A big number with a label above it, for the top of a detail page.
class StatCard extends StatelessWidget {
  const StatCard({super.key, required this.label, required this.value, this.sub, this.dark = false, this.valueColor});
  final String label;
  final String value;
  final String? sub;
  final bool dark;
  final Color? valueColor;

  @override
  Widget build(BuildContext context) {
    final labelColor = dark ? const Color(0xFFA9BABC) : Tokens.muted;
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(color: dark ? Tokens.nav : Colors.white, borderRadius: BorderRadius.circular(14), border: dark ? null : Border.all(color: Tokens.border)),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text(label, style: TextStyle(fontSize: 13, color: labelColor)),
        const SizedBox(height: 2),
        FittedBox(
          fit: BoxFit.scaleDown,
          alignment: AlignmentDirectional.centerStart,
          child: Text(value, style: TextStyle(fontSize: 32, fontWeight: FontWeight.w700, height: 1.2, color: valueColor ?? (dark ? Tokens.readout : Tokens.ink))),
        ),
        if (sub != null) Padding(padding: const EdgeInsets.only(top: 2), child: Text(sub!, style: TextStyle(fontSize: 13, color: labelColor))),
      ]),
    );
  }
}

/// Shown when the thing the owner opened is no longer in the shop's data (the shop deleted it).
class Gone extends StatelessWidget {
  const Gone({super.key});

  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Text(context.tr('This is no longer in the shop\'s data.', 'لم يعد هذا موجودًا في بيانات المحل.'),
              textAlign: TextAlign.center, style: const TextStyle(color: Tokens.muted, fontSize: 15)),
        ),
      );
}

/// Bars that grow up from the baseline one after another. [highlight] is drawn in full strength, the rest faded.
class BarChart extends StatelessWidget {
  const BarChart({super.key, required this.values, this.labels, this.highlight, this.height = 92, this.color = Tokens.primary, this.semanticsLabel});

  final List<double> values;
  final List<String>? labels;
  final int? highlight;
  final double height;
  final Color color;
  final String? semanticsLabel;

  @override
  Widget build(BuildContext context) {
    final max = values.fold<double>(0, (a, b) => b > a ? b : a);
    final gap = values.length > 14 ? 2.0 : 6.0;
    return Semantics(
      label: semanticsLabel,
      child: Column(children: [
        TweenAnimationBuilder<double>(
          tween: Tween<double>(begin: 0, end: 1),
          duration: Motion.scaled(context, const Duration(milliseconds: 800)),
          builder: (context, t, _) => SizedBox(
            height: height,
            child: Row(crossAxisAlignment: CrossAxisAlignment.end, children: [
              for (var i = 0; i < values.length; i++) ...[
                if (i > 0) SizedBox(width: gap),
                Expanded(
                  child: Container(
                    height: (max <= 0 ? 3.0 : (values[i] / max * height).clamp(3.0, height)) *
                        Motion.enter.transform(((t - i * (0.35 / values.length)) / 0.65).clamp(0.0, 1.0)),
                    constraints: const BoxConstraints(minHeight: 2),
                    decoration: BoxDecoration(
                      color: (highlight == null || highlight == i) ? color : color.withValues(alpha: 0.3),
                      borderRadius: BorderRadius.circular(3),
                    ),
                  ),
                ),
              ],
            ]),
          ),
        ),
        if (labels != null) ...[
          const SizedBox(height: 6),
          Row(children: [
            for (var i = 0; i < values.length; i++)
              Expanded(
                child: Center(child: Text(labels![i], maxLines: 1, overflow: TextOverflow.clip, style: const TextStyle(fontSize: 12, color: Tokens.muted))),
              ),
          ]),
        ],
      ]),
    );
  }
}
