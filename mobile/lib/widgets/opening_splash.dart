import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../theme.dart';
import 'motion.dart';

/// The opening screen ("Peek-a-boo"): only the two eyes show at first. They blink and look around,
/// then the monitor draws itself around them, the stand and base grow in, and the right eye winks.
/// Plays once when the app starts, then fades away. A tap skips it.
/// The app is already built underneath, so its data starts loading while the logo plays.
/// Nothing plays when the phone asks for reduced motion.
class OpeningSplash extends StatefulWidget {
  const OpeningSplash({super.key, required this.child, this.enabled = true});

  final Widget child;

  /// False shows the app at once (tests use this).
  final bool enabled;

  /// True while the logo still covers the app. The lock screen waits for this,
  /// so its fingerprint prompt does not pop up over the logo.
  static bool isPlaying(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<_SplashScope>()?.playing ?? false;

  @override
  State<OpeningSplash> createState() => _OpeningSplashState();
}

class _OpeningSplashState extends State<OpeningSplash> with SingleTickerProviderStateMixin {
  static const _fadeStartMs = LogoTimeline.endMs + 250; // rest a moment on the finished logo
  static const _totalMs = _fadeStartMs + 300;

  late final AnimationController _c = AnimationController(vsync: this, duration: const Duration(milliseconds: _totalMs));
  bool _covering = true; // the logo still hides the app
  bool _gone = false; // the overlay is removed
  bool _decided = false;

  double get _ms => _c.value * _totalMs;

  @override
  void initState() {
    super.initState();
    if (!widget.enabled) {
      _covering = false;
      _gone = true;
      _decided = true;
      return;
    }
    _c.addListener(_tick);
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (_decided) return;
    _decided = true;
    if (Motion.reduced(context)) {
      _covering = false;
      _gone = true;
    } else {
      _c.forward();
    }
  }

  void _tick() {
    final covering = _ms < _fadeStartMs;
    final gone = _c.isCompleted;
    if (covering != _covering || gone != _gone) setState(() { _covering = covering; _gone = gone; });
  }

  void _skip() {
    if (_ms < _fadeStartMs) _c.forward(from: _fadeStartMs / _totalMs);
  }

  @override
  void dispose() {
    _c.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return _SplashScope(
      playing: _covering,
      child: Stack(
        fit: StackFit.expand, // the app keeps the full screen, as without this widget
        children: [
          widget.child,
          if (!_gone)
            Positioned.fill(
              child: IgnorePointer(
                ignoring: !_covering,
                child: AnimatedBuilder(
                  animation: _c,
                  builder: (context, _) {
                    final fade = ((_ms - _fadeStartMs) / (_totalMs - _fadeStartMs)).clamp(0.0, 1.0);
                    return Opacity(
                      opacity: 1 - Curves.easeOut.transform(fade),
                      child: _SplashFace(frame: LogoTimeline.at(_ms), onTap: _skip),
                    );
                  },
                ),
              ),
            ),
        ],
      ),
    );
  }
}

class _SplashScope extends InheritedWidget {
  const _SplashScope({required this.playing, required super.child});
  final bool playing;

