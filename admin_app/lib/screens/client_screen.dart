import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../api/admin_api.dart';
import '../api/models.dart';
import '../state/session.dart';
import '../theme.dart';
import '../util/format.dart';
import '../widgets/common.dart';
import 'client_form.dart';
import 'licence_screens.dart';
import 'login_sheets.dart';
import 'payment_forms.dart';
import 'site_screen.dart';

/// Everything about one client in one page, like the portal's client page.
class _ClientPage {
  _ClientPage(this.client, this.licences, this.payments, this.events);
  final ClientDetail client;
  final List<Licence> licences;
  final List<Payment> payments;
  final List<ClientEvent> events;
}

class ClientScreen extends StatefulWidget {
  const ClientScreen({super.key, required this.clientId});
  final int clientId;

  @override
  State<ClientScreen> createState() => _ClientScreenState();
}

class _ClientScreenState extends State<ClientScreen> {
  final _page = GlobalKey<LoaderState<_ClientPage>>();
  /// The loaded client, for the title and the app bar's menu (the page body has its own copy from the loader).
  ClientDetail? _client;

  AdminApi get _api => context.read<Session>().api;

  Future<_ClientPage> _load(AdminApi api) async {
    final id = widget.clientId;
    final (client, licences, payments, events) =
        await (api.client(id), api.clientLicences(id), api.clientPayments(id), api.events(id, take: 30)).wait;
    if (mounted) setState(() => _client = client);
    return _ClientPage(client, licences, payments, events);
  }

  Future<void> _reload() async => _page.currentState?.reload();

  /// Runs a change and reloads the page.
  Future<void> _do(Future<void> Function() work, String done) async {
    if (await act(context, work, done: done)) await _reload();
  }

  Future<void> _showLink(LinkResult? link, String clientName) async {
    if (link == null || !mounted) return;
    await showShownOnce(
      context,
      ShownOnce(
        title: 'Send this link to $clientName',
        text: link.url,
        expiresAt: link.expiresAt,
        whatsAppUrl: link.whatsAppUrl ??
            'https://wa.me/?text=${Uri.encodeComponent('Coduo POS Online: open Coduo POS > Settings > Online, paste this link and press Connect.\n${link.url}')}',
        note: 'They paste it in Coduo POS > Settings > Online on the shop PC.',
      ),
    );
  }

