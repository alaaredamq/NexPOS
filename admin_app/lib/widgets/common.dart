import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';
import 'package:url_launcher/url_launcher.dart';

import '../api/admin_api.dart';
import '../api/models.dart';
import '../state/session.dart';
import '../theme.dart';
import '../util/format.dart';

enum Tone { ok, warn, bad, none, info }

(Color, Color) toneColors(Tone tone) => switch (tone) {
      Tone.ok => (Tokens.ok, Tokens.okBg),
      Tone.warn => (Tokens.warn, Tokens.warnBg),
      Tone.bad => (Tokens.bad, Tokens.badBg),
      Tone.info => (Tokens.navy, Tokens.navyLight),
      Tone.none => (Tokens.none, Tokens.noneBg),
    };

/// The same words and colours as the portal (Pills.Plan).
(Tone, String) planPill(PlanState state) => switch (state) {
      PlanState.active => (Tone.ok, 'Active'),
      PlanState.grace => (Tone.warn, 'Grace week'),
      PlanState.expired => (Tone.bad, 'Expired'),
      PlanState.suspended => (Tone.bad, 'Suspended'),
      PlanState.none => (Tone.none, 'No plan'),
    };

/// Pills.Health in the portal.
(Tone, String) healthPill(String health) => switch (health) {
      'Ok' => (Tone.ok, 'In contact'),
      'Stale' => (Tone.warn, 'Quiet'),
      'Down' => (Tone.bad, 'No contact'),
      _ => (Tone.none, 'Never connected'),
    };

Tone connectionTone(String connection) => switch (connection) {
      'Connected' => Tone.ok,
      'WaitingForShop' => Tone.info,
      'LinkExpired' || 'Disconnected' => Tone.warn,
      _ => Tone.none,
    };

/// LicenseRules.Pill in the portal.
Tone licenceTone(String status) => switch (status) {
      'Active' || 'Updates' => Tone.ok,
      'EndingSoon' || 'UpdatesEnding' => Tone.warn,
      'Grace' || 'Expired' => Tone.bad,
      _ => Tone.none,
    };

class Pill extends StatelessWidget {
  const Pill(this.text, {super.key, this.tone = Tone.none, this.dot = false});

  final String text;
  final Tone tone;
  final bool dot;

  @override
  Widget build(BuildContext context) {
    final (fg, bg) = toneColors(tone);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 3),
      decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(99)),
      child: Row(mainAxisSize: MainAxisSize.min, children: [
        if (dot) ...[Container(width: 6, height: 6, decoration: BoxDecoration(color: fg, shape: BoxShape.circle)), const SizedBox(width: 5)],
        Text(text, style: TextStyle(color: fg, fontSize: 12, fontWeight: FontWeight.w700)),
      ]),
    );
  }
}

/// A white card with a heading, the building block of every detail page.
class SectionCard extends StatelessWidget {
  const SectionCard({super.key, this.title, this.trailing, required this.children, this.padding = const EdgeInsets.all(16)});

  final String? title;
  final Widget? trailing;
  final List<Widget> children;
  final EdgeInsets padding;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: padding,
        child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          if (title != null || trailing != null)
            Padding(
              padding: const EdgeInsets.only(bottom: 10),
              child: Row(children: [
                if (title != null)
                  Expanded(child: Text(title!, style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700, color: Tokens.ink))),
                ?trailing,
              ]),
            ),
          ...children,
        ]),
      ),
    );
  }
}

/// "Label ........ value" on one line; the value wraps under it when long.
class InfoRow extends StatelessWidget {
  const InfoRow(this.label, this.value, {super.key, this.mono = false});

  final String label;
  final String value;
  final bool mono;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 5),
      child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
        SizedBox(width: 120, child: Text(label, style: const TextStyle(color: Tokens.muted, fontSize: 14))),
        Expanded(
          child: SelectableText(value.isEmpty ? '—' : value,
              style: TextStyle(fontSize: 14, color: Tokens.ink, fontFamily: mono ? 'monospace' : null, fontWeight: FontWeight.w500)),
        ),
      ]),
    );
  }
}

class MutedText extends StatelessWidget {
  const MutedText(this.text, {super.key, this.size = 14});
  final String text;
  final double size;

  @override
  Widget build(BuildContext context) => Text(text, style: TextStyle(color: Tokens.muted, fontSize: size, height: 1.35));
}

class EmptyState extends StatelessWidget {
  const EmptyState({super.key, required this.icon, required this.title, this.message, this.action});

