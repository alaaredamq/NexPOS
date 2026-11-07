import 'package:flutter/material.dart';

import '../../api/owner_api.dart';
import '../../l10n.dart';
import 'form_support.dart';

class CustomerFormScreen extends StatefulWidget {
  const CustomerFormScreen({super.key});

  @override
  State<CustomerFormScreen> createState() => _CustomerFormScreenState();
}

class _CustomerFormScreenState extends State<CustomerFormScreen> with SubmitsChange<CustomerFormScreen> {
  final _form = GlobalKey<FormState>();
  final _first = TextEditingController();
  final _last = TextEditingController();
  final _phone = TextEditingController();
  final _email = TextEditingController();
  final _address = TextEditingController();

  @override
  void dispose() {
    for (final c in [_first, _last, _phone, _email, _address]) {
      c.dispose();
    }
    super.dispose();
  }

  void _save() {
    if (!_form.currentState!.validate()) return;
    send(ChangeKind.customer, {
      'firstName': _first.text.trim(),
      'lastName': _last.text.trim(),
      'phoneNumber': orNull(_phone),
      'email': orNull(_email),
      'address': orNull(_address),
    }..removeWhere((_, v) => v == null));
  }

  String? _name(String? v) =>
      (v ?? '').trim().length >= 2 ? null : context.tr('At least 2 characters.', 'حرفان على الأقل.');

  @override
  Widget build(BuildContext context) {
    return FormPage(
      title: context.tr('New customer', 'زبون جديد'),
      formKey: _form,
      busy: busy,
      onSave: _save,
      children: [
        TextFormField(
          controller: _first,
          textCapitalization: TextCapitalization.words,
          decoration: InputDecoration(labelText: context.tr('First name', 'الاسم الأول')),
          validator: _name,
        ),
        TextFormField(
          controller: _last,
          textCapitalization: TextCapitalization.words,
          decoration: InputDecoration(labelText: context.tr('Last name', 'اسم العائلة')),
          validator: _name,
        ),
        TextFormField(
          controller: _phone,
          keyboardType: TextInputType.phone,
          textDirection: TextDirection.ltr,
          decoration: InputDecoration(
            labelText: context.tr('Phone (used for reminders)', 'الهاتف (يُستخدم للتذكير)'),
            hintText: '+961 71 234 567',
          ),
        ),
        TextFormField(
          controller: _email,
          keyboardType: TextInputType.emailAddress,
          textDirection: TextDirection.ltr,
          decoration: InputDecoration(labelText: context.tr('Email (optional)', 'البريد الإلكتروني (اختياري)')),
          validator: (v) => (v ?? '').trim().isEmpty || v!.contains('@') ? null : context.tr('That is not an email.', 'هذا ليس بريدًا إلكترونيًا.'),
        ),
        TextFormField(
          controller: _address,
          decoration: InputDecoration(labelText: context.tr('Address or note (optional)', 'العنوان أو ملاحظة (اختياري)')),
        ),
      ],
    );
  }
}
