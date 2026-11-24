import 'package:flutter/material.dart';

import '../api/models.dart';
import '../theme.dart';
import '../widgets/common.dart';
import 'client_screen.dart';
import 'home_screen.dart';

/// The portal's Clients page summary: what needs you, this month's money, plans and the shops' contact over 24 hours.
class OverviewScreen extends StatelessWidget {
  const OverviewScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Overview')),
      body: Loader<Overview>(
        load: (api) => api.overview(),
        builder: (context, o, reload) => PageList(children: [
          Text(o.summary, style: const TextStyle(color: Tokens.muted, fontSize: 15)),
          SectionCard(
            title: 'Needs you',
            trailing: o.attention.isEmpty ? null : Pill('${o.attention.length}', tone: o.attention.any((a) => a.bad) ? Tone.bad : Tone.warn),
            children: [
              if (o.attention.isEmpty)
                const Row(children: [
                  Icon(Icons.check_circle, color: Tokens.ok),
                  SizedBox(width: 10),
                  Expanded(child: Text('Nothing needs you right now.', style: TextStyle(fontWeight: FontWeight.w600))),
                ]),
              for (final a in o.attention)
                AttentionTile(a, onTap: () => openAttention(context, a, reload)),
            ],
          ),
          TileRow(
            StatTile(label: 'Collected this month', value: context.money(o.collected), color: Tokens.ok),
            StatTile(label: 'Spent this month', value: context.money(o.spent)),
          ),
          TileRow(
            StatTile(label: 'Profit this month', value: context.money(o.profit), color: o.profit < 0 ? Tokens.bad : Tokens.ink),
            StatTile(label: 'Balance', value: context.money(o.balance), note: 'everything in, minus everything out'),
          ),
          SectionCard(
            title: 'Online plans',
            trailing: TextButton(
              onPressed: () => context.findAncestorStateOfType<HomeScreenState>()?.show(1),
              child: const Text('All clients'),
            ),
            children: [
              Row(children: [
                Expanded(child: _Count(o.plansActive, 'active', Tokens.ok)),
                Expanded(child: _Count(o.plansEndingSoon, 'ending soon', Tokens.warn)),
                Expanded(child: _Count(o.plansEnded, 'ended or none', Tokens.muted)),
              ]),
            ],
          ),
          SectionCard(
            title: 'Shops in contact',
            children: [
              Text('${o.connected} of ${o.clients} client${o.clients == 1 ? '' : 's'} connected · ${o.litHours} of the last 24 hours with contact',
                  style: const TextStyle(color: Tokens.muted)),
              const SizedBox(height: 10),
              PulseStrip(o.pulse, height: 34),
              const SizedBox(height: 10),
              Text('${o.productsHeld} products and ${o.ordersHeld} orders held in the cloud', style: const TextStyle(color: Tokens.muted, fontSize: 13)),
            ],
          ),
        ]),
      ),
    );
  }
}

/// Opens what a "needs you" item is about: its client, or the Licences tab for a licence that is not linked to one.
Future<void> openAttention(BuildContext context, Attention a, Future<void> Function() reload) async {
  if (a.clientId != null) {
    await Navigator.push(context, MaterialPageRoute<void>(builder: (_) => ClientScreen(clientId: a.clientId!)));
    await reload();
  } else if (a.isLicence) {
    context.findAncestorStateOfType<HomeScreenState>()?.show(3);
  }
}

class _Count extends StatelessWidget {
  const _Count(this.n, this.label, this.color);
  final int n;
  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) => Column(children: [
        Text('$n', style: TextStyle(fontSize: 26, fontWeight: FontWeight.w800, color: color)),
        Text(label, textAlign: TextAlign.center, style: const TextStyle(color: Tokens.muted, fontSize: 12.5)),
      ]);
}
