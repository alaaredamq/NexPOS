import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../api/admin_api.dart';
import '../api/models.dart';
import '../state/session.dart';
import '../theme.dart';
import '../util/format.dart';
import '../widgets/common.dart';
import 'payment_forms.dart' show DayField;

/// One licence in a list: Machine ID, plan and kind, and where it stands.
class LicenceTile extends StatelessWidget {
  const LicenceTile(this.l, {super.key, this.onTap, this.showClient = false});
  final Licence l;
  final VoidCallback? onTap;
  final bool showClient;

  @override
  Widget build(BuildContext context) {
    return ListTile(
      contentPadding: EdgeInsets.zero,
      onTap: onTap,
      title: Text(showClient ? (l.clientName ?? '${l.customer} · not linked') : l.machineId,
          style: TextStyle(fontWeight: FontWeight.w600, fontFamily: showClient ? null : 'monospace', fontSize: showClient ? 15 : 13.5)),
      subtitle: Text([
        if (showClient) l.machineId,
        '${l.plan} · ${l.kind}${l.seats > 1 ? ' · ${l.seats} PCs' : ''}',
        '${l.timeLimited ? 'ends' : 'updates until'} ${formatDay(l.validUntil)}',
      ].join('\n')),
      isThreeLine: showClient,
      trailing: Pill(l.statusLabel, tone: licenceTone(l.status)),
    );
  }
}

/// Picks a client (or none) from the full list.
Future<(int?, String?)?> pickClient(BuildContext context, {bool allowNone = true}) async {
  final api = context.read<Session>().api;
  final clients = await api.clients();
  if (!context.mounted) return null;
  return showModalBottomSheet<(int?, String?)>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (context) => DraggableScrollableSheet(
      expand: false,
      initialChildSize: 0.7,
      builder: (context, scroll) => ListView(controller: scroll, children: [
        if (allowNone) ListTile(leading: const Icon(Icons.link_off), title: const Text('No client'), onTap: () => Navigator.pop(context, (null, null))),
        for (final c in clients)
          ListTile(
            leading: const Icon(Icons.storefront_outlined),
            title: Text(c.name),
            subtitle: Text(c.type.label),
            onTap: () => Navigator.pop(context, (c.id, c.name)),
          ),
      ]),
    ),
  );
}

class LicenceScreen extends StatefulWidget {
  const LicenceScreen({super.key, required this.licenceId});
  final int licenceId;

  @override
  State<LicenceScreen> createState() => _LicenceScreenState();
}

class _LicenceScreenState extends State<LicenceScreen> {
  final _page = GlobalKey<LoaderState<Licence>>();

  AdminApi get _api => context.read<Session>().api;

  Future<void> _link(Licence l) async {
    final picked = await pickClient(context);
    if (picked == null || !mounted) return;
    if (await act(context, () => _api.updateLicence(l.id, clientId: picked.$1, note: l.note), done: picked.$1 == null ? 'Unlinked' : 'Linked to ${picked.$2}')) {
      await _page.currentState?.reload();
    }
  }

