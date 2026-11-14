import 'package:flutter/foundation.dart';

/// Which bottom tab is showing. Screens use it to jump to another tab ("7 items are low" opens Stock).
class HomeTabs extends ChangeNotifier {
  static const today = 0, sales = 1, stock = 2, debts = 3, manage = 4;

  int index = today;

  void go(int tab) {
    if (tab == index) return;
    index = tab;
    notifyListeners();
  }
}