  Future<void> _menuAction(String action, ClientDetail c) async {
    switch (action) {
      case 'edit':
        if (await Navigator.push<bool>(context, MaterialPageRoute(builder: (_) => EditClientScreen(client: c))) == true) await _reload();
      case 'online-on':
        if (!await confirm(context, title: 'They bought Online?', message: c.sites.isEmpty ? 'Their first branch is added, with a connection link to send them.' : 'Their branches are followed again.', yes: 'Turn on')) return;
        if (!mounted) return;
        LinkResult? link;
        if (await act(context, () async => link = await _api.setOnline(c.id, true), done: 'Online turned on')) {
          await _reload();
          await _showLink(link, c.name);
        }
      case 'online-off':
        if (!await confirm(context, title: 'Make this client POS only?', message: 'For a shop that stopped using Online. Its branches and cloud data stay; the portal just stops asking about them.', yes: 'POS only')) return;
        if (!mounted) return;
        await _do(() => _api.setOnline(c.id, false), 'Now POS only');
      case 'add-pos':
        if (!await confirm(context, title: 'They bought the POS?', message: 'They become POS + Online, keeping their months and their menu. Their first branch is added, with a connection link to send them; issue their licence key as usual.', yes: 'Add the POS')) return;
        if (!mounted) return;
        LinkResult? link;
        if (await act(context, () async => link = await _api.addPos(c.id), done: 'Now POS + Online')) {
          await _reload();
          await _showLink(link, c.name);
        }
      case 'active':
        final off = c.isActive;
        if (!await confirm(context,
            title: off ? 'Deactivate this client?' : 'Reactivate this client?',
            message: off ? 'All their branches are blocked from the cloud and their online menu goes offline. Their shop keeps selling.' : 'Their branches can connect again.',
            yes: off ? 'Deactivate' : 'Reactivate',
            danger: off)) {
          return;
        }
        if (!mounted) return;
        await _do(() => _api.setClientActive(c.id, !off), off ? 'Client deactivated' : 'Client reactivated');
      case 'remove':
        final sites = c.sites.length;
        if (!await confirm(context,
            title: 'Remove ${c.name}?',
            message: '${sites == 0 ? '' : 'This also removes its $sites branch${sites == 1 ? '' : 'es'} and deletes their cloud copy of the data for good. '}'
                'The subscription and history go too; its payments stay in Finance. Nothing is deleted at the shop, and it keeps selling. This cannot be undone.',
            yes: 'Remove client',
            danger: true)) {
          return;
        }
        if (!mounted) return;
        if (await act(context, () => _api.removeClient(c.id), done: '${c.name} removed') && mounted) Navigator.pop(context);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(_client?.name ?? ''),
        actions: [
          Builder(builder: (context) {
            final c = _client;
            if (c == null) return const SizedBox.shrink();
            return PopupMenuButton<String>(
              tooltip: 'More',
              onSelected: (a) => _menuAction(a, c),
              itemBuilder: (_) => [
                const PopupMenuItem(value: 'edit', child: Text('Edit details')),
                if (c.type == ClientType.pos) const PopupMenuItem(value: 'online-on', child: Text('They bought Online')),
                if (c.type == ClientType.online) const PopupMenuItem(value: 'online-off', child: Text('Make POS only (no Online)')),
                if (c.type == ClientType.menu) const PopupMenuItem(value: 'add-pos', child: Text('They bought the POS')),
                PopupMenuItem(value: 'active', child: Text(c.isActive ? 'Deactivate client' : 'Reactivate client')),
                const PopupMenuItem(value: 'remove', child: Text('Remove client', style: TextStyle(color: Tokens.bad))),
              ],
            );
          }),
        ],
      ),
      body: Loader<_ClientPage>(
        key: _page,
        load: _load,
        builder: (context, page, reload) {
          final c = page.client;
          return PageList(children: [
            _Header(c),
            if (c.attention.isNotEmpty) SectionCard(title: 'Needs you', children: [for (final a in c.attention) AttentionTile(a)]),
            if (c.hasOnline) _OnlineCard(c, onChanged: _reload),
            if (c.type == ClientType.online) _BranchesCard(c, onChanged: _reload),
            if (c.type == ClientType.menu)
              SectionCard(
                title: 'Online menu only',
                children: [
                  const MutedText('No Coduo POS: no branch to connect and no licence key. The owner edits the menu on the owner website with their phone login.'),
                  const SizedBox(height: 12),
                  OutlinedButton(onPressed: () => _menuAction('add-pos', c), child: const Text('They bought the POS')),
                ],
              ),
            if (c.type == ClientType.pos)
              SectionCard(
                title: 'POS only',
                trailing: const Pill('offline'),
                children: [
                  const MutedText('This shop runs Coduo POS on its own PC without the cloud. There is nothing to connect; follow their licence and payments.'),
                  const SizedBox(height: 12),
                  OutlinedButton(onPressed: () => _menuAction('online-on', c), child: const Text('They bought Online')),
                ],
              ),
            if (c.hasPos || page.licences.isNotEmpty) _LicencesCard(c, page.licences, onChanged: _reload),
            _PaymentsCard(c, page.payments, onChanged: _reload),
            if (c.hasOnline) LoginsCard(c, onChanged: _reload),
            _HistoryCard(page.events),
          ]);
        },
      ),
    );
  }
}

class _Header extends StatelessWidget {
  const _Header(this.c);
  final ClientDetail c;

  @override
  Widget build(BuildContext context) {
    final contact = [?c.phone, ?c.email].where((x) => x.trim().isNotEmpty).join(' · ');
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Wrap(spacing: 6, runSpacing: 6, children: [
        Pill(c.type.label, tone: Tone.info),
        if (!c.isActive) const Pill('inactive', tone: Tone.bad),
      ]),
      const SizedBox(height: 8),
      Text(contact.isEmpty ? 'No contact details' : contact, style: const TextStyle(color: Tokens.muted)),
      if ((c.notes ?? '').trim().isNotEmpty) ...[const SizedBox(height: 4), Text(c.notes!, style: const TextStyle(color: Tokens.ink2))],
      if (c.phone != null && c.phone!.trim().isNotEmpty) ...[
        const SizedBox(height: 8),
        Wrap(spacing: 8, children: [
          OutlinedButton.icon(onPressed: () => openUrl(context, 'tel:${c.phone}'), icon: const Icon(Icons.call, size: 18), label: const Text('Call')),
          OutlinedButton.icon(
            onPressed: () => openUrl(context, 'https://wa.me/${c.phone!.replaceAll(RegExp(r'[^0-9]'), '')}'),
            icon: const Icon(Icons.chat_outlined, size: 18),
            label: const Text('WhatsApp'),
          ),
        ]),
      ],
    ]);
  }
}