  final IconData icon;
  final String title;
  final String? message;
  final Widget? action;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 48),
      child: Column(children: [
        Icon(icon, size: 44, color: Tokens.muted),
        const SizedBox(height: 12),
        Text(title, textAlign: TextAlign.center, style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700, color: Tokens.ink)),
        if (message != null) ...[const SizedBox(height: 6), Text(message!, textAlign: TextAlign.center, style: const TextStyle(color: Tokens.muted))],
        if (action != null) ...[const SizedBox(height: 16), action!],
      ]),
    );
  }
}

/// Loads something from the cloud and shows it, with a spinner first, the error and a retry when it fails, and pull-to-refresh.
/// [builder] gets a reload function to call after a change.
class Loader<T> extends StatefulWidget {
  const Loader({super.key, required this.load, required this.builder});

  final Future<T> Function(AdminApi api) load;
  final Widget Function(BuildContext context, T data, Future<void> Function() reload) builder;

  @override
  State<Loader<T>> createState() => LoaderState<T>();
}

class LoaderState<T> extends State<Loader<T>> {
  T? _data;
  Object? _error;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    reload();
  }

  Future<void> reload() async {
    final session = context.read<Session>();
    if (!session.signedIn) return;
    setState(() => _loading = _data == null);
    try {
      final data = await widget.load(session.api);
      if (!mounted) return;
      setState(() {
        _data = data;
        _error = null;
        _loading = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = e;
        _loading = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_loading && _data == null) return const Center(child: CircularProgressIndicator());
    if (_data == null) {
      return ListView(children: [
        EmptyState(
          icon: _error is OfflineException ? Icons.cloud_off_outlined : Icons.error_outline,
          title: _error is OfflineException ? 'No connection' : 'Could not load this',
          message: '$_error',
          action: FilledButton.icon(onPressed: reload, icon: const Icon(Icons.refresh), label: const Text('Try again')),
        ),
      ]);
    }
    return RefreshIndicator(
      onRefresh: reload,
      child: Column(children: [
        // A refresh that failed while the old figures are still on screen.
        if (_error != null)
          MaterialBanner(
            backgroundColor: Tokens.warnBg,
            content: Text('Not up to date: $_error', style: const TextStyle(color: Tokens.warn)),
            actions: [TextButton(onPressed: reload, child: const Text('Retry'))],
          ),
        Expanded(child: widget.builder(context, _data as T, reload)),
      ]),
    );
  }
}

/// Standard padding and spacing for a scrolling page of cards.
class PageList extends StatelessWidget {
  const PageList({super.key, required this.children, this.bottomPadding = 96});
  final List<Widget> children;
  final double bottomPadding;

  @override
  Widget build(BuildContext context) {
    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: EdgeInsets.fromLTRB(16, 8, 16, bottomPadding),
      children: [
        for (var i = 0; i < children.length; i++) ...[if (i > 0) const SizedBox(height: 12), children[i]],
      ],
    );
  }
}

void toast(BuildContext context, String message) {
  ScaffoldMessenger.of(context)
    ..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message), behavior: SnackBarBehavior.floating));
}

/// Runs a change against the cloud: a refusal is shown as its message, success as [done]. Returns whether it worked.
Future<bool> act(BuildContext context, Future<void> Function() work, {String? done}) async {
  try {
    await work();
    if (context.mounted && done != null) toast(context, done);
    return true;
  } on ApiException catch (e) {
    if (context.mounted) toast(context, e.message);
  } on OfflineException catch (e) {
    if (context.mounted) toast(context, '$e');
  }
  return false;
}

/// Asks before something that cannot be undone, or that a shop would notice.
Future<bool> confirm(BuildContext context, {required String title, required String message, required String yes, bool danger = false}) async {
  final ok = await showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      title: Text(title),
      content: Text(message),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
        FilledButton(
          style: danger ? FilledButton.styleFrom(backgroundColor: Tokens.bad) : null,
          onPressed: () => Navigator.pop(context, true),
          child: Text(yes),
        ),
      ],
    ),
  );
  return ok ?? false;
}

Future<void> openUrl(BuildContext context, String url) async {
  final ok = await launchUrl(Uri.parse(url), mode: LaunchMode.externalApplication).catchError((_) => false);
  if (!ok && context.mounted) toast(context, 'Could not open it on this phone.');
}

Future<void> copyText(BuildContext context, String text, {String done = 'Copied'}) async {
  await Clipboard.setData(ClipboardData(text: text));
  if (context.mounted) toast(context, done);
}

