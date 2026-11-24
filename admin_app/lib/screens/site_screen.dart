import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../api/admin_api.dart';
import '../api/models.dart';
import '../state/session.dart';
import '../theme.dart';
import '../util/format.dart';
import '../widgets/common.dart';

/// One branch (one shop PC): its connection, contact over 24 hours, the data the cloud holds, and what can be done to it.
class SiteScreen extends StatefulWidget {
  const SiteScreen({super.key, required this.siteId, required this.clientName});
  final String siteId;
  final String clientName;

  @override
  State<SiteScreen> createState() => _SiteScreenState();
}

class _SiteScreenState extends State<SiteScreen> {
  final _page = GlobalKey<LoaderState<SiteDetail>>();
  SiteDetail? _detail;

  Future<SiteDetail> _load(AdminApi api) async {
    final d = await api.site(widget.siteId);
    if (mounted) setState(() => _detail = d);
    return d;
  }

  Future<void> _reload() async => _page.currentState?.reload();

  Future<void> _newLink() async {
    final api = context.read<Session>().api;
    LinkResult? link;
    if (!await act(context, () async => link = await api.newLink(widget.siteId))) return;
    await _reload();
    if (!mounted || link == null) return;
    await showShownOnce(
      context,
      ShownOnce(
        title: 'Send this link to ${widget.clientName}',
        text: link!.url,
        expiresAt: link!.expiresAt,
        whatsAppUrl: link!.whatsAppUrl,
        note: 'They paste it in Coduo POS > Settings > Online on the shop PC. Older unused links stop working.',
      ),
    );
  }

