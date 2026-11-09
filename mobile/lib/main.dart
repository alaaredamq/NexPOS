import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:provider/provider.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'screens/home_screen.dart';
import 'screens/sign_in_screen.dart';
import 'state/session.dart';
import 'theme.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  final session = Session(prefs: await SharedPreferences.getInstance(), tokens: const SecureTokenStore());
  await session.restore();
  runApp(OwnerApp(session: session));
}

class OwnerApp extends StatelessWidget {
  const OwnerApp({super.key, required this.session});

  final Session session;

  @override
  Widget build(BuildContext context) {
    return MultiProvider(
      providers: [
        ChangeNotifierProvider<Session>.value(value: session),
        // Above the navigator, so the add-forms (separate routes) can reach it too.
        ChangeNotifierProvider<ChangesController>(create: (_) => ChangesController(session)),
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
          home: session.signedIn ? const HomeScreen() : const SignInScreen(),
        ),
      ),
    );
  }
}