  Future<void> _note(Licence l) async {
    final controller = TextEditingController(text: l.note);
    final note = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Note'),
        content: TextField(controller: controller, autofocus: true, maxLines: 3, minLines: 1),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(context, controller.text.trim()), child: const Text('Save')),
        ],
      ),
    );
    controller.dispose();
    if (note == null || !mounted) return;
    if (await act(context, () => _api.updateLicence(l.id, clientId: l.clientId, note: note), done: 'Saved')) await _page.currentState?.reload();
  }

  Future<void> _delete(Licence l) async {
    if (!await confirm(context,
        title: 'Delete this record?',
        message: 'Only the record in the cloud goes. The key keeps working at the shop: offline keys cannot be revoked.',
        yes: 'Delete',
        danger: true)) {
      return;
    }
    if (mounted && await act(context, () => _api.deleteLicence(l.id), done: 'Record deleted') && mounted) Navigator.pop(context);
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Licence')),
      body: Loader<Licence>(
        key: _page,
        load: (api) => api.licence(widget.licenceId),
        builder: (context, l, reload) => PageList(children: [
          SectionCard(
            title: '${l.plan} · ${l.kind}',
            trailing: Pill(l.statusLabel, tone: licenceTone(l.status), dot: true),
            children: [
              InfoRow('Client', l.clientName ?? 'not linked'),
              InfoRow('Name in the key', l.customer),
              InfoRow('Machine ID', l.machineId, mono: true),
              InfoRow('PCs', '${l.seats}'),
              InfoRow('Issued', formatDay(l.issuedOn)),
              InfoRow(l.timeLimited ? 'Ends' : 'Updates until', '${formatDay(l.validUntil)} · ${until(l.validUntil)}'),
              InfoRow('Serial', l.serial, mono: true),
              InfoRow('Came from', l.source),
              if (l.note != null) InfoRow('Note', l.note!),
            ],
          ),
          SectionCard(
            title: 'The key',
            children: [
              if (l.key == null)
                const MutedText('Only the details are known (imported, or seen at the shop PC). The key text was not kept.')
              else ...[
                Container(
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(color: Tokens.canvas, borderRadius: BorderRadius.circular(10), border: Border.all(color: Tokens.line)),
                  child: SelectableText(l.key!, style: const TextStyle(fontFamily: 'monospace', fontSize: 12.5)),
                ),
                const SizedBox(height: 10),
                Row(children: [
                  Expanded(child: OutlinedButton.icon(onPressed: () => copyText(context, l.key!, done: 'Key copied'), icon: const Icon(Icons.copy, size: 18), label: const Text('Copy'))),
                  const SizedBox(width: 10),
                  Expanded(
                    child: FilledButton.icon(
                      onPressed: () => openUrl(context, 'https://wa.me/?text=${Uri.encodeComponent(licenceMessage(l))}'),
                      icon: const Icon(Icons.send, size: 18),
                      label: const Text('WhatsApp'),
                    ),
                  ),
                ]),
              ],
            ],
          ),
          SectionCard(children: [
            ListTile(contentPadding: EdgeInsets.zero, leading: const Icon(Icons.link), title: Text(l.clientId == null ? 'Link to a client' : 'Change client'), onTap: () => _link(l)),
            ListTile(contentPadding: EdgeInsets.zero, leading: const Icon(Icons.edit_note), title: const Text('Edit note'), onTap: () => _note(l)),
            if (l.status != 'Superseded')
              ListTile(
                contentPadding: EdgeInsets.zero,
                leading: const Icon(Icons.autorenew),
                title: const Text('Renew: issue a new key for this PC'),
                onTap: () async {
                  await Navigator.push(context, MaterialPageRoute<void>(builder: (_) => IssueLicenceScreen(renew: l)));
                  await reload();
                },
              ),
            if (l.source != 'Cloud')
              ListTile(
                contentPadding: EdgeInsets.zero,
                leading: const Icon(Icons.delete_outline, color: Tokens.bad),
                title: const Text('Delete this record', style: TextStyle(color: Tokens.bad)),
                onTap: () => _delete(l),
              ),
          ]),
        ]),
      ),
    );
  }
}

/// What the customer is sent with their key: where to paste it, in English and Arabic.
String licenceMessage(Licence l) => 'Your Coduo POS licence key (${l.plan} ${l.kind.toLowerCase()}):\n${l.key}\n\n'
    'Open Coduo POS > License, paste the key and press Activate.\n'
    'افتح Coduo POS > الترخيص، الصق المفتاح واضغط تفعيل.';

/// Signs a new key on the server. The passphrase unlocks the signing key for this one key and is never kept.
class IssueLicenceScreen extends StatefulWidget {
  const IssueLicenceScreen({super.key, this.clientId, this.clientName, this.renew});
  final int? clientId;
  final String? clientName;

  /// A licence to renew: same PC, client, plan and seats.
  final Licence? renew;

  @override
  State<IssueLicenceScreen> createState() => _IssueLicenceScreenState();
}