  @override
  bool updateShouldNotify(_SplashScope old) => old.playing != playing;
}

/// Navy screen, the logo, and the wordmark under it.
class _SplashFace extends StatelessWidget {
  const _SplashFace({required this.frame, required this.onTap});
  final LogoFrame frame;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return AnnotatedRegion<SystemUiOverlayStyle>(
      value: SystemUiOverlayStyle.light,
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: ColoredBox(
          color: Tokens.primary,
          child: LayoutBuilder(
            builder: (context, box) {
              // One "unit" is one step of the logo drawing; the drawing is 120 x 132 units.
              final u = (box.maxWidth * 0.66 / 120).clamp(1.2, 2.6);
              return Center(
                child: Semantics(
                  label: 'Coduo POS',
                  child: SizedBox(
                    width: 120 * u,
                    height: 132 * u,
                    child: Stack(
                      clipBehavior: Clip.none,
                      children: [
                        Positioned.fill(child: CustomPaint(painter: LogoPainter(frame: frame, unit: u))),
                        Positioned(
                          left: 23 * u,
                          width: 74 * u,
                          top: (96 + frame.wordLift) * u,
                          child: Opacity(
                            opacity: frame.wordOpacity,
                            child: Image.asset(
                              'assets/brand/coduo-pos-wordmark-on-navy.png',
                              filterQuality: FilterQuality.medium,
                              excludeFromSemantics: true,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}

/// Where every part of the logo is at one moment.
class LogoFrame {
  const LogoFrame({
    this.leftEye = 1,
    this.rightEye = 1,
    this.blink = 1,
    this.wink = 1,
    this.lookX = 0,
    this.lookY = 0,
    this.monitor = 1,
    this.stand = 1,
    this.base = 1,
    this.wordOpacity = 1,
    this.wordLift = 0,
  });

  final double leftEye, rightEye; // eye size while popping in: 0 hidden, 1 full
  final double blink; // both eyes' height: 1 open, 0.1 closed
  final double wink; // right eye's height: 1 open, 0.1 closed
  final double lookX, lookY; // where the eyes look, in units
  final double monitor; // how much of the screen outline is drawn, 0 to 1
  final double stand; // stand height, growing up from the base: 0 to 1
  final double base; // base width, growing out from the middle: 0 to 1
  final double wordOpacity;
  final double wordLift; // units the wordmark sits below its place

  static const rest = LogoFrame();
}

/// The "Peek-a-boo" timeline, in milliseconds. Same timings as option D in the preview page.
class LogoTimeline {
  static const endMs = 3190;

  static const _back = Cubic(0.34, 1.56, 0.64, 1); // a small overshoot
  static const _out = Cubic(0.2, 0.8, 0.2, 1);

  static double _p(double ms, double start, double length) => ((ms - start) / length).clamp(0.0, 1.0);

  static LogoFrame at(double ms) {
    // The eyes pop in, the right one a moment later: 0–430 ms.
    final left = _back.transform(_p(ms, 0, 350));
    final right = _back.transform(_p(ms, 80, 350));
    // Both eyes blink: 500–740 ms.
    const blink = _Keys([0, .45, 1]);
    // They look left and down a little, then right: 800–1600 ms.
    const look = _Keys([0, .25, .45, .7, 1]);
    final l = Curves.easeInOut.transform(_p(ms, 800, 800));
    // The screen outline draws itself around the eyes: 1500–2200 ms.
    final monitor = Curves.easeInOut.transform(_p(ms, 1500, 700));
    // The stand grows up under the screen, then the base opens out: 2050–2500 ms.
    final stand = _out.transform(_p(ms, 2050, 250));
    final base = _back.transform(_p(ms, 2200, 300));
    // The wordmark rises in: 2250–2700 ms.
    final word = _out.transform(_p(ms, 2250, 450));
    // The right eye winks: 2550–3190 ms (closes fast, holds, opens).
    const wink = _Keys([0, 90 / 640, 510 / 640, 1]);

    return LogoFrame(
      leftEye: left,
      rightEye: right,
      blink: blink.value(_p(ms, 500, 240), const [1, .1, 1]),
      lookX: look.value(l, const [0, -4, -4, 4, 0]),
      lookY: look.value(l, const [0, 2, 2, -1, 0]),
      monitor: monitor,
      stand: stand,
      base: base,
      wink: wink.value(_p(ms, 2550, 640), const [1, .1, .1, 1]),
      wordOpacity: word,
      wordLift: 8 * (1 - word),
    );
  }
}

/// Keyframes: stops between 0 and 1, with straight-line movement between them.
class _Keys {
  const _Keys(this.stops);
  final List<double> stops;

  double value(double p, List<double> values) {
    for (var i = 1; i < stops.length; i++) {
      if (p <= stops[i]) {
        final span = stops[i] - stops[i - 1];
        final local = span == 0 ? 1.0 : ((p - stops[i - 1]) / span).clamp(0.0, 1.0);
        return values[i - 1] + (values[i] - values[i - 1]) * local;
      }
    }
    return values.last;
  }
}

/// Draws the Coduo POS mark (monitor, two eyes, stand, base) as in the brand kit's "on navy" version.
class LogoPainter extends CustomPainter {
  LogoPainter({required this.frame, required this.unit});

  final LogoFrame frame;
  final double unit;

  static const _monitor = Color(0xFFFFFFFF);
  static const _eyes = Color(0xFF3DDBCB);

  /// The screen outline, starting at the top-left and going clockwise, so it can be drawn bit by bit.
  static final Path _outline = () {
    const r = Radius.circular(10);
    return Path()
      ..moveTo(-21, -30.5)
      ..lineTo(21, -30.5)
      ..arcToPoint(const Offset(31, -20.5), radius: r)
      ..lineTo(31, 5.5)
      ..arcToPoint(const Offset(21, 15.5), radius: r)
      ..lineTo(-21, 15.5)
      ..arcToPoint(const Offset(-31, 5.5), radius: r)
      ..lineTo(-31, -20.5)
      ..arcToPoint(const Offset(-21, -30.5), radius: r)
      ..close();
  }();

  @override
  void paint(Canvas canvas, Size size) {
    canvas.save();
    canvas.scale(unit);
    // Same layout as the preview: the drawing's centre sits 60 units in and 48 units down.
    canvas.translate(60, 48);

    final solid = Paint()..color = _monitor;
    final outline = Paint()
      ..color = _monitor
      ..style = PaintingStyle.stroke
      ..strokeWidth = 8;
    if (frame.monitor >= 1) {
      canvas.drawPath(_outline, outline);
    } else if (frame.monitor > 0) {
      final m = _outline.computeMetrics().first;
      canvas.drawPath(m.extractPath(0, m.length * frame.monitor), outline);
    }
    if (frame.stand > 0) {
      final h = 12 * frame.stand; // grows from the base upwards
      canvas.drawRect(Rect.fromLTWH(-6, 27.5 - h, 12, h), solid);
    }
    if (frame.base > 0) {
      final w = 21 * frame.base; // opens out from the middle
      canvas.drawRRect(RRect.fromLTRBR(-w, 26.5, w, 34.5, Radius.circular(4 * frame.base.clamp(0.0, 1.0))), solid);
    }

    final eye = Paint()..color = _eyes;
    for (final (x, size, height) in [(-9.0, frame.leftEye, frame.blink), (9.0, frame.rightEye, frame.blink * frame.wink)]) {
      if (size <= 0) continue;
      canvas.save();
      canvas.translate(x + frame.lookX, -8.5 + frame.lookY);
      canvas.scale(size, size * height);
      canvas.drawCircle(Offset.zero, 5.5, eye);
      canvas.restore();
    }
    canvas.restore();
  }

  @override
  bool shouldRepaint(LogoPainter old) => old.frame != frame || old.unit != unit;
}
