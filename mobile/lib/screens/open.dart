import 'package:flutter/material.dart';

import 'customer_screen.dart';
import 'product_screen.dart';
import 'sale_detail_screen.dart';

/// Where tapping a sale, a customer or a product leads. One place, so every list opens the same page.
void openSale(BuildContext context, int saleId) =>
    Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => SaleDetailPage(saleId: saleId)));

void openCustomer(BuildContext context, int customerId) =>
    Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => CustomerPage(customerId: customerId)));

void openProduct(BuildContext context, int productId) =>
    Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => ProductPage(productId: productId)));
