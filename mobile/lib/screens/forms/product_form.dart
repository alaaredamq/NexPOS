import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../api/owner_api.dart';
import '../../l10n.dart';
import '../../theme.dart';
import 'form_support.dart';
import 'lookup_loader.dart';

class ProductFormScreen extends StatelessWidget {
  const ProductFormScreen({super.key});

  @override
  Widget build(BuildContext context) => LookupLoader(
        title: context.tr('New product', 'منتج جديد'),
        builder: (context, lookups) => _ProductForm(lookups: lookups),
      );
}

class _ProductForm extends StatefulWidget {
  const _ProductForm({required this.lookups});

  final Lookups lookups;

  @override
  State<_ProductForm> createState() => _ProductFormState();
}

class _ProductFormState extends State<_ProductForm> with SubmitsChange<_ProductForm> {
  final _form = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _barcode = TextEditingController();
  final _price = TextEditingController();
  final _cost = TextEditingController();
  final _stock = TextEditingController(text: '0');
  final _min = TextEditingController(text: '0');
  String? _unit;
  int? _categoryId;
  int? _supplierId;

  @override
  void initState() {
    super.initState();
    final l = widget.lookups;
    if (l.categories.length == 1) _categoryId = l.categories.first.id;
    if (l.suppliers.length == 1) _supplierId = l.suppliers.first.id;
    _price.addListener(() => setState(() {}));
    _cost.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    for (final c in [_name, _barcode, _price, _cost, _stock, _min]) {
      c.dispose();
    }
    super.dispose();
  }

  void _save() {
    if (!_form.currentState!.validate()) return;
    send(ChangeKind.product, {
      'name': _name.text.trim(),
      'barcode': _barcode.text.trim(),
      'categoryId': _categoryId,
      'supplierId': _supplierId,
      'purchasePrice': parseNumber(_cost.text),
      'sellingPrice': parseNumber(_price.text),
      'stockQuantity': parseNumber(_stock.text) ?? 0,
      'minStockLevel': parseNumber(_min.text) ?? 0,
      'unit': _unit,
      'isActive': true,
    }..removeWhere((_, v) => v == null));
  }

  String? _money(String? v) {
    final n = parseNumber(v ?? '');
    return n != null && n > 0 ? null : context.tr('Enter an amount above 0.', 'أدخل مبلغًا أكبر من 0.');
  }

  String? _quantity(String? v) {
    final n = parseNumber(v ?? '');
    return n != null && n >= 0 ? null : context.tr('Enter 0 or more.', 'أدخل 0 أو أكثر.');
  }

  /// "Margin $1.70 (31%)", so a price typed into the wrong box is noticed before it is sent.
  String? get _margin {
    final price = parseNumber(_price.text);
    final cost = parseNumber(_cost.text);
    if (price == null || cost == null || price <= 0) return null;
    final gain = price - cost;
    return '${context.tr('Margin', 'الهامش')} ${gain.toStringAsFixed(2)} (${(gain / price * 100).round()}%)';
  }

  @override
  Widget build(BuildContext context) {
    final l = widget.lookups;
    final decimals = [FilteringTextInputFormatter.allow(RegExp(r'[0-9.,]'))];
    final margin = _margin;
    return FormPage(
      title: context.tr('New product', 'منتج جديد'),
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
        TextFormField(
          controller: _barcode,
          textDirection: TextDirection.ltr,
          style: const TextStyle(fontFamily: 'monospace'),
          decoration: InputDecoration(
            labelText: context.tr('Barcode', 'الباركود'),
            helperText: context.tr('Type it or read it from the packaging.', 'اكتبه أو انقله من العبوة.'),
          ),
          validator: (v) => (v ?? '').trim().isEmpty ? context.tr('Enter a barcode.', 'أدخل الباركود.') : null,
        ),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: DropdownButtonFormField<int>(
                initialValue: _categoryId,
                isExpanded: true,
                decoration: InputDecoration(labelText: context.tr('Category', 'الفئة')),
                items: [for (final c in l.categories) DropdownMenuItem(value: c.id, child: Text(c.name, overflow: TextOverflow.ellipsis))],
                onChanged: (v) => setState(() => _categoryId = v),
                validator: (v) => v == null ? context.tr('Choose one.', 'اختر واحدًا.') : null,
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: DropdownButtonFormField<int>(
                initialValue: _supplierId,
                isExpanded: true,
                decoration: InputDecoration(labelText: context.tr('Supplier', 'المورّد')),
                items: [for (final s in l.suppliers) DropdownMenuItem(value: s.id, child: Text(s.name, overflow: TextOverflow.ellipsis))],
                onChanged: (v) => setState(() => _supplierId = v),
                validator: (v) => v == null ? context.tr('Choose one.', 'اختر واحدًا.') : null,
              ),
            ),
          ],
        ),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
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
                validator: _money,
              ),
            ),
          ],
        ),
        if (margin != null) Text(margin, style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: Color(0xFF11703F))),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: TextFormField(
                controller: _stock,
                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                inputFormatters: decimals,
                textDirection: TextDirection.ltr,
                decoration: InputDecoration(labelText: context.tr('Stock on hand now', 'المخزون الحالي')),
                validator: _quantity,
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: TextFormField(
                controller: _min,
                keyboardType: const TextInputType.numberWithOptions(decimal: true),
                inputFormatters: decimals,
                textDirection: TextDirection.ltr,
                decoration: InputDecoration(labelText: context.tr('Warn me below', 'نبّهني عند أقل من')),
                validator: _quantity,
              ),
            ),
          ],
        ),
        DropdownButtonFormField<String?>(
          initialValue: _unit,
          isExpanded: true,
          decoration: InputDecoration(labelText: context.tr('Sold by (optional)', 'وحدة البيع (اختياري)')),
          items: [
            DropdownMenuItem<String?>(value: null, child: Text(context.tr('Not set', 'غير محددة'), style: const TextStyle(color: Tokens.muted))),
            for (final u in productUnits) DropdownMenuItem<String?>(value: u, child: Text(unitName(context, u))),
          ],
          onChanged: (v) => setState(() => _unit = v),
        ),
      ],
    );
  }
}