class _IssueLicenceScreenState extends State<IssueLicenceScreen> {
  late final _machine = TextEditingController(text: widget.renew?.machineId);
  final _customer = TextEditingController();
  final _note = TextEditingController();
  final _payment = TextEditingController();
  final _passphrase = TextEditingController();
  late int? _clientId = widget.clientId ?? widget.renew?.clientId;
  late String? _clientName = widget.clientName ?? widget.renew?.clientName;
  String _kind = 'full';
  late String? _plan = widget.renew?.plan.toLowerCase();
  late int _seats = widget.renew?.seats ?? 1;
  bool _customLength = false;
  DateTime? _until;
  String _method = 'Cash';
  bool _busy = false;
  String? _error;
  Signer? _signer;
  List<String> _methods = const ['Cash'];

  static const _kinds = [
    ('full', 'Full', 'Bought: never stops; the date ends its updates plan'),
    ('provisional', 'Provisional', 'Instalments: stops at the date unless renewed'),
    ('trial', 'Trial', 'To try it: stops at the date'),
  ];

  /// The usual length of each kind, as the cloud uses when none is given.
  int get _defaultDays => switch (_kind) { 'provisional' => 45, 'trial' => 14, _ => 365 };

  @override
  void initState() {
    super.initState();
    _loadSigner();
  }

