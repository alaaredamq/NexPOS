# Business Rules
in this ERP POS system, we need to follow the following rules:

- ERP is to be used in lebanon, so there will be 2 currencies, LBP and USD.
- the exchange rate will be set by the user, and it will be used to convert prices between the two currencies.
- yet having multiple suppliers will allow each to have a different exchange rate than the other.
- the user can set the default currency for the system, and all prices will be displayed in that currency.
- the user can set the default currency for each supplier, and all prices for that supplier will be displayed in that currency.

## now how will the db design be like?
we will have a table for:
- products 
- product categories
- suppliers
- customers
- cart
- cart items
- orders that will have cart implemented in it so that way both orders and carts uses cart items ( or maybe the order will be the same as the cart but with a status of completed? )
- receipts
- users ( employees )
- roles ( admin, cashier, manager )
- permissions ( view products, add products, edit products, delete products, view orders, add
- the permissions will be mostly used in the management sections
- AppSettings
- Currencies
- ExchangeRates ( will have a from_currency, to_currency, rate, supplier_id ( nullable ) )
- Taxes ( will have a name, rate, is_default, supplier_id ( nullable ) )
- Discounts ( will have a name, rate, is_default, supplier_id ( nullable ) )
- PaymentMethods ( will have a name, is_default, supplier_id ( nullable ) )
- CustomerCarts ( since carts could be held or paused ( a customer can have multiple ), status of cart will be OnHold, will have customer Id )

## how will the currency conversion work?
- when a product is added to the cart, the price will be converted to the default currency.
- when i add a product the default currency and conversion rate of this product will be those of the supplier of this product.
- when the user changes the default currency, all prices will be converted to the new default currency.
- when the user changes the default currency for a supplier, all prices for that supplier will be converted to the new default currency for that supplier.
- when the user adds or edits a product, the price will be in the default currency of the supplier of that product.
- when the user adds or edits a supplier, the default currency for that supplier will be set.
- the system at the end will always store the prices in the default currency of the supplier of that product.

## Reporting
- reports will be generated based on the default currency of the system.
- reports will be generated based on the default currency of the supplier.
- reports will be generated based on the currency of the order.
- reports will be generated based on the currency of the receipt.
- we will generate a page where we use chartjs to show the following reports:
  - income / expenses by day
  - income / expenses by week
  - income / expenses by month
  - income / expenses by year
  - profit by day
  - profit by week
  - profit by month
  - profit by year
  - sales by day
  - sales by week
  - sales by month
  - sales by year
  - top selling products
  - top customers
  - top suppliers
  - sales by discount

## alerts and notification (push notifications)
- when a product is low in stock, the user will be notified.
- when a product is out of stock, the user will be notified.
- when a product is expired, the user will be notified.
- when a product is about to expire, the user will be notified.
- a notification bell icon will be in the top right corner of the screen, when clicked, a dropdown will show the notifications.
- the notifications will be stored in a table, with a read/unread status.
- the notifications will be marked as read when the user clicks on them.
- the dropdown will show the last 5 notifications, with a link to view all notifications.
- the notifications will be deleted after 30 days.

## MainScreenProducts
in the main screen, we will have a grid view of products to easy-access them.
this section will be called MainScreenProducts.
- the user can add products to this section, and remove them from this section. ( done in a separate management section )
- it will have a searchbar to search for products by name or barcode.
- typing in the search bar will search for the product and show it in under the search bar. ( as select2 does it ) like a select element.
- clicking on a product in the search results will add it to the cart.
- clicking on a product in the grid view will add it to the cart.
- so we will use an html class that adds the product to the cart when clicked.

## Inventory Management
we will have the following features:
- add products to inventory
  - will fill the product info
  - will assign a supplier to the product
  - will assign a category to the product
  - products can be linked to each other as variants ( example: product A has variants B and C )
  - products could have packs ( example: product A is a pack of 6 of product B )
  - products could not have a barcode, they will be used in the MainScreenProducts but they will have a generated barcode for them

## User Management
- we will have a user management section, where the admin can add, edit, delete users
- we will have roles and permissions, where the admin can assign roles to users, and assign permissions to roles
- we will have a default admin user, with the username "admin" and password "admin"
- the admin user will have all permissions
- the admin user will be prompted to change the password on first login
- we will have a user profile section, where the user can change their password and view their profile info
- we will have a user activity log, where the admin can view the actions performed by users

## Logging
- we will have a logging system, where all actions performed by users will be logged
- the logs will be stored in a table, with the following info:
  - user id
  - action performed
  - timestamp
  - ip address
  - user agent
  - additional info ( json )
- the logs will be viewable by the admin in a separate section
- the logs will be filterable by user, action, date range
- the logs will be searchable by action and additional info

## Backup and Restore
- we will have a backup and restore system, where the admin can create a backup of the database
- the backups will be stored in a separate table, with the following info:
  - backup id
  - timestamp
  - user id ( who created the backup )
  - file path ( where the backup file is stored )
- the admin can restore a backup from the list of backups
- the admin can delete a backup from the list of backups
- the backups will be created in a separate folder, outside of the web root, to prevent direct access
- the backups will be created in a format that can be easily restored ( sql file )
- the backups will be downloadable by the admin
- the backups will be created manually by the admin, there will be no automatic backups
- the backups will be created with a confirmation prompt, to prevent accidental backups
- the restores will be created with a confirmation prompt, to prevent accidental restores
- the restores will be created with a warning, that all data will be lost, and the backup will be restored
- the restores will be created with a warning, that the backup will overwrite the current data
- the restores will be created with a warning, that the backup will not be restorable if the backup file is corrupted or missing
- the restores will be created with a warning, that the backup will not be restorable if the backup file is not compatible with the current version of the system
- the restores will be created with a warning, that the backup will not be restorable if the backup file is not a valid

## styling
- mainpage
  - no scroll... it will take the full height of the screen
  - right side will be the products grid ( MainScreenProducts )
  - left side will be the cart
  - top will be the header ( with the logo, user info, notifications, settings )
  - bottom will be the footer ( with the total, payment methods, checkout button )
  - the cart will have a search bar to search for products by name or barcode ( this will always be focused )
  - the cart will have a list of products added to the cart, with the following info:
    - product name
    - product price ( in the default currency )
    - product quantity
    - + and - buttons to increase or decrease the quantity
    - remove button to remove the product from the cart

- management pages
  - will have a sidebar on the left with the different sections ( products, categories, suppliers, customers, orders, receipts, users, roles, permissions, settings )
  - will have a header on the top with the logo, user info, notifications, settings
  - will have a footer on the bottom with the version number and copyright info
  - will have a main content area in the center with the different sections
  - will use datatables for the tables, with pagination, search, sort, export ( csv, excel, pdf )
  - will use modals for adding and editing items
  - will use forms for adding and editing items
  - will use alerts for success and error messages
  - will use confirmations for delete actions

## tools to be used
- bootstrap 5 for the styling and layout
- jquery for the javascript
- datatables for the tables
- select2 for the select elements
- chartjs for the charts
- fontawesome for the icons
- sweetalert2 for the alerts and confirmations
- .Net Core for the backend
- Entity Framework Core for the ORM
- SQL Server for the database

## Discounts
- discounts can be applied to the cart as a whole, or to individual products
- discounts can be a percentage or a fixed amount
- discounts can be applied before or after tax
- discounts can be applied to specific products, categories, or suppliers
- discounts can be applied to specific customers or customer groups
- discounts can be scheduled to be active only during a specific date range
- discounts can be limited to a specific number of uses per customer or overall
- discounts can be combined with other discounts, or not
- discounts can be applied automatically based on predefined rules, or manually by the user
- discounts can be displayed in the cart and on the receipt
- discounts can be tracked in the reporting system, to see their impact on sales and profit