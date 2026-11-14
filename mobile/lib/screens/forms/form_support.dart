import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:uuid/uuid.dart';

import '../../api/owner_api.dart';
import '../../l10n.dart';
import '../../state/session.dart';
import '../../theme.dart';

/// Optional text: empty becomes "not sent", because the shop treats "" as a value to check (an empty email is not an email).
String? orNull(TextEditingController c) {
  final t = c.text.trim();
  return t.isEmpty ? null : t;
}

/// Reads a number typed with a dot or an Arabic/European comma.
double? parseNumber(String text) => double.tryParse(text.trim().replaceAll(',', '.'));

/// What every "add" screen does: keep one idempotency key for the screen's life, send, and explain any failure.
/// A key is made when the screen opens and reused for retries, so pressing Save twice or retrying on a bad connection
/// can never create two of the same thing.
mixin SubmitsChange<T extends StatefulWidget> on State<T> {
  final String idempotencyKey = const Uuid().v4();
  bool busy = false;

  Future<void> send(ChangeKind kind, Map<String, dynamic> body) async {
    if (busy) return;
    setState(() => busy = true);
    final session = context.read<Session>();
    final changes = context.read<ChangesController>();
    final messenger = ScaffoldMessenger.of(context);
    final navigator = Navigator.of(context);
    final sent = context.tr(
      'Sent to the shop. You can follow it under Changes.',
      'أُرسل إلى المحل. يمكنك متابعته من تبويب التغييرات.',
    );
    final offline = context.tr(
      'No connection, so nothing was sent. Try again when you are online.',
      'لا يوجد اتصال فلم يُرسل شيء. حاول مجددًا عند الاتصال.',
    );

    try {
      await session.api.submit(kind, body, idempotencyKey);
      changes.refresh();
      messenger.showSnackBar(SnackBar(content: Text(sent)));
      navigator.pop(true);
    } on OfflineException {
      messenger.showSnackBar(SnackBar(content: Text(offline)));
    } on ApiException catch (e) {
      if (e.isUnauthorized) {
        await session.handleUnauthorized();
        return;
      }
      messenger.showSnackBar(SnackBar(content: Text(e.message), backgroundColor: Tokens.danger));
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }
}

/// Page frame shared by the forms: title, scrolling fields, and a Save button that stays above the keyboard.
class FormPage extends StatelessWidget {
  const FormPage({super.key, required this.title, required this.children, required this.onSave, required this.busy, this.formKey});

  final String title;
  final List<Widget> children;
  final VoidCallback onSave;
  final bool busy;
  final GlobalKey<FormState>? formKey;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(title, style: const TextStyle(fontWeight: FontWeight.w700))),
      body: SafeArea(
        child: Column(
          children: [
            Expanded(
              child: Form(
                key: formKey,
                child: ListView(
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
                  keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
                  children: [
                    for (final c in children) Padding(padding: const EdgeInsets.only(bottom: 14), child: c),
                  ],
                ),
              ),
            ),
            Container(
              padding: const EdgeInsets.fromLTRB(16, 10, 16, 14),
              decoration: const BoxDecoration(color: Colors.white, border: Border(top: BorderSide(color: Tokens.border))),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    context.tr('It is added at the shop as soon as the shop PC is online.',
                        'تُضاف إلى المحل فور اتصال جهاز المحل بالإنترنت.'),
                    style: const TextStyle(fontSize: 12, color: Tokens.muted),
                  ),
                  const SizedBox(height: 10),
                  FilledButton(
                    onPressed: busy ? null : onSave,
                    child: busy
                        ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
                        : Text(context.tr('Save', 'حفظ')),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