/// The Online subscription: state, end, add months, suspend; and whether their online menu answers.
class _OnlineCard extends StatelessWidget {
  const _OnlineCard(this.c, {required this.onChanged});
  final ClientDetail c;
  final Future<void> Function() onChanged;

  Future<void> _extend(BuildContext context, int months) async {
    final api = context.read<Session>().api;
    if (!await confirm(context,
        title: 'Add $months month${months == 1 ? '' : 's'}?',
        message: 'Renewing early keeps the time left; renewing late starts today. Got paid? Record the payment too.',
        yes: 'Add $months')) {
      return;
    }
    if (!context.mounted) return;
    Subscription? sub;
    if (await act(context, () async => sub = await api.extend(c.id, months))) {
      if (context.mounted) toast(context, 'Added $months month${months == 1 ? '' : 's'}. Online now ends ${formatDay(sub?.expiresAt)}');
      await onChanged();
    }
  }

  Future<void> _suspend(BuildContext context, bool suspend) async {
    final api = context.read<Session>().api;
    final menuOnly = c.type == ClientType.menu;
    if (!await confirm(context,
        title: suspend ? 'Suspend Online?' : 'Resume Online?',
        message: suspend
            ? (menuOnly ? 'Their online menu goes offline until you resume.' : 'Their shop stops sending data to the cloud and their online menu goes offline. It keeps selling, and sends what it saved when you resume.')
            : (menuOnly ? 'Their online menu opens again.' : 'Their shop sends data again, and their online menu opens again.'),
        yes: suspend ? 'Suspend' : 'Resume',
        danger: suspend)) {
      return;
    }
    if (!context.mounted) return;
    if (await act(context, () => api.setSuspended(c.id, suspend), done: suspend ? 'Online suspended' : 'Online resumed')) await onChanged();
  }

  @override
  Widget build(BuildContext context) {
    final sub = c.subscription;
    final (tone, label) = planPill(c.state);
    return SectionCard(
      title: 'Online subscription',
      trailing: Pill(label, tone: tone, dot: true),
      children: [
        if (sub == null)
          MutedText(c.type == ClientType.menu ? 'No months yet: their online menu is offline. Add the months they paid for.' : 'No months yet: their shop cannot send data and their online menu is offline.')
        else ...[
          Text.rich(TextSpan(children: [
            const TextSpan(text: 'Ends ', style: TextStyle(fontSize: 22)),
            TextSpan(text: formatDay(sub.expiresAt), style: const TextStyle(fontSize: 22, fontWeight: FontWeight.w800)),
          ])),
          const SizedBox(height: 2),
          MutedText([
            until(sub.expiresAt),
            if (c.state == PlanState.grace) 'grace week until ${formatDay(sub.graceEndsAt)}',
            if (c.state == PlanState.expired) 'their online menu is offline',
          ].join(' · ')),
        ],
        const SizedBox(height: 10),
        Row(children: [
          Icon(c.menuLive ? Icons.restaurant_menu : Icons.cloud_off_outlined, size: 18, color: c.menuLive ? Tokens.ok : Tokens.bad),
          const SizedBox(width: 8),
          Expanded(child: Text(c.menuLive ? 'Online menu: on' : 'Online menu: offline', style: TextStyle(color: c.menuLive ? Tokens.ok : Tokens.bad, fontWeight: FontWeight.w600))),
          TextButton(
            onPressed: () => openUrl(context, '${context.read<Session>().cloudUrl}/owner/menu.html?client=${c.id}'),
            child: const Text('Menu editor'),
          ),
        ]),
        const SizedBox(height: 10),
        const Text('Add time', style: TextStyle(fontWeight: FontWeight.w600, color: Tokens.ink2, fontSize: 13)),
        const SizedBox(height: 6),
        Row(children: [
          for (final m in const [1, 3, 6, 12]) ...[
            Expanded(
              child: OutlinedButton(
                style: OutlinedButton.styleFrom(padding: const EdgeInsets.symmetric(horizontal: 4)),
                onPressed: () => _extend(context, m),
                child: Text('+$m mo', maxLines: 1),
              ),
            ),
            if (m != 12) const SizedBox(width: 8),
          ],
        ]),
        if (sub != null) ...[
          const SizedBox(height: 10),
          Align(
            alignment: AlignmentDirectional.centerStart,
            child: TextButton.icon(
              style: TextButton.styleFrom(foregroundColor: sub.suspended ? Tokens.navy : Tokens.bad),
              onPressed: () => _suspend(context, !sub.suspended),
              icon: Icon(sub.suspended ? Icons.play_arrow : Icons.pause, size: 18),
              label: Text(sub.suspended ? 'Resume Online' : 'Suspend Online'),
            ),
          ),
        ],
      ],
    );
  }
}

