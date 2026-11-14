import 'package:flutter/widgets.dart';

/// English / Arabic text kept next to where it is used: `context.tr('Save', 'حفظ')`.
/// The Arabic wording has not been read by a native speaker yet.
extension Tr on BuildContext {
  bool get isArabic => Localizations.localeOf(this).languageCode == 'ar';
  String tr(String en, String ar) => isArabic ? ar : en;
}

/// The till's validation messages are keys; these are the ones the phone's forms can run into.
/// Anything not listed is shown as the shop sent it.
String friendlyError(BuildContext context, String key) {
  final known = <String, (String, String)>{
    'Prod_BarcodeExists': ('Another product already uses this barcode.', 'منتج آخر يستخدم هذا الباركود.'),
    'Prod_NameRequired': ('Enter a name.', 'أدخل الاسم.'),
    'Prod_NameLength': ('The name must be at least 2 characters.', 'يجب أن يكون الاسم حرفين على الأقل.'),
    'Barcode is required': ('Enter a barcode.', 'أدخل الباركود.'),
    'Category is required': ('Choose a category.', 'اختر الفئة.'),
    'Supplier is required': ('Choose a supplier.', 'اختر المورّد.'),
    'Selling price must be greater than 0': ('The selling price must be more than 0.', 'يجب أن يكون سعر البيع أكبر من 0.'),
    'Purchase price must be greater than 0': ('The cost must be more than 0.', 'يجب أن تكون التكلفة أكبر من 0.'),
    'Unknown currency.': ('That currency does not exist in the shop.', 'هذه العملة غير موجودة في المحل.'),
  };
  final hit = known[key];
  return hit == null ? key : context.tr(hit.$1, hit.$2);
}

/// Turns a field name from the shop (`sellingPrice`) into a label.
String friendlyField(BuildContext context, String field) {
  final known = <String, (String, String)>{
    'name': ('Name', 'الاسم'),
    'firstName': ('First name', 'الاسم الأول'),
    'lastName': ('Last name', 'اسم العائلة'),
    'barcode': ('Barcode', 'الباركود'),
    'categoryId': ('Category', 'الفئة'),
    'supplierId': ('Supplier', 'المورّد'),
    'currencyId': ('Currency', 'العملة'),
    'purchasePrice': ('Cost', 'التكلفة'),
    'sellingPrice': ('Selling price', 'سعر البيع'),
    'phoneNumber': ('Phone', 'الهاتف'),
    'phone': ('Phone', 'الهاتف'),
    'email': ('Email', 'البريد الإلكتروني'),
  };
  final hit = known[field];
  return hit == null ? field : context.tr(hit.$1, hit.$2);
}
