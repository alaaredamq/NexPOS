import 'package:flutter/material.dart';

import '../../api/owner_api.dart';
import '../../l10n.dart';
import 'form_support.dart';
import 'lookup_loader.dart';

class SupplierFormScreen extends StatelessWidget {
  const SupplierFormScreen({super.key});

  @override
  Widget build(BuildContext context) => LookupLoader(
        title: context.tr('New supplier', 'مورّد جديد'),
        builder: (context, lookups) => _SupplierForm(lookups: lookups),
      );
}

class _SupplierForm extends StatefulWidget {
  const _SupplierForm({required this.lookups});

  final Lookups lookups;

  @override
  State<_SupplierForm> createState() => _SupplierFormState();
}

class _SupplierFormState extends State<_SupplierForm> with SubmitsChange<_SupplierForm> {
  final _form = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _phone = TextEditingController();
  final _email = TextEditingController();
  final _city = TextEditingController();
  final _address = TextEditingController();
  late int? _currencyId = widget.lookups.defaultCurrencyId;

  @override
  void dispose() {
    for (final c in [_name, _phone, _email, _city, _address]) {
      c.dispose();
    }
    super.dispose();
  }

  void _save() {
    if (!_form.currentState!.validate()) return;
    send(ChangeKind.supplier, {
      'name': _name.text.trim(),
      'currencyId': _currencyId,
      'phone': orNull(_phone),
      'email': orNull(_email),
      'city': orNull(_city),
      'address': orNull(_address),
    }..removeWhere((_, v) => v == null));
  }

  @override
  Widget build(BuildContext context) {
    return FormPage(
      title: context.tr('New supplier', 'مورّد جديد'),
      formKey: _form,
      busy: busy,
      onSave: _save,
      children: [
        TextFormField(
          controller: _name,
          textCapitalization: TextCapitalization.words,
          decoration: InputDecoration(labelText: context.tr('Company or person', 'الشركة أو الشخص')),
          validator: (v) => (v ?? '').trim().length >= 2 ? null : context.tr('At least 2 characters.', 'حرفان على الأقل.'),
        ),
        TextFormField(
          controller: _phone,
          keyboardType: TextInputType.phone,
          textDirection: TextDirection.ltr,
          decoration: InputDecoration(labelText: context.tr('Phone', 'الهاتف'), hintText: '+961 3 456 789'),
        ),
        TextFormField(
          controller: _email,
          keyboardType: TextInputType.emailAddress,
          textDirection: TextDirection.ltr,
          decoration: InputDecoration(labelText: context.tr('Email (optional)', 'البريد الإلكتروني (اختياري)')),
          validator: (v) => (v ?? '').trim().isEmpty || v!.contains('@') ? null : context.tr('That is not an email.', 'هذا ليس بريدًا إلكترونيًا.'),
        ),
        TextFormField(controller: _city, decoration: InputDecoration(labelText: context.tr('City (optional)', 'المدينة (اختياري)'))),
        TextFormField(controller: _address, decoration: InputDecoration(labelText: context.tr('Address (optional)', 'العنوان (اختياري)'))),
        DropdownButtonFormField<int>(
          initialValue: _currencyId,
          decoration: InputDecoration(labelText: context.tr('They bill you in', 'يفوترونك بعملة')),
          items: [
            for (final c in widget.lookups.currencies) DropdownMenuItem(value: c.id, child: Text('${c.code} (${c.notation})')),
          ],
          onChanged: (v) => setState(() => _currencyId = v),
          validator: (v) => v == null ? context.tr('Choose a currency.', 'اختر العملة.') : null,
        ),
      ],
    );
  }
}
