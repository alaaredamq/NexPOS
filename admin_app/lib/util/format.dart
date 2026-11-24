import 'package:intl/intl.dart';

/// "$1,650" or "$12.50": whole amounts without decimals, the rest with two.
String formatMoney(double amount, String currency) {
  final whole = amount == amount.roundToDouble();
  final number = NumberFormat(whole ? '#,##0' : '#,##0.00', 'en_US').format(amount.abs());
  return '${amount < 0 ? '-' : ''}$currency$number';
}

/// "24 Nov 2026".
String formatDay(DateTime? d) => d == null ? '' : DateFormat('d MMM yyyy', 'en_US').format(d);

/// "24 Nov 2026, 14:05".
String formatTime(DateTime? d) => d == null ? '' : DateFormat('d MMM yyyy, HH:mm', 'en_US').format(d);

/// "just now", "5 min ago", "3 h ago", "2 days ago", else the day.
String ago(DateTime? d, {DateTime? now}) {
  if (d == null) return 'never';
  final span = (now ?? DateTime.now()).difference(d);
  if (span.inMinutes < 1) return 'just now';
  if (span.inMinutes < 60) return '${span.inMinutes} min ago';
  if (span.inHours < 24) return '${span.inHours} h ago';
  if (span.inDays < 14) return '${span.inDays} day${span.inDays == 1 ? '' : 's'} ago';
  return formatDay(d);
}

/// "in 29 days", "today", "3 days ago".
String until(DateTime? d, {DateTime? now}) {
  if (d == null) return '';
  final today = now ?? DateTime.now();
  final days = DateTime(d.year, d.month, d.day).difference(DateTime(today.year, today.month, today.day)).inDays;
  if (days == 0) return 'today';
  if (days == 1) return 'tomorrow';
  if (days == -1) return 'yesterday';
  return days > 0 ? 'in $days days' : '${-days} days ago';
}

/// "2026-11" as "November 2026".
String monthLabel(String yyyyMm) {
  final d = DateTime.tryParse('$yyyyMm-01');
  return d == null ? yyyyMm : DateFormat('MMMM yyyy', 'en_US').format(d);
}

String monthKey(DateTime d) => '${d.year.toString().padLeft(4, '0')}-${d.month.toString().padLeft(2, '0')}';

/// Parses what a person typed as an amount: "1,200", "12.5", " 40 ".
double? parseAmount(String text) {
  final t = text.trim().replaceAll(',', '').replaceAll(' ', '');
  if (t.isEmpty) return null;
  final v = double.tryParse(t);
  return v == null || v.isNaN || v.isInfinite ? null : v;
}
