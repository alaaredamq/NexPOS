import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../l10n.dart';
import '../state/session.dart';
import '../theme.dart';
import 'changes_screen.dart';
import 'forms/customer_form.dart';
import 'forms/product_form.dart';
import 'forms/supplier_form.dart';

/// The signed-in app. Today / Sales / Stock / Debts arrive with the read endpoints; for now the owner can add things and follow them.
class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  int _tab = 0;
  late final ChangesController _changes = context.read<ChangesController>();

  @override
  void initState() {
    super.initState();
    _changes.start();
  }

  @override
  void dispose() {
    _changes.stop();
    _changes.reset(); // the next person to sign in must not see this owner's list
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final changes = context.watch<ChangesController>();
    final badge = changes.pending + changes.needFixing;
    return Scaffold(
      body: SafeArea(child: _tab == 0 ? const _ManageTab() : const ChangesScreen()),
      bottomNavigationBar: NavigationBar(
        selectedIndex: _tab,
        onDestinationSelected: (i) {
          setState(() => _tab = i);
          if (i == 1) _changes.refresh(); // opening the list should show where things stand now, not 6 seconds ago
        },
        backgroundColor: Colors.white,
        indicatorColor: const Color(0xFFE1F0F1),
        destinations: [
          NavigationDestination(
            icon: const Icon(Icons.add_circle_outline),
            selectedIcon: const Icon(Icons.add_circle, color: Tokens.primary),
            label: context.tr('Manage', 'إدارة'),
          ),
          NavigationDestination(
            icon: Badge(isLabelVisible: badge > 0, label: Text('$badge'), child: const Icon(Icons.sync_outlined)),
            selectedIcon: Badge(isLabelVisible: badge > 0, label: Text('$badge'), child: const Icon(Icons.sync, color: Tokens.primary)),
            label: context.tr('Changes', 'التغييرات'),
          ),
        ],
      ),
    );
  }
}

class _ManageTab extends StatelessWidget {
  const _ManageTab();

  @override
  Widget build(BuildContext context) {
    final session = context.watch<Session>();
    final entries = <({IconData icon, String title, String subtitle, Widget Function() page})>[
      (
        icon: Icons.inventory_2_outlined,
        title: context.tr('Add a product', 'إضافة منتج'),
        subtitle: context.tr('Name, barcode, prices and stock', 'الاسم والباركود والأسعار والمخزون'),
        page: () => const ProductFormScreen(),
      ),
      (
        icon: Icons.person_add_alt_outlined,
        title: context.tr('Add a customer', 'إضافة زبون'),
        subtitle: context.tr('Name, phone and address', 'الاسم والهاتف والعنوان'),
        page: () => const CustomerFormScreen(),
      ),
      (
        icon: Icons.local_shipping_outlined,
        title: context.tr('Add a supplier', 'إضافة مورّد'),
        subtitle: context.tr('Company, phone and currency', 'الشركة والهاتف والعملة'),
        page: () => const SupplierFormScreen(),
      ),
    ];

    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Row(
          children: [
            Container(
              width: 40,
              height: 40,
              decoration: BoxDecoration(color: Tokens.primary, borderRadius: BorderRadius.circular(10)),
              alignment: Alignment.center,
              child: const Text('N', style: TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.w700)),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(session.clientName, style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
                  Text(session.displayName, style: const TextStyle(fontSize: 12, color: Tokens.muted)),
                ],
              ),
            ),
            IconButton(
              tooltip: context.tr('Language', 'اللغة'),
              onPressed: () => session.setLanguage(context.isArabic ? 'en' : 'ar'),
              icon: const Icon(Icons.translate),
            ),
            IconButton(
              tooltip: context.tr('Sign out', 'تسجيل الخروج'),
              onPressed: () => _confirmSignOut(context, session),
              icon: const Icon(Icons.logout),
            ),
          ],
        ),
        const SizedBox(height: 20),
        for (final e in entries) ...[
          Card(
            child: ListTile(
              contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
              leading: CircleAvatar(
                backgroundColor: const Color(0xFFE1F0F1),
                foregroundColor: Tokens.primary,
                child: Icon(e.icon),
              ),
              title: Text(e.title, style: const TextStyle(fontWeight: FontWeight.w700)),
              subtitle: Text(e.subtitle),
              trailing: Icon(context.isArabic ? Icons.chevron_left : Icons.chevron_right),
              onTap: () => Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => e.page())),
            ),
          ),
          const SizedBox(height: 10),
        ],
        const SizedBox(height: 6),
        Text(
          context.tr(
            'Changes are sent to the shop PC and applied there. You can follow them under Changes.',
            'تُرسل التغييرات إلى جهاز المحل وتُطبَّق هناك. يمكنك متابعتها من تبويب التغييرات.',
          ),
          style: const TextStyle(fontSize: 12, color: Tokens.muted, height: 1.5),
        ),
      ],
    );
  }

  Future<void> _confirmSignOut(BuildContext context, Session session) async {
    final yes = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text(ctx.tr('Sign out?', 'تسجيل الخروج؟')),
        content: Text(ctx.tr('Changes already sent to the shop are not affected.', 'التغييرات المرسلة إلى المحل لن تتأثر.')),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: Text(ctx.tr('Cancel', 'إلغاء'))),
          TextButton(onPressed: () => Navigator.pop(ctx, true), child: Text(ctx.tr('Sign out', 'تسجيل الخروج'))),
        ],
      ),
    );
    if (yes == true) await session.signOut();
  }
}
