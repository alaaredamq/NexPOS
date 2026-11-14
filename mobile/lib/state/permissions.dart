/// What a phone login may do, as the cloud decided when the owner (or staff member) signed in. The same bits as the cloud's
/// `OwnerPermissions` (`Cloud/Data/OwnerEntities.cs`). The cloud is what enforces them; the app uses them to show only what a login can use.
class Perm {
  const Perm._();

  static const sales = 1;
  static const stock = 2;
  static const debts = 4;
  static const add = 8;
  static const edit = 16;

  /// Profit, cost prices, margins and the cash drawer.
  static const financials = 32;
  static const all = sales | stock | debts | add | edit | financials;

  /// The tables a login with [permissions] may read. Must match `OwnerRoles.MayRead` in the cloud.
  static bool mayRead(int permissions, String table) => switch (table) {
        'Currencies' || 'PaymentMethods' => true,
        'Receipts' || 'ReceiptItems' => permissions & sales != 0,
        'Products' || 'ProductCategories' => permissions & stock != 0,
        'Customers' || 'CustomerDebtTransactions' || 'Suppliers' => permissions & debts != 0,
        'CashDrawerSession' || 'CashDrop' || 'Orders' || 'Users' => permissions & financials != 0,
        _ => permissions == all,
      };
}