  @override
  void dispose() {
    for (final c in [_machine, _customer, _note, _payment, _passphrase]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _loadSigner() async {
    final api = context.read<Session>().api;
    try {
      final (signer, options) = await (api.signer(), api.financeOptions()).wait;
      if (mounted) {
        setState(() {
          _signer = signer;
          _methods = options.methods;
          _method = options.methods.isEmpty ? 'Cash' : options.methods.first;
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = '$e');
    }
  }

  Future<void> _issue() async {
    final payment = parseAmount(_payment.text);
    final error = _machine.text.trim().isEmpty
        ? 'Enter the Machine ID from the shop PC\'s License page.'
        : _plan == null
            ? 'Pick the plan: Basic or Pro.'
            : _passphrase.text.isEmpty
                ? 'Enter the signing key\'s passphrase.'
                : (_payment.text.trim().isNotEmpty && (payment == null || payment <= 0))
                    ? 'The payment amount does not look right.'
                    : null;
    if (error != null) {
      setState(() => _error = error);
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final issued = await context.read<Session>().api.issueLicence(
            clientId: _clientId,
            machineId: _machine.text.trim(),
            customer: _customer.text.trim().isEmpty ? null : _customer.text.trim(),
            kind: _kind,
            plan: _plan!,
            seats: _plan == 'basic' ? 1 : _seats,
            until: _customLength ? _until : null,
            note: _note.text.trim().isEmpty ? null : _note.text.trim(),
            paymentAmount: payment,
            paymentMethod: payment == null ? null : _method,
            passphrase: _passphrase.text,
          );
      _passphrase.clear();
      if (!mounted) return;
      final l = issued.licence;
      await showShownOnce(
        context,
        ShownOnce(
          title: 'Key ready: send it to ${l.clientName ?? l.customer}',
          text: l.key ?? '',
          whatsAppUrl: issued.whatsAppUrl ?? 'https://wa.me/?text=${Uri.encodeComponent(licenceMessage(l))}',
          note: '${l.plan} · ${l.kind}${l.seats > 1 ? ' · ${l.seats} PCs' : ''} · ${l.timeLimited ? 'ends' : 'updates until'} ${formatDay(l.validUntil)}. '
              'It is also kept on the Licences tab.',
        ),
      );
      if (mounted) Navigator.pop(context, true);
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } on OfflineException catch (e) {
      setState(() => _error = '$e');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final signer = _signer;
    return Scaffold(
      appBar: AppBar(title: Text(widget.renew == null ? 'Issue a key' : 'Renew a key')),
      body: signer == null
          ? Center(child: _error == null ? const CircularProgressIndicator() : Padding(padding: const EdgeInsets.all(24), child: Text(_error!)))
          : !signer.ready
              ? ListView(children: [
                  EmptyState(icon: Icons.key_off_outlined, title: 'Keys cannot be issued here yet', message: signer.message),
                  const Padding(
                    padding: EdgeInsets.symmetric(horizontal: 24),
                    child: MutedText('Issue it from your PC with the keygen; it is recorded here by itself once the keygen is connected to the cloud.'),
                  ),
                ])
              : ListView(padding: const EdgeInsets.all(16), children: [
                  Card(
                    child: ListTile(
                      leading: const Icon(Icons.storefront_outlined),
                      title: Text(_clientName ?? 'No client'),
                      subtitle: const Text('The licence is listed under this client'),
                      trailing: widget.clientId == null ? const Icon(Icons.edit_outlined) : null,
                      onTap: widget.clientId != null
                          ? null
                          : () async {
                              final picked = await pickClient(context);
                              if (picked != null) {
                                setState(() {
                                  _clientId = picked.$1;
                                  _clientName = picked.$2;
                                });
                              }
                            },
                    ),
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    controller: _machine,
                    textCapitalization: TextCapitalization.characters,
                    autocorrect: false,
                    decoration: const InputDecoration(labelText: 'Machine ID', hintText: 'XXXXX-XXXXX-XXXXX-XXXXX', helperText: 'From the License page on the shop PC'),
                  ),
                  const SizedBox(height: 12),
                  TextField(controller: _customer, decoration: const InputDecoration(labelText: 'Name in the key (optional)', helperText: 'Defaults to the client\'s name')),
                  const SizedBox(height: 16),
                  const Text('Plan', style: TextStyle(fontWeight: FontWeight.w700, color: Tokens.ink2)),
                  const SizedBox(height: 8),
                  SegmentedButton<String>(
                    emptySelectionAllowed: true,
                    segments: const [
                      ButtonSegment(value: 'basic', label: Text('Basic'), icon: Icon(Icons.looks_one_outlined)),
                      ButtonSegment(value: 'pro', label: Text('Pro'), icon: Icon(Icons.workspace_premium_outlined)),
                    ],
                    selected: {?_plan},
                    onSelectionChanged: (s) => setState(() => _plan = s.isEmpty ? null : s.first),
                  ),
                  const SizedBox(height: 6),
                  const MutedText('Basic: 1 PC, 3 users, 7-day reports, no inventory. Pro: everything, and the PCs you sell.', size: 13),
                  if (_plan == 'pro') ...[
                    const SizedBox(height: 12),
                    Row(children: [
                      const Expanded(child: Text('PCs (the store server plus its tills)', style: TextStyle(fontWeight: FontWeight.w600))),
                      IconButton(onPressed: _seats > 1 ? () => setState(() => _seats--) : null, icon: const Icon(Icons.remove_circle_outline)),
                      Text('$_seats', style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w800)),
                      IconButton(onPressed: _seats < 255 ? () => setState(() => _seats++) : null, icon: const Icon(Icons.add_circle_outline)),
                    ]),
                  ],
                  const SizedBox(height: 16),
                  const Text('Kind', style: TextStyle(fontWeight: FontWeight.w700, color: Tokens.ink2)),
                  RadioGroup<String>(
                    groupValue: _kind,
                    onChanged: (k) => setState(() => _kind = k!),
                    child: Column(children: [
                      for (final (code, label, help) in _kinds)
                        RadioListTile<String>(contentPadding: EdgeInsets.zero, value: code, title: Text(label), subtitle: Text(help)),
                    ]),
                  ),
                  SwitchListTile(
                    contentPadding: EdgeInsets.zero,
                    title: Text(_customLength ? 'Until a day I pick' : 'The usual length: $_defaultDays days'),
                    subtitle: Text(_kind == 'full' ? 'For a Full key this is the end of its updates plan' : 'The day the key stops'),
                    value: _customLength,
                    onChanged: (on) => setState(() {
                      _customLength = on;
                      _until ??= DateTime.now().add(Duration(days: _defaultDays));
                    }),
                  ),
                  if (_customLength)
                    DayField(label: 'Until', value: _until!, last: DateTime.now().add(const Duration(days: 3650)), onChanged: (d) => setState(() => _until = d)),
                  const SizedBox(height: 16),
                  TextField(
                    controller: _payment,
                    keyboardType: const TextInputType.numberWithOptions(decimal: true),
                    decoration: InputDecoration(labelText: 'Payment received (optional, ${context.read<Session>().currency})', helperText: 'Also recorded in Finance as "POS licence"'),
                    onChanged: (_) => setState(() {}),
                  ),
                  if (_payment.text.trim().isNotEmpty) ...[
                    const SizedBox(height: 8),
                    Wrap(spacing: 8, runSpacing: 8, children: [
                      for (final m in _methods) ChoiceChip(label: Text(m), selected: _method == m, onSelected: (_) => setState(() => _method = m)),
                    ]),
                  ],
                  const SizedBox(height: 12),
                  TextField(controller: _note, decoration: const InputDecoration(labelText: 'Note (optional)')),
                  const SizedBox(height: 16),
                  TextField(
                    controller: _passphrase,
                    obscureText: true,
                    autocorrect: false,
                    enableSuggestions: false,
                    decoration: const InputDecoration(
                      labelText: 'Signing key passphrase',
                      prefixIcon: Icon(Icons.lock_outline),
                      helperText: 'Used for this one key and never kept. Five wrong tries lock issuing for 15 minutes.',
                      helperMaxLines: 2,
                    ),
                  ),
                  if (_error != null) ...[const SizedBox(height: 12), Text(_error!, style: const TextStyle(color: Tokens.bad, fontWeight: FontWeight.w600))],
                  const SizedBox(height: 20),
                  FilledButton.icon(onPressed: _busy ? null : _issue, icon: const Icon(Icons.key), label: Text(_busy ? 'Signing...' : 'Issue the key')),
                ]),
    );
  }
}

/// Records a key issued somewhere else (the cloud checks its signature).
class RecordLicenceScreen extends StatefulWidget {
  const RecordLicenceScreen({super.key});

  @override
  State<RecordLicenceScreen> createState() => _RecordLicenceScreenState();
}

class _RecordLicenceScreenState extends State<RecordLicenceScreen> {
  final _key = TextEditingController();
  int? _clientId;
  String? _clientName;
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _key.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (!_key.text.trim().startsWith('NXL1-')) {
      setState(() => _error = 'Paste the whole key: it starts with NXL1-.');
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await context.read<Session>().api.recordLicence(_key.text.trim(), clientId: _clientId);
      if (!mounted) return;
      toast(context, 'Key recorded');
      Navigator.pop(context, true);
    } catch (e) {
      setState(() => _error = '$e');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Record a key')),
      body: ListView(padding: const EdgeInsets.all(16), children: [
        const MutedText('For a key issued before the keygen was connected to the cloud. Its signature is checked; a key the shops would refuse is refused here too.'),
        const SizedBox(height: 16),
        TextField(controller: _key, maxLines: 5, minLines: 3, autocorrect: false, decoration: const InputDecoration(labelText: 'Licence key', hintText: 'NXL1-...')),
        const SizedBox(height: 12),
        Card(
          child: ListTile(
            leading: const Icon(Icons.storefront_outlined),
            title: Text(_clientName ?? 'Find the client by itself'),
            subtitle: const Text('By the Machine ID, or the name in the key'),
            trailing: const Icon(Icons.edit_outlined),
            onTap: () async {
              final picked = await pickClient(context);
              if (picked != null) {
                setState(() {
                  _clientId = picked.$1;
                  _clientName = picked.$2;
                });
              }
            },
          ),
        ),
        if (_error != null) ...[const SizedBox(height: 12), Text(_error!, style: const TextStyle(color: Tokens.bad, fontWeight: FontWeight.w600))],
        const SizedBox(height: 20),
        FilledButton(onPressed: _busy ? null : _save, child: const Text('Record key')),
      ]),
    );
  }
}
