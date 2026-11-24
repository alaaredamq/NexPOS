import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../api/admin_api.dart';
import '../api/models.dart';
import '../state/session.dart';
import '../theme.dart';
import '../widgets/common.dart';

/// "New client": what they bought decides what comes next (a branch link, a licence key, or a phone login for the menu).
class NewClientScreen extends StatefulWidget {
  const NewClientScreen({super.key});

  @override
  State<NewClientScreen> createState() => _NewClientScreenState();
}

class _NewClientScreenState extends State<NewClientScreen> {
  final _name = TextEditingController();
  final _phone = TextEditingController();
  final _email = TextEditingController();
  final _notes = TextEditingController();
  ClientType _type = ClientType.online;
  int _months = 12;
  bool _monthsPicked = false;
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    for (final c in [_name, _phone, _email, _notes]) {
      c.dispose();
    }
    super.dispose();
  }

  void _pickType(ClientType type) => setState(() {
        _type = type;
        // The menu is sold by the year and cannot be sold with no months.
        if (type == ClientType.menu && (!_monthsPicked || _months == 0)) _months = 12;
      });

  Future<void> _save() async {
    if (_name.text.trim().isEmpty) {
      setState(() => _error = "Enter the shop's name.");
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final created = await context.read<Session>().api.createClient(
            name: _name.text.trim(),
            phone: _phone.text.trim(),
            email: _email.text.trim(),
            notes: _notes.text.trim(),
            type: _type,
            months: _type == ClientType.pos ? 0 : _months,
          );
      if (!mounted) return;
      if (created.link != null) {
        await showShownOnce(
          context,
          ShownOnce(
            title: 'Send this link to ${created.client.name}',
            text: created.link!,
            expiresAt: created.linkExpiresAt,
            whatsAppUrl: 'https://wa.me/?text=${Uri.encodeComponent('Coduo POS Online: open Coduo POS > Settings > Online, paste this link and press Connect.\n${created.link}')}',
            note: 'Their first branch is ready. They paste this in Coduo POS > Settings > Online on the shop PC.',
          ),
        );
      }
      if (mounted) Navigator.pop(context, created.client.id);
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
    final monthChoices = [if (_type == ClientType.online) 0, 1, 3, 6, 12];
    return Scaffold(
      appBar: AppBar(title: const Text('New client')),
      body: ListView(padding: const EdgeInsets.all(16), children: [
        TextField(controller: _name, decoration: const InputDecoration(labelText: 'Shop name'), textCapitalization: TextCapitalization.words),
        const SizedBox(height: 12),
        TextField(controller: _phone, decoration: const InputDecoration(labelText: 'Phone (optional)'), keyboardType: TextInputType.phone),
        const SizedBox(height: 12),
        TextField(controller: _email, decoration: const InputDecoration(labelText: 'Email (optional)'), keyboardType: TextInputType.emailAddress),
        const SizedBox(height: 12),
        TextField(controller: _notes, decoration: const InputDecoration(labelText: 'Notes (optional)'), maxLines: 3, minLines: 1),
        const SizedBox(height: 20),
        const Text('What they bought', style: TextStyle(fontWeight: FontWeight.w700, color: Tokens.ink2)),
        const SizedBox(height: 8),
        RadioGroup<ClientType>(
          groupValue: _type,
          onChanged: (t) => _pickType(t!),
          child: Column(children: [
            for (final t in ClientType.values)
              Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: Card(
                  color: _type == t ? Tokens.navyLight : Colors.white,
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(14),
                    side: BorderSide(color: _type == t ? Tokens.navy : Tokens.line, width: _type == t ? 1.5 : 1),
                  ),
                  child: RadioListTile<ClientType>(
                    value: t,
                    title: Text(t.label, style: const TextStyle(fontWeight: FontWeight.w700)),
                    subtitle: Text(t.help),
                  ),
                ),
              ),
          ]),
        ),
        if (_type != ClientType.pos) ...[
          const SizedBox(height: 12),
          const Text('Months of Online, from today', style: TextStyle(fontWeight: FontWeight.w700, color: Tokens.ink2)),
          const SizedBox(height: 8),
          Wrap(spacing: 8, runSpacing: 8, children: [
            for (final m in monthChoices)
              ChoiceChip(
                label: Text(m == 0 ? 'None yet' : '$m month${m == 1 ? '' : 's'}'),
                selected: _months == m,
                onSelected: (_) => setState(() {
                  _months = m;
                  _monthsPicked = true;
                }),
              ),
          ]),
          const SizedBox(height: 8),
          const MutedText(
              'One Online subscription covers the cloud copy, the phone app and the online menu. When the months run out (plus a grace week) '
              'all of it stops, the menu included, until you add more.',
              size: 13),
        ],
        if (_error != null) ...[const SizedBox(height: 14), Text(_error!, style: const TextStyle(color: Tokens.bad, fontWeight: FontWeight.w600))],
        const SizedBox(height: 20),
        FilledButton(onPressed: _busy ? null : _save, child: Text(_busy ? 'Creating...' : 'Create client')),
      ]),
    );
  }
}

/// Name, phone, email and notes of an existing client.
class EditClientScreen extends StatefulWidget {
  const EditClientScreen({super.key, required this.client});
  final ClientDetail client;

  @override
  State<EditClientScreen> createState() => _EditClientScreenState();
}

class _EditClientScreenState extends State<EditClientScreen> {
  late final _name = TextEditingController(text: widget.client.name);
  late final _phone = TextEditingController(text: widget.client.phone);
  late final _email = TextEditingController(text: widget.client.email);
  late final _notes = TextEditingController(text: widget.client.notes);
  bool _busy = false;

  @override
  void dispose() {
    for (final c in [_name, _phone, _email, _notes]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _save() async {
    setState(() => _busy = true);
    final ok = await act(context, () => context.read<Session>().api.updateClient(widget.client.id,
        name: _name.text.trim(), phone: _phone.text.trim(), email: _email.text.trim(), notes: _notes.text.trim()), done: 'Saved');
    if (!mounted) return;
    setState(() => _busy = false);
    if (ok) Navigator.pop(context, true);
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Edit details')),
      body: ListView(padding: const EdgeInsets.all(16), children: [
        TextField(controller: _name, decoration: const InputDecoration(labelText: 'Shop name')),
        const SizedBox(height: 12),
        TextField(controller: _phone, decoration: const InputDecoration(labelText: 'Phone'), keyboardType: TextInputType.phone),
        const SizedBox(height: 12),
        TextField(controller: _email, decoration: const InputDecoration(labelText: 'Email'), keyboardType: TextInputType.emailAddress),
        const SizedBox(height: 12),
        TextField(controller: _notes, decoration: const InputDecoration(labelText: 'Notes'), maxLines: 4, minLines: 2),
        const SizedBox(height: 20),
        FilledButton(onPressed: _busy ? null : _save, child: const Text('Save changes')),
      ]),
    );
  }
}
