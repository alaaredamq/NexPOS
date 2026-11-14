import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../l10n.dart';
import '../state/app_lock.dart';
import '../state/session.dart';
import '../theme.dart';
import '../widgets/decor.dart';
import '../widgets/motion.dart';

/// Puts the lock screen over the whole app (above every page, dialogs included) while the app is locked. The pages underneath stay
/// alive, so unlocking returns to exactly where the owner was.
class LockGate extends StatelessWidget {
  const LockGate({super.key, required this.child});
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final lock = context.watch<AppLock>();
    final signedIn = context.watch<Session>().signedIn;
    final show = lock.locked && signedIn;
    return Stack(
      children: [
        // Not painted while locked, so nothing of the shop is behind the lock screen even for a frame.
        Offstage(offstage: show, child: TickerMode(enabled: !show, child: child)),
        if (show) const Positioned.fill(child: LockScreen()),
      ],
    );
  }
}

class LockScreen extends StatefulWidget {
  const LockScreen({super.key});

  @override
  State<LockScreen> createState() => _LockScreenState();
}

class _LockScreenState extends State<LockScreen> {
  bool _asking = false;

  @override
  void initState() {
    super.initState();
    // Ask straight away: most of the time the owner just wants in.
    WidgetsBinding.instance.addPostFrameCallback((_) => _unlock());
  }

  Future<void> _unlock() async {
    if (_asking || !mounted) return;
    final lock = context.read<AppLock>();
    final reason = context.tr('Unlock NexPOS Owner', 'افتح NexPOS للمالك');
    setState(() => _asking = true);
    final ok = await lock.unlock(reason);
    if (!ok) Haptics.problem();
    if (mounted) setState(() => _asking = false);
  }

  @override
  Widget build(BuildContext context) {
    return Material(
      color: Tokens.nav,
      child: Stack(
        children: [
          Positioned.fill(child: IgnorePointer(child: CustomPaint(painter: OrbitPainter(alignEnd: !context.isArabic, strength: 0.14)))),
          SafeArea(
            child: Center(
              child: Padding(
                padding: const EdgeInsets.all(32),
                child: FadeUp(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Container(
                        width: 72,
                        height: 72,
                        decoration: BoxDecoration(
                          gradient: const LinearGradient(begin: Alignment.topLeft, end: Alignment.bottomRight, colors: [Color(0xFF0FA3B0), Tokens.primary]),
                          borderRadius: BorderRadius.circular(20),
                          boxShadow: [BoxShadow(color: Tokens.primary.withValues(alpha: 0.45), blurRadius: 22, offset: const Offset(0, 8))],
                        ),
                        child: const Icon(Icons.lock_outline, color: Colors.white, size: 34),
                      ),
                      const SizedBox(height: 22),
                      Text(context.tr('NexPOS is locked', 'NexPOS مقفل'),
                          style: const TextStyle(color: Colors.white, fontSize: 24, fontWeight: FontWeight.w700), textAlign: TextAlign.center),
                      const SizedBox(height: 8),
                      Text(
                        context.tr('Use your fingerprint, face or phone PIN to open your shop.', 'استخدم بصمتك أو وجهك أو رمز الهاتف لفتح محلك.'),
                        style: const TextStyle(color: Color(0xFFA9BABC), fontSize: 15, height: 1.5),
                        textAlign: TextAlign.center,
                      ),
                      const SizedBox(height: 28),
                      FilledButton.icon(
                        onPressed: _asking ? null : _unlock,
                        icon: const Icon(Icons.fingerprint),
                        label: Text(context.tr('Unlock', 'فتح')),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
