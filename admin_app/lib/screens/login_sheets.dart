import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../api/models.dart';
import '../state/session.dart';
import '../theme.dart';
import '../util/format.dart';
import '../widgets/common.dart';

const roles = [
  ('owner', 'Owner', 'Everything, including profit and the cash drawer'),
  ('manager', 'Manager', 'Sales, stock, customers and debts; can add and edit; no profit or cash drawer'),
  ('staff', 'Staff', 'Sales and stock only, nothing can be changed'),
];

String roleLabel(String role) => roles.firstWhere((r) => r.$1 == role, orElse: () => (role, role, '')).$2;

/// Shows the new password once, with the message the cloud made for the owner (it differs for an online-menu-only client).
Future<void> showHandOver(BuildContext context, LoginHandOver h) => showShownOnce(
      context,
      ShownOnce(
        title: 'Login ready: send it to the owner',
        text: h.message ?? 'Sign in with: ${h.login.signIn}\nPassword: ${h.password}',
        whatsAppUrl: h.whatsAppUrl,
        note: 'The password is shown only now. If it is lost, reset it and a new one is made.',
      ),
    );

/// The client's phone logins: what the owner and their staff type in the owner app or on the owner website.
class LoginsCard extends StatelessWidget {
  const LoginsCard(this.client, {super.key, required this.onChanged});
  final ClientDetail client;
  final Future<void> Function() onChanged;

  Future<void> _add(BuildContext context) async {
    final h = await Navigator.push<LoginHandOver>(context, MaterialPageRoute(builder: (_) => AddLoginScreen(client: client)));
    if (h == null || !context.mounted) return;
    await onChanged();
    if (context.mounted) await showHandOver(context, h);
  }

  Future<void> _actions(BuildContext context, OwnerLogin login) async {
    final api = context.read<Session>().api;
    final action = await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          ListTile(title: Text(login.signIn, style: const TextStyle(fontWeight: FontWeight.w700)), subtitle: Text(roleLabel(login.role))),
          const Divider(),
          for (final (code, label, help) in roles)
            if (code != login.role)
              ListTile(leading: const Icon(Icons.badge_outlined), title: Text('Make $label'), subtitle: Text(help), onTap: () => Navigator.pop(context, 'role:$code')),
          ListTile(leading: const Icon(Icons.password), title: const Text('Reset password'), onTap: () => Navigator.pop(context, 'reset')),
          ListTile(
            leading: Icon(login.isActive ? Icons.block : Icons.check_circle_outline),
            title: Text(login.isActive ? 'Disable' : 'Enable'),
            onTap: () => Navigator.pop(context, 'toggle'),
          ),
          ListTile(
            leading: const Icon(Icons.delete_outline, color: Tokens.bad),
            title: const Text('Remove', style: TextStyle(color: Tokens.bad)),
            onTap: () => Navigator.pop(context, 'remove'),
          ),
        ]),
      ),
    );
    if (action == null || !context.mounted) return;

    if (action.startsWith('role:')) {
      final role = action.substring(5);
      if (!await confirm(context,
          title: 'Change what this login can do?',
          message: 'Their phone is signed out so the new limits apply straight away. They sign in again with the same password.',
          yes: 'Make ${roleLabel(role)}')) {
        return;
      }
      if (context.mounted && await act(context, () => api.updateLogin(client.id, login.id, role: role), done: 'Role updated')) await onChanged();
    } else if (action == 'reset') {
      if (!await confirm(context, title: 'Reset this password?', message: 'A new password is made and the old one stops working. Their phone is signed out.', yes: 'Reset')) return;
      if (!context.mounted) return;
      LoginHandOver? h;
      if (await act(context, () async => h = await api.resetPassword(client.id, login.id))) {
        await onChanged();
        if (context.mounted && h != null) await showHandOver(context, h!);
      }
    } else if (action == 'toggle') {
      final off = login.isActive;
      if (!await confirm(context,
          title: off ? 'Disable this login?' : 'Enable this login?',
          message: off ? 'They can no longer sign in, and their phone is signed out. The shop is not affected.' : 'They can sign in again.',
          yes: off ? 'Disable' : 'Enable',
          danger: off)) {
        return;
      }
      if (context.mounted && await act(context, () => api.updateLogin(client.id, login.id, isActive: !off), done: off ? 'Login disabled' : 'Login enabled')) {
        await onChanged();
      }
    } else if (action == 'remove') {
      if (!await confirm(context,
          title: 'Remove ${login.signIn}?',
          message: '${login.signIn} is signed out and can no longer sign in. The shop\'s data is not touched.',
          yes: 'Remove login',
          danger: true)) {
        return;
      }
      if (context.mounted && await act(context, () => api.removeLogin(client.id, login.id), done: 'Login removed')) await onChanged();
    }
  }

  @override
  Widget build(BuildContext context) {
    return SectionCard(
      title: 'Phone logins',
      trailing: TextButton.icon(onPressed: () => _add(context), icon: const Icon(Icons.person_add_alt, size: 18), label: const Text('Add')),
      children: [
        MutedText(
          client.type == ClientType.menu
              ? 'What the owner types on the owner website to edit their online menu. Staff cannot change the menu.'
              : 'What the owner types in the Coduo POS phone app or on the owner website. Sales, stock and debts also need the shop\'s recovery code, typed once on the phone.',
          size: 13,
        ),
        const SizedBox(height: 6),
        if (client.logins.isEmpty) const MutedText('No phone login yet.'),
        for (final l in client.logins)
          ListTile(
            contentPadding: EdgeInsets.zero,
            leading: CircleAvatar(
              backgroundColor: l.isActive ? Tokens.navyLight : Tokens.noneBg,
              foregroundColor: l.isActive ? Tokens.navy : Tokens.muted,
              child: Text((l.displayName?.isNotEmpty == true ? l.displayName! : l.signIn).characters.first.toUpperCase()),
            ),
            title: Text(l.signIn, style: const TextStyle(fontWeight: FontWeight.w600)),
            subtitle: Text([
              roleLabel(l.role),
              if (l.displayName?.isNotEmpty == true) l.displayName!,
              l.lastSignInAt == null ? 'never signed in' : 'signed in ${ago(l.lastSignInAt)}',
            ].join(' · ')),
            trailing: l.isActive ? const Icon(Icons.more_vert) : const Pill('disabled'),
            onTap: () => _actions(context, l),
          ),
      ],
    );
  }
}