class _BranchesCard extends StatelessWidget {
  const _BranchesCard(this.c, {required this.onChanged});
  final ClientDetail c;
  final Future<void> Function() onChanged;

  Future<void> _add(BuildContext context) async {
    final result = await showDialog<(String, String)>(context: context, builder: (_) => const _SiteDialog());
    if (result == null || !context.mounted) return;
    final api = context.read<Session>().api;
    SiteDetail? added;
    if (await act(context, () async => added = await api.addSite(c.id, result.$1, machineId: result.$2), done: 'Branch added')) {
      await onChanged();
      if (added != null && context.mounted) {
        await Navigator.push(context, MaterialPageRoute<void>(builder: (_) => SiteScreen(siteId: added!.site.id, clientName: c.name)));
        await onChanged();
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return SectionCard(
      title: 'Branches',
      trailing: TextButton.icon(onPressed: () => _add(context), icon: const Icon(Icons.add, size: 18), label: const Text('Add')),
      children: [
        if (c.sites.isEmpty) const MutedText('No branch yet. Add one for each shop that has its own Coduo POS database. Extra PCs in the same shop do not need one.'),
        for (final s in c.sites)
          InkWell(
            borderRadius: BorderRadius.circular(10),
            onTap: () async {
              await Navigator.push(context, MaterialPageRoute<void>(builder: (_) => SiteScreen(siteId: s.id, clientName: c.name)));
              await onChanged();
            },
            child: Padding(
              padding: const EdgeInsets.symmetric(vertical: 8),
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Row(children: [
                  Expanded(child: Text(s.name, style: const TextStyle(fontWeight: FontWeight.w700))),
                  if (s.isDisabled) const Padding(padding: EdgeInsetsDirectional.only(end: 6), child: Pill('disabled', tone: Tone.bad)),
                  Pill(s.connectionLabel, tone: connectionTone(s.connection)),
                  const Icon(Icons.chevron_right, color: Tokens.muted),
                ]),
                const SizedBox(height: 2),
                Text([s.lastSeenAt == null ? 'Never in contact' : 'Last contact ${ago(s.lastSeenAt)}', ?s.machineId].join(' · '),
                    style: const TextStyle(color: Tokens.muted, fontSize: 13)),
                const SizedBox(height: 6),
                PulseStrip(s.pulse, height: 14),
              ]),
            ),
          ),
      ],
    );
  }
}

/// Name and (optional) Machine ID of a branch.
class _SiteDialog extends StatefulWidget {
  const _SiteDialog();

  @override
  State<_SiteDialog> createState() => _SiteDialogState();
}

class _SiteDialogState extends State<_SiteDialog> {
  final _name = TextEditingController();
  final _machine = TextEditingController();

  @override
  void dispose() {
    _name.dispose();
    _machine.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('Add a branch'),
      content: Column(mainAxisSize: MainAxisSize.min, children: [
        TextField(controller: _name, autofocus: true, decoration: const InputDecoration(labelText: 'Name', hintText: 'Second shop')),
        const SizedBox(height: 12),
        TextField(
          controller: _machine,
          textCapitalization: TextCapitalization.characters,
          decoration: const InputDecoration(labelText: 'Machine ID (optional)', hintText: 'XXXXX-XXXXX-XXXXX-XXXXX'),
        ),
      ]),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
        FilledButton(
          onPressed: () => _name.text.trim().isEmpty ? null : Navigator.pop(context, (_name.text.trim(), _machine.text.trim())),
          child: const Text('Add branch'),
        ),
      ],
    );
  }
}

class _LicencesCard extends StatelessWidget {
  const _LicencesCard(this.c, this.licences, {required this.onChanged});
  final ClientDetail c;
  final List<Licence> licences;
  final Future<void> Function() onChanged;

