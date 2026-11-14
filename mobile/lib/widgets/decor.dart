import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../theme.dart';

/// Thin concentric rings from a top corner, like a radar or a till display. Drawn in the brand teal at low strength so text on top stays readable.
class OrbitPainter extends CustomPainter {
  const OrbitPainter({this.alignEnd = true, this.strength = 0.12});

  /// Which top corner the rings radiate from (the end side, so it follows Arabic's right-to-left).
  final bool alignEnd;
  final double strength;

  static double _radius(int i) => 70.0 * i + i * i * 4;

  @override
  void paint(Canvas canvas, Size size) {
    final centre = Offset(alignEnd ? size.width : 0, 0);
    final ring = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.2;
    for (var i = 1; i <= 5; i++) {
      ring.color = Tokens.readout.withValues(alpha: (1 - i / 6.5) * strength);
      canvas.drawCircle(centre, _radius(i), ring);
    }
    // A few dots on the rings, so it reads as designed rather than as a pattern fill.
    final dot = Paint()..color = Tokens.readout.withValues(alpha: strength * 2.2);
    final side = alignEnd ? -1.0 : 1.0;
    for (final (angle, ring) in const [(0.5, 2), (1.05, 3), (1.35, 4)]) {
      final r = _radius(ring);
      canvas.drawCircle(centre + Offset(side * r * math.cos(angle), r * math.sin(angle)), 3, dot);
    }
  }

  @override
  bool shouldRepaint(OrbitPainter old) => old.alignEnd != alignEnd || old.strength != strength;
}
