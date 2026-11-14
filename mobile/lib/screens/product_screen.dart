import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:intl/intl.dart' show DateFormat;
import 'package:provider/provider.dart';

import '../api/owner_api.dart';
import '../data/details.dart';
import '../data/reports.dart';
import '../data/shop_data.dart';
import '../l10n.dart';
import '../state/permissions.dart';
import '../state/session.dart';
import '../theme.dart';
import '../widgets/common.dart';
import '../widgets/detail.dart';
import '../widgets/motion.dart';
import 'forms/form_support.dart';
import 'open.dart';

/// One product: stock, prices, how it sells, and the two things the owner may do from the phone: edit it and correct its stock.
class ProductPage extends StatelessWidget {
  const ProductPage({super.key, required this.productId});
  final int productId;

  @override
  Widget build(BuildContext context) {
    final shop = context.watch<ShopData>();
    final changes = context.watch<ChangesController>();
    final d = productDetail(shop, productId, DateTime.now());
    final waiting = changes.items.where((c) => c.status.isPending && c.productId == productId).length;
    return Scaffold(
      appBar: AppBar(title: Text(d?.name ?? context.tr('Product', 'منتج'), style: const TextStyle(fontWeight: FontWeight.w700))),
      body: d == null ? const Gone() : _body(context, d, waiting, context.watch<Session>()),
    );
  }