  @override
  Widget build(BuildContext context) {
    final current = licences.where((l) => l.status != 'Superseded').length;
    return SectionCard(
      title: 'POS licences',
      trailing: TextButton.icon(
        onPressed: () async {
          await Navigator.push(context, MaterialPageRoute<void>(builder: (_) => IssueLicenceScreen(clientId: c.id, clientName: c.name)));
          await onChanged();
        },
        icon: const Icon(Icons.key, size: 18),
        label: const Text('Issue a key'),
      ),
      children: [
        if (licences.isEmpty) const MutedText('No licence on record. Issue their key here, or record one you issued before on the Licences tab.'),
        if (licences.isNotEmpty && current != licences.length) MutedText('$current in force, ${licences.length - current} replaced by a newer key', size: 13),
        for (final l in licences)
          LicenceTile(l, onTap: () async {
            await Navigator.push(context, MaterialPageRoute<void>(builder: (_) => LicenceScreen(licenceId: l.id)));
            await onChanged();
          }),
      ],
    );
  }
}

class _PaymentsCard extends StatelessWidget {
  const _PaymentsCard(this.c, this.payments, {required this.onChanged});
  final ClientDetail c;
  final List<Payment> payments;
  final Future<void> Function() onChanged;

  /// What this client usually pays for, ticked to start with (the portal does the same).
  String get _usualPurpose => switch (c.type) {
        ClientType.pos => 'POS licence',
        ClientType.menu => 'Online menu',
        ClientType.online => 'Online subscription',
      };

  @override
  Widget build(BuildContext context) {
    return SectionCard(
      title: 'Payments',
      trailing: TextButton.icon(
        onPressed: () async {
          final saved = await Navigator.push<bool>(
              context, MaterialPageRoute(builder: (_) => PaymentFormScreen(clientId: c.id, clientName: c.name, purposes: [_usualPurpose])));
          if (saved == true) await onChanged();
        },
        icon: const Icon(Icons.add, size: 18),
        label: const Text('Record'),
      ),
      children: [
        if (payments.isEmpty)
          MutedText('Nothing recorded from ${c.name} yet.')
        else ...[
          Row(children: [
            Expanded(child: _Figure(context.money(c.paymentsTotal), 'collected in all')),
            Expanded(child: _Figure(context.money(c.paymentsLast12Months), 'last 12 months')),
          ]),
          const SizedBox(height: 8),
          for (final p in payments.take(10))
            ListTile(
              contentPadding: EdgeInsets.zero,
              title: Text(p.purpose, style: const TextStyle(fontWeight: FontWeight.w600)),
              subtitle: Text([formatDay(p.date), p.method, ?p.reference].join(' · ')),
              trailing: Text(context.money(p.amount), style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 15)),
              onTap: () async {
                final saved = await Navigator.push<bool>(context, MaterialPageRoute(builder: (_) => PaymentFormScreen(paymentId: p.id, clientName: c.name)));
                if (saved == true) await onChanged();
              },
            ),
          if (payments.length > 10) MutedText('and ${payments.length - 10} older (Finance has them all)', size: 13),
        ],
      ],
    );
  }
}

class _Figure extends StatelessWidget {
  const _Figure(this.value, this.label);
  final String value;
  final String label;

  @override
  Widget build(BuildContext context) => Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text(value, style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w800)),
        Text(label, style: const TextStyle(color: Tokens.muted, fontSize: 12.5)),
      ]);
}

class _HistoryCard extends StatefulWidget {
  const _HistoryCard(this.events);
  final List<ClientEvent> events;

  @override
  State<_HistoryCard> createState() => _HistoryCardState();
}

class _HistoryCardState extends State<_HistoryCard> {
  bool _all = false;

  @override
  Widget build(BuildContext context) {
    final shown = _all ? widget.events : widget.events.take(6).toList();
    return SectionCard(
      title: 'History',
      children: [
        if (widget.events.isEmpty) const MutedText('Nothing yet.'),
        for (final e in shown)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 5),
            child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
              SizedBox(width: 92, child: Text(formatDay(e.at), style: const TextStyle(color: Tokens.muted, fontSize: 12.5))),
              Expanded(child: Text(e.text, style: const TextStyle(fontSize: 13.5))),
            ]),
          ),
        if (!_all && widget.events.length > shown.length)
          Align(alignment: AlignmentDirectional.centerStart, child: TextButton(onPressed: () => setState(() => _all = true), child: const Text('Show more'))),
      ],
    );
  }
}
