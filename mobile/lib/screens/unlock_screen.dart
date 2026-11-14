import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';

import '../api/owner_api.dart';
import '../data/shop_data.dart';
import '../l10n.dart';
import '../theme.dart';
import '../widgets/common.dart';
import '../widgets/motion.dart';

/// Asks for the shop's recovery code once. The phone keeps the key (secure storage) and checks it against the shop's real data
/// before accepting it, so a code from another shop or an old one is refused instead of showing an empty app.
class UnlockScreen extends StatefulWidget {
  const UnlockScreen({super.key});

  @override
  State<UnlockScreen> createState() => _UnlockScreenState();
}

class _UnlockScreenState extends State<UnlockScreen> {
  final _code = TextEditingController();
  bool _busy = false;
  String? _problem;

  @override
  void dispose() {
    _code.dispose();
    super.dispose();
  }

  Future<void> _paste() async {
    final clip = await Clipboard.getData(Clipboard.kTextPlain);
    if (clip?.text != null) setState(() => _code.text = clip!.text!.trim());
  }

  Future<void> _unlock() async {
    final data = context.read<ShopData>();
    final navigator = Navigator.of(context);
    setState(() {
      _busy = true;
      _problem = null;
    });
    try {
      final result = await data.unlock(_code.text);
      if (!mounted) return;
      switch (result) {
        case Unlock.ok:
        case Unlock.unverified:
          unawaited(data.refresh());
          await showSavedCheck(navigator.context, label: context.tr('Unlocked', 'تم الفتح'));
          navigator.pop();
        case Unlock.invalid:
          Haptics.problem();
          setState(() => _problem = context.tr(
                'That is not a valid recovery code. Check it against the printed copy: it has 56 letters and digits, in 14 groups of four.',
                'هذا ليس رمز استرداد صالحًا. قارنه بالنسخة المطبوعة: 56 حرفًا ورقمًا في 14 مجموعة من أربعة.',
              ));
        case Unlock.wrongKey:
          setState(() => _problem = context.tr(
                'The code is well-formed but does not open this shop\'s data. Make sure it is this shop\'s code, from the shop PC.',
                'الرمز سليم الشكل لكنه لا يفتح بيانات هذا المحل. تأكد أنه رمز هذا المحل من جهاز المحل.',
              ));
      }
    } on ApiException catch (e) {
      if (mounted) setState(() => _problem = e.message);
    } catch (_) {
      if (mounted) {
        setState(() => _problem = context.tr('Could not reach the cloud. Check your connection and try again.', 'تعذّر الوصول إلى السحابة. تحقق من الاتصال وحاول مجددًا.'));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(context.tr('Recovery code', 'رمز الاسترداد'), style: const TextStyle(fontWeight: FontWeight.w700))),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Text(
            context.tr(
              'On the shop PC open Settings > Online > Recovery code. Type it here, or copy it and press Paste.',
              'على جهاز المحل افتح الإعدادات > الأونلاين > رمز الاسترداد. اكتبه هنا، أو انسخه واضغط لصق.',
            ),
            style: const TextStyle(height: 1.5),
          ),
          const SizedBox(height: 16),
          TextField(
            controller: _code,
            enabled: !_busy,
            autocorrect: false,
            enableSuggestions: false,
            textCapitalization: TextCapitalization.characters,
            keyboardType: TextInputType.visiblePassword,
            minLines: 3,
            maxLines: 4,
            textDirection: TextDirection.ltr,
            style: const TextStyle(fontFamily: 'monospace', fontSize: 17, letterSpacing: 1.5),
            decoration: InputDecoration(
              labelText: context.tr('Recovery code', 'رمز الاسترداد'),
              hintText: 'ABCD-EFGH-IJKL-…',
              errorText: _problem,
              errorMaxLines: 5,
            ),
          ),
          Align(
            alignment: AlignmentDirectional.centerEnd,
            child: TextButton.icon(onPressed: _busy ? null : _paste, icon: const Icon(Icons.content_paste, size: 18), label: Text(context.tr('Paste', 'لصق'))),
          ),
          const SizedBox(height: 8),
          FilledButton(
            onPressed: _busy ? null : _unlock,
            child: _busy
                ? const SizedBox(height: 22, width: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
                : Text(context.tr('Unlock', 'فتح')),
          ),
          const SizedBox(height: 20),
          Notice(
            icon: Icons.shield_outlined,
            color: Tokens.successBg,
            textColor: Tokens.success,
            text: context.tr(
              'Your data is encrypted on the shop PC before it is sent. Neither Coduo POS nor the cloud can read it, and nobody can recover this code for you, so keep the printed copy safe.',
              'تُشفَّر بياناتك على جهاز المحل قبل إرسالها. لا يستطيع Coduo POS ولا السحابة قراءتها، ولا يمكن لأحد استرجاع هذا الرمز لك، لذا احفظ النسخة المطبوعة جيدًا.',
            ),
          ),
        ],
      ),
    );
  }
}

