import 'dart:async';

import 'package:flutter/material.dart';

import '../api/models.dart';
import '../widgets/common.dart';
import 'licence_screens.dart';

/// Every POS licence key the cloud knows about, like the portal's Licences page.
class LicencesScreen extends StatefulWidget {
  const LicencesScreen({super.key});

  @override
  State<LicencesScreen> createState() => _LicencesScreenState();
}

class _LicencesScreenState extends State<LicencesScreen> {
  static const _shows = [('current', 'In force'), ('attention', 'Needs you'), ('unassigned', 'Not linked'), ('all', 'All')];

  final _list = GlobalKey<LoaderState<List<Licence>>>();
  final _search = TextEditingController();
  String _show = 'current';
  Timer? _debounce;

  @override
  void dispose() {
    _search.dispose();
    _debounce?.cancel();
    super.dispose();
  }

  Future<void> _reload() async => _list.currentState?.reload();

  Future<void> _add() async {
    final what = await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          ListTile(leading: const Icon(Icons.key), title: const Text('Issue a key'), subtitle: const Text('Signed here, with the passphrase'), onTap: () => Navigator.pop(context, 'issue')),
          ListTile(leading: const Icon(Icons.content_paste), title: const Text('Record a key issued elsewhere'), onTap: () => Navigator.pop(context, 'record')),
        ]),
      ),
    );
    if (!mounted || what == null) return;
    await Navigator.push(context, MaterialPageRoute<void>(builder: (_) => what == 'issue' ? const IssueLicenceScreen() : const RecordLicenceScreen()));
    await _reload();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Licences')),
      floatingActionButton: FloatingActionButton.extended(heroTag: 'add-key', onPressed: _add, icon: const Icon(Icons.add), label: const Text('Key')),
      body: Column(children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 4, 16, 8),
          child: TextField(
            controller: _search,
            decoration: const InputDecoration(hintText: 'Search Machine ID, serial or name', prefixIcon: Icon(Icons.search)),
            onChanged: (_) {
              _debounce?.cancel();
              _debounce = Timer(const Duration(milliseconds: 350), _reload);
            },
          ),
        ),
        SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          padding: const EdgeInsets.symmetric(horizontal: 16),
          child: Row(children: [
            for (final (code, label) in _shows)
              Padding(
                padding: const EdgeInsetsDirectional.only(end: 8),
                child: ChoiceChip(
                  label: Text(label),
                  selected: _show == code,
                  onSelected: (_) {
                    setState(() => _show = code);
                    _reload();
                  },
                ),
              ),
          ]),
        ),
        Expanded(
          child: Loader<List<Licence>>(
            key: _list,
            load: (api) => api.licences(show: _show, search: _search.text.trim()),
            builder: (context, list, reload) => list.isEmpty
                ? ListView(children: const [EmptyState(icon: Icons.key_off_outlined, title: 'No licence here', message: 'Issue a key, or record one issued before.')])
                : ListView.separated(
                    physics: const AlwaysScrollableScrollPhysics(),
                    padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
                    itemCount: list.length,
                    separatorBuilder: (_, _) => const SizedBox(height: 8),
                    itemBuilder: (context, i) => Card(
                      child: Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 4),
                        child: LicenceTile(list[i], showClient: true, onTap: () async {
                          await Navigator.push(context, MaterialPageRoute<void>(builder: (_) => LicenceScreen(licenceId: list[i].id)));
                          await reload();
                        }),
                      ),
                    ),
                  ),
          ),
        ),
      ]),
    );
  }
}
