import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../api/owner_api.dart';
import '../../l10n.dart';
import '../../data/shop_data.dart';
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

  /// From the shop's own rows on this phone. If they have not been read yet, read them now; if the phone has no key, say so
  /// rather than show empty lists.
  Future<Lookups> _load() async {
    final data = context.read<ShopData>();
    if (!data.hasKey) throw const _NeedsKey();
    if (!data.hasData) await data.refresh();
    if (!data.hasData) throw data.error ?? Exception('not loaded');
    return data.lookups;
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
                          snapshot.error is _NeedsKey
                              ? context.tr('Enter the shop\'s recovery code first (Manage > Shop data locked). The lists come from your shop\'s encrypted data.', 'أدخل رمز استرداد المحل أولًا (إدارة > بيانات المحل مقفلة). تأتي القوائم من بيانات محلك المشفّرة.')
                              : snapshot.error is ApiException
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

/// The phone has no recovery code, so the shop's lists cannot be read.
class _NeedsKey implements Exception {
  const _NeedsKey();
}