  Widget _body(BuildContext context, ProductDetail d, int waiting, Session session) {
    final locale = Localizations.localeOf(context).languageCode;
    final unit = d.unit.isEmpty ? '' : ' ${unitShort(context, d.unit)}';
    final (levelText, levelBg, levelFg) = switch (d.level) {
      StockLevel.out => (context.tr('Out of stock', 'نفد'), Tokens.dangerBg, Tokens.danger),
      StockLevel.low => (context.tr('Running low', 'منخفض'), Tokens.warningBg, Tokens.warning),
      StockLevel.ok => (context.tr('In stock', 'متوفر'), Tokens.successBg, Tokens.success),
    };
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 28),
      children: [
        if (waiting > 0) ...[
          Notice(
            icon: Icons.schedule,
            text: context.tr('$waiting change(s) to this product are waiting for the shop PC. The figures below update when it applies them.',
                'يوجد $waiting تغيير على هذا المنتج بانتظار جهاز المحل. ستتحدث الأرقام عند تطبيقه.'),
          ),
          const SizedBox(height: 10),
        ],
        FadeUp(
          child: StatCard(
            dark: true,
            label: context.tr('In stock', 'في المخزون'),
            value: '${qty(d.stock)}$unit',
            sub: d.minimum != null && d.minimum! > 0 ? context.tr('Warn below ${qty(d.minimum!)}', 'تنبيه عند أقل من ${qty(d.minimum!)}') : null,
          ),
        ),
        const SizedBox(height: 10),
        FadeUp(
          index: 1,
          child: Wrap(spacing: 8, runSpacing: 6, children: [
            Chip2(levelText, bg: levelBg, fg: levelFg),
            if (!d.isActive) Chip2(context.tr('Not on sale', 'غير معروض'), bg: const Color(0xFFEEF1F5), fg: const Color(0xFF44505F)),
          ]),
        ),
        const SizedBox(height: 14),
        if (session.can(Perm.edit))
        FadeUp(
          index: 2,
          child: Row(children: [
            Expanded(
              child: FilledButton.icon(
                onPressed: () => Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => StockAdjustScreen(productId: d.id))),
                icon: const Icon(Icons.inventory_2_outlined),
                label: FittedBox(fit: BoxFit.scaleDown, child: Text(context.tr('Adjust stock', 'تعديل المخزون'))),
              ),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: OutlinedButton.icon(
                style: OutlinedButton.styleFrom(minimumSize: const Size.fromHeight(52), shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10))),
                onPressed: () => Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => ProductEditScreen(productId: d.id))),
                icon: const Icon(Icons.edit_outlined),
                label: FittedBox(fit: BoxFit.scaleDown, child: Text(context.tr('Edit', 'تعديل'))),
              ),
            ),
          ]),
        ),
        SectionTitle(context.tr('Prices', 'الأسعار')),
        RowGroup(children: [
          Fact(context.tr('Selling price', 'سعر البيع'), money(d.price, d.notation), strong: true),
          if (session.can(Perm.financials) && d.cost != null) Fact(context.tr('Cost', 'التكلفة'), money(d.cost!, d.notation)),
          if (session.can(Perm.financials) && d.marginPercent != null) Fact(context.tr('Margin', 'الهامش'), '${d.marginPercent!.round()}%', color: d.marginPercent! < 10 ? Tokens.warning : Tokens.success),
        ]),
        SectionTitle(context.tr('Last ${ProductDetail.soldWindowDays} days', 'آخر ${ProductDetail.soldWindowDays} يومًا')),
        RowGroup(children: [
          Fact(context.tr('Sold', 'المباع'), '${qty(d.soldQuantity)}$unit'),
          Fact(context.tr('Money taken', 'المبالغ المحصّلة'), money(d.soldRevenue, d.notation)),
        ]),
        SectionTitle(context.tr('Details', 'التفاصيل')),
        RowGroup(children: [
          if (d.barcode.isNotEmpty) Fact(context.tr('Barcode', 'الباركود'), d.barcode),
          if (d.category.isNotEmpty) Fact(context.tr('Category', 'الفئة'), d.category),
          if (d.supplier.isNotEmpty) Fact(context.tr('Supplier', 'المورّد'), d.supplier),
        ]),
        SectionTitle(context.tr('Recent sales', 'آخر المبيعات')),
        RowGroup(
          emptyText: context.tr('Not sold yet.', 'لم يُبع بعد.'),
          children: [
            for (final s in d.recent.take(10))
              TapRow(
                onTap: () => openSale(context, s.saleId),
                child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 11),
                  child: Row(children: [
                    Expanded(
                      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        Text('#${s.number}', style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600)),
                        if (s.when != null) Text(DateFormat('d MMM · HH:mm', locale).format(s.when!), style: const TextStyle(fontSize: 13, color: Tokens.muted)),
                      ]),
                    ),
                    // Huge figures shrink instead of pushing the receipt number off the row.
                    ConstrainedBox(
                      constraints: const BoxConstraints(maxWidth: 110),
                      child: FittedBox(fit: BoxFit.scaleDown, child: Text('${qty(s.quantity)}$unit', style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700))),
                    ),
                    const SizedBox(width: 10),
                    ConstrainedBox(
                      constraints: const BoxConstraints(maxWidth: 110),
                      child: FittedBox(fit: BoxFit.scaleDown, child: Text(money(s.total, d.notation), style: const TextStyle(fontSize: 15, color: Tokens.muted))),
                    ),
                  ]),
                ),
              ),
          ],
        ),
        const SizedBox(height: 14),
        const FreshnessNotice(),
      ],
    );
  }
}

/// Edit what the phone is allowed to change on a product. Only what was changed is sent; the shop keeps everything else.
class ProductEditScreen extends StatefulWidget {
  const ProductEditScreen({super.key, required this.productId});
  final int productId;

  @override
  State<ProductEditScreen> createState() => _ProductEditScreenState();
}

class _ProductEditScreenState extends State<ProductEditScreen> with SubmitsChange<ProductEditScreen> {
  final _form = GlobalKey<FormState>();
  late final ProductDetail _was = productDetail(context.read<ShopData>(), widget.productId, DateTime.now())!;
  late final _name = TextEditingController(text: _was.name);
  late final _price = TextEditingController(text: qty(_was.price));
  late final _cost = TextEditingController(text: _was.cost == null ? '' : qty(_was.cost!));
  late final _min = TextEditingController(text: _was.minimum == null ? '' : qty(_was.minimum!));
  late bool _active = _was.isActive;

  @override
  void dispose() {
    for (final c in [_name, _price, _cost, _min]) {
      c.dispose();
    }
    super.dispose();
  }

