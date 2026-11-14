import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:provider/provider.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'data/shop_data.dart';
import 'screens/home_screen.dart';
import 'screens/lock_screen.dart';
import 'screens/sign_in_screen.dart';
import 'state/app_lock.dart';
import 'state/home_tabs.dart';
import 'state/notify_settings.dart';
import 'state/session.dart';
import 'theme.dart';
import 'widgets/common.dart' show isolateAmounts;

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  final prefs = await SharedPreferences.getInstance();
  final session = Session(prefs: prefs, tokens: const SecureTokenStore());
  await session.restore();
  final shop = ShopData(session, const SecureKeyStore());
  await shop.restore();
  final lock = AppLock(prefs: prefs, biometrics: const DeviceBiometrics())..attach();
  final notify = NotifySettings(prefs: prefs, control: const DeviceWatchControl(), session: session);
  runApp(OwnerApp(session: session, shop: shop, lock: lock, notify: notify));
}

class OwnerApp extends StatelessWidget {
  const OwnerApp({super.key, required this.session, required this.shop, required this.lock, required this.notify});

  final Session session;
  final ShopData shop;
  final AppLock lock;
  final NotifySettings notify;

  @override
  Widget build(BuildContext context) {
    return MultiProvider(
      providers: [
        ChangeNotifierProvider<Session>.value(value: session),
        ChangeNotifierProvider<ShopData>.value(value: shop),
        ChangeNotifierProvider<AppLock>.value(value: lock),
        ChangeNotifierProvider<NotifySettings>.value(value: notify),
        // Above the navigator, so the add-forms (separate routes) can reach it too.
        ChangeNotifierProvider<ChangesController>(create: (_) => ChangesController(session, keyOf: () => shop.key)),
        ChangeNotifierProvider<HomeTabs>(create: (_) => HomeTabs()),
      ],
      child: Consumer<Session>(
        builder: (context, session, _) => MaterialApp(
          title: 'NexPOS Owner',
          debugShowCheckedModeBanner: false,
          theme: buildTheme(),
          locale: session.locale,
          supportedLocales: const [Locale('en'), Locale('ar')],
          localizationsDelegates: const [
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          // Over every page, so the lock covers dialogs and pushed screens too.
          builder: (context, child) {
            isolateAmounts(Localizations.localeOf(context).languageCode == 'ar'); // amounts keep their order inside Arabic text
            return LockGate(child: child ?? const SizedBox.shrink());
          },
          home: session.signedIn ? const HomeScreen() : const SignInScreen(),
        ),
      ),
    );
  }
}
