import 'dart:async';

import 'package:flutter/material.dart';

import '../api/models.dart';
import '../theme.dart';
import '../widgets/common.dart';
import 'client_form.dart';
import 'client_screen.dart';

/// Every client, searchable, with the portal's filters.
class ClientsScreen extends StatefulWidget {
  const ClientsScreen({super.key});

  @override
  State<ClientsScreen> createState() => _ClientsScreenState();
}

class _ClientsScreenState extends State<ClientsScreen> {
  static const _filters = [
    ('all', 'All'),
    ('attention', 'Needs you'),
    ('connected', 'Connected'),
    ('waiting', 'Waiting for a link'),
    ('offline', 'POS only'),
    ('menu', 'Menu only'),
  ];

  final _search = TextEditingController();
  final _list = GlobalKey<LoaderState<List<ClientRow>>>();
  String _filter = 'all';
  Timer? _debounce;

  @override
  void dispose() {
    _search.dispose();
    _debounce?.cancel();
    super.dispose();
  }

  void _reload() => _list.currentState?.reload();

  Future<void> _create() async {
    final id = await Navigator.push<int>(context, MaterialPageRoute(builder: (_) => const NewClientScreen()));
    _reload();
    if (id != null && mounted) {
      await Navigator.push(context, MaterialPageRoute<void>(builder: (_) => ClientScreen(clientId: id)));
      _reload();
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Clients')),
      floatingActionButton: FloatingActionButton.extended(heroTag: 'new-client', onPressed: _create, icon: const Icon(Icons.add), label: const Text('New client')),
      body: Column(children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 4, 16, 8),
          child: TextField(
            controller: _search,
            decoration: InputDecoration(
              hintText: 'Search name, phone or email',
              prefixIcon: const Icon(Icons.search),
              suffixIcon: _search.text.isEmpty
                  ? null
                  : IconButton(
                      tooltip: 'Clear',
                      icon: const Icon(Icons.close),
                      onPressed: () {
                        _search.clear();
                        _reload();
                        setState(() {});
                      },
                    ),
            ),
            onChanged: (_) {
              setState(() {});
              _debounce?.cancel();
              _debounce = Timer(const Duration(milliseconds: 350), _reload);
            },
          ),
        ),
        // A few chips: all built at once, so each can be scrolled to.
        SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: Row(
            children: [
              for (final (code, label) in _filters)
                Padding(
                  padding: const EdgeInsetsDirectional.only(end: 8),
                  child: ChoiceChip(
                    label: Text(label),
                    selected: _filter == code,
                    onSelected: (_) {
                      setState(() => _filter = code);
                      _reload();
                    },
                  ),
                ),
            ],
          ),
        ),
        Expanded(
          child: Loader<List<ClientRow>>(
            key: _list,
            load: (api) => api.clients(search: _search.text.trim(), filter: _filter == 'all' ? null : _filter),
            builder: (context, rows, reload) => rows.isEmpty
                ? ListView(children: [
                    EmptyState(
                      icon: Icons.storefront_outlined,
                      title: _search.text.isEmpty && _filter == 'all' ? 'No clients yet' : 'No client matches',
                      message: _search.text.isEmpty && _filter == 'all' ? 'Add the first shop you sell to.' : null,
                    ),
                  ])
                : ListView.separated(
                    physics: const AlwaysScrollableScrollPhysics(),
                    padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
                    itemCount: rows.length,
                    separatorBuilder: (_, _) => const SizedBox(height: 10),
                    itemBuilder: (context, i) => ClientCard(
                      rows[i],
                      onTap: () async {
                        await Navigator.push(context, MaterialPageRoute<void>(builder: (_) => ClientScreen(clientId: rows[i].id)));
                        await reload();
                      },
                    ),
                  ),
          ),
        ),
      ]),
    );
  }
}

class ClientCard extends StatelessWidget {
  const ClientCard(this.row, {super.key, this.onTap});
  final ClientRow row;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final (tone, label) = planPill(row.state);
    final posOnly = row.type == ClientType.pos && row.state == PlanState.none;
    final sub = [
      row.type == ClientType.online ? (row.branches == 1 ? '1 branch' : '${row.branches} branches') : row.type.label,
      ?row.phone,
    ].join(' · ');
    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              Expanded(
                child: Text(row.name, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
              ),
              if (!row.isActive) const Padding(padding: EdgeInsetsDirectional.only(end: 6), child: Pill('inactive')),
              if (row.tags.contains('attention')) const Padding(padding: EdgeInsetsDirectional.only(end: 6), child: Icon(Icons.error, size: 18, color: Tokens.warn)),
              posOnly ? const Pill('POS only') : Pill(label, tone: tone, dot: true),
            ]),
            const SizedBox(height: 4),
            Text(sub, style: const TextStyle(color: Tokens.muted, fontSize: 13.5)),
            if (!posOnly && row.planNote.isNotEmpty) Text('Online ${row.planNote}', style: const TextStyle(color: Tokens.muted, fontSize: 13)),
            if (row.type == ClientType.online) ...[
              const SizedBox(height: 10),
              PulseStrip(row.pulse, height: 16),
              const SizedBox(height: 6),
              Text(
                [
                  'Last contact: ${row.seen}',
                  if (row.products != null) '${row.products} products',
                  if (row.orders != null) '${row.orders} orders',
                ].join(' · '),
                style: const TextStyle(color: Tokens.muted, fontSize: 12.5),
              ),
            ],
          ]),
        ),
      ),
    );
  }
}