  void _save() {
    if (!_form.currentState!.validate()) return;
    final name = _name.text.trim();
    final price = parseNumber(_price.text);
    final cost = parseNumber(_cost.text);
    final min = parseNumber(_min.text);
    // Only what actually changed, so the shop's other values (and any sale that happened meanwhile) are left alone.
    final body = <String, dynamic>{
      'productId': _was.id,
      if (name != _was.name) 'name': name,
      if (price != null && price != _was.price) 'sellingPrice': price,
      if (cost != null && cost != _was.cost) 'purchasePrice': cost,
      if (min != null && min != _was.minimum) 'minStockLevel': min,
      if (_active != _was.isActive) 'isActive': _active,
    };
    if (body.length == 1) {
      Haptics.problem();
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(context.tr('Nothing was changed.', 'لم يتغير شيء.'))));
      return;
    }
    send(ChangeKind.productEdit, body);
  }

  String? _money(String? v) {
    final n = parseNumber(v ?? '');
    return n != null && n > 0 ? null : context.tr('Enter an amount above 0.', 'أدخل مبلغًا أكبر من 0.');
  }

  String? _optional(String? v) {
    if ((v ?? '').trim().isEmpty) return null;
    final n = parseNumber(v!);
    return n != null && n >= 0 ? null : context.tr('Enter 0 or more.', 'أدخل 0 أو أكثر.');
  }

  @override
  Widget build(BuildContext context) {
    final decimals = [FilteringTextInputFormatter.allow(RegExp(r'[0-9.,]'))];
    return FormPage(
      title: context.tr('Edit product', 'تعديل المنتج'),
      formKey: _form,
      busy: busy,
      onSave: _save,
      children: [
        TextFormField(
          controller: _name,
          textCapitalization: TextCapitalization.sentences,
          decoration: InputDecoration(labelText: context.tr('Name', 'الاسم')),
          validator: (v) => (v ?? '').trim().length >= 2 ? null : context.tr('At least 2 characters.', 'حرفان على الأقل.'),
        ),
        Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Expanded(
            child: TextFormField(
              controller: _price,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              inputFormatters: decimals,
              textDirection: TextDirection.ltr,
              decoration: InputDecoration(labelText: context.tr('Selling price', 'سعر البيع')),
              validator: _money,
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: TextFormField(
              controller: _cost,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              inputFormatters: decimals,
              textDirection: TextDirection.ltr,
              decoration: InputDecoration(labelText: context.tr('Cost', 'التكلفة')),
              validator: (v) => (v ?? '').trim().isEmpty ? null : _money(v),
            ),
          ),
        ]),
        TextFormField(
          controller: _min,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          inputFormatters: decimals,
          textDirection: TextDirection.ltr,
          decoration: InputDecoration(labelText: context.tr('Warn me below', 'نبّهني عند أقل من')),
          validator: _optional,
        ),
        SwitchListTile(
          contentPadding: EdgeInsets.zero,
          title: Text(context.tr('On sale at the till', 'معروض للبيع في الصندوق')),
          subtitle: Text(context.tr('Turn off to stop selling it without deleting it.', 'أطفئه لإيقاف بيعه دون حذفه.')),
          value: _active,
          onChanged: (v) => setState(() => _active = v),
        ),
        Text(
          context.tr('Stock is changed with Adjust stock, so every change is recorded.', 'يُعدَّل المخزون من "تعديل المخزون" ليُسجَّل كل تغيير.'),
          style: const TextStyle(fontSize: 12, color: Tokens.muted),
        ),
      ],
    );
  }
}

enum _StockMode { add, remove, count }

/// Add stock that arrived, remove what was lost, or enter a count. A count sets the stock to exactly that figure at the shop, even
/// if sales happened meanwhile, and the difference is recorded with the reason.
class StockAdjustScreen extends StatefulWidget {
  const StockAdjustScreen({super.key, required this.productId});
  final int productId;

  @override
  State<StockAdjustScreen> createState() => _StockAdjustScreenState();
}

class _StockAdjustScreenState extends State<StockAdjustScreen> with SubmitsChange<StockAdjustScreen> {
  final _form = GlobalKey<FormState>();
  late final ProductDetail _was = productDetail(context.read<ShopData>(), widget.productId, DateTime.now())!;
  final _amount = TextEditingController();
  final _reason = TextEditingController();
  _StockMode _mode = _StockMode.add;

