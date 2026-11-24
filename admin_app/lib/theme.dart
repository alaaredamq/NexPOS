import 'package:flutter/cupertino.dart' show CupertinoPageTransitionsBuilder;
import 'package:flutter/material.dart';

/// The portal's tokens (Cloud/wwwroot/cloud.css): Coduo navy, soft blue-grey canvas, teal accent from the logo.
class Tokens {
  static const navy = Color(0xFF1E3A8A);
  static const navyDark = Color(0xFF172E6E);
  static const navyLight = Color(0xFFE3EAFB);
  static const ink = Color(0xFF0B1633);
  static const ink2 = Color(0xFF3B4A72);
  static const muted = Color(0xFF5E6B8C);
  static const canvas = Color(0xFFEEF2FA);
  static const line = Color(0xFFD9E0EF);
  static const accent = Color(0xFF3DDBCB);
  static const data = Color(0xFF0E9A8D);
  static const ok = Color(0xFF138A4B);
  static const okBg = Color(0xFFDDF4E6);
  static const warn = Color(0xFF9A5A00);
  static const warnBg = Color(0xFFFDECC2);
  static const bad = Color(0xFFB92F2F);
  static const badBg = Color(0xFFFADCDC);
  static const none = Color(0xFF5E6B8C);
  static const noneBg = Color(0xFFE6EBF6);
}

ThemeData buildTheme() {
  final scheme = ColorScheme.fromSeed(seedColor: Tokens.navy).copyWith(primary: Tokens.navy, surface: Colors.white, error: Tokens.bad);
  const radius = BorderRadius.all(Radius.circular(12));
  return ThemeData(
    useMaterial3: true,
    colorScheme: scheme,
    scaffoldBackgroundColor: Tokens.canvas,
    pageTransitionsTheme: const PageTransitionsTheme(builders: {
      TargetPlatform.android: FadeForwardsPageTransitionsBuilder(),
      TargetPlatform.iOS: CupertinoPageTransitionsBuilder(),
    }),
    appBarTheme: const AppBarTheme(
      backgroundColor: Tokens.canvas,
      foregroundColor: Tokens.ink,
      elevation: 0,
      scrolledUnderElevation: 0,
      titleTextStyle: TextStyle(color: Tokens.ink, fontSize: 20, fontWeight: FontWeight.w700),
    ),
    navigationBarTheme: NavigationBarThemeData(
      backgroundColor: Colors.white,
      indicatorColor: Tokens.navyLight,
      labelTextStyle: WidgetStateProperty.resolveWith((states) => TextStyle(
            fontSize: 12,
            fontWeight: states.contains(WidgetState.selected) ? FontWeight.w700 : FontWeight.w500,
            color: states.contains(WidgetState.selected) ? Tokens.navy : Tokens.muted,
          )),
    ),
    inputDecorationTheme: const InputDecorationTheme(
      filled: true,
      fillColor: Colors.white,
      border: OutlineInputBorder(borderRadius: radius, borderSide: BorderSide(color: Tokens.line)),
      enabledBorder: OutlineInputBorder(borderRadius: radius, borderSide: BorderSide(color: Tokens.line)),
      contentPadding: EdgeInsets.symmetric(horizontal: 14, vertical: 14),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        minimumSize: const Size(64, 48),
        shape: const RoundedRectangleBorder(borderRadius: radius),
        textStyle: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700),
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        minimumSize: const Size(48, 44),
        foregroundColor: Tokens.navy,
        side: const BorderSide(color: Tokens.line, width: 1.5),
        shape: const RoundedRectangleBorder(borderRadius: radius),
        textStyle: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600),
      ),
    ),
    chipTheme: const ChipThemeData(
      side: BorderSide(color: Tokens.line),
      shape: StadiumBorder(),
    ),
    cardTheme: const CardThemeData(
      color: Colors.white,
      elevation: 0,
      margin: EdgeInsets.zero,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.all(Radius.circular(16)), side: BorderSide(color: Tokens.line)),
    ),
    dividerTheme: const DividerThemeData(color: Tokens.line, space: 1, thickness: 1),
  );
}