  Future<void> _action(String action, Site s) async {
    final api = context.read<Session>().api;
    switch (action) {
      case 'rename':
        final result = await showDialog<(String, String)>(context: context, builder: (_) => _RenameDialog(s));
        if (result != null && mounted && await act(context, () => api.updateSite(s.id, result.$1, machineId: result.$2), done: 'Saved')) await _reload();
      case 'resync':
        if (!await confirm(context, title: 'Re-send everything?', message: 'At its next contact the shop PC sends all its data again. Use it when the cloud copy looks wrong.', yes: 'Re-send')) return;
        if (mounted && await act(context, () => api.resync(s.id), done: 'The shop PC re-sends everything at its next contact')) await _reload();
      case 'disconnect':
        if (!await confirm(context,
            title: 'Disconnect ${s.name}?',
            message: 'The shop PC stops syncing until it is linked again with a new link. Its cloud data stays. The shop keeps selling.',
            yes: 'Disconnect',
            danger: true)) {
          return;
        }
        if (mounted && await act(context, () => api.disconnect(s.id), done: 'Disconnected')) await _reload();
      case 'disable':
        final off = !s.isDisabled;
        if (!await confirm(context,
            title: off ? 'Disable ${s.name}?' : 'Enable ${s.name}?',
            message: off ? 'The cloud refuses this branch until you enable it again. The shop keeps selling and queues its changes.' : 'This branch can sync again.',
            yes: off ? 'Disable' : 'Enable',
            danger: off)) {
          return;
        }
        if (mounted && await act(context, () => api.setSiteDisabled(s.id, off), done: off ? 'Branch disabled' : 'Branch enabled')) await _reload();
      case 'remove':
        if (!await confirm(context,
            title: 'Remove ${s.name}?',
            message: '${s.connected ? 'This branch is connected. Its cloud copy of the data is deleted for good and it stops syncing. The shop keeps selling and everything stays on its PC. ' : 'Its cloud copy of the data is deleted for good. '}This cannot be undone.',
            yes: 'Remove branch',
            danger: true)) {
          return;
        }
        if (mounted && await act(context, () => api.removeSite(s.id), done: 'Branch removed') && mounted) Navigator.pop(context);
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = _detail?.site;
    return Scaffold(
      appBar: AppBar(
        title: Text(s?.name ?? 'Branch'),
        actions: [
          if (s != null)
            PopupMenuButton<String>(
              tooltip: 'More',
              onSelected: (a) => _action(a, s),
              itemBuilder: (_) => [
                const PopupMenuItem(value: 'rename', child: Text('Rename or set Machine ID')),
                if (s.connected) const PopupMenuItem(value: 'resync', child: Text('Re-send everything')),
                if (s.connected) const PopupMenuItem(value: 'disconnect', child: Text('Disconnect')),
                PopupMenuItem(value: 'disable', child: Text(s.isDisabled ? 'Enable branch' : 'Disable branch')),
                const PopupMenuItem(value: 'remove', child: Text('Remove branch', style: TextStyle(color: Tokens.bad))),
              ],
            ),
        ],
      ),
      body: Loader<SiteDetail>(
        key: _page,
        load: _load,
        builder: (context, d, reload) {
          final s = d.site;
          final (hTone, hLabel) = healthPill(s.health);
          return PageList(children: [
            Text(widget.clientName, style: const TextStyle(color: Tokens.muted)),
            SectionCard(
              title: 'Connection',
              trailing: Pill(s.connectionLabel, tone: connectionTone(s.connection), dot: true),
              children: [
                MutedText(s.connectionHelp),
                if (s.isDisabled) ...[const SizedBox(height: 8), const Pill('disabled: the cloud refuses it', tone: Tone.bad)],
                if (d.openLinkExpiresAt != null) ...[const SizedBox(height: 8), MutedText('An unused link works until ${formatDay(d.openLinkExpiresAt)}.', size: 13)],
                const SizedBox(height: 12),
                FilledButton.icon(
                  onPressed: _newLink,
                  icon: const Icon(Icons.link),
                  label: Text(s.connected ? 'New link (to move to another PC)' : 'Make a link to send'),
                ),
              ],
            ),
            SectionCard(
              title: 'Contact',
              trailing: Pill(hLabel, tone: hTone),
              children: [
                InfoRow('Last contact', s.lastSeenAt == null ? 'never' : '${ago(s.lastSeenAt)} · ${formatTime(s.lastSeenAt)}'),
                InfoRow('Last data', s.lastSyncAt == null ? 'never' : '${ago(s.lastSyncAt)} · ${formatTime(s.lastSyncAt)}'),
                if (s.resyncRequested) const InfoRow('Re-send', 'asked; happens at the next contact'),
                const SizedBox(height: 8),
                const Text('Last 24 hours', style: TextStyle(color: Tokens.muted, fontSize: 13)),
                const SizedBox(height: 6),
                PulseStrip(s.pulse, height: 30),
              ],
            ),
            SectionCard(
              title: 'The shop PC',
              children: [
                InfoRow('Machine ID', s.machineId ?? '', mono: true),
                InfoRow('Licence plan', s.shopPlan ?? 'not reported yet'),
                if (s.licenseKind != null)
                  InfoRow('Licence', [s.licenseKind!, if (s.licenseUntil != null) 'until ${formatDay(s.licenseUntil)}', if ((s.seats ?? 1) > 1) '${s.seats} PCs'].join(' · ')),
              ],
            ),
            SectionCard(
              title: 'Data in the cloud',
              children: [
                if (!d.held)
                  const MutedText('Nothing yet: the shop PC has not sent its data.')
                else ...[
                  if (d.encrypted) const MutedText('End-to-end encrypted: only the owner\'s phone can read it. Counts only here.', size: 13),
                  for (final e in d.counts.entries) InfoRow(e.key, '${e.value}'),
                ],
              ],
            ),
            if (d.syncLog.isNotEmpty)
              SectionCard(
                title: 'Recent syncs',
                children: [
                  for (final l in d.syncLog.take(10))
                    Padding(
                      padding: const EdgeInsets.symmetric(vertical: 4),
                      child: Row(children: [
                        Icon(l.outcome == 'applied' ? Icons.check_circle : Icons.error_outline, size: 16, color: l.outcome == 'applied' ? Tokens.ok : Tokens.bad),
                        const SizedBox(width: 8),
                        Expanded(child: Text('${formatTime(l.receivedAt)} · ${l.changes} change${l.changes == 1 ? '' : 's'}${l.error == null ? '' : ' · ${l.error}'}', style: const TextStyle(fontSize: 13))),
                      ]),
                    ),
                ],
              ),
            if (d.tokens.isNotEmpty)
              SectionCard(title: 'Device token', children: [
                for (final t in d.tokens) InfoRow('${t.prefix}...', 'made ${formatDay(t.createdAt)}${t.lastUsedAt == null ? '' : ' · used ${ago(t.lastUsedAt)}'}'),
              ]),
          ]);
        },
      ),
    );
  }
}

class _RenameDialog extends StatefulWidget {
  const _RenameDialog(this.site);
  final Site site;

  @override
  State<_RenameDialog> createState() => _RenameDialogState();
}

class _RenameDialogState extends State<_RenameDialog> {
  late final _name = TextEditingController(text: widget.site.name);
  late final _machine = TextEditingController(text: widget.site.machineId);

  @override
  void dispose() {
    _name.dispose();
    _machine.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('Branch'),
      content: Column(mainAxisSize: MainAxisSize.min, children: [
        TextField(controller: _name, decoration: const InputDecoration(labelText: 'Name')),
        const SizedBox(height: 12),
        TextField(controller: _machine, textCapitalization: TextCapitalization.characters, decoration: const InputDecoration(labelText: 'Machine ID')),
      ]),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
        FilledButton(onPressed: () => Navigator.pop(context, (_name.text.trim(), _machine.text.trim())), child: const Text('Save')),
      ],
    );
  }
}
