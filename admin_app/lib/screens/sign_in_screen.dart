import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../api/admin_api.dart';
import '../config.dart';
import '../state/session.dart';
import '../theme.dart';
import '../widgets/common.dart';

/// The provider's sign-in: the same username and password as the portal at /admin.
class SignInScreen extends StatefulWidget {
  const SignInScreen({super.key});

  @override
  State<SignInScreen> createState() => _SignInScreenState();
}

class _SignInScreenState extends State<SignInScreen> {
  final _user = TextEditingController();
  final _password = TextEditingController();
  late final _cloud = TextEditingController(text: context.read<Session>().cloudUrl);
  bool _busy = false;
  bool _showPassword = false;
  String? _error;

  @override
  void dispose() {
    _user.dispose();
    _password.dispose();
    _cloud.dispose();
    super.dispose();
  }

  String get _deviceName => switch (defaultTargetPlatform) {
        TargetPlatform.android => 'Admin app (Android)',
        TargetPlatform.iOS => 'Admin app (iPhone)',
        _ => 'Admin app',
      };

  Future<void> _signIn() async {
    if (_user.text.trim().isEmpty || _password.text.isEmpty) {
      setState(() => _error = 'Enter your username and password.');
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await context.read<Session>().signIn(_user.text, _password.text, deviceName: _deviceName, cloud: _cloud.text);
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
    final reason = context.watch<Session>().signedOutReason;
    return Scaffold(
      backgroundColor: Tokens.ink,
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                Image.asset('assets/brand/coduo-cloud-logo-for-dark.png', height: 120, semanticLabel: 'Coduo Cloud'),
                const SizedBox(height: 8),
                const Text('Admin', textAlign: TextAlign.center, style: TextStyle(color: Color(0xFFB9C5E8), fontSize: 16, letterSpacing: 2)),
                const SizedBox(height: 28),
                Card(
                  child: Padding(
                    padding: const EdgeInsets.all(20),
                    child: AutofillGroup(
                      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                        const Text('Sign in', style: TextStyle(fontSize: 22, fontWeight: FontWeight.w800)),
                        const SizedBox(height: 4),
                        const MutedText('The same username and password as the admin portal.'),
                        if (reason != null) ...[const SizedBox(height: 12), Text(reason, style: const TextStyle(color: Tokens.warn))],
                        const SizedBox(height: 18),
                        TextField(
                          controller: _user,
                          decoration: const InputDecoration(labelText: 'Username'),
                          autofillHints: const [AutofillHints.username],
                          autocorrect: false,
                          textInputAction: TextInputAction.next,
                        ),
                        const SizedBox(height: 12),
                        TextField(
                          controller: _password,
                          obscureText: !_showPassword,
                          decoration: InputDecoration(
                            labelText: 'Password',
                            suffixIcon: IconButton(
                              tooltip: _showPassword ? 'Hide password' : 'Show password',
                              icon: Icon(_showPassword ? Icons.visibility_off : Icons.visibility),
                              onPressed: () => setState(() => _showPassword = !_showPassword),
                            ),
                          ),
                          autofillHints: const [AutofillHints.password],
                          onSubmitted: (_) => _signIn(),
                        ),
                        if (_error != null) ...[
                          const SizedBox(height: 12),
                          Text(_error!, style: const TextStyle(color: Tokens.bad, fontWeight: FontWeight.w600)),
                        ],
                        const SizedBox(height: 18),
                        FilledButton(
                          onPressed: _busy ? null : _signIn,
                          child: _busy
                              ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
                              : const Text('Sign in'),
                        ),
                        const SizedBox(height: 6),
                        Theme(
                          data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
                          child: ExpansionTile(
                            tilePadding: EdgeInsets.zero,
                            title: const Text('Cloud address', style: TextStyle(fontSize: 14, color: Tokens.muted)),
                            children: [
                              TextField(
                                controller: _cloud,
                                keyboardType: TextInputType.url,
                                autocorrect: false,
                                decoration: const InputDecoration(helperText: 'Only to sign in to a test cloud. Normally leave it.'),
                              ),
                              Align(
                                alignment: AlignmentDirectional.centerEnd,
                                child: TextButton(onPressed: () => _cloud.text = defaultCloudUrl, child: const Text('Use the live cloud')),
                              ),
                            ],
                          ),
                        ),
                      ]),
                    ),
                  ),
                ),
              ]),
            ),
          ),
        ),
      ),
    );
  }
}