class AddLoginScreen extends StatefulWidget {
  const AddLoginScreen({super.key, required this.client});
  final ClientDetail client;

  @override
  State<AddLoginScreen> createState() => _AddLoginScreenState();
}

class _AddLoginScreenState extends State<AddLoginScreen> {
  final _signIn = TextEditingController();
  final _name = TextEditingController();
  final _password = TextEditingController();
  String _role = 'owner';
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _signIn.dispose();
    _name.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (_signIn.text.trim().isEmpty) {
      setState(() => _error = 'Enter the sign-in name: an email, a phone number or a username.');
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final h = await context.read<Session>().api.addLogin(widget.client.id,
          signIn: _signIn.text.trim(), displayName: _name.text.trim(), password: _password.text.isEmpty ? null : _password.text, role: _role);
      if (mounted) Navigator.pop(context, h);
    } catch (e) {
      setState(() => _error = '$e');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Add a phone login')),
      body: ListView(padding: const EdgeInsets.all(16), children: [
        TextField(
          controller: _signIn,
          autocorrect: false,
          decoration: const InputDecoration(labelText: 'Sign-in name', helperText: 'Email, phone number or username. A phone works with or without the country code.'),
        ),
        const SizedBox(height: 12),
        TextField(controller: _name, decoration: const InputDecoration(labelText: 'Name (shown in the app, optional)')),
        const SizedBox(height: 16),
        const Text('What can they do?', style: TextStyle(fontWeight: FontWeight.w700, color: Tokens.ink2)),
        RadioGroup<String>(
          groupValue: _role,
          onChanged: (r) => setState(() => _role = r!),
          child: Column(children: [
            for (final (code, label, help) in roles)
              RadioListTile<String>(contentPadding: EdgeInsets.zero, value: code, title: Text(label), subtitle: Text(help)),
          ]),
        ),
        const SizedBox(height: 8),
        TextField(
          controller: _password,
          autocorrect: false,
          decoration: const InputDecoration(labelText: 'Password (optional)', helperText: 'Leave empty to make a strong one. At least 8 characters.'),
        ),
        if (_error != null) ...[const SizedBox(height: 12), Text(_error!, style: const TextStyle(color: Tokens.bad, fontWeight: FontWeight.w600))],
        const SizedBox(height: 20),
        FilledButton(onPressed: _busy ? null : _save, child: const Text('Add phone login')),
      ]),
    );
  }
}
