import 'reports.dart';
import 'shop_data.dart';

/// The pages you reach by tapping something: one sale with its items, one customer with their account, one product with how it sells.
/// Like [reports.dart], these are pure functions of [ShopData], so they are tested without a screen.

int? _int(Object? v) => v == null ? null : (v is num ? v.toInt() : int.tryParse('$v'));

DateTime _day(DateTime d) => DateTime(d.year, d.month, d.day);

// ---- one sale ----

class SaleItemLine {
  SaleItemLine({required this.productId, required this.name, required this.quantity, required this.unit, required this.unitPrice, required this.discount, required this.total});
  final int? productId;
  final String name;
  final double quantity;
  final String unit;
  final double unitPrice;
  final double discount;
  final double total;
}

class SaleDetail {
  SaleDetail({
    required this.sale,
    required this.customerId,
    required this.customerName,
    required this.subTotal,
    required this.discount,
    required this.tax,
    required this.paid,
    required this.voidReason,
    required this.items,
  });

  final SaleLine sale;

  /// Set when the sale was made on a customer's account, so the page can open that customer.
  final int? customerId;
  final String customerName;
  final double subTotal;
  final double discount;
  final double tax;
  final double paid;
  final String voidReason;
  final List<SaleItemLine> items;

  /// What is still owed on this sale (a sale on account): the total less what was paid. Never negative (change is not debt).
  double get unpaid => (sale.total - paid) > 0.004 ? sale.total - paid : 0;
}

SaleDetail? saleDetail(ShopData data, int receiptId) {
  final r = data.row('Receipts', receiptId);
  if (r == null || flag(r['IsDeleted'])) return null;
  final currencies = Currencies(data);
  final items = <SaleItemLine>[
    for (final e in data.live('ReceiptItems'))
      if (_int(e.value['ReceiptId']) == receiptId)
        SaleItemLine(
          productId: _int(e.value['ProductId']),
          name: text(e.value['ProductName']),
          quantity: toNum(e.value['Quantity']),
          unit: text(e.value['Unit']),
          unitPrice: toNum(e.value['UnitPrice']),
          discount: toNum(e.value['DiscountAmount']),
          total: toNum(e.value['LineTotal']),
        ),
  ]..sort((a, b) => a.name.compareTo(b.name));

  final customerId = _int(r['CustomerId']);
  final customer = data.row('Customers', customerId);
  return SaleDetail(
    sale: saleLineOf(data, currencies, receiptId, r),
    customerId: customer == null ? null : customerId,
    customerName: customer == null ? text(r['CustomerName']) : '${text(customer['FirstName'])} ${text(customer['LastName'])}'.trim(),
    subTotal: toNum(r['SubTotal']),
    discount: toNum(r['DiscountAmount']),
    tax: toNum(r['TaxAmount']),
    paid: toNum(r['AmountPaid']),
    voidReason: text(r['VoidReason']),
    items: items,
  );
}

// ---- one customer ----

enum LedgerKind { sale, payment, adjustment, reversal, transfer }

class LedgerLine {
  LedgerLine({required this.when, required this.amount, required this.kind, required this.note});
  final DateTime? when;

  /// Signed: positive added to what the customer owes, negative reduced it.
  final double amount;
  final LedgerKind kind;
  final String note;
}

class CustomerDetail {
  CustomerDetail({
    required this.id,
    required this.name,
    required this.phone,
    required this.address,
    required this.balance,
    required this.notation,
    required this.ledger,
    required this.purchases,
    required this.totalBought,
  });
  final int id;
  final String name;
  final String phone;
  final String address;

  /// Positive: owes the shop. Negative: the shop owes them.
  final double balance;
  final String notation;
  final List<LedgerLine> ledger;

  /// Their sales, newest first (voided ones are listed, marked).
  final List<SaleLine> purchases;

  /// Everything they bought in the shop's main currency, voids left out.
  final double totalBought;
}

