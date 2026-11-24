import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:provider/provider.dart';

import 'screens/home_screen.dart';
import 'screens/sign_in_screen.dart';
import 'state/session.dart';
import 'theme.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  final session = Session(store: const SecureTokenStore());
  await session.restore();
  runApp(AdminApp(session: session));
}

/// Coduo Admin: the provider's portal (/admin) on a phone. Clients, their Online plan, branches, phone logins, licences and the books.
class AdminApp extends StatelessWidget {
  const AdminApp({super.key, required this.session});

  final Session session;

  @override
  Widget build(BuildContext context) {
    return ChangeNotifierProvider<Session>.value(
      value: session,
      child: Consumer<Session>(
        builder: (context, session, _) => MaterialApp(
          title: 'Coduo Admin',
          debugShowCheckedModeBanner: false,
          theme: buildTheme(),
          supportedLocales: const [Locale('en')],
          localizationsDelegates: const [
            GlobalMaterialLocalizations.delegate,
            GlobalWidgetsLocalizations.delegate,
            GlobalCupertinoLocalizations.delegate,
          ],
          // A new key on sign-in/out drops every pushed page, so nothing of the last session stays on screen.
          home: session.signedIn ? HomeScreen(key: ValueKey(session.me?.username)) : const SignInScreen(),
        ),
      ),
    );
  }
}
