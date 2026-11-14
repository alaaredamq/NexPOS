import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../l10n.dart';
import '../theme.dart';

/// One set of timings and curves for the whole app, so every movement has the same rhythm.
/// Rules followed: 150-300 ms for small things, at most ~450 ms for entrances, ease-out on the way in,
/// movement only through transform/opacity, nothing blocks a tap, and nothing moves when the phone asks for reduced motion.
class Motion {
  static const fast = Duration(milliseconds: 150);
  static const base = Duration(milliseconds: 250);
  static const slow = Duration(milliseconds: 420);
  static const enter = Curves.easeOutCubic;

  /// True when the owner turned animations off in the phone's accessibility settings.
  static bool reduced(BuildContext context) => MediaQuery.maybeOf(context)?.disableAnimations ?? false;

  static Duration scaled(BuildContext context, Duration d) => reduced(context) ? Duration.zero : d;
}

/// Light taps for confirmations. Never used for scrolling or for every touch.
class Haptics {
  static void tap() => HapticFeedback.selectionClick();
  static void success() => HapticFeedback.lightImpact();
  static void problem() => HapticFeedback.mediumImpact();
}

/// Fades and lifts its child into place. Give list neighbours rising [index] values for a quick staggered reveal.
/// Plays once, when the widget first appears; later rebuilds do not replay it.
class FadeUp extends StatefulWidget {
  const FadeUp({super.key, required this.child, this.index = 0, this.animate = true});

  final Widget child;
  final int index;

  /// False shows the child at once (used for long lists, where only the first rows are worth animating).
  final bool animate;

  @override
  State<FadeUp> createState() => _FadeUpState();
}

class _FadeUpState extends State<FadeUp> with SingleTickerProviderStateMixin {
  static const _step = 45; // ms between neighbours
  static const _length = 380; // ms for one item
  AnimationController? _controller;
  late Animation<double> _curve;
  bool _started = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_started) return;
    _started = true;
    if (!widget.animate || Motion.reduced(context)) return;
    final delay = math.min(widget.index, 8) * _step;
    final total = delay + _length;
    final c = AnimationController(vsync: this, duration: Duration(milliseconds: total));
    _curve = CurvedAnimation(parent: c, curve: Interval(delay / total, 1, curve: Motion.enter));
    _controller = c..forward();
  }

  @override
  void dispose() {
    _controller?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final c = _controller;
    if (c == null) return widget.child;
    return AnimatedBuilder(
      animation: _curve,
      child: widget.child,
      builder: (context, child) => Opacity(
        opacity: _curve.value,
        child: Transform.translate(offset: Offset(0, (1 - _curve.value) * 14), child: child),
      ),
    );
  }
}

/// A number that counts up to its value, and counts from the old value to the new one when the figure changes.
class CountUp extends StatelessWidget {
  const CountUp({super.key, required this.value, required this.format, this.style, this.duration = const Duration(milliseconds: 900)});

  final double value;
  final String Function(double value) format;
  final TextStyle? style;
  final Duration duration;

  @override
  Widget build(BuildContext context) {
    return TweenAnimationBuilder<double>(
      tween: Tween<double>(begin: 0, end: value),
      duration: Motion.scaled(context, duration),
      curve: Motion.enter,
      builder: (context, v, _) => Text(format(v), style: style),
    );
  }
}

/// Shrinks a touch while a finger is down on it, then springs back. It only listens to the pointer, so the child keeps its own tap handling.
class PressScale extends StatefulWidget {
  const PressScale({super.key, required this.child, this.scale = 0.97});

  final Widget child;
  final double scale;

  @override
  State<PressScale> createState() => _PressScaleState();
}

class _PressScaleState extends State<PressScale> {
  bool _down = false;

  void _set(bool v) {
    if (_down != v && mounted) setState(() => _down = v);
  }

  @override
  Widget build(BuildContext context) {
    return Listener(
      onPointerDown: (_) => _set(true),
      onPointerUp: (_) => _set(false),
      onPointerCancel: (_) => _set(false),
      child: AnimatedScale(
        scale: _down && !Motion.reduced(context) ? widget.scale : 1,
        duration: Motion.scaled(context, Motion.fast),
        curve: Curves.easeOut,
        child: widget.child,
      ),
    );
  }
}

/// Shakes sideways once each time [trigger] goes up. For "that was wrong".
class ShakeX extends StatefulWidget {
  const ShakeX({super.key, required this.trigger, required this.child});

  final int trigger;
  final Widget child;

  @override
  State<ShakeX> createState() => _ShakeXState();
}

class _ShakeXState extends State<ShakeX> with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(vsync: this, duration: const Duration(milliseconds: 420));

  @override
  void didUpdateWidget(ShakeX old) {
    super.didUpdateWidget(old);
    if (widget.trigger != old.trigger && !Motion.reduced(context)) _c.forward(from: 0);
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: _c,
      child: widget.child,
      builder: (context, child) {
        final t = _c.value;
        return Transform.translate(offset: Offset(math.sin(t * math.pi * 5) * 9 * (1 - t), 0), child: child);
      },
    );
  }
}

/// A grey placeholder block that breathes while the real content loads. It stops after a few seconds, so an
/// endless wait does not keep the screen redrawing; the block then stays as a calm grey shape.
class SkeletonBox extends StatefulWidget {
  const SkeletonBox({super.key, this.height = 16, this.width, this.radius = 8});

  final double height;
  final double? width;
  final double radius;

  @override
  State<SkeletonBox> createState() => _SkeletonBoxState();
}

