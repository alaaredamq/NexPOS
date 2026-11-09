import 'package:flutter/foundation.dart' show kDebugMode;
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../api/owner_api.dart';
import '../l10n.dart';
import '../state/session.dart';
import '../theme.dart';

class SignInScreen extends StatefulWidget {
  const SignInScreen({super.key});

  @override
  State<SignInScreen> createState() => _SignInScreenState();
}

class _SignInScreenState extends State<SignInScreen> {
  final _form = GlobalKey<FormState>();
  late final TextEditingController _url;
  final _email = TextEditingController();
  final _password = TextEditingController();
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    final saved = context.read<Session>().cloudUrl;
    // Debug builds start on the PC running the cloud (the emulator's name for it); release builds start empty.
    _url = TextEditingController(text: saved.isNotEmpty ? saved : (kDebugMode ? 'http://10.0.2.2:51158' : ''));
  }

  @override
  void dispose() {
    _url.dispose();
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_form.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await context.read<Session>().signIn(cloudUrl: _url.text, email: _email.text, password: _password.text);
    } on ApiException catch (e) {
      setState(() => _error = e.message);
    } on OfflineException {
      setState(() => _error = context.tr(
            'Could not reach the cloud. Check the address and your connection.',
            'تعذّر الوصول إلى السحابة. تحقق من العنوان والاتصال.',
          ));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final session = context.watch<Session>();
    return Scaffold(
      backgroundColor: Tokens.nav,
      body: SafeArea(
        child: LayoutBuilder(
          builder: (context, box) => SingleChildScrollView(
            child: ConstrainedBox(
              constraints: BoxConstraints(minHeight: box.maxHeight),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Align(
                    alignment: AlignmentDirectional.centerEnd,
                    child: TextButton(
                      onPressed: () => session.setLanguage(context.isArabic ? 'en' : 'ar'),
                      child: Text(context.isArabic ? 'English' : 'عربي', style: const TextStyle(color: Colors.white)),
                    ),
                  ),
                  const SizedBox(height: 56),
                  Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 28),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Container(
                          width: 56,
                          height: 56,
                          decoration: BoxDecoration(color: Tokens.primary, borderRadius: BorderRadius.circular(14)),
                          alignment: Alignment.center,
                          child: const Text('N', style: TextStyle(color: Colors.white, fontSize: 28, fontWeight: FontWeight.w700)),
                        ),
                        const SizedBox(height: 20),
                        Text(
                          context.tr('Your shop,\nin your pocket.', 'محلّك،\nفي جيبك.'),
                          style: const TextStyle(color: Colors.white, fontSize: 32, height: 1.15, fontWeight: FontWeight.w700),
                        ),
                        const SizedBox(height: 10),
                        Text(
                          context.tr('See what sells and manage products, customers and suppliers, wherever you are.',
                              'تابع المبيعات وأدر المنتجات والزبائن والموردين من أي مكان.'),
                          style: const TextStyle(color: Color(0xFF8FA3A6), fontSize: 15, height: 1.5),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 32),
                  Container(
                    padding: const EdgeInsets.fromLTRB(24, 28, 24, 32),
                    decoration: const BoxDecoration(
                      color: Colors.white,
                      borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
                    ),
                    child: Form(
                      key: _form,
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          TextFormField(
                            controller: _url,
                            keyboardType: TextInputType.url,
                            autocorrect: false,
                            textDirection: TextDirection.ltr,
                            decoration: InputDecoration(
                              labelText: context.tr('Cloud address', 'عنوان السحابة'),
                              hintText: 'https://cloud.example.com',
                            ),
                            validator: (v) => (Uri.tryParse((v ?? '').trim())?.hasScheme ?? false)
                                ? null
                                : context.tr('Enter the address your provider gave you.', 'أدخل العنوان الذي زوّدك به مزوّد الخدمة.'),
                          ),
                          const SizedBox(height: 14),
                          TextFormField(
                            controller: _email,
                            keyboardType: TextInputType.emailAddress,
                            autocorrect: false,
                            textDirection: TextDirection.ltr,
                            decoration: InputDecoration(labelText: context.tr('Email', 'البريد الإلكتروني')),
                            validator: (v) => (v ?? '').contains('@') ? null : context.tr('Enter your email.', 'أدخل بريدك الإلكتروني.'),
                          ),
                          const SizedBox(height: 14),
                          TextFormField(
                            controller: _password,
                            obscureText: true,
                            decoration: InputDecoration(labelText: context.tr('Password', 'كلمة المرور')),
                            validator: (v) => (v ?? '').isEmpty ? context.tr('Enter your password.', 'أدخل كلمة المرور.') : null,
                            onFieldSubmitted: (_) => _submit(),
                          ),
                          if (_error != null) ...[
                            const SizedBox(height: 14),
                            Text(_error!, style: const TextStyle(color: Tokens.danger, fontWeight: FontWeight.w600)),
                          ],
                          const SizedBox(height: 20),
                          FilledButton(
                            onPressed: _busy ? null : _submit,
                            child: _busy
                                ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
                                : Text(context.tr('Sign in', 'تسجيل الدخول')),
                          ),
                          const SizedBox(height: 14),
                          Text(
                            context.tr('Your sales stay on your shop PC. This app shows a copy that updates every few minutes.',
                                'مبيعاتك تبقى على جهاز المحل. يعرض التطبيق نسخة تتحدث كل بضع دقائق.'),
                            textAlign: TextAlign.center,
                            style: const TextStyle(color: Tokens.muted, fontSize: 12, height: 1.5),
                          ),
                        ],
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