/// A link, key or password the cloud shows only once: copy it or send it on WhatsApp now.
Future<void> showShownOnce(BuildContext context, ShownOnce item) {
  return showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (context) => SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Text(item.title, style: const TextStyle(fontSize: 19, fontWeight: FontWeight.w700)),
          const SizedBox(height: 6),
          MutedText(item.note ?? 'Shown only now. Copy it or send it; if it is lost, make a new one.'),
          const SizedBox(height: 14),
          Container(
            padding: const EdgeInsets.all(14),
            decoration: BoxDecoration(color: Tokens.canvas, borderRadius: BorderRadius.circular(12), border: Border.all(color: Tokens.line)),
            child: SelectableText(item.text, style: const TextStyle(fontFamily: 'monospace', fontSize: 14.5, height: 1.4)),
          ),
          if (item.expiresAt != null) ...[const SizedBox(height: 8), MutedText('Works until ${formatDay(item.expiresAt)}.', size: 13)],
          const SizedBox(height: 16),
          Row(children: [
            Expanded(
              child: OutlinedButton.icon(onPressed: () => copyText(context, item.text), icon: const Icon(Icons.copy, size: 18), label: const Text('Copy')),
            ),
            if (item.whatsAppUrl != null) ...[
              const SizedBox(width: 10),
              Expanded(
                child: FilledButton.icon(
                  onPressed: () => openUrl(context, item.whatsAppUrl!),
                  icon: const Icon(Icons.send, size: 18),
                  label: const Text('WhatsApp'),
                ),
              ),
            ],
          ]),
        ]),
      ),
    ),
  );
}

/// The 24-hour contact strip: one bar per hour, lit when a shop PC was heard from.
class PulseStrip extends StatelessWidget {
  const PulseStrip(this.cells, {super.key, this.height = 22});
  final List<PulseCell> cells;
  final double height;

  @override
  Widget build(BuildContext context) {
    final most = cells.fold<int>(1, (m, c) => c.contacts > m ? c.contacts : m);
    return Semantics(
      label: '${cells.where((c) => c.contacts > 0).length} of the last 24 hours with contact',
      child: SizedBox(
        height: height,
        child: Row(crossAxisAlignment: CrossAxisAlignment.end, children: [
          for (final c in cells)
            Expanded(
              child: Container(
                margin: const EdgeInsets.symmetric(horizontal: 1),
                height: c.contacts == 0 ? 3 : 6 + (height - 6) * c.contacts / most,
                decoration: BoxDecoration(color: c.contacts == 0 ? Tokens.line : Tokens.data, borderRadius: BorderRadius.circular(2)),
              ),
            ),
        ]),
      ),
    );
  }
}

/// One "needs you" item; tapping it opens what it is about.
class AttentionTile extends StatelessWidget {
  const AttentionTile(this.item, {super.key, this.onTap});
  final Attention item;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final (fg, bg) = toneColors(item.bad ? Tone.bad : Tone.warn);
    return ListTile(
      onTap: onTap,
      contentPadding: const EdgeInsets.symmetric(horizontal: 4),
      leading: CircleAvatar(backgroundColor: bg, foregroundColor: fg, child: Icon(item.bad ? Icons.error_outline : Icons.schedule, size: 20)),
      title: Text(item.title, style: const TextStyle(fontWeight: FontWeight.w600)),
      subtitle: Text(item.reason),
      trailing: onTap == null ? null : Icon(Icons.chevron_right, color: Tokens.muted, semanticLabel: item.action),
    );
  }
}

/// A big number with a small label under it (the money and count tiles).
class StatTile extends StatelessWidget {
  const StatTile({super.key, required this.label, required this.value, this.note, this.color = Tokens.ink});
  final String label;
  final String value;
  final String? note;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(label, style: const TextStyle(color: Tokens.muted, fontSize: 13, fontWeight: FontWeight.w600)),
          const SizedBox(height: 6),
          FittedBox(
            fit: BoxFit.scaleDown,
            alignment: AlignmentDirectional.centerStart,
            child: Text(value, style: TextStyle(fontSize: 24, fontWeight: FontWeight.w800, color: color)),
          ),
          if (note != null) ...[const SizedBox(height: 2), Text(note!, style: const TextStyle(color: Tokens.muted, fontSize: 12))],
        ]),
      ),
    );
  }
}

/// Two tiles side by side.
class TileRow extends StatelessWidget {
  const TileRow(this.left, this.right, {super.key});
  final Widget left;
  final Widget right;

  @override
  Widget build(BuildContext context) =>
      IntrinsicHeight(child: Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [Expanded(child: left), const SizedBox(width: 12), Expanded(child: right)]));
}

extension SessionMoney on BuildContext {
  /// The provider's currency (from /me), so every amount reads the same.
  String money(double amount) => formatMoney(amount, read<Session>().currency);
}
