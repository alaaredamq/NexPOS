import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../api/owner_api.dart';
import '../../l10n.dart';
import '../../state/session.dart';
import '../../theme.dart';

/// Loads the shop's categories, suppliers and currencies, then shows [builder]. The product and supplier forms need them.
class LookupLoader extends StatefulWidget {
  const LookupLoader({super.key, required this.title, required this.builder});

  final String title;
  final Widget Function(BuildContext context, Lookups lookups) builder;

  @override
  State<LookupLoader> createState() => _LookupLoaderState();
}

class _LookupLoaderState extends State<LookupLoader> {
  late Future<Lookups> _future = _load();

  Future<Lookups> _load() async {
    final session = context.read<Session>();
    try {
      return await session.api.lookups();
    } on ApiException catch (e) {
      if (e.isUnauthorized) await session.handleUnauthorized();
      rethrow;
    }
  }

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<Lookups>(
      future: _future,
      builder: (context, snapshot) {
        if (snapshot.hasData) return widget.builder(context, snapshot.data!);
        return Scaffold(
          appBar: AppBar(title: Text(widget.title, style: const TextStyle(fontWeight: FontWeight.w700))),
          body: Center(
            child: snapshot.hasError
                ? Padding(
                    padding: const EdgeInsets.all(24),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Text(
                          snapshot.error is ApiException
                              ? (snapshot.error as ApiException).message
                              : context.tr('Could not load the shop\'s lists. Check your connection.', 'تعذّر تحميل قوائم المحل. تحقق من الاتصال.'),
                          textAlign: TextAlign.center,
                          style: const TextStyle(color: Tokens.danger),
                        ),
                        const SizedBox(height: 12),
                        OutlinedButton(
                          onPressed: () => setState(() => _future = _load()),
                          child: Text(context.tr('Try again', 'حاول مجددًا')),
                        ),
                      ],
                    ),
                  )
                : const CircularProgressIndicator(),
          ),
        );
      },
    );
  }
}
