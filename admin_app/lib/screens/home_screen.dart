import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../state/session.dart';

import 'clients_screen.dart';
import 'finance_screen.dart';
import 'licences_screen.dart';
import 'more_screen.dart';
import 'overview_screen.dart';

/// The five sections, like the portal's side menu: Overview, Clients, Finance, Licences, More.
class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});

  @override
  State<HomeScreen> createState() => HomeScreenState();
}

class HomeScreenState extends State<HomeScreen> with WidgetsBindingObserver {
  int _tab = 0;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  /// Back in the app after a while (the portal or another device may have changed things): every screen loads again.
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) context.read<Session>().dataChanged();
  }

  /// Lets a section jump to another (the overview's "See all clients").
  void show(int tab) => setState(() => _tab = tab);

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: IndexedStack(index: _tab, children: const [
        OverviewScreen(),
        ClientsScreen(),
        FinanceScreen(),
        LicencesScreen(),
        MoreScreen(),
      ]),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _tab,
        onDestinationSelected: show,
        destinations: const [
          NavigationDestination(icon: Icon(Icons.dashboard_outlined), selectedIcon: Icon(Icons.dashboard), label: 'Overview'),
          NavigationDestination(icon: Icon(Icons.storefront_outlined), selectedIcon: Icon(Icons.storefront), label: 'Clients'),
          NavigationDestination(icon: Icon(Icons.bar_chart_outlined), selectedIcon: Icon(Icons.bar_chart), label: 'Finance'),
          NavigationDestination(icon: Icon(Icons.key_outlined), selectedIcon: Icon(Icons.key), label: 'Licences'),
          NavigationDestination(icon: Icon(Icons.more_horiz), label: 'More'),
        ],
      ),
    );
  }
}
