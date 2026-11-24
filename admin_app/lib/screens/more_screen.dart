import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../api/models.dart';
import '../state/session.dart';
import '../theme.dart';
import '../util/format.dart';
import '../widgets/common.dart';

/// The account: who is signed in, on which devices (sign a lost phone out), and sign-out.
class MoreScreen extends StatelessWidget {
  const MoreScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final session = context.watch<Session>();
    final me = session.me;
    return Scaffold(
      appBar: AppBar(title: const Text('More')),
      body: Loader<List<SessionInfo>>(
        load: (api) => api.sessions(),
        builder: (context, sessions, reload) => PageList(children: [
          SectionCard(title: 'Signed in', children: [
            InfoRow('Username', me?.username ?? ''),
            InfoRow('Cloud', session.cloudUrl),
            InfoRow('Currency', me?.currency ?? ''),
            InfoRow('Time zone', me?.timeZone ?? ''),
          ]),
          SectionCard(
            title: 'Devices signed in',
            children: [
              const MutedText('Each phone or app signed in to your admin account. Sign out one you lost or no longer use.', size: 13),
              const SizedBox(height: 6),
              for (final s in sessions)
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  leading: Icon(s.current ? Icons.smartphone : Icons.devices_other, color: s.current ? Tokens.navy : Tokens.muted),
                  title: Text(s.deviceName ?? 'Unnamed device', style: const TextStyle(fontWeight: FontWeight.w600)),
                  subtitle: Text([
                    if (s.current) 'this device',
                    'used ${ago(s.lastUsedAt ?? s.createdAt)}',
                    'until ${formatDay(s.expiresAt)}',
                  ].join(' · ')),
                  trailing: s.current
                      ? null
                      : TextButton(
                          onPressed: () async {
                            final api = context.read<Session>().api;
                            if (!await confirm(context, title: 'Sign this device out?', message: '${s.deviceName ?? 'It'} must sign in again to open anything.', yes: 'Sign out', danger: true)) return;
                            if (context.mounted && await act(context, () => api.revokeSession(s.id), done: 'Signed out')) await reload();
                          },
                          child: const Text('Sign out'),
                        ),
                ),
            ],
          ),
          SectionCard(children: [
            ListTile(
              contentPadding: EdgeInsets.zero,
              leading: const Icon(Icons.open_in_browser),
              title: const Text('Open the admin portal'),
              subtitle: const Text('Everything here, and the menu editor, in the browser'),
              onTap: () => openUrl(context, '${session.cloudUrl}/admin'),
            ),
            ListTile(
              contentPadding: EdgeInsets.zero,
              leading: const Icon(Icons.logout, color: Tokens.bad),
              title: const Text('Sign out', style: TextStyle(color: Tokens.bad, fontWeight: FontWeight.w600)),
              onTap: () async {
                if (await confirm(context, title: 'Sign out?', message: 'You will need your username and password to sign in again.', yes: 'Sign out')) {
                  if (context.mounted) await context.read<Session>().signOut();
                }
              },
            ),
          ]),
          const Center(child: MutedText('Coduo Admin 1.0', size: 12)),
        ]),
      ),
    );
  }
}
