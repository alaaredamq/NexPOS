import 'package:flutter/cupertino.dart' show CupertinoPageTransitionsBuilder;
import 'package:flutter/material.dart';

/// The same tokens as the till (BoussiCell look): teal brand, near-black rail, soft teal canvas.
class Tokens {
  static const primary = Color(0xFF0A7B85);
  static const readout = Color(0xFF5AD7E2);
  static const nav = Color(0xFF101E20);
  static const canvas = Color(0xFFF2F7F7);
  static const ink = Color(0xFF171B26);
  static const muted = Color(0xFF5C6472);
  static const border = Color(0xFFE3E7EE);
  static const success = Color(0xFF11703F);
  static const successBg = Color(0xFFE7F6EE);
  static const warning = Color(0xFF7A5200);
  static const warningBg = Color(0xFFFDF3D9);
  static const danger = Color(0xFFA82A3B);
  static const dangerBg = Color(0xFFFDEAED);
}

ThemeData buildTheme() {
  final scheme = ColorScheme.fromSeed(seedColor: Tokens.primary).copyWith(
    primary: Tokens.primary,
    surface: Colors.white,
    error: Tokens.danger,
  );
  const radius = BorderRadius.all(Radius.circular(10));
  return ThemeData(
    useMaterial3: true,
    colorScheme: scheme,
    scaffoldBackgroundColor: Tokens.canvas,
    // Forward = the new page fades in and rises slightly; back reverses it. iOS keeps its own swipe-back slide.
    pageTransitionsTheme: const PageTransitionsTheme(builders: {
      TargetPlatform.android: FadeForwardsPageTransitionsBuilder(),
      TargetPlatform.iOS: CupertinoPageTransitionsBuilder(),
    }),
    appBarTheme: const AppBarTheme(
      backgroundColor: Tokens.canvas,
      foregroundColor: Tokens.ink,
      elevation: 0,
      scrolledUnderElevation: 0,
    ),
    inputDecorationTheme: const InputDecorationTheme(
      filled: true,
      fillColor: Colors.white,
      border: OutlineInputBorder(borderRadius: radius, borderSide: BorderSide(color: Tokens.border)),
      enabledBorder: OutlineInputBorder(borderRadius: radius, borderSide: BorderSide(color: Tokens.border)),
      contentPadding: EdgeInsets.symmetric(horizontal: 14, vertical: 14),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        minimumSize: const Size.fromHeight(52),
        shape: const RoundedRectangleBorder(borderRadius: radius),
        textStyle: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700),
      ),
    ),
    cardTheme: const CardThemeData(
      color: Colors.white,
      elevation: 0,
      margin: EdgeInsets.zero,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.all(Radius.circular(12)),
        side: BorderSide(color: Tokens.border),
      ),
    ),
  );
}