CustomerDetail? customerDetail(ShopData data, int id) {
  final c = data.row('Customers', id);
  if (c == null || flag(c['IsDeleted'])) return null;
  final currencies = Currencies(data);

  final ledger = <LedgerLine>[
    for (final e in data.live('CustomerDebtTransactions'))
      if (_int(e.value['CustomerId']) == id)
        LedgerLine(
          when: utcDate(e.value['CreatedAt'])?.toLocal(),
          amount: toNum(e.value['Amount']),
          kind: switch (_int(e.value['Type'])) {
            1 => LedgerKind.payment,
            2 => LedgerKind.adjustment,
            3 => LedgerKind.reversal,
            4 => LedgerKind.transfer,
            _ => LedgerKind.sale,
          },
          note: text(e.value['Note']),
        ),
  ]..sort((a, b) => (b.when ?? DateTime(0)).compareTo(a.when ?? DateTime(0)));

  final purchases = <SaleLine>[
    for (final e in data.live('Receipts'))
      if (_int(e.value['CustomerId']) == id) saleLineOf(data, currencies, e.key, e.value),
  ]..sort((a, b) => (b.when ?? DateTime(0)).compareTo(a.when ?? DateTime(0)));

  final main = currencies.main;
  return CustomerDetail(
    id: id,
    name: '${text(c['FirstName'])} ${text(c['LastName'])}'.trim(),
    phone: text(c['PhoneNumber']),
    address: [text(c['Address']), text(c['City'])].where((s) => s.isNotEmpty).join(', '),
    balance: toNum(c['Balance']),
    notation: main.notation,
    ledger: ledger,
    purchases: purchases,
    totalBought: purchases.where((s) => !s.isVoid && (s.currencyId == null || s.currencyId == main.id || main.id == null)).fold(0.0, (a, s) => a + s.total),
  );
}

// ---- one product ----

class ProductSale {
  ProductSale({required this.saleId, required this.number, required this.when, required this.quantity, required this.total});
  final int saleId;
  final String number;
  final DateTime? when;
  final double quantity;
  final double total;
}

class ProductDetail {
  ProductDetail({
    required this.id,
    required this.name,
    required this.barcode,
    required this.category,
    required this.supplier,
    required this.price,
    required this.cost,
    required this.stock,
    required this.minimum,
    required this.unit,
    required this.isActive,
    required this.notation,
    required this.soldQuantity,
    required this.soldRevenue,
    required this.recent,
  });
  final int id;
  final String name;
  final String barcode;
  final String category;
  final String supplier;
  final double price;
  final double? cost;
  final double stock;
  final double? minimum;
  final String unit;
  final bool isActive;
  final String notation;

  /// Units and money from sales in the last [soldWindowDays] days (voided sales left out).
  final double soldQuantity;
  final double soldRevenue;
  final List<ProductSale> recent;

  static const soldWindowDays = 30;

  /// Selling price less cost, as a share of the selling price; null when there is no cost to compare with.
  double? get marginPercent => cost == null || cost! <= 0 || price <= 0 ? null : (price - cost!) / price * 100;

  StockLevel get level =>
      stock <= 0 ? StockLevel.out : (minimum != null && minimum! > 0 && stock <= minimum! ? StockLevel.low : StockLevel.ok);
}

ProductDetail? productDetail(ShopData data, int id, DateTime now) {
  final p = data.row('Products', id);
  if (p == null || flag(p['IsDeleted'])) return null;
  final currencies = Currencies(data);
  final since = _day(now).subtract(const Duration(days: ProductDetail.soldWindowDays - 1));

  var qty = 0.0, revenue = 0.0;
  final recent = <ProductSale>[];
  for (final e in data.live('ReceiptItems')) {
    if (_int(e.value['ProductId']) != id) continue;
    final receipt = data.row('Receipts', _int(e.value['ReceiptId']));
    if (receipt == null || flag(receipt['IsDeleted']) || flag(receipt['IsVoid'])) continue;
    final when = utcDate(receipt['ReceiptDate'])?.toLocal();
    final q = toNum(e.value['Quantity']);
    final line = toNum(e.value['LineTotal']);
    if (when != null && !when.isBefore(since)) {
      qty += q;
      revenue += line;
    }
    recent.add(ProductSale(saleId: _int(e.value['ReceiptId'])!, number: text(receipt['ReceiptNumber']), when: when, quantity: q, total: line));
  }
  recent.sort((a, b) => (b.when ?? DateTime(0)).compareTo(a.when ?? DateTime(0)));

  return ProductDetail(
    id: id,
    name: text(p['Name']),
    barcode: text(p['Barcode']),
    category: text(data.row('ProductCategories', _int(p['CategoryId']))?['Name']),
    supplier: text(data.row('Suppliers', _int(p['SupplierId']))?['Name']),
    price: toNum(p['SellingPrice']),
    cost: p['CostPrice'] == null ? null : toNum(p['CostPrice']),
    stock: toNum(p['StockQuantity']),
    minimum: p['MinimumStock'] == null ? null : toNum(p['MinimumStock']),
    unit: text(p['Unit']),
    isActive: flag(p['IsActive']),
    notation: currencies.notation(_int(p['CurrencyId'])),
    soldQuantity: qty,
    soldRevenue: revenue,
    recent: recent.take(20).toList(),
  );
}
