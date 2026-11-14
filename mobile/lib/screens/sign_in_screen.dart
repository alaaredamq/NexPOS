import 'package:flutter/foundation.dart' show kDebugMode;
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../api/owner_api.dart';
import '../l10n.dart';
import '../state/session.dart';
import '../theme.dart';
import '../widgets/common.dart';
import '../widgets/decor.dart';
import '../widgets/motion.dart';

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
  bool _showPassword = false;
  String? _error;
  int _shakes = 0;

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

  void _fail(String message) {
    Haptics.problem();
    setState(() {
      _error = message;
      _shakes++;
    });
  }

  Future<void> _submit() async {
    if (_busy) return;
    if (!_form.currentState!.validate()) {
      Haptics.problem();
      setState(() => _shakes++);
      return;
    }
    FocusScope.of(context).unfocus();
    final offline = context.tr(
      'Could not reach the cloud. Check the address and your connection.',
      'تعذّر الوصول إلى السحابة. تحقق من العنوان والاتصال.',
    );
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await context.read<Session>().signIn(
        cloudUrl: _url.text,
        email: _email.text,
        password: _password.text,
      );
    } on ApiException catch (e) {
      _fail(e.message);
    } on OfflineException {
      _fail(offline);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final session = context.watch<Session>();
    return Scaffold(
      backgroundColor: Tokens.nav,
      body: Stack(
        children: [
          Positioned.fill(
            child: IgnorePointer(
              child: CustomPaint(
                painter: OrbitPainter(
                  alignEnd: !context.isArabic,
                  strength: 0.14,
                ),
              ),
            ),
          ),
          SafeArea(
            bottom: false, // the white card runs to the bottom edge and pads itself for the gesture bar
            child: LayoutBuilder(
              builder: (context, box) => SingleChildScrollView(
                keyboardDismissBehavior:
                    ScrollViewKeyboardDismissBehavior.onDrag,
                child: ConstrainedBox(
                  constraints: BoxConstraints(minHeight: box.maxHeight),
                  child: IntrinsicHeight(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Align(
                          alignment: AlignmentDirectional.centerEnd,
                          child: TextButton(
                            style: TextButton.styleFrom(
                              minimumSize: const Size(64, 48),
                            ),
                            onPressed: () => session.setLanguage(
                              context.isArabic ? 'en' : 'ar',
                            ),
                            child: Text(
                              context.isArabic ? 'English' : 'عربي',
                              style: const TextStyle(
                                color: Colors.white,
                                fontSize: 15,
                              ),
                            ),
                          ),
                        ),
                        const SizedBox(height: 40),
                        Padding(
                          padding: const EdgeInsets.symmetric(horizontal: 28),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              const _Logo(),
                              const SizedBox(height: 22),
                              FadeUp(
                                index: 1,
                                child: Text(
                                  context.tr(
                                    'Your shop,\nin your pocket.',
                                    'محلّك،\nفي جيبك.',
                                  ),
                                  style: const TextStyle(
                                    color: Colors.white,
                                    fontSize: 34,
                                    height: 1.15,
                                    fontWeight: FontWeight.w700,
                                  ),
                                ),
                              ),
                              const SizedBox(height: 10),
                              FadeUp(
                                index: 2,
                                child: Text(
                                  context.tr(
                                    'See what sells and manage products, customers and suppliers, wherever you are.',
                                    'تابع المبيعات وأدر المنتجات والزبائن والموردين من أي مكان.',
                                  ),
                                  style: const TextStyle(
                                    color: Color(0xFFA9BABC),
                                    fontSize: 16,
                                    height: 1.5,
                                  ),
                                ),
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(height: 32),
                        Expanded(
                          child: FadeUp(
                            index: 3,
                            child: Container(
                              padding: EdgeInsets.fromLTRB(
                                24,
                                28,
                                24,
                                32 + MediaQuery.paddingOf(context).bottom,
                              ),
                              decoration: const BoxDecoration(
                                color: Colors.white,
                                borderRadius: BorderRadius.vertical(
                                  top: Radius.circular(28),
                                ),
                              ),
                              child: ShakeX(
                                trigger: _shakes,
                                child: AutofillGroup(
                                  child: Form(
                                    key: _form,
                                    child: Column(
                                      crossAxisAlignment:
                                          CrossAxisAlignment.stretch,
                                      children: [
                                        Text(
                                          context.tr(
                                            'Welcome back',
                                            'أهلًا بعودتك',
                                          ),
                                          style: const TextStyle(
                                            fontSize: 20,
                                            fontWeight: FontWeight.w700,
                                          ),
                                        ),
                                        const SizedBox(height: 16),
                                        TextFormField(
                                          controller: _url,
                                          keyboardType: TextInputType.url,
                                          textInputAction: TextInputAction.next,
                                          autocorrect: false,
                                          textDirection: TextDirection.ltr,
                                          decoration: InputDecoration(
                                            labelText: context.tr(
                                              'Cloud address',
                                              'عنوان السحابة',
                                            ),
                                            hintText:
                                                'https://cloud.example.com',
                                            prefixIcon: const Icon(
                                              Icons.cloud_outlined,
                                            ),
                                          ),
                                          validator: (v) =>
                                              (Uri.tryParse((v ?? '').trim())
                                                      ?.hasScheme ??
                                                  false)
                                              ? null
                                              : context.tr(
                                                  'Enter the address your provider gave you.',
                                                  'أدخل العنوان الذي زوّدك به مزوّد الخدمة.',
                                                ),
                                        ),
                                        const SizedBox(height: 14),
                                        TextFormField(
                                          controller: _email,
                                          keyboardType:
                                              TextInputType.emailAddress,
                                          textInputAction: TextInputAction.next,
                                          autofillHints: const [
                                            AutofillHints.email,
                                          ],
                                          autocorrect: false,
                                          textDirection: TextDirection.ltr,
                                          decoration: InputDecoration(
                                            labelText: context.tr(
                                              'Email',
                                              'البريد الإلكتروني',
                                            ),
                                            prefixIcon: const Icon(
                                              Icons.alternate_email,
                                            ),
                                          ),
                                          validator: (v) =>
                                              (v ?? '').contains('@')
                                              ? null
                                              : context.tr(
                                                  'Enter your email.',
                                                  'أدخل بريدك الإلكتروني.',
                                                ),
                                        ),
                                        const SizedBox(height: 14),
                                        TextFormField(
                                          controller: _password,
                                          obscureText: !_showPassword,
                                          textInputAction: TextInputAction.done,
                                          autofillHints: const [
                                            AutofillHints.password,
                                          ],
                                          decoration: InputDecoration(
                                            labelText: context.tr(
                                              'Password',
                                              'كلمة المرور',
                                            ),
                                            prefixIcon: const Icon(
                                              Icons.lock_outline,
                                            ),
                                            suffixIcon: IconButton(
                                              tooltip: _showPassword
                                                  ? context.tr(
                                                      'Hide password',
                                                      'إخفاء كلمة المرور',
                                                    )
                                                  : context.tr(
                                                      'Show password',
                                                      'إظهار كلمة المرور',
                                                    ),
                                              onPressed: () => setState(
                                                () => _showPassword =
                                                    !_showPassword,
                                              ),
                                              icon: AnimatedSwitcher(
                                                duration: Motion.scaled(
                                                  context,
                                                  Motion.fast,
                                                ),
                                                transitionBuilder:
                                                    (child, anim) =>
                                                        ScaleTransition(
                                                          scale: anim,
                                                          child: child,
                                                        ),
                                                child: Icon(
                                                  _showPassword
                                                      ? Icons
                                                            .visibility_off_outlined
                                                      : Icons
                                                            .visibility_outlined,
                                                  key: ValueKey(_showPassword),
                                                ),
                                              ),
                                            ),
                                          ),
                                          validator: (v) => (v ?? '').isEmpty
                                              ? context.tr(
                                                  'Enter your password.',
                                                  'أدخل كلمة المرور.',
                                                )
                                              : null,
                                          onFieldSubmitted: (_) => _submit(),
                                        ),
                                        AnimatedSize(
                                          duration: Motion.scaled(
                                            context,
                                            Motion.base,
                                          ),
                                          curve: Motion.enter,
                                          alignment: Alignment.topCenter,
                                          child: _error == null
                                              ? const SizedBox(
                                                  width: double.infinity,
                                                )
                                              : Padding(
                                                  padding:
                                                      const EdgeInsets.only(
                                                        top: 14,
                                                      ),
                                                  child: Notice(
                                                    icon: Icons.error_outline,
                                                    color: Tokens.dangerBg,
                                                    textColor: Tokens.danger,
                                                    text: _error!,
                                                  ),
                                                ),
                                        ),
                                        const SizedBox(height: 20),
                                        PressScale(
                                          child: FilledButton(
                                            onPressed: _busy ? null : _submit,
                                            child: AnimatedSwitcher(
                                              duration: Motion.scaled(
                                                context,
                                                Motion.fast,
                                              ),
                                              child: _busy
                                                  ? const SizedBox(
                                                      key: ValueKey('busy'),
                                                      width: 22,
                                                      height: 22,
                                                      child:
                                                          CircularProgressIndicator(
                                                            strokeWidth: 2.5,
                                                            color: Colors.white,
                                                          ),
                                                    )
                                                  : Text(
                                                      context.tr(
                                                        'Sign in',
                                                        'تسجيل الدخول',
                                                      ),
                                                      key: const ValueKey(
                                                        'label',
                                                      ),
                                                    ),
                                            ),
                                          ),
                                        ),
                                        const SizedBox(height: 16),
                                        Row(
                                          mainAxisAlignment:
                                              MainAxisAlignment.center,
                                          children: [
                                            const Icon(
                                              Icons.shield_outlined,
                                              size: 16,
                                              color: Tokens.muted,
                                            ),
                                            const SizedBox(width: 6),
                                            Flexible(
                                              child: Text(
                                                context.tr(
                                                  'Your sales stay on your shop PC. This app shows an encrypted copy.',
                                                  'مبيعاتك تبقى على جهاز المحل. يعرض التطبيق نسخة مشفّرة منها.',
                                                ),
                                                style: const TextStyle(
                                                  color: Tokens.muted,
                                                  fontSize: 13,
                                                  height: 1.4,
                                                ),
                                              ),
                                            ),
                                          ],
                                        ),
                                      ],
                                    ),
                                  ),
                                ),
                              ),
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// The brand mark pops in with a slight overshoot, and a ring around it expands once.
class _Logo extends StatelessWidget {
  const _Logo();

  @override
  Widget build(BuildContext context) {
    return TweenAnimationBuilder<double>(
      tween: Tween<double>(begin: 0, end: 1),
      duration: Motion.scaled(context, const Duration(milliseconds: 700)),
      curve: Curves.easeOutBack,
      builder: (context, t, _) {
        final ring = Curves.easeOut.transform(t.clamp(0.0, 1.0));
        return SizedBox(
          width: 72,
          height: 72,
          child: Stack(
            alignment: Alignment.center,
            children: [
              Opacity(
                opacity: (1 - ring).clamp(0.0, 1.0) * 0.6,
                child: Container(
                  width: 56 + 16 * ring,
                  height: 56 + 16 * ring,
                  decoration: BoxDecoration(
                    borderRadius: BorderRadius.circular(18 + 6 * ring),
                    border: Border.all(color: Tokens.readout, width: 1.5),
                  ),
                ),
              ),
              Transform.scale(
                scale: 0.55 + 0.45 * t,
                child: Container(
                  width: 56,
                  height: 56,
                  decoration: BoxDecoration(
                    gradient: const LinearGradient(
                      begin: Alignment.topLeft,
                      end: Alignment.bottomRight,
                      colors: [Color(0xFF0FA3B0), Tokens.primary],
                    ),
                    borderRadius: BorderRadius.circular(16),
                    boxShadow: [
                      BoxShadow(
                        color: Tokens.primary.withValues(alpha: 0.45),
                        blurRadius: 18,
                        offset: const Offset(0, 6),
                      ),
                    ],
                  ),
                  alignment: Alignment.center,
                  child: const Text(
                    'N',
                    style: TextStyle(
                      color: Colors.white,
                      fontSize: 28,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ),
              ),
            ],
          ),
        );
      },
    );
  }
}