class _SkeletonBoxState extends State<SkeletonBox> with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(vsync: this, duration: const Duration(milliseconds: 900));
  int _flips = 0;

  @override
  void initState() {
    super.initState();
    _c.addStatusListener((status) {
      if (_flips >= 10 || !mounted) return;
      if (status == AnimationStatus.completed) {
        _flips++;
        _c.reverse();
      } else if (status == AnimationStatus.dismissed) {
        _flips++;
        _c.forward();
      }
    });
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (!Motion.reduced(context) && !_c.isAnimating && _flips == 0) _c.forward();
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: _c,
      builder: (context, _) => Container(
        width: widget.width,
        height: widget.height,
        decoration: BoxDecoration(
          color: Color.lerp(const Color(0xFFE3EAEB), const Color(0xFFF1F5F5), Curves.easeInOut.transform(_c.value)),
          borderRadius: BorderRadius.circular(widget.radius),
        ),
      ),
    );
  }
}

/// A round badge with a check mark that draws itself. Calls [onDone] when it has finished and held for a moment.
class AnimatedCheck extends StatefulWidget {
  const AnimatedCheck({super.key, this.size = 72, this.color = Tokens.success, this.onDone});

  final double size;
  final Color color;
  final VoidCallback? onDone;

  @override
  State<AnimatedCheck> createState() => _AnimatedCheckState();
}

class _AnimatedCheckState extends State<AnimatedCheck> with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(vsync: this, duration: const Duration(milliseconds: 1100));
  bool _started = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_started) return;
    _started = true;
    if (Motion.reduced(context)) _c.duration = const Duration(milliseconds: 500);
    _c.forward().whenComplete(() {
      if (mounted) widget.onDone?.call();
    });
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: _c,
      builder: (context, _) {
        final pop = Curves.easeOutBack.transform((_c.value / 0.35).clamp(0.0, 1.0));
        final draw = Curves.easeOut.transform(((_c.value - 0.2) / 0.4).clamp(0.0, 1.0));
        return Transform.scale(
          scale: pop,
          child: SizedBox(
            width: widget.size,
            height: widget.size,
            child: CustomPaint(painter: _CheckPainter(widget.color, draw)),
          ),
        );
      },
    );
  }
}

class _CheckPainter extends CustomPainter {
  _CheckPainter(this.color, this.progress);
  final Color color;
  final double progress;

  @override
  void paint(Canvas canvas, Size size) {
    final r = size.width / 2;
    canvas.drawCircle(Offset(r, r), r, Paint()..color = color.withValues(alpha: 0.14));
    canvas.drawCircle(Offset(r, r), r - 1.5, Paint()..color = color..style = PaintingStyle.stroke..strokeWidth = 3);

    final a = Offset(size.width * 0.28, size.height * 0.52);
    final b = Offset(size.width * 0.44, size.height * 0.68);
    final c = Offset(size.width * 0.74, size.height * 0.36);
    final path = Path()..moveTo(a.dx, a.dy);
    final firstLeg = (b - a).distance;
    final secondLeg = (c - b).distance;
    final drawn = progress * (firstLeg + secondLeg);
    if (drawn <= firstLeg) {
      final p = a + (b - a) * (drawn / firstLeg);
      path.lineTo(p.dx, p.dy);
    } else {
      path.lineTo(b.dx, b.dy);
      final p = b + (c - b) * ((drawn - firstLeg) / secondLeg);
      path.lineTo(p.dx, p.dy);
    }
    canvas.drawPath(
      path,
      Paint()
        ..color = color
        ..style = PaintingStyle.stroke
        ..strokeWidth = 5
        ..strokeCap = StrokeCap.round
        ..strokeJoin = StrokeJoin.round,
    );
  }

  @override
  bool shouldRepaint(_CheckPainter old) => old.progress != progress || old.color != color;
}

/// Shows the check in the middle of the screen until it has drawn, then closes itself. Use before leaving a form that just saved.
Future<void> showSavedCheck(BuildContext context, {String? label}) {
  Haptics.success();
  return showGeneralDialog<void>(
    context: context,
    barrierDismissible: false,
    barrierColor: Colors.black.withValues(alpha: 0.35),
    transitionDuration: Motion.scaled(context, Motion.fast),
    pageBuilder: (ctx, _, _) => PopScope(
      canPop: false,
      child: Center(
        child: Material(
          color: Colors.white,
          elevation: 6,
          borderRadius: BorderRadius.circular(20),
          child: Padding(
            padding: const EdgeInsets.fromLTRB(36, 28, 36, 24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                AnimatedCheck(onDone: () => Navigator.of(ctx).pop()),
                const SizedBox(height: 14),
                Text(label ?? ctx.tr('Sent', 'تم الإرسال'), style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700)),
              ],
            ),
          ),
        ),
      ),
    ),
    transitionBuilder: (ctx, anim, _, child) => FadeTransition(opacity: anim, child: child),
  );
}

/// Fades and nudges the active page in whenever [index] changes, while keeping every page alive underneath (an IndexedStack).
class TabStage extends StatefulWidget {
  const TabStage({super.key, required this.index, required this.children});

  final int index;
  final List<Widget> children;

  @override
  State<TabStage> createState() => _TabStageState();
}

class _TabStageState extends State<TabStage> with SingleTickerProviderStateMixin {
  late final AnimationController _c = AnimationController(vsync: this, duration: Motion.base, value: 1);

  @override
  void didUpdateWidget(TabStage old) {
    super.didUpdateWidget(old);
    if (old.index != widget.index && !Motion.reduced(context)) _c.forward(from: 0);
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final curve = CurvedAnimation(parent: _c, curve: Motion.enter);
    return AnimatedBuilder(
      animation: curve,
      child: IndexedStack(index: widget.index, children: widget.children),
      builder: (context, child) => Opacity(
        opacity: 0.35 + 0.65 * curve.value,
        child: Transform.translate(offset: Offset(0, (1 - curve.value) * 10), child: child),
      ),
    );
  }
}