  @override
  void initState() {
    super.initState();
    _amount.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _amount.dispose();
    _reason.dispose();
    super.dispose();
  }

  double? get _typed => parseNumber(_amount.text);

  /// What the stock should read afterwards, as far as this phone can tell.
  double? get _after {
    final n = _typed;
    if (n == null) return null;
    return switch (_mode) {
      _StockMode.add => _was.stock + n,
      _StockMode.remove => _was.stock - n,
      _StockMode.count => n,
    };
  }

  void _save() {
    if (!_form.currentState!.validate()) return;
    send(ChangeKind.stock, {
      'productId': _was.id,
      'type': switch (_mode) { _StockMode.add => 'add', _StockMode.remove => 'remove', _StockMode.count => 'set' },
      'quantity': _typed,
      if (orNull(_reason) != null) 'reason': orNull(_reason),
    });
  }

  @override
  Widget build(BuildContext context) {
    final after = _after;
    final unit = _was.unit.isEmpty ? '' : ' ${unitShort(context, _was.unit)}';
    return FormPage(
      title: context.tr('Adjust stock', 'تعديل المخزون'),
      formKey: _form,
      busy: busy,
      onSave: _save,
      children: [
        Text(_was.name, style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
        Text(context.tr('Now in stock: ${qty(_was.stock)}$unit', 'المخزون الحالي: ${qty(_was.stock)}$unit'), style: const TextStyle(color: Tokens.muted)),
        SegmentedButton<_StockMode>(
          showSelectedIcon: false,
          segments: [
            ButtonSegment(value: _StockMode.add, label: Text(context.tr('Add', 'إضافة'))),
            ButtonSegment(value: _StockMode.remove, label: Text(context.tr('Remove', 'سحب'))),
            ButtonSegment(value: _StockMode.count, label: Text(context.tr('Count', 'جرد'))),
          ],
          selected: {_mode},
          onSelectionChanged: (s) {
            Haptics.tap();
            setState(() => _mode = s.first);
          },
        ),
        TextFormField(
          controller: _amount,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          inputFormatters: [FilteringTextInputFormatter.allow(RegExp(r'[0-9.,]'))],
          textDirection: TextDirection.ltr,
          autofocus: true,
          decoration: InputDecoration(
            labelText: switch (_mode) {
              _StockMode.add => context.tr('How many arrived', 'كم وصل'),
              _StockMode.remove => context.tr('How many to take out', 'كم تريد سحبه'),
              _StockMode.count => context.tr('What you counted', 'ما عددته'),
            },
            suffixText: _was.unit.isEmpty ? null : unitShort(context, _was.unit),
          ),
          validator: (v) {
            final n = parseNumber(v ?? '');
            if (n == null || (_mode != _StockMode.count && n <= 0) || n < 0) return context.tr('Enter a quantity.', 'أدخل كمية.');
            if (_mode == _StockMode.remove && n > _was.stock) {
              return context.tr('Only ${qty(_was.stock)}$unit in stock.', 'المتوفر ${qty(_was.stock)}$unit فقط.');
            }
            return null;
          },
        ),
        if (after != null)
          AnimatedSwitcher(
            duration: Motion.scaled(context, Motion.fast),
            child: Text(
              context.tr('Afterwards: ${qty(after)}$unit', 'بعد التعديل: ${qty(after)}$unit'),
              key: ValueKey(after),
              style: TextStyle(fontSize: 14, fontWeight: FontWeight.w700, color: after < 0 ? Tokens.danger : Tokens.success),
            ),
          ),
        TextFormField(
          controller: _reason,
          textCapitalization: TextCapitalization.sentences,
          decoration: InputDecoration(
            labelText: context.tr('Reason (optional)', 'السبب (اختياري)'),
            hintText: switch (_mode) {
              _StockMode.add => context.tr('Delivery', 'توريد'),
              _StockMode.remove => context.tr('Damaged, expired, lost', 'تالف، منتهي، مفقود'),
              _StockMode.count => context.tr('Stock count', 'جرد'),
            },
          ),
        ),
      ],
    );
  }
}
