/* Coduo POS Online, owner site.
   Signs in with the owner account, asks for the shop's recovery code once, then reads the encrypted copy of the shop through
   /owner/v1 and decrypts it here in the browser (AES-256-GCM, same format as the phone app: "e1." + base64(nonce|ciphertext|tag),
   table and row id bound in as authenticated data). The key never leaves this browser and is kept as a non-extractable CryptoKey.
   Changes (new product/customer/supplier, product edit, stock adjustment) go the phone app's way: sealed here with the same key
   (purpose "command", bound to the change type), queued by the cloud, applied by the shop PC, whose sealed answer is opened here. */
(function () {
'use strict';
var $ = function (s) { return document.querySelector(s); };
var API = '/owner/v1';
var BRAND = window.CODUO_BRAND;

/* ---------- copy ---------- */
var D = {
en: {
 today: 'Today', sales: 'Sales', stock: 'Stock', products: 'Products', productsSub: 'Prices as of the last copy. Tap an item to change it.', shop: 'Shop PC', signout: 'Sign out', lock: 'Lock data', dark: 'Dark mode', online: 'Online',
 greetM: 'Good morning', greetA: 'Good afternoon', greetE: 'Good evening',
 copyAt: 'Copy updated {0}', never: 'The shop PC has not sent anything yet.', refresh: 'Refresh', updating: 'Updating your shop data...',
 salesToday: 'Sales today', vsLast: '{0} vs last {1}, same hours', legToday: 'Today', legLast: 'Last {0}', soFar: 'so far',
 payments: 'Payments', receipts: 'Receipts', avgBasket: 'Average basket', noSalesYet: 'No sales yet today.',
 needs: 'Needs you', recent: 'Recent receipts', seeAll: 'See all sales', allGood: 'Nothing needs you right now.',
 nLow: '{0} items to reorder', nLowD: '{0} below their minimum and {1} out of stock.', seeStock: 'See stock',
 nDebt: 'Customers owe {0}', nDebtD: '{0} customers have an unpaid balance.', nDebtD1: '1 customer has an unpaid balance.', findSale: 'Search receipts',
 shopQuiet: 'The shop PC has been quiet for {0}', shopQuietD: 'Sales made since then appear here once it reconnects. The till keeps selling.', seeShop: 'See shop PC',
 salesSub: 'Every receipt your tills copied to the cloud, newest first.', yesterday: 'Yesterday', byDay: 'Takings by day', daysShown: 'Last {0} days', daysShown1: 'That day', allPay: 'All payments', pickOne: 'Pick a receipt to see what was sold.', walkIn: 'Walk-in', nRcpt: '{0} receipts', nRcpt1: '1 receipt',
 backTo: 'Showing receipts back to {0}.', older: 'Load older receipts', loadingOlder: 'Loading older receipts...', allHere: 'That is every receipt the cloud holds.',
 lookedTo: 'Searched receipts back to {0}.', lookMore: 'Search further back', noReceipts: 'No receipts yet. They appear here once a till sells.', findHint: 'Receipt number or customer',
 shownOf: 'Showing {0} of {1}.', showMore: 'Show more',
 all: 'All', cTime: 'Time', cId: 'Receipt', cCust: 'Customer', cItems: 'Items', cPay: 'Payment', cTot: 'Total',
 noMatch: 'Nothing matches.', rcpt: 'Receipt {0}', rMeta: '{0}, {1}', rTotal: 'Total', discount: 'Discount', voided: 'Void', close: 'Close', loadingItems: 'Loading the items...', noItems: 'No item lines were copied for this receipt.',
 stockSub: 'Quantities as of the last copy. They change when a till sells.', find: 'Search items', low: 'Low', out: 'Out of stock', cName: 'Item', cQty: 'In stock', cMin: 'Minimum', cPrice: 'Price',
 inStock: 'In stock', lowS: 'Low', outS: 'Out', min: 'min',
 shopSub: 'Whether your shop PC is talking to the cloud. A till keeps selling when it loses contact.', lastContact: 'Last contact', lastCopy: 'Last copy of your data', plan: 'Online plan', renews: 'Runs until {0}',
 sActive: 'Active', sGrace: 'Ended recently, still updating', sExpired: 'Ended', sSuspended: 'Paused by your provider', sNone: 'Not started',
 planNote: 'If the plan ends the tills keep selling. This site stops updating after 7 days.', menuH: 'Online menu', menuP: 'A public page with your products, pictures and prices. Share it by link or print its QR code for the tables.', menuOpen: 'Open the menu editor', contactOk: 'In contact', contactLate: 'Quiet', contactOff: 'Offline',
 signTitle: 'Sign in', signSub: 'See your shop from your phone.', email: 'Email, phone or username', pass: 'Password', show: 'Show', hide: 'Hide', signBtn: 'Sign in', signing: 'Signing in',
 errEmail: 'Enter your email, phone number or username.', errPass: 'Enter your password.', noOnline: 'No account yet? Ask your provider to set one up for you.',
 lpH: 'See the till without standing at it.', lpP: 'Sales, stock and the shop PC\'s status, copied from the till every few minutes.',
 lp1: 'What you sold today and how it compares with last week', lp2: 'What is running low', lp3: 'Whether the shop PC is connected',
 trust: 'Your shop\'s data is locked with a key that only you hold. We cannot read it, and neither can this server.',
 startTitle: 'What would you like to open?', startSub: 'Signed in to {0}.', startShop: 'Your shop', startShopP: 'Sales, stock and debts from the shop PC. Asks for the recovery code once in this browser.',
 startMenu: 'Online menu', startMenuP: 'Your public menu: dishes, pictures, prices and the QR code for the tables.', startBack: 'Back',
 unlockTitle: 'Unlock your shop data', unlockSub: 'Type the recovery code printed from the shop PC (Settings, then Online). It stays in this browser and is never sent to us.',
 code: 'Recovery code', unlockBtn: 'Unlock', unlocking: 'Checking the code', errInvalid: 'That is not a recovery code. Check for a typo; it has 56 letters and numbers.',
 errWrong: 'That code does not open this shop\'s data. It may belong to another shop or an older key.',
 hint: 'Example format: ABCD-EFGH-IJKL-... (dashes and capitals are optional)',
 errNoShop: 'No shop PC is connected to this account yet.', errPlan: 'The online plan is not active. Contact your provider.', errNet: 'Could not reach the cloud. Check your internet and try again.',
 errRate: 'Too many attempts. Try again in a few minutes.', errGeneric: 'Something went wrong. Try again.', errCreds: 'The sign-in name or password is not right.', errMayNot: 'Your login is not allowed to see this.',
 cash: 'Cash', ago: '{0} ago', min1: '{0} min', hr1: '{0} h', day1: '{0} d', justNow: 'just now', ready: 'Data decrypted in this browser', retry: 'Try again',
 changes: 'Changes', chSub: 'What you asked the shop PC to do. It applies each change when it is online.',
 newProduct: 'New product', newCustomer: 'New customer', newSupplier: 'New supplier', editProduct: 'Edit product', adjust: 'Adjust stock',
 fName: 'Name', fBarcode: 'Barcode', fBarcodeH: 'Type it or read it from the packaging.', fCategory: 'Category', fSupplier: 'Supplier', fPrice: 'Selling price', fCost: 'Cost',
 fStock: 'In stock now', fMin: 'Warn me below', fUnit: 'Sold by (optional)', notSet: 'Not set', choose: 'Choose...',
 uPiece: 'Piece', uKg: 'Kilogram', uLb: 'Pound', uLiter: 'Liter', uGallon: 'Gallon', uBox: 'Box', uPack: 'Pack',
 fFirst: 'First name', fLast: 'Last name', fPhoneC: 'Phone (used for reminders)', fEmailO: 'Email (optional)', fAddrNote: 'Address or note (optional)',
 fCompany: 'Company or person', fPhone: 'Phone', fCity: 'City (optional)', fAddr: 'Address (optional)', fBills: 'They bill you in',
 fOnSale: 'On sale at the till', fOnSaleH: 'Turn off to stop selling it without deleting it.', stockNote: 'Stock is changed with Adjust stock, so every change is recorded.',
 mAdd: 'Add', mRemove: 'Remove', mCount: 'Count', fQty: 'Quantity', fCounted: 'Counted quantity', fReason: 'Reason (optional)', nowIn: 'Now in stock: {0}', after: 'After this change: {0}',
 eMin2: 'At least 2 characters.', eBarcode: 'Enter a barcode.', eChoose: 'Choose one.', eAmount: 'Enter an amount above 0.', eQty: 'Enter 0 or more.', eQtyPos: 'Enter a quantity above 0.',
 eEmail: 'That is not an email.', eCurrency: 'Choose a currency.', eNothing: 'Nothing was changed.', margin: 'Margin {0} ({1}%)',
 send: 'Send to shop', sending: 'Sending', cancel: 'Cancel', sent: 'Sent to the shop. You can follow it under Changes.',
 needKey: 'To send changes from this browser, enter the recovery code once more.', reenter: 'Enter the code',
 stWait: 'Waiting for shop', stDone: 'Done', stFix: 'Needs a fix', stGone: 'Withdrawn', withdraw: 'Withdraw', withdrawnT: 'Withdrawn. The shop PC will not get it.',
 kCustomer: 'New customer', kSupplier: 'New supplier', kProduct: 'New product', kEdit: 'Product edit', kStock: 'Stock change', kOther: 'Change',
 hiddenC: 'Encrypted change', hiddenD: 'Sealed with a key this browser does not hold.', noChanges: 'Nothing sent yet. Add a product, customer or supplier with the buttons above.',
 sName: 'Name: {0}', sPrice: 'Price {0}', sCost: 'Cost {0}', sMin: 'Minimum {0}', sOn: 'On sale', sOff: 'Stopped', sAdd: 'Add {0}', sRemove: 'Remove {0}', sSet: 'Stock count: {0}',
 cmdErr: 'Could not load your changes.', product: 'Product', pMin: 'Minimum', pPrice: 'Price', pCost: 'Cost', pQty: 'In stock'
},
ar: {
 today: 'اليوم', sales: 'المبيعات', stock: 'المخزون', products: 'المنتجات', productsSub: 'الأسعار كما في آخر نسخة. اضغط على صنف لتعديله.', shop: 'كمبيوتر المحل', signout: 'تسجيل الخروج', lock: 'قفل البيانات', dark: 'الوضع الداكن', online: 'أونلاين',
 greetM: 'صباح الخير', greetA: 'نهارك سعيد', greetE: 'مساء الخير',
 copyAt: 'آخر تحديث للنسخة {0}', never: 'لم يرسل كمبيوتر المحل أي شيء بعد.', refresh: 'تحديث', updating: 'جارٍ تحديث بيانات محلك...',
 salesToday: 'مبيعات اليوم', vsLast: '{0} مقارنة بـ{1} الماضي في الساعات نفسها', legToday: 'اليوم', legLast: '{0} الماضي', soFar: 'حتى الآن',
 payments: 'المدفوعات', receipts: 'الإيصالات', avgBasket: 'متوسط السلة', noSalesYet: 'لا مبيعات اليوم بعد.',
 needs: 'يحتاج انتباهك', recent: 'آخر الإيصالات', seeAll: 'كل المبيعات', allGood: 'لا شيء يحتاج انتباهك الآن.',
 nLow: '{0} أصناف تحتاج إعادة طلب', nLowD: '{0} تحت الحد الأدنى و{1} نفدت.', seeStock: 'المخزون',
 nDebt: 'الزبائن مدينون بـ {0}', nDebtD: '{0} زبائن عليهم رصيد غير مدفوع.', nDebtD1: 'زبون واحد عليه رصيد غير مدفوع.', findSale: 'ابحث عن إيصال',
 shopQuiet: 'كمبيوتر المحل صامت منذ {0}', shopQuietD: 'المبيعات منذ ذلك الوقت تظهر هنا عند عودة الاتصال. الصندوق يتابع البيع.', seeShop: 'كمبيوتر المحل',
 salesSub: 'كل إيصال نسخته صناديقك إلى السحابة، الأحدث أولاً.', yesterday: 'أمس', byDay: 'المبيعات حسب اليوم', daysShown: 'آخر {0} أيام', daysShown1: 'ذلك اليوم', allPay: 'كل طرق الدفع', pickOne: 'اختر إيصالاً لترى ما بيع فيه.', walkIn: 'زبون عابر', nRcpt: '{0} إيصال', nRcpt1: 'إيصال واحد',
 backTo: 'الإيصالات معروضة حتى {0}.', older: 'حمّل إيصالات أقدم', loadingOlder: 'جارٍ تحميل إيصالات أقدم...', allHere: 'هذه كل الإيصالات المحفوظة في السحابة.',
 lookedTo: 'تم البحث في الإيصالات حتى {0}.', lookMore: 'ابحث في الأقدم', noReceipts: 'لا إيصالات بعد. تظهر هنا عندما يبيع صندوق.', findHint: 'رقم الإيصال أو الزبون',
 shownOf: 'يظهر {0} من {1}.', showMore: 'اعرض المزيد',
 all: 'الكل', cTime: 'الوقت', cId: 'الإيصال', cCust: 'الزبون', cItems: 'الأصناف', cPay: 'الدفع', cTot: 'المجموع',
 noMatch: 'لا نتائج.', rcpt: 'إيصال {0}', rMeta: '{0}، {1}', rTotal: 'المجموع', discount: 'الخصم', voided: 'ملغى', close: 'إغلاق', loadingItems: 'جارٍ تحميل الأصناف...', noItems: 'لم تُنسخ أصناف هذا الإيصال.',
 stockSub: 'الكميات حتى آخر نسخة. تتغير عندما يبيع صندوق.', find: 'ابحث عن صنف', low: 'منخفض', out: 'نفد', cName: 'الصنف', cQty: 'المتوفر', cMin: 'الحد الأدنى', cPrice: 'السعر',
 inStock: 'متوفر', lowS: 'منخفض', outS: 'نفد', min: 'أدنى',
 shopSub: 'هل يتحدث كمبيوتر المحل مع السحابة. الصندوق يتابع البيع إذا انقطع اتصاله.', lastContact: 'آخر اتصال', lastCopy: 'آخر نسخة من بياناتك', plan: 'خطة أونلاين', renews: 'تنتهي في {0}',
 sActive: 'فعّالة', sGrace: 'انتهت مؤخراً وما زالت تُحدَّث', sExpired: 'منتهية', sSuspended: 'موقوفة من المزوّد', sNone: 'لم تبدأ',
 planNote: 'إذا انتهت الخطة تتابع الصناديق البيع. يتوقف هذا الموقع عن التحديث بعد 7 أيام.', menuH: 'القائمة على الإنترنت', menuP: 'صفحة عامة فيها منتجاتك وصورها وأسعارها. شاركها برابط أو اطبع رمز QR للطاولات.', menuOpen: 'افتح محرر القائمة', contactOk: 'متصل', contactLate: 'صامت', contactOff: 'غير متصل',
 signTitle: 'تسجيل الدخول', signSub: 'شاهد محلك من هاتفك.', email: 'البريد الإلكتروني أو الهاتف أو اسم المستخدم', pass: 'كلمة المرور', show: 'إظهار', hide: 'إخفاء', signBtn: 'تسجيل الدخول', signing: 'جارٍ تسجيل الدخول',
 errEmail: 'أدخل بريدك الإلكتروني أو رقم هاتفك أو اسم المستخدم.', errPass: 'أدخل كلمة المرور.', noOnline: 'ليس لديك حساب بعد؟ اطلب من المزوّد إنشاء واحد لك.',
 lpH: 'شاهد الصندوق دون أن تقف عنده.', lpP: 'المبيعات والمخزون وحالة كمبيوتر المحل، منسوخة من الصندوق كل بضع دقائق.',
 lp1: 'ما بعته اليوم ومقارنته بالأسبوع الماضي', lp2: 'ما يوشك على النفاد', lp3: 'هل كمبيوتر المحل متصل',
 trust: 'بيانات محلك مقفلة بمفتاح لا يملكه غيرك. لا نستطيع قراءتها ولا هذا الخادم.',
 startTitle: 'ماذا تريد أن تفتح؟', startSub: 'دخلت إلى {0}.', startShop: 'محلك', startShopP: 'المبيعات والمخزون والديون من كمبيوتر المحل. يطلب رمز الاسترداد مرة واحدة في هذا المتصفح.',
 startMenu: 'القائمة على الإنترنت', startMenuP: 'قائمتك العامة: الأصناف والصور والأسعار ورمز QR للطاولات.', startBack: 'رجوع',
 unlockTitle: 'افتح بيانات محلك', unlockSub: 'اكتب رمز الاسترداد المطبوع من كمبيوتر المحل (الإعدادات ثم أونلاين). يبقى في هذا المتصفح ولا يُرسل إلينا.',
 code: 'رمز الاسترداد', unlockBtn: 'فتح', unlocking: 'جارٍ فحص الرمز', errInvalid: 'هذا ليس رمز استرداد. تحقق من الأخطاء؛ يتكون من 56 حرفاً ورقماً.',
 errWrong: 'هذا الرمز لا يفتح بيانات هذا المحل. ربما يخص محلاً آخر أو مفتاحاً قديماً.',
 hint: 'الشكل: ABCD-EFGH-IJKL-... (الشرطات والأحرف الكبيرة اختيارية)',
 errNoShop: 'لا يوجد كمبيوتر محل متصل بهذا الحساب بعد.', errPlan: 'خطة أونلاين غير فعّالة. تواصل مع المزوّد.', errNet: 'تعذر الوصول إلى السحابة. تحقق من الإنترنت وحاول مجدداً.',
 errRate: 'محاولات كثيرة. حاول بعد بضع دقائق.', errGeneric: 'حدث خطأ. حاول مجدداً.', errCreds: 'اسم الدخول أو كلمة المرور غير صحيحة.', errMayNot: 'حسابك غير مسموح له برؤية هذا.',
 cash: 'نقداً', ago: 'قبل {0}', min1: '{0} د', hr1: '{0} س', day1: '{0} ي', justNow: 'الآن', ready: 'البيانات مفتوحة في هذا المتصفح', retry: 'حاول مجدداً',
 changes: 'التغييرات', chSub: 'ما طلبته من كمبيوتر المحل. يطبّق كل تغيير عندما يكون متصلاً.',
 newProduct: 'منتج جديد', newCustomer: 'زبون جديد', newSupplier: 'مورّد جديد', editProduct: 'تعديل المنتج', adjust: 'تعديل المخزون',
 fName: 'الاسم', fBarcode: 'الباركود', fBarcodeH: 'اكتبه أو انقله من العبوة.', fCategory: 'الفئة', fSupplier: 'المورّد', fPrice: 'سعر البيع', fCost: 'التكلفة',
 fStock: 'المخزون الآن', fMin: 'نبّهني عند أقل من', fUnit: 'وحدة البيع (اختياري)', notSet: 'غير محددة', choose: 'اختر...',
 uPiece: 'قطعة', uKg: 'كيلوغرام', uLb: 'رطل', uLiter: 'لتر', uGallon: 'غالون', uBox: 'صندوق', uPack: 'عبوة',
 fFirst: 'الاسم الأول', fLast: 'اسم العائلة', fPhoneC: 'الهاتف (يُستخدم للتذكير)', fEmailO: 'البريد الإلكتروني (اختياري)', fAddrNote: 'العنوان أو ملاحظة (اختياري)',
 fCompany: 'الشركة أو الشخص', fPhone: 'الهاتف', fCity: 'المدينة (اختياري)', fAddr: 'العنوان (اختياري)', fBills: 'يفوترونك بعملة',
 fOnSale: 'معروض للبيع في الصندوق', fOnSaleH: 'أطفئه لإيقاف بيعه دون حذفه.', stockNote: 'يُعدَّل المخزون من "تعديل المخزون" ليُسجَّل كل تغيير.',
 mAdd: 'إضافة', mRemove: 'سحب', mCount: 'جرد', fQty: 'الكمية', fCounted: 'الكمية المعدودة', fReason: 'السبب (اختياري)', nowIn: 'المخزون الحالي: {0}', after: 'بعد هذا التغيير: {0}',
 eMin2: 'حرفان على الأقل.', eBarcode: 'أدخل الباركود.', eChoose: 'اختر واحدًا.', eAmount: 'أدخل مبلغًا أكبر من 0.', eQty: 'أدخل 0 أو أكثر.', eQtyPos: 'أدخل كمية أكبر من 0.',
 eEmail: 'هذا ليس بريدًا إلكترونيًا.', eCurrency: 'اختر العملة.', eNothing: 'لم يتغير شيء.', margin: 'الهامش {0} ({1}%)',
 send: 'أرسل إلى المحل', sending: 'جارٍ الإرسال', cancel: 'إلغاء', sent: 'أُرسل إلى المحل. يمكنك متابعته من التغييرات.',
 needKey: 'لإرسال تغييرات من هذا المتصفح، أدخل رمز الاسترداد مرة أخرى.', reenter: 'أدخل الرمز',
 stWait: 'بانتظار المحل', stDone: 'تم', stFix: 'يحتاج تعديلًا', stGone: 'تم السحب', withdraw: 'سحب', withdrawnT: 'تم السحب. لن يصل إلى كمبيوتر المحل.',
 kCustomer: 'زبون جديد', kSupplier: 'مورّد جديد', kProduct: 'منتج جديد', kEdit: 'تعديل منتج', kStock: 'تعديل مخزون', kOther: 'تغيير',
 hiddenC: 'تغيير مشفّر', hiddenD: 'مقفل بمفتاح لا يملكه هذا المتصفح.', noChanges: 'لم يُرسل شيء بعد. أضف منتجًا أو زبونًا أو مورّدًا بالأزرار أعلاه.',
 sName: 'الاسم: {0}', sPrice: 'السعر {0}', sCost: 'التكلفة {0}', sMin: 'الحد الأدنى {0}', sOn: 'معروض للبيع', sOff: 'متوقف', sAdd: 'إضافة {0}', sRemove: 'سحب {0}', sSet: 'جرد المخزون: {0}',
 cmdErr: 'تعذّر تحميل التغييرات.', product: 'منتج', pMin: 'الحد الأدنى', pPrice: 'السعر', pCost: 'التكلفة', pQty: 'المتوفر'
}};
var lang = 'en';
try { lang = localStorage.getItem('coduo-lang') || ((navigator.language || '').slice(0, 2) === 'ar' ? 'ar' : 'en'); } catch (e) {}
function t(k) { var s = D[lang][k] != null ? D[lang][k] : D.en[k]; for (var i = 1; i < arguments.length; i++) s = s.replace('{' + (i - 1) + '}', arguments[i]); return s; }
function loc() { return lang === 'ar' ? 'ar-u-nu-latn' : 'en-GB'; }

/* ---------- small helpers ---------- */
function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
function ic(id, c) { return '<svg class="ic ' + (c || '') + '" aria-hidden="true"><use href="#i-' + id + '"/></svg>'; }
function chip(k, i, txt) { return '<span class="chip ' + k + '">' + ic(i) + esc(txt) + '</span>'; }
function toNum(v) { if (typeof v === 'number') return v; var n = parseFloat(v == null ? '' : v); return isNaN(n) ? 0 : n; }
function flag(v) { return v === true || v === 1 || v === '1' || (typeof v === 'string' && v.toLowerCase() === 'true'); }
function text(v) { return v == null ? '' : String(v); }
function toInt(v) { if (v == null) return null; var n = parseInt(v, 10); return isNaN(n) ? null : n; }
/* The till stores UTC dates as text ("2026-11-07 17:23:57.1234567"). */
function utcDate(v) {
  if (v == null) return null;
  var s = String(v).trim().replace(' ', 'T'); if (!s) return null;
  var tail = s.length > 10 ? s.slice(10) : '';
  var hasZone = /Z$/.test(s) || /[+-]\d\d:?\d\d$/.test(tail);
  s = s.replace(/(\.\d{3})\d+/, '$1');
  var d = new Date(hasZone ? s : s + 'Z'); return isNaN(d) ? null : d;
}
function sameDay(a, b) { return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate(); }
function startOfDay(d) { return new Date(d.getFullYear(), d.getMonth(), d.getDate()); }
function timeStr(d) { return d.toLocaleTimeString(loc(), { hour: 'numeric', minute: '2-digit' }); }
function dateStr(d) { return d.toLocaleDateString(loc(), { weekday: 'long', day: 'numeric', month: 'long' }); }
function dayName(d) { return d.toLocaleDateString(loc(), { weekday: 'long' }); }
function agoStr(then, now) {
  var m = Math.max(0, Math.round(((now || new Date()) - then) / 60000));
  if (m < 1) return t('justNow');
  var span = m < 60 ? t('min1', m) : m < 1440 ? t('hr1', Math.round(m / 60)) : t('day1', Math.round(m / 1440));
  return t('ago', span);
}
function spanStr(then) { var m = Math.max(1, Math.round((new Date() - then) / 60000)); return m < 60 ? t('min1', m) : m < 1440 ? t('hr1', Math.round(m / 60)) : t('day1', Math.round(m / 1440)); }
function toast(msg) { var el = $('#toast'); el.firstChild.textContent = msg; el.classList.add('on'); clearTimeout(toast.h); toast.h = setTimeout(function () { el.classList.remove('on'); }, 3800); }
function isDark() { var r = document.documentElement; return r.dataset.theme ? r.dataset.theme === 'dark' : matchMedia('(prefers-color-scheme:dark)').matches; }

/* ---------- session and key storage ---------- */
function load(k) { try { return JSON.parse(localStorage.getItem(k)); } catch (e) { return null; } }
function save(k, v) { try { localStorage.setItem(k, JSON.stringify(v)); } catch (e) {} }
function drop(k) { try { localStorage.removeItem(k); } catch (e) {} }
var session = load('coduo-owner');
if (session && session.expiresAtUtc && new Date(session.expiresAtUtc) < new Date()) { session = null; drop('coduo-owner'); }

function keyDb() {
  return new Promise(function (res, rej) {
    var r = indexedDB.open('coduo-owner', 1);
    r.onupgradeneeded = function () { r.result.createObjectStore('keys'); };
    r.onsuccess = function () { res(r.result); };
    r.onerror = function () { rej(r.error); };
  });
}
function keyPut(k) { return keyDb().then(function (db) { return new Promise(function (res, rej) { var tx = db.transaction('keys', 'readwrite'); tx.objectStore('keys').put(k, 'shop'); tx.oncomplete = res; tx.onerror = function () { rej(tx.error); }; }); }); }
function keyGet() { return keyDb().then(function (db) { return new Promise(function (res) { var q = db.transaction('keys').objectStore('keys').get('shop'); q.onsuccess = function () { res(q.result || null); }; q.onerror = function () { res(null); }; }); }).catch(function () { return null; }); }
function keyDel() { return keyDb().then(function (db) { return new Promise(function (res) { var tx = db.transaction('keys', 'readwrite'); tx.objectStore('keys').delete('shop'); tx.oncomplete = res; tx.onerror = res; }); }).catch(function () {}); }

/* ---------- crypto: recovery code and rows ---------- */
var ALPHA = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
function parseCode(code) {
  var clean = String(code || '').toUpperCase().replace(/[\s-]/g, '').replace(/0/g, 'O').replace(/1/g, 'I').replace(/8/g, 'B');
  if (clean.length !== 56) return Promise.resolve(null);
  var out = [], buf = 0, bits = 0;
  for (var i = 0; i < clean.length; i++) {
    var v = ALPHA.indexOf(clean[i]); if (v < 0) return Promise.resolve(null);
    buf = (buf << 5) | v; bits += 5;
    if (bits >= 8) { out.push((buf >> (bits - 8)) & 255); bits -= 8; }
    buf &= (1 << bits) - 1;
  }
  if (out.length !== 35) return Promise.resolve(null);
  var keyBytes = new Uint8Array(out.slice(0, 32));
  return crypto.subtle.digest('SHA-256', keyBytes).then(function (h) {
    var sum = new Uint8Array(h).slice(0, 3), diff = 0;
    for (var j = 0; j < 3; j++) diff |= out[32 + j] ^ sum[j];
    return diff === 0 ? keyBytes : null;
  });
}
/* Encrypt is allowed too, for sealing changes. A key stored by an older version of this page can only decrypt (canSeal()). */
function importKey(bytes) { return crypto.subtle.importKey('raw', bytes, { name: 'AES-GCM' }, false, ['encrypt', 'decrypt']); }
function canSeal() { return !!(cryptoKey && cryptoKey.usages && cryptoKey.usages.indexOf('encrypt') > -1); }
function b64(s) { var bin = atob(s), a = new Uint8Array(bin.length); for (var i = 0; i < bin.length; i++) a[i] = bin.charCodeAt(i); return a; }
function b64e(bytes) { var s = ''; for (var i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000)); return btoa(s); }
/* "e1." + base64(nonce 12 | ciphertext | tag 16), with aad "<purpose or table>\n<context or id>": the phone app's ShopKey format. */
function openSealed(k, aad, payload) {
  if (typeof payload !== 'string' || payload.indexOf('e1.') !== 0) return Promise.reject(new Error('format'));
  var packed;
  try { packed = b64(payload.slice(3)); } catch (e) { return Promise.reject(new Error('format')); }
  if (packed.length < 28) return Promise.reject(new Error('format'));
  return crypto.subtle.decrypt({ name: 'AES-GCM', iv: packed.slice(0, 12), additionalData: new TextEncoder().encode(aad), tagLength: 128 }, k, packed.slice(12))
    .then(function (plain) { return JSON.parse(new TextDecoder().decode(plain)); }, function () { throw new Error('wrongkey'); });
}
function decryptRow(k, table, id, payload) { return openSealed(k, table + '\n' + id, payload); }
function seal(k, aad, obj) {
  var iv = crypto.getRandomValues(new Uint8Array(12));
  return crypto.subtle.encrypt({ name: 'AES-GCM', iv: iv, additionalData: new TextEncoder().encode(aad), tagLength: 128 }, k, new TextEncoder().encode(JSON.stringify(obj)))
    .then(function (ct) { var out = new Uint8Array(12 + ct.byteLength); out.set(iv, 0); out.set(new Uint8Array(ct), 12); return 'e1.' + b64e(out); });
}

/* ---------- cloud calls ---------- */
function ApiError(status, code, message) { this.status = status; this.code = code; this.message = message; }
function api(path) {
  var headers = { Accept: 'application/json' };
  if (session && session.token) headers.Authorization = 'Bearer ' + session.token;
  return fetch(API + path, { headers: headers }).then(handle, function () { throw new ApiError(0, 'network', t('errNet')); });
}
function handle(r) {
  if (r.status === 204) return null;
  return r.json().catch(function () { return {}; }).then(function (j) {
    if (r.ok) return j;
    if (r.status === 401 && session && session.token) { signOut(true); }
    throw new ApiError(r.status, j.code || '', j.message || '');
  });
}
function explain(e) {
  if (!(e instanceof ApiError)) return t('errGeneric');
  if (e.code === 'network') return t('errNet');
  if (e.status === 429) return t('errRate');
  if (e.code === 'no_shop') return t('errNoShop');
  if (e.status === 402) return t('errPlan');
  if (e.status === 403) return t('errMayNot');
  if (e.code === 'invalid_credentials') return t('errCreds');
  // The change endpoints explain themselves (a bad body, a change that can no longer be withdrawn).
  if (e.status >= 400 && e.status < 500 && e.message) return e.message;
  return t('errGeneric');
}
function call(method, path, body, idem) {
  var headers = { Accept: 'application/json' };
  if (session && session.token) headers.Authorization = 'Bearer ' + session.token;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (idem) headers['Idempotency-Key'] = idem;
  return fetch(API + path, { method: method, headers: headers, body: body === undefined ? undefined : JSON.stringify(body) })
    .then(handle, function () { throw new ApiError(0, 'network', t('errNet')); });
}

/* ---------- the shop's data in memory ---------- */
var KEEP = {
  Currencies: ['Name', 'Code', 'Notation', 'IsDefault', 'IsActive', 'IsDeleted'],
  PaymentMethods: ['Name', 'IsDefault', 'IsDeleted'],
  Products: ['Name', 'Barcode', 'StockQuantity', 'MinimumStock', 'SellingPrice', 'CostPrice', 'CurrencyId', 'CategoryId', 'SupplierId', 'Unit', 'IsActive', 'IsDeleted'],
  ProductCategories: ['Name', 'IsActive', 'IsDeleted'],
  Suppliers: ['Name', 'Phone', 'CurrencyId', 'OrderNumber', 'IsActive', 'IsDeleted'],
  Customers: ['FirstName', 'LastName', 'PhoneNumber', 'Balance', 'IsActive', 'IsDeleted'],
  Receipts: ['ReceiptNumber', 'ReceiptDate', 'TotalAmount', 'DiscountAmount', 'CurrencyId', 'CustomerName', 'PaymentMethodId', 'IsVoid', 'VoidReason', 'IsDeleted'],
  ReceiptItems: ['ReceiptId', 'ProductName', 'Quantity', 'Unit', 'UnitPrice', 'LineTotal', 'IsDeleted']
};
/* Tables read whole: they are the shop's lists, which stay small. Receipts and their lines only ever grow, so they are read
   differently (below): newest first a page at a time, and lines only for the receipt that is opened. 'Receipts' here marks when. */
var ORDER = ['Currencies', 'PaymentMethods', 'Receipts', 'Products', 'ProductCategories', 'Suppliers', 'Customers'];
var PAGE = 150;
/* Permission bits, the cloud's OwnerPermissions (the cloud enforces them; this only hides what a login cannot use). */
var P = { sales: 1, stock: 2, debts: 4, add: 8, edit: 16, financials: 32 };
function perms() { var p = me && me.permissions != null ? me.permissions : session && session.permissions; return p == null ? 0 : +p; }
function can(bit) { return (perms() & bit) !== 0; }
/* The shop runs the Basic licence plan: no stock tracking and no suppliers, so they are left out (the shop PC refuses those changes anyway).
   Unknown (an older shop PC or cloud) counts as Pro, which is what every shop was before plans. */
function basic() { return (shopPlan || (me && me.shopPlan)) === 'Basic'; }
var cryptoKey = null, rows = {}, cursors = {}, denied = {}, asOf = null, shopPlan = null, me = null, busy = false, loadError = null, feed, lines;
function freshFeed() { return { before: null, hasMore: true, oldest: null, loading: null, started: false }; }
function resetData() {
  rows = {}; cursors = {}; denied = {}; asOf = null; busy = false; loadError = null; Object.keys(KEEP).forEach(function (k) { rows[k] = {}; });
  feed = freshFeed();
  lines = { of: {}, seen: {}, done: {}, known: [] };
}
resetData();

function noted(p) { if (p.asOfUtc) asOf = new Date(p.asOfUtc); if (p.shopPlan) shopPlan = p.shopPlan; return p; }
/* Decrypts a page of rows, keeps the columns this site uses, and hands back [id, row] pairs. */
function openRows(table, list) {
  return Promise.all((list || []).map(function (r) {
    return decryptRow(cryptoKey, table, r.id, r.payload).then(function (plain) {
      var o = {}; KEEP[table].forEach(function (c) { o[c] = plain[c]; }); rows[table][r.id] = o; return [r.id, o];
    });
  }));
}
function refused(table) { return function (e) { if (e instanceof ApiError && e.status === 403) { denied[table] = true; return 'denied'; } throw e; }; }
/* Everything that arrived or changed since the last read (all of it the first time). maxPages: give up past that and say 'toolong'. */
function pullTable(table, maxPages) {
  var n = 0;
  function page() {
    if (maxPages && ++n > maxPages) return Promise.resolve('toolong');
    var q = '/rows?table=' + encodeURIComponent(table) + '&limit=1000' + (cursors[table] ? '&cursor=' + encodeURIComponent(cursors[table]) : '');
    return api(q).then(noted).then(function (p) {
      return openRows(table, p.rows).then(function () { cursors[table] = p.nextCursor; if (p.hasMore) return page(); });
    });
  }
  return page().catch(refused(table));
}

/* ---- receipts: newest first, a page at a time ----
   The cloud cannot search or sort what it cannot read, so it hands receipts out by the till's own numbering, newest first, and this
   page asks for older ones only when they are needed: the Today screen needs back to the same day last week, the Sales list more as
   you scroll or search. A refresh then asks only for what arrived or changed since (new receipts, voids), never the history again. */
function olderReceipts() {
  if (feed.loading) return feed.loading;
  if (!feed.hasMore || denied.Receipts) return Promise.resolve();
  var mine = feed, first = !feed.started;
  var q = '/rows?table=Receipts&newest=true&limit=' + PAGE + (feed.before != null ? '&before=' + feed.before : '');
  mine.loading = api(q).then(noted).then(function (p) {
    if (first) cursors.Receipts = p.changesCursor || null;
    return openRows('Receipts', p.rows).then(function (got) {
      if (feed !== mine) return;
      got.forEach(function (e) { var d = utcDate(e[1].ReceiptDate); if (d && (!mine.oldest || d < mine.oldest)) mine.oldest = d; });
      mine.started = true; mine.before = p.nextBefore; mine.hasMore = !!p.hasMore && got.length > 0;
    });
  }).catch(refused('Receipts')).then(function (r) { mine.loading = null; return r; }, function (e) { mine.loading = null; throw e; });
  return mine.loading;
}
/* Reads older pages until the oldest receipt held is from before `date` (or there are no more). */
function receiptsSince(date) {
  if (denied.Receipts || !feed.hasMore || (feed.oldest && feed.oldest < date)) return Promise.resolve();
  return olderReceipts().then(function () { return receiptsSince(date); });
}
function weekBack() { var d = startOfDay(new Date()); d.setDate(d.getDate() - 7); return d; }
function newReceipts() {
  if (!feed.started || !cursors.Receipts) { if (feed.started) { rows.Receipts = {}; feed = freshFeed(); } return receiptsSince(weekBack()); }
  // A whole new copy from the shop PC changes every row at once: then start over from the newest instead of reading it all.
  return pullTable('Receipts', 3).then(function (r) { if (r === 'toolong') { rows.Receipts = {}; feed = freshFeed(); return receiptsSince(weekBack()); } });
}
/* The receipts held in one unbroken run from the newest down (a change to an older one can arrive on its own: it waits its turn). */
function inRun(id) { return !feed.hasMore || feed.before == null || id >= feed.before; }

/* ---- a receipt's lines, fetched when it is opened ----
   Lines are numbered by the till as they are rung up, so a receipt's lines sit together, just above those of earlier receipts.
   The cloud cannot look a receipt up, so this finds the spot by halving the range with one-row reads (about 20 for a million lines),
   then reads the few pages around it. SLACK covers two tills ringing up at once. */
var SLACK = 3;
function linePage(before, limit) {
  return api('/rows?table=ReceiptItems&newest=true&limit=' + limit + (before != null ? '&before=' + before : '')).then(noted).then(function (p) {
    return openRows('ReceiptItems', p.rows).then(function (got) {
      got.forEach(function (e) {
        var rid = toInt(e[1].ReceiptId); lines.known.push([e[0], rid]);
        if (!lines.seen[e[0]]) { lines.seen[e[0]] = true; (lines.of[rid] = lines.of[rid] || []).push(e[0]); }
      });
      return { got: got, more: !!p.hasMore, next: p.nextBefore };
    });
  });
}
function linesFor(rid) {
  if (lines.done[rid]) return Promise.resolve();
  var top = rid + SLACK, lo = 0, hi = null;
  lines.known.forEach(function (k) { if (k[1] > top) { if (hi == null || k[0] < hi) hi = k[0]; } else if (k[0] > lo) lo = k[0]; });
  function narrow() {
    if (hi == null) return linePage(null, 1).then(function (r) {
      if (!r.got.length) return 'none';
      var last = r.got[0]; if (toInt(last[1].ReceiptId) <= top) { hi = last[0] + 1; return; }
      hi = last[0]; return narrow();
    });
    if (hi - lo <= 200) return Promise.resolve();
    var mid = Math.floor((lo + hi) / 2);
    return linePage(mid + 1, 1).then(function (r) {
      var p = r.got[0];
      if (p && p[0] > lo && toInt(p[1].ReceiptId) > top) hi = p[0]; else lo = mid;
      return narrow();
    });
  }
  function walk(before) {
    return linePage(before, 200).then(function (r) {
      var past = r.got.some(function (e) { return toInt(e[1].ReceiptId) < rid - SLACK; });
      if (r.more && !past) return walk(r.next);
    });
  }
  return narrow().then(function (x) { if (x !== 'none') return walk(hi); }).then(function () { lines.done[rid] = true; });
}

function refresh() {
  if (!cryptoKey || busy) return Promise.resolve();
  busy = true; loadError = null; render();
  var chain = api('/me').then(function (m) { me = m; render(); });
  ORDER.forEach(function (table) { chain = chain.then(function () { return table === 'Receipts' ? newReceipts() : pullTable(table); }).then(function () { render(); }); });
  return chain.catch(function (e) {
    if (e && e.message === 'wrongkey') { lockData(); return; }
    loadError = e;
  }).then(function () { busy = false; render(); });
}
function lockData() { cryptoKey = null; return keyDel().then(function () { resetData(); go('unlock'); }); }
function signOut(expired) {
  var token = session && session.token;
  session = null; cryptoKey = null; me = null; drop('coduo-owner'); resetData();
  if (token && !expired) fetch(API + '/auth/logout', { method: 'POST', headers: { Authorization: 'Bearer ' + token } }).catch(function () {});
  keyDel().then(function () { go('signin'); });
}

/* ---------- reports (same rules as the phone app) ---------- */
function live(table) { var out = []; for (var id in rows[table]) { var r = rows[table][id]; if (!flag(r.IsDeleted)) out.push([+id, r]); } return out; }
function currencies() {
  var by = {}, main = null;
  live('Currencies').forEach(function (e) {
    var r = e[1], c = { id: e[0], code: text(r.Code), notation: text(r.Notation) || text(r.Code), isDefault: flag(r.IsDefault) };
    by[e[0]] = c; if (c.isDefault) main = c;
  });
  var any = Object.keys(by)[0];
  main = main || (any ? by[any] : { id: null, code: '', notation: '', isDefault: true });
  return { by: by, main: main, of: function (id) { return by[id] || main; } };
}
function money(n, cur) {
  var dec = /^(USD|EUR|GBP|CHF)$/i.test(cur.code) ? 2 : (Math.abs(n) % 1 ? 2 : 0);
  var num = Math.abs(n).toLocaleString('en-US', { minimumFractionDigits: dec, maximumFractionDigits: dec });
  var sign = n < 0 ? '-' : '';
  return /^[$€£]$/.test(cur.notation) ? sign + cur.notation + num : sign + num + (cur.notation ? ' ' + cur.notation : '');
}
function methodName(id) { var m = rows.PaymentMethods[id]; return m ? text(m.Name) : ''; }
function todayReport(now) {
  var cur = currencies(), today = startOfDay(now), lastWeek = new Date(today); lastWeek.setDate(lastWeek.getDate() - 7);
  var hours = {}, lastHours = {}, pay = {}, others = {}, orders = 0, total = 0, lastTotalSoFar = 0, lastWeekAll = 0;
  live('Receipts').forEach(function (e) {
    var r = e[1]; if (flag(r.IsVoid)) return;
    var when = utcDate(r.ReceiptDate); if (!when) return;
    var amount = toNum(r.TotalAmount), cid = toInt(r.CurrencyId), isMain = cid == null || cid === cur.main.id || cur.main.id == null;
    if (sameDay(when, lastWeek) && isMain) {
      lastHours[when.getHours()] = (lastHours[when.getHours()] || 0) + amount; lastWeekAll += amount;
      if (when.getHours() < now.getHours() || (when.getHours() === now.getHours() && when.getMinutes() <= now.getMinutes())) lastTotalSoFar += amount;
    }
    if (!sameDay(when, today)) return;
    orders++;
    if (isMain) { total += amount; hours[when.getHours()] = (hours[when.getHours()] || 0) + amount; var m = methodName(toInt(r.PaymentMethodId)); if (m) pay[m] = (pay[m] || 0) + amount; }
    else { var n = cur.of(cid).notation; others[n] = (others[n] || 0) + amount; }
  });
  var first = Math.min(8, now.getHours()); Object.keys(hours).forEach(function (h) { first = Math.min(first, +h); });
  var last = Math.max(20, now.getHours()); Object.keys(lastHours).forEach(function (h) { last = Math.max(last, +h); });
  var series = []; for (var h = first; h <= last; h++) series.push({ h: h, today: h <= now.getHours() ? (hours[h] || 0) : null, last: lastHours[h] || 0 });
  return {
    cur: cur, total: total, orders: orders, series: series, lastWeek: lastWeek,
    pay: Object.keys(pay).map(function (k) { return [k, pay[k]]; }).sort(function (a, b) { return b[1] - a[1]; }),
    others: Object.keys(others).map(function (k) { return [k, others[k]]; }),
    pct: lastTotalSoFar > 0 ? (total - lastTotalSoFar) / lastTotalSoFar * 100 : null
  };
}
function sales(limit) {
  var cur = currencies();
  var list = live('Receipts').filter(function (e) { return inRun(e[0]); }).map(function (e) {
    var r = e[1], cid = toInt(r.CurrencyId);
    return { id: e[0], number: text(r.ReceiptNumber), when: utcDate(r.ReceiptDate), total: toNum(r.TotalAmount), discount: toNum(r.DiscountAmount), cur: cur.of(cid), customer: text(r.CustomerName), pay: methodName(toInt(r.PaymentMethodId)), isVoid: flag(r.IsVoid), voidReason: text(r.VoidReason) };
  }).sort(function (a, b) { return (b.when ? b.when.getTime() : 0) - (a.when ? a.when.getTime() : 0); });
  return limit ? list.slice(0, limit) : list;
}
function stockItems() {
  var cur = currencies(), out = [];
  live('Products').forEach(function (e) {
    var p = e[1]; if (!flag(p.IsActive)) return;
    var q = toNum(p.StockQuantity), min = p.MinimumStock == null ? null : toNum(p.MinimumStock);
    out.push({ id: e[0], name: text(p.Name), barcode: text(p.Barcode), qty: q, min: min, unit: text(p.Unit), price: toNum(p.SellingPrice), cur: cur.of(toInt(p.CurrencyId)),
      level: q <= 0 ? 0 : (min != null && min > 0 && q <= min ? 1 : 2) });
  });
  out.sort(function (a, b) { return a.level - b.level || a.name.toLowerCase().localeCompare(b.name.toLowerCase()); });
  return out;
}
function debts() {
  var cur = currencies().main, n = 0, sum = 0;
  live('Customers').forEach(function (e) { var b = toNum(e[1].Balance); if (b > 0) { n++; sum += b; } });
  return { n: n, sum: sum, cur: cur };
}
function freshness() {
  var seen = me && me.shopLastSeenUtc ? new Date(me.shopLastSeenUtc) : null, minutes = seen ? (new Date() - seen) / 60000 : null;
  return { seen: seen, state: seen == null ? 'off' : minutes <= 10 ? 'ok' : minutes <= 60 ? 'late' : 'off' };
}

/* ---------- router ---------- */
var state = { salesFilter: 'all', stockFilter: 'all', stockQ: '', salesQ: '', salesLook: 4, stockShow: 200, sel: null };
var APP_ROUTES = ['today', 'sales', 'stock', 'changes', 'shop'];
function route() {
  var h = (location.hash || '').slice(1);
  if (!session) return 'signin';
  // A shop with no POS (no till has ever reached us) only has its online menu: that is where it goes.
  if (session.hasTill === false) return 'menu';
  // Not unlocked here yet: first a choice between the shop's data (needs the recovery code) and the menu (does not).
  if (!cryptoKey) return h === 'unlock' || !can(P.edit) ? 'unlock' : 'start';
  return APP_ROUTES.indexOf(h) > -1 ? h : 'today';
}
var MENU_URL = '/owner/menu.html';
function go(r) { if (location.hash.slice(1) === r) render(); else location.hash = '#' + r; }

function topBar() {
  return '<div class="grp"></div><div class="grp"><div class="seg" role="group" aria-label="Language">' +
    '<button type="button" data-lang="en" aria-pressed="' + (lang === 'en') + '">EN</button>' +
    '<button type="button" data-lang="ar" aria-pressed="' + (lang === 'ar') + '" lang="ar">العربية</button></div>' +
    '<div class="seg"><button type="button" id="themeBtn" aria-pressed="' + isDark() + '">' + ic('moon', 'sm') + t('dark') + '</button></div></div>';
}

/* ---------- sign in ---------- */
function signinView() {
  var pts = ['lp1', 'lp2', 'lp3'].map(function (k) { return '<div class="nr">' + ic('check') + '<b>' + t(k) + '</b></div>'; }).join('');
  return '<div class="login"><section class="lf"><div class="lgrow">' + BRAND.logo + '<span class="chip info">' + t('online') + '</span></div>' +
    '<form id="lform" novalidate><h1>' + t('signTitle') + '</h1><p class="muted" style="margin-bottom:22px">' + t('signSub') + '</p>' +
    '<div class="fld"><label for="em">' + t('email') + '</label><input class="inp" id="em" type="text" dir="ltr" autocomplete="username" autocapitalize="off" spellcheck="false" aria-describedby="emerr"><div id="emerr" class="err" role="alert" hidden>' + ic('alert', 'sm') + t('errEmail') + '</div></div>' +
    '<div class="fld"><label for="pw">' + t('pass') + '</label><div class="pw"><input class="inp" id="pw" type="password" autocomplete="current-password" aria-describedby="pwerr"><button type="button" id="pwt" aria-controls="pw">' + t('show') + '</button></div><div id="pwerr" class="err" role="alert" hidden>' + ic('alert', 'sm') + t('errPass') + '</div></div>' +
    '<div id="formerr" class="err" role="alert" hidden style="margin-bottom:12px"></div>' +
    '<button class="btn pri" id="sbtn" type="submit">' + t('signBtn') + '</button></form>' +
    '<p class="foot">' + t('noOnline') + '</p></section>' +
    '<section class="lp"><h2>' + t('lpH') + '</h2><p class="lead">' + t('lpP') + '</p><div class="now">' + pts + '</div>' +
    '<p class="trust">' + ic('shield') + '<span>' + t('trust') + '</span></p></section></div>';
}
function bindSignin() {
  var em = $('#em'), pw = $('#pw');
  $('#pwt').onclick = function () { var s = pw.type === 'password'; pw.type = s ? 'text' : 'password'; this.textContent = s ? t('hide') : t('show'); };
  $('#lform').onsubmit = function (e) {
    e.preventDefault();
    var bad = false; $('#emerr').hidden = !!em.value.trim(); $('#pwerr').hidden = !!pw.value; em.toggleAttribute('aria-invalid', !em.value.trim()); pw.toggleAttribute('aria-invalid', !pw.value);
    if (!em.value.trim()) { em.focus(); bad = true; } else if (!pw.value) { pw.focus(); bad = true; }
    if (bad) return;
    var b = $('#sbtn'), fe = $('#formerr'); fe.hidden = true; b.disabled = true; b.textContent = t('signing') + '...';
    fetch(API + '/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: em.value.trim(), password: pw.value }) })
      .then(handle, function () { throw new ApiError(0, 'network', t('errNet')); })
      .then(function (j) { session = j; save('coduo-owner', j); return keyGet(); })
      .then(function (k) {
        if (session.hasTill === false) { location.replace(MENU_URL); return; }
        if (k) { cryptoKey = k; refresh(); go('today'); } else go(can(P.edit) ? 'start' : 'unlock');
      })
      .catch(function (err) { fe.innerHTML = ic('alert', 'sm') + esc(explain(err)); fe.hidden = false; b.disabled = false; b.textContent = t('signBtn'); });
  };
}

/* ---------- start: the shop's data or its online menu ---------- */
function startView() {
  var pick = function (href, icon, title, text) {
    return '<a class="pick" href="' + href + '">' + '<span class="pi">' + ic(icon) + '</span><span><b>' + title + '</b><small>' + text + '</small></span>' + ic('next', 'flip') + '</a>';
  };
  return '<div class="login"><section class="lf"><div class="lgrow">' + BRAND.logo + '<span class="chip info">' + t('online') + '</span></div>' +
    '<div><h1>' + t('startTitle') + '</h1><p class="muted" style="margin-bottom:22px">' + esc(t('startSub', session.clientName || '')) + '</p>' +
    '<div class="picks">' + pick('#unlock', 'key', t('startShop'), t('startShopP')) + pick(MENU_URL, 'menu', t('startMenu'), t('startMenuP')) + '</div></div>' +
    '<p class="foot"><button type="button" class="linkbtn" id="sout">' + t('signout') + '</button></p></section>' +
    '<section class="lp"><h2>' + t('lpH') + '</h2><p class="lead">' + t('trust') + '</p></section></div>';
}

/* ---------- unlock ---------- */
function unlockView() {
  return '<div class="login"><section class="lf"><div class="lgrow">' + BRAND.logo + '<span class="chip info">' + t('online') + '</span></div>' +
    '<form id="uform" novalidate><h1>' + t('unlockTitle') + '</h1><p class="muted" style="margin-bottom:22px;max-width:46ch">' + t('unlockSub') + '</p>' +
    '<div class="fld"><label for="code">' + t('code') + '</label><input class="inp mono" id="code" type="text" autocomplete="off" autocapitalize="characters" spellcheck="false" dir="ltr" aria-describedby="cerr chint"><p class="help" id="chint">' + t('hint') + '</p><div id="cerr" class="err" role="alert" hidden></div></div>' +
    '<button class="btn pri" id="ubtn" type="submit">' + t('unlockBtn') + '</button></form>' +
    '<p class="foot">' + (can(P.edit) ? '<a class="linkbtn" href="#start">' + t('startBack') + '</a> · ' : '') + '<button type="button" class="linkbtn" id="uout">' + t('signout') + '</button></p></section>' +
    '<section class="lp"><h2>' + t('lpH') + '</h2><p class="lead">' + t('trust') + '</p></section></div>';
}
function bindUnlock() {
  var code = $('#code'), err = $('#cerr');
  $('#uout').onclick = function () { signOut(); };
  function fail(msg) { err.innerHTML = ic('alert', 'sm') + esc(msg); err.hidden = false; code.setAttribute('aria-invalid', 'true'); $('#ubtn').disabled = false; $('#ubtn').textContent = t('unlockBtn'); code.focus(); }
  $('#uform').onsubmit = function (e) {
    e.preventDefault(); err.hidden = true; code.removeAttribute('aria-invalid');
    var b = $('#ubtn'); b.disabled = true; b.textContent = t('unlocking') + '...';
    parseCode(code.value).then(function (bytes) {
      if (!bytes) return fail(t('errInvalid'));
      return importKey(bytes).then(function (k) {
        /* Check the key against the shop's own data before keeping it, so a wrong code is refused instead of showing blanks. */
        var tables = ['Currencies', 'Products', 'Customers', 'Receipts'], i = 0;
        function next() {
          if (i >= tables.length) return Promise.resolve('unverified');
          var table = tables[i++];
          return api('/rows?table=' + table + '&limit=1').then(function (p) {
            if (!p.rows || !p.rows.length) return next();
            return decryptRow(k, table, p.rows[0].id, p.rows[0].payload).then(function () { return 'ok'; }, function () { return 'wrong'; });
          }, function (e) { if (e instanceof ApiError && e.status === 403) return next(); throw e; });
        }
        return next().then(function (r) {
          if (r === 'wrong') return fail(t('errWrong'));
          cryptoKey = k; return keyPut(k).then(function () { resetData(); refresh(); go('today'); });
        });
      });
    }).catch(function (e2) { fail(explain(e2)); });
  };
}

/* ---------- app shell ---------- */
var NAV = [['today', 'today'], ['sales', 'sales'], ['stock', 'stock'], ['changes', 'changes'], ['shop', 'tills']];
function shell(r, inner) {
  var nav = NAV.filter(function (n) { return n[0] !== 'changes' || can(P.add) || can(P.edit); });
  var label = function (n) { return t(n[0] === 'stock' && basic() ? 'products' : n[0]); };
  var rail = nav.map(function (n) { return '<a class="nav" href="#' + n[0] + '"' + (r === n[0] ? ' aria-current="page"' : '') + '>' + ic(n[1]) + label(n) + '</a>'; }).join('');
  var tabs = nav.map(function (n) { return '<a href="#' + n[0] + '"' + (r === n[0] ? ' aria-current="page"' : '') + '>' + ic(n[1]) + label(n) + '</a>'; }).join('');
  var name = session.clientName || '', initials = (session.displayName || name || '?').split(/\s+/).map(function (w) { return w[0]; }).join('').slice(0, 2).toUpperCase();
  return '<div class="mtop"><div class="brand">' + BRAND.mark + '<span><b>Coduo POS</b><small>' + esc(name) + '</small></span></div><button type="button" id="mout" aria-label="' + t('signout') + '">' + ic('out') + '</button></div>' +
    '<div class="app"><aside class="rail"><div class="brand">' + BRAND.mark + '<span><b>Coduo POS</b><small>' + t('online') + '</small></span></div>' +
    '<nav aria-label="Main" style="display:flex;flex-direction:column;gap:4px">' + rail + '</nav><div class="sp"></div>' +
    '<div class="me"><span class="av" aria-hidden="true">' + esc(initials) + '</span><span style="flex:1;min-width:0">' + esc(session.displayName || '') + '<small>' + esc(name) + '</small></span></div>' +
    '<button type="button" class="nav" id="rlock" style="background:none;border:0;width:100%;text-align:start;cursor:pointer">' + ic('key') + t('lock') + '</button>' +
    '<button type="button" class="nav" id="rout" style="background:none;border:0;width:100%;text-align:start;cursor:pointer">' + ic('out') + t('signout') + '</button></aside>' +
    '<section class="main" id="mainc" tabindex="-1">' + banner() + inner + '</section></div><nav class="tabbar" aria-label="Main">' + tabs + '</nav>';
}
function banner() {
  if (loadError) return '<div class="banner bad" role="alert">' + ic('alert') + '<span>' + esc(explain(loadError)) + '</span><button type="button" class="btn sm" id="retry">' + t('retry') + '</button></div>';
  if (busy) return '<div class="banner" role="status">' + ic('refresh') + '<span>' + t('updating') + '</span></div>';
  return '';
}
function copyChip() {
  if (!asOf) return chip('mute', 'clock', t('never'));
  var m = (new Date() - asOf) / 60000;
  return chip(m <= 15 ? 'good' : m <= 120 ? 'warn' : 'bad', m <= 15 ? 'check' : 'alert', t('copyAt', agoStr(asOf)));
}

/* Today */
function niceTop(max) {
  if (max <= 0) return 100;
  var step = max / 4, p = Math.pow(10, Math.floor(Math.log10(step))), m = [1, 2, 2.5, 5, 10].filter(function (x) { return x * p >= step; })[0];
  return m * p * 4;
}
function todayView() {
  var now = new Date(), rep = todayReport(now), cur = rep.cur.main, g = now.getHours() < 12 ? 'greetM' : now.getHours() < 18 ? 'greetA' : 'greetE';
  var maxV = 0; rep.series.forEach(function (s) { maxV = Math.max(maxV, s.today || 0, s.last); });
  var top = niceTop(maxV);
  var ticks = [0, 1, 2, 3, 4].map(function (i) { return top / 4 * i; });
  var ylab = '', grid = '';
  ticks.forEach(function (v) { ylab += '<span style="bottom:' + (v / top * 100) + '%">' + Math.round(v).toLocaleString('en-US') + '</span>'; grid += '<div class="gl" style="bottom:' + (v / top * 100) + '%"></div>'; });
  var n = rep.series.length, cols = '', xl = '';
  rep.series.forEach(function (s) {
    var cur2 = s.h === now.getHours();
    cols += '<div class="col">' + (s.today != null && s.today > 0 ? '<div class="bar' + (cur2 ? ' part' : '') + '" style="height:' + (s.today / top * 100) + '%"></div>' : '') +
      (s.last > 0 ? '<div class="tick' + (s.today == null ? ' fut' : '') + '" style="bottom:' + (s.last / top * 100) + '%"></div>' : '') + '</div>';
    xl += '<span>' + (s.h < 10 ? '0' : '') + s.h + '</span>';
  });
  var payTotal = rep.pay.reduce(function (a, p) { return a + p[1]; }, 0), colors = ['var(--c1)', 'var(--c2)', 'var(--c3)', 'var(--muted)'];
  var stack = rep.pay.map(function (p, i) { return '<i style="width:' + (p[1] / payTotal * 100) + '%;background:' + colors[Math.min(i, 3)] + '"></i>'; }).join('');
  var prow = rep.pay.map(function (p, i) { return '<div class="pr"><span class="sw" style="background:' + colors[Math.min(i, 3)] + '"></span><span class="nm">' + esc(p[0]) + '</span><span class="pc num">' + (p[1] / payTotal * 100).toFixed(1) + '%</span><span class="am num">' + money(p[1], cur) + '</span></div>'; }).join('');
  var rec = sales(5).map(function (s) { return '<button type="button" class="rrow" data-sale="' + s.id + '"><span class="tm num">' + (s.when ? timeStr(s.when) : '') + '</span><span class="id">' + esc(s.number) + '</span>' + (s.pay ? '<span class="chip mute">' + esc(s.pay) + '</span>' : '') + '<span class="tot num">' + money(s.total, s.cur) + '</span></button>'; }).join('');
  var stk = stockItems(), low = stk.filter(function (s) { return s.level === 1; }).length, outN = stk.filter(function (s) { return s.level === 0; }).length, db = debts(), fr = freshness(), att = '';
  if (fr.state !== 'ok') att += '<div class="ai"><span class="ico ' + (fr.state === 'off' ? 'bad' : 'warn') + '">' + ic('alert') + '</span><div class="tx"><b>' + (fr.seen ? t('shopQuiet', spanStr(fr.seen)) : t('never')) + '</b><p>' + t('shopQuietD') + '</p></div><a href="#shop">' + t('seeShop') + '</a></div>';
  if (low + outN > 0 && !basic()) att += '<div class="ai"><span class="ico warn">' + ic('stock') + '</span><div class="tx"><b>' + t('nLow', low + outN) + '</b><p>' + t('nLowD', low, outN) + '</p></div><a href="#stock">' + t('seeStock') + '</a></div>';
  if (db.n > 0) att += '<div class="ai"><span class="ico info">' + ic('users') + '</span><div class="tx"><b>' + t('nDebt', money(db.sum, db.cur)) + '</b><p>' + (db.n === 1 ? t('nDebtD1') : t('nDebtD', db.n)) + '</p></div></div>';
  if (!att) att = '<p class="muted">' + t('allGood') + '</p>';
  var delta = rep.pct == null ? '' : chip(rep.pct >= 0 ? 'good' : 'warn', rep.pct >= 0 ? 'up' : 'down', '‎' + (rep.pct >= 0 ? '+' : '') + rep.pct.toFixed(1) + '%');
  var others = rep.others.map(function (o) { return '<div class="lbp">+ ' + esc(o[1].toLocaleString('en-US', { maximumFractionDigits: 2 })) + ' ' + esc(o[0]) + '</div>'; }).join('');
  var lw = dayName(rep.lastWeek);
  var inner = '<header class="head"><div><h1>' + t(g) + (session.displayName ? ', ' + esc(session.displayName.split(' ')[0]) : '') + '</h1><p>' + dateStr(now) + '</p></div><div style="display:flex;gap:8px;flex-wrap:wrap;align-items:center">' + copyChip() + '<button type="button" class="btn sm" id="rfr">' + ic('refresh', 'sm') + t('refresh') + '</button></div></header>' +
    '<div class="g"><section class="panel s8" aria-labelledby="h-sales"><div class="ph"><h2 id="h-sales">' + t('salesToday') + '</h2>' + delta + '</div>' +
    '<div class="big">' + money(rep.total, cur) + '</div>' + others + (rep.pct == null ? '' : '<p class="muted" style="font-size:13.5px;margin-top:6px">' + t('vsLast', '<bdi dir="ltr">' + (rep.pct >= 0 ? '+' : '') + rep.pct.toFixed(1) + '%</bdi>', esc(lw)) + '</p>') +
    (rep.orders === 0 ? '<p class="empty">' + t('noSalesYet') + '</p>' : '') +
    '<div class="chart" dir="ltr" style="--n:' + n + '" role="img" aria-label="' + t('salesToday') + ': ' + esc(money(rep.total, cur)) + '"><div class="yax">' + ylab + '</div><div class="plot">' + grid + '<div class="cols" style="grid-template-columns:repeat(' + n + ',minmax(0,1fr))">' + cols + '</div></div><div class="xl" style="grid-template-columns:repeat(' + n + ',minmax(0,1fr))">' + xl + '</div></div>' +
    '<div class="legend"><span><i class="lg-bar"></i>' + t('legToday') + '</span><span><i class="lg-tick"></i>' + t('legLast', esc(lw)) + '</span></div></section>' +
    '<section class="panel s4" aria-labelledby="h-pay"><div class="ph"><h2 id="h-pay">' + t('payments') + '</h2></div>' + (rep.pay.length ? '<div class="stack">' + stack + '</div><div class="pay">' + prow + '</div>' : '<p class="muted">' + t('noSalesYet') + '</p>') +
    '<div class="kv"><div><b class="num">' + rep.orders + '</b><span>' + t('receipts') + '</span></div><div><b class="num">' + (rep.orders ? money(rep.total / rep.orders, cur) : '-') + '</b><span>' + t('avgBasket') + '</span></div></div></section>' +
    '<section class="panel s7" aria-labelledby="h-need"><div class="ph"><h2 id="h-need">' + t('needs') + '</h2></div><div class="att">' + att + '</div></section>' +
    '<section class="panel s5" aria-labelledby="h-rec"><div class="ph"><h2 id="h-rec">' + t('recent') + '</h2></div><div class="rl">' + (rec || '<p class="muted">' + t('noSalesYet') + '</p>') + '</div><a class="more" href="#sales">' + t('seeAll') + ic('arrow', 'sm') + '</a></section></div>';
  return shell('today', inner);
}

/* Sales */
function salesFiltered() { return !!state.salesQ.trim() || state.salesFilter !== 'all'; }
function dayLabel(d) {
  var today = startOfDay(new Date()), y = new Date(today); y.setDate(y.getDate() - 1);
  var full = d.toLocaleDateString(loc(), { weekday: 'short', day: 'numeric', month: 'short' });
  if (sameDay(d, today)) return '<b>' + t('legToday') + '</b><span>' + esc(full) + '</span>';
  if (sameDay(d, y)) return '<b>' + t('yesterday') + '</b><span>' + esc(full) + '</span>';
  return '<b>' + esc(d.toLocaleDateString(loc(), { weekday: 'long' })) + '</b><span>' + esc(d.toLocaleDateString(loc(), { day: 'numeric', month: 'short', year: d.getFullYear() === today.getFullYear() ? undefined : 'numeric' })) + '</span>';
}
function compact(n) { return n >= 1000 ? new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 1 }).format(n) : Math.round(n).toLocaleString('en-US'); }
/* Wide screens show the list and the chosen receipt side by side; narrower ones open the receipt as a sheet. */
function wideSales() { return matchMedia('(min-width:1180px)').matches; }
function salesView() {
  var all = sales(), main = currencies().main;
  // The filter offers the shop's own payment methods (plus any a receipt names that is no longer on the list).
  var pays = live('PaymentMethods').map(function (e) { return text(e[1].Name); }).filter(Boolean);
  all.forEach(function (s) { if (s.pay && pays.indexOf(s.pay) < 0) pays.push(s.pay); });
  if (state.salesFilter !== 'all' && pays.indexOf(state.salesFilter) < 0) state.salesFilter = 'all';
  var seg = ['all'].concat(pays).map(function (k) { return '<button type="button" data-sf="' + esc(k) + '" aria-pressed="' + (state.salesFilter === k) + '">' + (k === 'all' ? t('all') : esc(k)) + '</button>'; }).join('');
  var q = state.salesQ.trim().toLowerCase();
  var list = all.filter(function (s) { return (state.salesFilter === 'all' || s.pay === state.salesFilter) && (!q || (s.number + ' ' + s.customer).toLowerCase().indexOf(q) > -1); });

  // One group per day, with the day's count and takings (voids left out), so the list reads like the till's own day reports.
  var groups = [], g = null;
  list.forEach(function (s) {
    var day = s.when ? startOfDay(s.when).getTime() : 0;
    if (!g || g.day !== day) { g = { day: day, items: [], total: 0, n: 0 }; groups.push(g); }
    g.items.push(s);
    if (!s.isVoid) { g.n++; if (s.cur.id === main.id || main.id == null) g.total += s.total; }
  });
  var lastPartial = function (i) { return i === groups.length - 1 && feed.hasMore; };

  var wide = wideSales();
  if (wide && list.length && !list.some(function (s) { return s.id === state.sel; })) state.sel = list[0].id;
  if (!wide) state.sel = null;

  // Takings by day: the newest fortnight held, oldest on the start side. A bar is also the way to that day in the list.
  var strip = groups.filter(function (gr) { return gr.day; }).slice(0, 14).reverse(), top = 0;
  strip.forEach(function (gr) { top = Math.max(top, gr.total); });
  var bars = strip.map(function (gr) {
    var d = new Date(gr.day), now = sameDay(d, new Date()), part = lastPartial(groups.indexOf(gr));
    return '<button type="button" class="dbar' + (now ? ' now' : '') + (part ? ' part' : '') + '" data-day="' + gr.day + '" style="--h:' + (top ? gr.total / top : 0).toFixed(3) + '" aria-label="' + esc(d.toLocaleDateString(loc(), { weekday: 'long', day: 'numeric', month: 'long' }) + ': ' + money(gr.total, main)) + '">' +
      '<span class="v num">' + compact(gr.total) + '</span><span class="b"></span><span class="w">' + esc(now ? t('legToday') : d.toLocaleDateString(loc(), { weekday: 'short' })) + '</span><span class="n num">' + d.getDate() + '</span></button>';
  }).join('');
  // The period totals leave out the oldest day while it may still be missing receipts (unless it is the only one).
  var newest = groups[0], sum = 0, cnt = 0, days = 0;
  groups.forEach(function (gr, i) { if (!lastPartial(i) || groups.length === 1) { sum += gr.total; cnt += gr.n; days++; } });
  var band = strip.length ? '<section class="panel band" aria-labelledby="h-days">' +
    '<div class="kpis"><div><span>' + (newest && sameDay(new Date(newest.day), new Date()) ? t('legToday') : esc(new Date(newest.day).toLocaleDateString(loc(), { weekday: 'long' }))) + '</span><b class="num">' + money(newest.total, main) + '</b><small>' + (newest.n === 1 ? t('nRcpt1') : t('nRcpt', newest.n)) + '</small></div>' +
    '<div><span>' + (days === 1 ? t('daysShown1') : t('daysShown', days)) + '</span><b class="num">' + money(sum, main) + '</b><small>' + (cnt === 1 ? t('nRcpt1') : t('nRcpt', cnt)) + '</small></div>' +
    '<div><span>' + t('avgBasket') + '</span><b class="num">' + (cnt ? money(sum / cnt, main) : '-') + '</b><small>' + esc(state.salesFilter === 'all' ? t('allPay') : state.salesFilter) + '</small></div></div>' +
    '<div class="dstrip" dir="ltr"><h2 id="h-days" class="sr">' + t('byDay') + '</h2>' + bars + '</div></section>' : '';

  var body = groups.map(function (gr, i) {
    var head = '<div class="dayh" id="d-' + gr.day + '"><span class="dl">' + (gr.day ? dayLabel(new Date(gr.day)) : '<b>-</b>') + '</span>' +
      '<span class="dn num">' + (gr.n === 1 ? t('nRcpt1') : t('nRcpt', gr.n)) + '</span><span class="dt num">' + money(gr.total, main) + (lastPartial(i) ? ' <small>' + t('soFar') + '</small>' : '') + '</span></div>';
    return head + gr.items.map(function (s) {
      return '<button type="button" class="srow' + (s.isVoid ? ' void' : '') + (s.id === state.sel ? ' sel' : '') + '" data-sale="' + s.id + '"' + (s.id === state.sel ? ' aria-current="true"' : '') + '>' +
        '<span class="c-time num">' + (s.when ? esc(timeStr(s.when)) : '') + '</span>' +
        '<span class="c-who"><span class="c-id mono" dir="ltr">' + esc(s.number) + '</span><span class="c-cust' + (s.customer ? '' : ' none') + '">' + esc(s.customer || t('walkIn')) + '</span></span>' +
        '<span class="c-pay">' + (s.isVoid ? chip('bad', 'alert', t('voided')) : s.pay ? '<span class="pdot" data-p="' + Math.max(0, pays.indexOf(s.pay)) % 4 + '"></span>' + esc(s.pay) : '') + '</span>' +
        '<span class="c-tot num">' + money(s.total, s.cur) + '</span></button>';
    }).join('');
  }).join('');

  var back = feed.oldest ? esc(feed.oldest.toLocaleDateString(loc(), { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' })) : '';
  var foot;
  if (denied.Receipts) foot = '<p>' + t('errMayNot') + '</p>';
  else if (feed.loading) foot = '<p class="ld">' + ic('refresh', 'sm spin') + t('loadingOlder') + '</p>';
  else if (!feed.hasMore) foot = feed.started && all.length ? '<p>' + t('allHere') + '</p>' : '';
  else foot = '<p>' + (salesFiltered() ? t('lookedTo', back) : t('backTo', back)) + '</p><button type="button" class="btn sm" id="older">' + t(salesFiltered() ? 'lookMore' : 'older') + '</button>';

  var empty = !list.length && !feed.loading && !busy ? '<p class="empty">' + (all.length ? t('noMatch') : t('noReceipts')) + '</p>' : '';
  var chosen = wide && state.sel != null ? all.filter(function (s) { return s.id === state.sel; })[0] : null;
  var pane = wide ? '<aside class="rpane" aria-label="' + t('cId') + '">' + (chosen ? receiptCard(chosen, lines.done[chosen.id] ? itemsOf(chosen.id) : null) : '<div class="slip"><p class="muted">' + t('pickOne') + '</p></div>') + '</aside>' : '';

  var inner = '<header class="head"><div><h1>' + t('sales') + '</h1><p>' + t('salesSub') + '</p></div><div class="hacts">' + copyChip() + '<button type="button" class="btn sm" id="rfr">' + ic('refresh', 'sm') + t('refresh') + '</button></div></header>' +
    band +
    '<div class="tools"><div class="srch"><label class="sr" for="sq">' + t('findSale') + '</label>' + ic('search', 'sm') + '<input id="sq" type="search" placeholder="' + t('findHint') + '" value="' + esc(state.salesQ) + '" autocomplete="off"></div>' +
    '<div class="segf" role="group" aria-label="' + t('cPay') + '">' + seg + '</div></div>' +
    '<div class="split' + (wide ? ' two' : '') + '"><section class="panel ledger" aria-label="' + t('sales') + '">' +
    body + empty + (foot ? '<div class="lfoot" id="more">' + foot + '</div>' : '') + '</section>' + pane + '</div>';
  return shell('sales', inner);
}
/* The chosen receipt in the side pane fetches its lines once, then draws again. */
function fillPane() {
  if (route() !== 'sales' || state.sel == null || lines.done[state.sel] || lines.busy === state.sel) return;
  var id = state.sel; lines.busy = id;
  linesFor(id).catch(function () { lines.done[id] = true; }).then(function () { lines.busy = null; if (state.sel === id) render(); });
}
/* Older receipts: when the end of the list comes into view, or on the button. A search or filter reads a few pages on its own
   (so a match from last month still turns up), then waits for the button rather than walking the whole history unasked. */
function loadOlder() {
  if (feed.loading || !feed.hasMore) return;
  var p = olderReceipts(); render();
  p.then(function () { render(); }, function (e) { loadError = e; render(); });
}
var seeEnd = null;
function watchEnd() {
  if (seeEnd) { seeEnd.disconnect(); seeEnd = null; }
  var end = $('#more'); if (!end || !('IntersectionObserver' in window) || route() !== 'sales' || busy) return;
  seeEnd = new IntersectionObserver(function (es) {
    if (!es[0].isIntersecting || feed.loading || !feed.hasMore) return;
    if (salesFiltered()) { if (state.salesLook <= 0) return; state.salesLook--; }
    loadOlder();
  }, { rootMargin: '400px 0px' });
  seeEnd.observe(end);
}

/* Stock */
function stockView() {
  if (basic()) state.stockFilter = 'all';
  var nb = !basic();
  var all = stockItems(), low = all.filter(function (i) { return i.level === 1; }).length, out = all.filter(function (i) { return i.level === 0; }).length;
  var fc = [['all', t('all'), all.length], ['low', t('low'), low], ['out', t('out'), out]].map(function (a) { return '<button type="button" class="fc" data-kf="' + a[0] + '" aria-pressed="' + (state.stockFilter === a[0]) + '">' + a[1] + ' <em>' + a[2] + '</em></button>'; }).join('');
  var q = state.stockQ.toLowerCase();
  var matched = all.filter(function (it) { return (state.stockFilter === 'all' || (state.stockFilter === 'low' && it.level === 1) || (state.stockFilter === 'out' && it.level === 0)) && (!q || (it.name + ' ' + it.barcode).toLowerCase().indexOf(q) > -1); });
  var list = matched.slice(0, state.stockShow);
  // Drawing thousands of rows at once is what makes a long list slow; show them in steps.
  var more = matched.length > list.length ? '<div class="lfoot"><p>' + t('shownOf', list.length.toLocaleString('en-US'), matched.length.toLocaleString('en-US')) + '</p><button type="button" class="btn sm" id="kmore">' + t('showMore') + '</button></div>' : '';
  var rowsHtml = list.length ? list.map(function (it) {
    var st = !nb ? null : it.level === 0 ? ['bad', 'alert', t('outS')] : it.level === 1 ? ['warn', 'alert', t('lowS')] : ['good', 'check', t('inStock')];
    var q2 = it.qty.toLocaleString('en-US', { maximumFractionDigits: 3 }), m2 = it.min == null ? '-' : it.min.toLocaleString('en-US', { maximumFractionDigits: 3 });
    // Each row opens the item (details, and Edit / Adjust stock for a login allowed to change it).
    return '<button type="button" class="krow" data-prod="' + it.id + '"><span class="nm"><b>' + esc(it.name) + '</b><small class="mono">' + esc(it.barcode) + '</small><small class="m-only"> ' + (nb ? q2 + ' / ' + t('min') + ' ' + m2 + ' &nbsp;' : '') + money(it.price, it.cur) + '</small></span>' +
      '<span class="num r d-only">' + (nb ? q2 + (it.unit ? ' ' + esc(it.unit) : '') : '') + '</span><span class="num r d-only muted">' + (nb ? m2 : '') + '</span><span class="num r d-only">' + money(it.price, it.cur) + '</span><span class="d-only">' + (st ? chip(st[0], st[1], st[2]) : '') + '</span>' +
      '<span class="m-only" style="justify-self:end">' + (st ? chip(st[0], st[1], st[2]) : '') + '</span></button>';
  }).join('') : '<p class="empty">' + t('noMatch') + '</p>';
  var addBtn = canAddProduct() ? '<button type="button" class="btn sm pri" data-new="product">' + ic('plus', 'sm') + t('newProduct') + '</button>' : '';
  var inner = '<header class="head"><div><h1>' + t(nb ? 'stock' : 'products') + '</h1><p>' + t(nb ? 'stockSub' : 'productsSub') + '</p></div><div class="hacts">' + copyChip() + addBtn + '</div></header>' +
    '<div class="tools"><div class="srch"><label class="sr" for="kq">' + t('find') + '</label>' + ic('search', 'sm') + '<input id="kq" type="search" placeholder="' + t('find') + '" value="' + esc(state.stockQ) + '" autocomplete="off"></div>' + (nb ? fc : '') + '</div>' +
    '<section class="panel tbl"><div class="kth k4"><span>' + t('cName') + '</span><span class="r">' + (nb ? t('cQty') : '') + '</span><span class="r">' + (nb ? t('cMin') : '') + '</span><span class="r">' + t('cPrice') + '</span><span></span></div>' + rowsHtml + more + '</section>';
  return shell('stock', inner);
}

/* Shop PC */
function shopView() {
  var fr = freshness(), m = me || {}, planName = { Active: 'sActive', Grace: 'sGrace', Expired: 'sExpired', Suspended: 'sSuspended', None: 'sNone' }[m.subscription] || 'sActive';
  var planChip = chip(m.subscription === 'Active' ? 'good' : m.subscription === 'Grace' ? 'warn' : 'bad', m.subscription === 'Active' ? 'check' : 'alert', t(planName));
  var contact = fr.state === 'ok' ? chip('good', 'check', t('contactOk')) : fr.state === 'late' ? chip('warn', 'clock', t('contactLate')) : chip('bad', 'alert', t('contactOff'));
  var inner = '<header class="head"><div><h1>' + t('shop') + '</h1><p>' + t('shopSub') + '</p></div></header><div class="g">' +
    '<section class="panel s8 till"><div class="th2"><h2>' + esc(m.siteName || session.siteName || t('shop')) + '</h2>' + contact + '</div>' +
    '<div class="meta"><span>' + t('lastContact') + ': <b class="num">' + (fr.seen ? esc(agoStr(fr.seen)) + ' (' + esc(timeStr(fr.seen)) + ')' : '-') + '</b></span><span>' + t('lastCopy') + ': <b class="num">' + (asOf ? esc(agoStr(asOf)) + ' (' + esc(timeStr(asOf)) + ')' : '-') + '</b></span></div></section>' +
    '<aside class="panel s4" style="align-self:start"><div class="ph"><h2>' + t('plan') + '</h2>' + planChip + '</div>' + (m.expiresAtUtc ? '<p>' + t('renews', esc(new Date(m.expiresAtUtc).toLocaleDateString(loc(), { day: 'numeric', month: 'long', year: 'numeric' }))) + '</p>' : '') + '<p class="muted" style="font-size:14px;margin-top:10px">' + t('planNote') + '</p></aside>' +
    (can(P.edit) ? '<section class="panel s12"><div class="ph"><h2>' + t('menuH') + '</h2></div><p class="muted" style="font-size:14px">' + t('menuP') + '</p><p style="margin-top:12px"><a class="btn" href="/owner/menu.html">' + t('menuOpen') + '</a></p></section>' : '') + '</div>';
  return shell('shop', inner);
}

/* receipt: drawn as the till's slip, in the side pane on wide screens and in a sheet otherwise */
function receiptCard(s, items, closable) {
  var cur = s.cur;
  var rows2 = items === null ? '<div class="sk"><i></i><i></i><i></i></div><p class="muted ldl">' + t('loadingItems') + '</p>'
    : items.length ? items.map(function (x) {
      return '<div class="ln"><div class="a">' + esc(x.name) + '<small class="num"><bdi dir="ltr">' + x.qty.toLocaleString('en-US', { maximumFractionDigits: 3 }) + (x.unit ? ' ' + esc(x.unit) : '') + ' × ' + money(x.price, cur) + '</bdi></small></div><b class="num">' + money(x.total, cur) + '</b></div>';
    }).join('') : '<p class="muted">' + t('noItems') + '</p>';
  var when = s.when ? s.when.toLocaleString(loc(), { weekday: 'short', day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' }) : '';
  var count = items && items.length ? items.reduce(function (a, x) { return a + x.qty; }, 0) : null;
  return '<div class="slip' + (s.isVoid ? ' void' : '') + '">' +
    '<div class="sh"><span class="k">' + t('cId') + '</span>' + (s.isVoid ? chip('bad', 'alert', t('voided')) : s.pay ? '<span class="chip mute">' + esc(s.pay) + '</span>' : '') + '</div>' +
    '<h2 id="dlg-t" class="mono" dir="ltr">' + esc(s.number) + '</h2>' +
    '<p class="sm2">' + esc(when) + '</p>' +
    '<dl class="who"><div><dt>' + t('cCust') + '</dt><dd>' + esc(s.customer || t('walkIn')) + '</dd></div>' + (count != null ? '<div><dt>' + t('cItems') + '</dt><dd class="num">' + count.toLocaleString('en-US', { maximumFractionDigits: 3 }) + '</dd></div>' : '') + '</dl>' +
    '<div class="lines">' + rows2 + '</div>' +
    (s.discount > 0 ? '<div class="ln disc"><div class="a">' + t('discount') + '</div><b class="num">-' + money(s.discount, cur) + '</b></div>' : '') +
    '<div class="tt"><span>' + t('rTotal') + '</span><span class="num">' + money(s.total, cur) + '</span></div>' +
    (s.isVoid && s.voidReason ? '<p class="vr">' + ic('alert', 'sm') + esc(s.voidReason) + '</p>' : '') +
    (closable ? '<div class="df"><button type="button" class="btn pri" data-close>' + t('close') + '</button></div>' : '') +
    '</div>';
}
function openSale(id) {
  // Wide Sales screen: the receipt opens in the side pane instead of on top of the list.
  if (route() === 'sales' && wideSales()) { state.sel = id; render(); fillPane(); return; }
  var s = sales().filter(function (x) { return x.id === id; })[0]; if (!s) return;
  var d = $('#dlg');
  function body(items) { d.innerHTML = receiptCard(s, items, true); }
  var ready = lines.done[id];
  body(ready ? itemsOf(id) : null);
  if (!d.open) d.showModal();
  d.dataset.sale = id;
  if (!ready) linesFor(id).then(function () { if (d.open && d.dataset.sale == id) body(itemsOf(id)); }, function () { if (d.open && d.dataset.sale == id) body([]); });
}

function itemsOf(receiptId) {
  return (lines.of[receiptId] || []).slice().sort(function (a, b) { return a - b; }).map(function (lid) { return rows.ReceiptItems[lid]; }).filter(function (r) { return r && !flag(r.IsDeleted); })
    .map(function (r) { return { name: text(r.ProductName), qty: toNum(r.Quantity), unit: text(r.Unit), price: toNum(r.UnitPrice), total: toNum(r.LineTotal) }; });
}

/* ---------- changes: the phone app's five, sealed to the shop ---------- */
var KINDS = {
  customer: { route: '/customers', type: 'customer.create', label: 'kCustomer' },
  supplier: { route: '/suppliers', type: 'supplier.create', label: 'kSupplier' },
  product: { route: '/products', type: 'product.create', label: 'kProduct' },
  productEdit: { route: '/product-edits', type: 'product.update', label: 'kEdit' },
  stock: { route: '/stock-changes', type: 'stock.adjust', label: 'kStock' }
};
function kindOf(type) { for (var k in KINDS) if (KINDS[k].type === type) return KINDS[k]; return null; }
/* The product form offers the shop's categories and suppliers, so (like the phone) it needs to be allowed to read both. */
function canAddProduct() { return can(P.add) && can(P.stock) && can(P.debts); }
function uuid() {
  if (crypto.randomUUID) return crypto.randomUUID();
  var b = crypto.getRandomValues(new Uint8Array(16)), h = ''; b[6] = (b[6] & 15) | 64; b[8] = (b[8] & 63) | 128;
  for (var i = 0; i < 16; i++) h += (b[i] + 256).toString(16).slice(1) + ([3, 5, 7, 9].indexOf(i) > -1 ? '-' : '');
  return h;
}
/* Sends one change: { enc: seal({ k, body }) } bound to the change type, with the idempotency key the form made when it opened,
   so pressing Send twice or retrying after a dropped connection can never create two of the same thing. */
function submitChange(kind, body, idem) {
  return seal(cryptoKey, 'command\n' + kind.type, { k: idem, body: body }).then(function (enc) { return call('POST', kind.route, { enc: enc }, idem); });
}
/* A change as the owner should see it: request (and the shop's answer) opened with the key; anything that will not open is marked hidden. */
function openChange(c) {
  var req = c.payload && typeof c.payload.enc === 'string', ans = c.data && typeof c.data.enc === 'string';
  if (!req && !ans) return Promise.resolve(c);
  var out = { id: c.id, type: c.type, status: c.status, requestedBy: c.requestedBy, createdAtUtc: c.createdAtUtc, completedAtUtc: c.completedAtUtc, code: c.code, message: c.message, data: c.data, payload: c.payload };
  var p1 = req ? openSealed(cryptoKey, 'command\n' + c.type, c.payload.enc).then(function (o) { out.payload = (o && o.body) || {}; }) : Promise.resolve();
  var p2 = ans ? openSealed(cryptoKey, 'result\n' + c.id, c.data.enc).then(function (o) { if (o.message) out.message = o.message; out.data = o.data || null; }) : Promise.resolve();
  return Promise.all([p1, p2]).then(function () { return out; }, function () { out.hidden = true; return out; });
}
var cmds = { list: null, error: null, busy: false, timer: null };
function loadChanges() {
  if (cmds.busy || !cryptoKey) return Promise.resolve();
  cmds.busy = true; clearTimeout(cmds.timer);
  return call('GET', '/commands?limit=50').then(function (list) { return Promise.all((list || []).map(openChange)); })
    .then(function (list) { cmds.list = list; cmds.error = null; }, function (e) { cmds.error = e; })
    .then(function () {
      cmds.busy = false;
      if (route() === 'changes') {
        renderKeepFocus();
        // Follow changes still on their way, so "Waiting for shop" turns into Done or Needs a fix by itself.
        if ((cmds.list || []).some(function (c) { return c.status === 'queued' || c.status === 'delivered'; })) cmds.timer = setTimeout(loadChanges, 8000);
      }
    });
}
function renderKeepFocus() { if ($('#dlg').open) return; render(); }

function nfmt(v) { return toNum(v).toLocaleString('en-US', { maximumFractionDigits: 3 }); }
function changeTitle(c) {
  if (c.hidden) return t('hiddenC');
  var k = kindOf(c.type), p = c.payload || {};
  if (c.type === 'customer.create') return [text(p.firstName), text(p.lastName)].join(' ').trim() || t(k.label);
  if (c.type === 'supplier.create' || c.type === 'product.create') return text(p.name) || t(k.label);
  if (c.type === 'product.update' || c.type === 'stock.adjust') { var pr = rows.Products[p.productId]; return pr ? text(pr.Name) : t('product'); }
  return k ? t(k.label) : t('kOther');
}
function changeDetail(c) {
  if (c.hidden) return t('hiddenD');
  var p = c.payload || {}, bits = [];
  if (c.type === 'stock.adjust') bits.push(p.type === 'add' ? t('sAdd', nfmt(p.quantity)) : p.type === 'remove' ? t('sRemove', nfmt(p.quantity)) : t('sSet', nfmt(p.quantity)));
  if (c.type === 'product.update') {
    if (p.name != null) bits.push(t('sName', p.name));
    if (p.sellingPrice != null) bits.push(t('sPrice', nfmt(p.sellingPrice)));
    if (p.purchasePrice != null) bits.push(t('sCost', nfmt(p.purchasePrice)));
    if (p.minStockLevel != null) bits.push(t('sMin', nfmt(p.minStockLevel)));
    if (p.isActive != null) bits.push(p.isActive ? t('sOn') : t('sOff'));
  }
  if (p.reason) bits.push(p.reason);
  return bits.join(' · ');
}
function changesView() {
  var acts = (canAddProduct() ? '<button type="button" class="btn sm" data-new="product">' + ic('plus', 'sm') + t('newProduct') + '</button>' : '') +
    (can(P.add) ? '<button type="button" class="btn sm" data-new="customer">' + ic('plus', 'sm') + t('newCustomer') + '</button>' + (basic() ? '' : '<button type="button" class="btn sm" data-new="supplier">' + ic('plus', 'sm') + t('newSupplier') + '</button>') : '');
  var body;
  if (cmds.list === null && !cmds.error) body = '<p class="empty">' + t('updating') + '</p>';
  else if (cmds.error && cmds.list === null) body = '<div class="banner bad" role="alert" style="margin:12px">' + ic('alert') + '<span>' + t('cmdErr') + ' ' + esc(explain(cmds.error)) + '</span><button type="button" class="btn sm" id="cretry">' + t('retry') + '</button></div>';
  else if (!cmds.list.length) body = '<p class="empty">' + t('noChanges') + '</p>';
  else body = cmds.list.map(function (c) {
    var k = kindOf(c.type), st = c.status === 'applied' ? ['good', 'check', t('stDone')] : c.status === 'rejected' ? ['bad', 'alert', t('stFix')] : c.status === 'discarded' ? ['mute', 'clock', t('stGone')] : ['warn', 'clock', t('stWait')];
    var when = c.createdAtUtc ? new Date(c.createdAtUtc) : null, detail = changeDetail(c);
    return '<div class="crow"><div class="cm"><span class="ck">' + (k ? t(k.label) : t('kOther')) + (when ? ' · <span class="num">' + esc(agoStr(when)) + '</span>' : '') + (c.requestedBy ? ' · ' + esc(c.requestedBy) : '') + '</span>' +
      '<b>' + esc(changeTitle(c)) + '</b>' + (detail ? '<small>' + esc(detail) + '</small>' : '') +
      (c.status === 'rejected' && c.message ? '<small class="cbad">' + ic('alert', 'sm') + esc(c.message) + '</small>' : '') + '</div>' +
      '<div class="ca">' + chip(st[0], st[1], st[2]) + (c.status === 'queued' ? '<button type="button" class="btn sm" data-withdraw="' + esc(c.id) + '">' + t('withdraw') + '</button>' : '') + '</div></div>';
  }).join('');
  var inner = '<header class="head"><div><h1>' + t('changes') + '</h1><p>' + t('chSub') + '</p></div><div class="hacts">' + acts + '</div></header>' +
    '<section class="panel tbl">' + body + '</section>';
  return shell('changes', inner);
}

/* ---------- forms (in the dialog) ---------- */
/* Reads a number typed with a dot, a comma or the Arabic decimal sign, in Western or Arabic-Indic digits. */
function parseNum(s) {
  s = String(s == null ? '' : s).trim().replace(/[٠-٩]/g, function (d) { return String(d.charCodeAt(0) - 0x660); }).replace(/[۰-۹]/g, function (d) { return String(d.charCodeAt(0) - 0x6F0); }).replace(/[,٫]/g, '.');
  if (!s || !/^-?\d*\.?\d+$|^-?\d+\.$/.test(s)) return null;
  var n = Number(s); return isFinite(n) ? n : null;
}
function fld(id, label, o) {
  o = o || {};
  var attrs = o.num ? ' inputmode="decimal" dir="ltr"' : o.ltr ? ' dir="ltr"' : '';
  return '<div class="fld"><label for="' + id + '">' + label + '</label><input class="inp' + (o.mono ? ' mono' : '') + '" id="' + id + '" type="' + (o.type || 'text') + '"' + attrs +
    ' value="' + esc(o.value == null ? '' : o.value) + '" autocomplete="off"' + (o.cap ? ' autocapitalize="sentences"' : '') + ' aria-describedby="' + id + '-e' + (o.help ? ' ' + id + '-h' : '') + '">' +
    (o.help ? '<p class="help" id="' + id + '-h">' + o.help + '</p>' : '') + '<div class="err" id="' + id + '-e" role="alert" hidden></div></div>';
}
function sel(id, label, options, value, blank) {
  var opts = (blank != null ? '<option value="">' + esc(blank) + '</option>' : '') + options.map(function (o) { return '<option value="' + esc(o[0]) + '"' + (String(o[0]) === String(value) ? ' selected' : '') + '>' + esc(o[1]) + '</option>'; }).join('');
  return '<div class="fld"><label for="' + id + '">' + label + '</label><select class="inp" id="' + id + '" aria-describedby="' + id + '-e">' + opts + '</select><div class="err" id="' + id + '-e" role="alert" hidden></div></div>';
}
function pair(a, b) { return '<div class="fpair">' + a + b + '</div>'; }
function val(id) { var el = $('#' + id); return el ? el.value.trim() : ''; }
function orNull(id) { var v = val(id); return v ? v : null; }
/* Shows or clears one field's message; returns whether the field is fine. */
function check(id, msg) {
  var el = $('#' + id), e = $('#' + id + '-e'); if (!el) return true;
  if (msg) { e.innerHTML = ic('alert', 'sm') + esc(msg); e.hidden = false; el.setAttribute('aria-invalid', 'true'); } else { e.hidden = true; el.removeAttribute('aria-invalid'); }
  return !msg;
}
var UNITS = [['piece', 'uPiece'], ['kg', 'uKg'], ['lb', 'uLb'], ['liter', 'uLiter'], ['gallon', 'uGallon'], ['box', 'uBox'], ['pack', 'uPack']];
function lookups() {
  var cats = live('ProductCategories').filter(function (e) { return flag(e[1].IsActive); }).map(function (e) { return [e[0], text(e[1].Name)]; })
    .sort(function (a, b) { return a[1].localeCompare(b[1]); });
  var sups = live('Suppliers').filter(function (e) { return flag(e[1].IsActive); }).sort(function (a, b) {
    var ao = a[1].OrderNumber == null, bo = b[1].OrderNumber == null; if (ao !== bo) return ao ? 1 : -1;
    return (toNum(a[1].OrderNumber) - toNum(b[1].OrderNumber)) || text(a[1].Name).localeCompare(text(b[1].Name));
  }).map(function (e) { return [e[0], text(e[1].Name)]; });
  var curs = live('Currencies').filter(function (e) { return flag(e[1].IsActive); }).sort(function (a, b) {
    return (flag(b[1].IsDefault) ? 1 : 0) - (flag(a[1].IsDefault) ? 1 : 0) || text(a[1].Name).localeCompare(text(b[1].Name));
  }).map(function (e) { return [e[0], text(e[1].Code) + (e[1].Notation ? ' (' + text(e[1].Notation) + ')' : '')]; });
  return { cats: cats, sups: sups, curs: curs };
}

/* One form in the dialog: the fields, a Send button, and what Send does. build() returns the body, or null after marking what is wrong. */
function openForm(title, fieldsHtml, kind, build, afterBind) {
  var d = $('#dlg'), idem = uuid(), sending = false;
  d.innerHTML = '<form id="cf" novalidate><div class="dh"><h2 id="dlg-t" style="font-size:19px">' + esc(title) + '</h2></div>' +
    '<div class="db">' + (canSeal() ? '' : '<div class="banner" role="status" style="margin:0 0 14px">' + ic('key') + '<span>' + t('needKey') + '</span><button type="button" class="btn sm" id="cfkey">' + t('reenter') + '</button></div>') +
    fieldsHtml + '<div id="cferr" class="err" role="alert" hidden style="margin-bottom:6px"></div></div>' +
    '<div class="df"><button type="button" class="btn" data-close>' + t('cancel') + '</button><button type="submit" class="btn pri" id="cfsend"' + (canSeal() ? '' : ' disabled') + '>' + t('send') + '</button></div></form>';
  if (!d.open) d.showModal();
  var kb = $('#cfkey'); if (kb) kb.onclick = function () { d.close(); lockData(); };
  if (afterBind) afterBind();
  // A field's message goes as soon as it is edited; Send checks everything again.
  d.querySelectorAll('.inp').forEach(function (el) {
    var clear = function () { check(el.id, null); $('#cferr').hidden = true; };
    el.addEventListener('input', clear); el.addEventListener('change', clear);
  });
  var first = d.querySelector('input,select'); if (first) first.focus();
  $('#cf').onsubmit = function (e) {
    e.preventDefault(); if (sending || !canSeal()) return;
    var fe = $('#cferr'); fe.hidden = true;
    var body = build(); if (!body) { var bad = d.querySelector('[aria-invalid="true"]'); if (bad) bad.focus(); return; }
    if (body.error) { fe.innerHTML = ic('alert', 'sm') + esc(body.error); fe.hidden = false; return; }
    var b = $('#cfsend'); sending = true; b.disabled = true; b.textContent = t('sending') + '...';
    submitChange(kind, body, idem).then(function () {
      d.close(); toast(t('sent'));
      loadChanges();
    }, function (err) {
      sending = false; b.disabled = false; b.textContent = t('send');
      fe.innerHTML = ic('alert', 'sm') + esc(explain(err)); fe.hidden = false;
    });
  };
}
function strip(o) { var r = {}; for (var k in o) if (o[k] != null) r[k] = o[k]; return r; }

function newCustomer() {
  openForm(t('newCustomer'), pair(fld('cfirst', t('fFirst'), { cap: true }), fld('clast', t('fLast'), { cap: true })) +
    fld('cphone', t('fPhoneC'), { type: 'tel', ltr: true }) + fld('cmail', t('fEmailO'), { type: 'email', ltr: true }) + fld('caddr', t('fAddrNote')), KINDS.customer, function () {
    var mail = val('cmail');
    var ok = [check('cfirst', val('cfirst').length >= 2 ? null : t('eMin2')), check('clast', val('clast').length >= 2 ? null : t('eMin2')),
      check('cmail', !mail || mail.indexOf('@') > -1 ? null : t('eEmail'))].every(Boolean);
    return ok ? strip({ firstName: val('cfirst'), lastName: val('clast'), phoneNumber: orNull('cphone'), email: orNull('cmail'), address: orNull('caddr') }) : null;
  });
}
function newSupplier() {
  var l = lookups();
  openForm(t('newSupplier'), fld('sname', t('fCompany'), { cap: true }) + fld('sphone', t('fPhone'), { type: 'tel', ltr: true }) + fld('smail', t('fEmailO'), { type: 'email', ltr: true }) +
    pair(fld('scity', t('fCity')), fld('saddr', t('fAddr'))) + sel('scur', t('fBills'), l.curs, l.curs.length ? l.curs[0][0] : '', l.curs.length ? null : t('choose')), KINDS.supplier, function () {
    var mail = val('smail');
    var ok = [check('sname', val('sname').length >= 2 ? null : t('eMin2')), check('smail', !mail || mail.indexOf('@') > -1 ? null : t('eEmail')),
      check('scur', val('scur') ? null : t('eCurrency'))].every(Boolean);
    return ok ? strip({ name: val('sname'), currencyId: +val('scur'), phone: orNull('sphone'), email: orNull('smail'), city: orNull('scity'), address: orNull('saddr') }) : null;
  });
}
function marginText(priceId, costId) {
  var p = parseNum(val(priceId)), c = parseNum(val(costId));
  if (p == null || c == null || p <= 0) return '';
  var gain = p - c; return t('margin', gain.toFixed(2), Math.round(gain / p * 100));
}
function newProduct() {
  var l = lookups(), nb = !basic();
  var hidden = function (id, v) { return '<input type="hidden" id="' + id + '" value="' + esc(String(v)) + '">'; };
  var defSup = l.sups.filter(function (x) { return String(x[0]) === '1'; })[0] || l.sups[0];
  var units = UNITS.map(function (u) { return [u[0], t(u[1])]; });
  openForm(t('newProduct'),
    fld('pname', t('fName'), { cap: true }) + fld('pbar', t('fBarcode'), { mono: true, ltr: true, help: t('fBarcodeH') }) +
    (nb ? pair(sel('pcat', t('fCategory'), l.cats, l.cats.length === 1 ? l.cats[0][0] : '', t('choose')), sel('psup', t('fSupplier'), l.sups, l.sups.length === 1 ? l.sups[0][0] : '', t('choose')))
      : sel('pcat', t('fCategory'), l.cats, l.cats.length === 1 ? l.cats[0][0] : '', t('choose')) + hidden('psup', defSup ? defSup[0] : '')) +
    pair(fld('pprice', t('fPrice'), { num: true }), fld('pcost', t('fCost'), { num: true })) + '<p class="help fmargin" id="pmargin"></p>' +
    (nb ? pair(fld('pstock', t('fStock'), { num: true, value: '0' }), fld('pmin', t('fMin'), { num: true, value: '0' })) : hidden('pstock', 0) + hidden('pmin', 0)) +
    sel('punit', t('fUnit'), units, '', t('notSet')), KINDS.product, function () {
    var price = parseNum(val('pprice')), cost = parseNum(val('pcost')), stock = parseNum(val('pstock') || '0'), min = parseNum(val('pmin') || '0');
    var ok = [check('pname', val('pname').length >= 2 ? null : t('eMin2')), check('pbar', val('pbar') ? null : t('eBarcode')),
      check('pcat', val('pcat') ? null : t('eChoose')), check('psup', val('psup') ? null : t('eChoose')),
      check('pprice', price != null && price > 0 ? null : t('eAmount')), check('pcost', cost != null && cost > 0 ? null : t('eAmount')),
      check('pstock', stock != null && stock >= 0 ? null : t('eQty')), check('pmin', min != null && min >= 0 ? null : t('eQty'))].every(Boolean);
    return ok ? strip({ name: val('pname'), barcode: val('pbar'), categoryId: +val('pcat'), supplierId: +val('psup'), purchasePrice: cost, sellingPrice: price,
      stockQuantity: stock, minStockLevel: min, unit: orNull('punit'), isActive: true }) : null;
  }, function () {
    function upd() { $('#pmargin').textContent = marginText('pprice', 'pcost'); }
    $('#pprice').oninput = upd; $('#pcost').oninput = upd;
  });
}
function productRow(id) { var r = rows.Products[id]; return r && !flag(r.IsDeleted) ? r : null; }
function editProduct(id) {
  var r = productRow(id); if (!r) return;
  var was = { name: text(r.Name), price: toNum(r.SellingPrice), cost: r.CostPrice == null ? null : toNum(r.CostPrice), min: r.MinimumStock == null ? null : toNum(r.MinimumStock), active: flag(r.IsActive) };
  var showCost = can(P.financials);
  openForm(t('editProduct'), fld('ename', t('fName'), { value: was.name, cap: true }) +
    (showCost ? pair(fld('eprice', t('fPrice'), { num: true, value: nfmt(was.price).replace(/,/g, '') }), fld('ecost', t('fCost'), { num: true, value: was.cost == null ? '' : nfmt(was.cost).replace(/,/g, '') }))
      : fld('eprice', t('fPrice'), { num: true, value: nfmt(was.price).replace(/,/g, '') })) +
    (basic() ? '<input type="hidden" id="emin" value="">' : fld('emin', t('fMin'), { num: true, value: was.min == null ? '' : nfmt(was.min).replace(/,/g, '') })) +
    '<label class="tgl"><input type="checkbox" id="eon"' + (was.active ? ' checked' : '') + '><span><b>' + t('fOnSale') + '</b><small>' + t('fOnSaleH') + '</small></span></label>' +
    (basic() ? '' : '<p class="help" style="margin-top:12px">' + t('stockNote') + '</p>'), KINDS.productEdit, function () {
    var name = val('ename'), price = parseNum(val('eprice')), cost = showCost && val('ecost') ? parseNum(val('ecost')) : null, min = val('emin') ? parseNum(val('emin')) : null, on = $('#eon').checked;
    var ok = [check('ename', name.length >= 2 ? null : t('eMin2')), check('eprice', price != null && price > 0 ? null : t('eAmount')),
      showCost ? check('ecost', !val('ecost') || (cost != null && cost > 0) ? null : t('eAmount')) : true,
      check('emin', !val('emin') || (min != null && min >= 0) ? null : t('eQty'))].every(Boolean);
    if (!ok) return null;
    var body = { productId: id };
    if (name !== was.name) body.name = name;
    if (price !== was.price) body.sellingPrice = price;
    if (cost != null && cost !== was.cost) body.purchasePrice = cost;
    if (min != null && min !== was.min) body.minStockLevel = min;
    if (on !== was.active) body.isActive = on;
    return Object.keys(body).length > 1 ? body : { error: t('eNothing') };
  });
}
function adjustStock(id) {
  var r = productRow(id); if (!r) return;
  var now = toNum(r.StockQuantity), unit = text(r.Unit), mode = 'add';
  openForm(t('adjust'), '<p style="margin:0 0 4px;font-weight:600">' + esc(text(r.Name)) + '</p><p class="help" style="margin:0 0 14px">' + t('nowIn', nfmt(now) + (unit ? ' ' + esc(unit) : '')) + '</p>' +
    '<div class="seg" role="group" aria-label="' + t('adjust') + '" style="margin-bottom:14px"><button type="button" data-mode="add" aria-pressed="true">' + t('mAdd') + '</button><button type="button" data-mode="remove" aria-pressed="false">' + t('mRemove') + '</button><button type="button" data-mode="set" aria-pressed="false">' + t('mCount') + '</button></div>' +
    fld('aqty', t('fQty'), { num: true }) + '<p class="help" id="aafter" style="margin:-8px 0 14px"></p>' + fld('areason', t('fReason')), KINDS.stock, function () {
    var q = parseNum(val('aqty'));
    var ok = check('aqty', q == null ? (mode === 'set' ? t('eQty') : t('eQtyPos')) : mode === 'set' ? (q >= 0 ? null : t('eQty')) : (q > 0 ? null : t('eQtyPos')));
    return ok ? strip({ productId: id, type: mode, quantity: q, reason: orNull('areason') }) : null;
  }, function () {
    function upd() {
      var q = parseNum(val('aqty')), after = q == null ? null : mode === 'add' ? now + q : mode === 'remove' ? now - q : q;
      $('#aafter').textContent = after == null ? '' : t('after', nfmt(after) + (unit ? ' ' + unit : ''));
      $('label[for="aqty"]').textContent = mode === 'set' ? t('fCounted') : t('fQty');
    }
    document.querySelectorAll('#dlg [data-mode]').forEach(function (b) {
      b.onclick = function () { mode = b.dataset.mode; document.querySelectorAll('#dlg [data-mode]').forEach(function (x) { x.setAttribute('aria-pressed', String(x === b)); }); upd(); };
    });
    $('#aqty').oninput = upd;
  });
}
/* The item window from the Stock list: the facts, and the two changes a login allowed to edit may send. */
function openProduct(id) {
  var r = productRow(id); if (!r) return;
  var cur = currencies().of(toInt(r.CurrencyId)), q = toNum(r.StockQuantity), unit = text(r.Unit);
  var fact = function (k, v) { return '<div class="ln"><div class="a muted">' + k + '</div><b class="num" style="font-weight:500">' + v + '</b></div>'; };
  var d = $('#dlg');
  d.innerHTML = '<div class="dh"><div><h2 id="dlg-t" style="font-size:19px">' + esc(text(r.Name)) + '</h2><p class="muted mono" style="font-size:13.5px;margin-top:2px">' + esc(text(r.Barcode)) + '</p></div>' +
    (flag(r.IsActive) ? '' : chip('mute', 'clock', t('sOff'))) + '</div>' +
    '<div class="db">' + (basic() ? '' : fact(t('pQty'), nfmt(q) + (unit ? ' ' + esc(unit) : '')) + fact(t('pMin'), r.MinimumStock == null ? '-' : nfmt(r.MinimumStock))) + fact(t('pPrice'), money(toNum(r.SellingPrice), cur)) +
    (can(P.financials) && r.CostPrice != null ? fact(t('pCost'), money(toNum(r.CostPrice), cur)) : '') + '</div>' +
    '<div class="df"><button type="button" class="btn" data-close>' + t('close') + '</button>' +
    (can(P.edit) ? '<button type="button" class="btn" id="pdedit">' + ic('edit', 'sm') + t('editProduct') + '</button>' + (basic() ? '' : '<button type="button" class="btn pri" id="pdadj">' + ic('stock', 'sm') + t('adjust') + '</button>') : '') + '</div>';
  if (!d.open) d.showModal();
  var e1 = $('#pdedit'); if (e1) e1.onclick = function () { editProduct(id); };
  var e2 = $('#pdadj'); if (e2) e2.onclick = function () { adjustStock(id); };
}
function withdraw(id, btn) {
  btn.disabled = true;
  call('DELETE', '/commands/' + encodeURIComponent(id)).then(function () { toast(t('withdrawnT')); loadChanges(); }, function (e) { btn.disabled = false; toast(explain(e)); loadChanges(); });
}

/* ---------- render ---------- */
function render() {
  var r = route();
  // Data arriving while someone types in a search box must not take the cursor away.
  var fa = document.activeElement, fid = fa && (fa.id === 'sq' || fa.id === 'kq') ? fa.id : null, pos = fid ? fa.selectionStart : 0;
  if (r === 'menu') { location.replace(MENU_URL); return; }
  document.documentElement.lang = lang; document.documentElement.dir = lang === 'ar' ? 'rtl' : 'ltr';
  $('#topbar').innerHTML = topBar();
  var lg = $('#login'), ap = $('#app');
  var isApp = APP_ROUTES.indexOf(r) > -1;
  lg.hidden = isApp; ap.hidden = !isApp;
  if (r === 'signin') { lg.innerHTML = signinView(); bindSignin(); }
  else if (r === 'unlock') { lg.innerHTML = unlockView(); bindUnlock(); }
  else if (r === 'start') { lg.innerHTML = startView(); $('#sout').onclick = function () { signOut(); }; }
  else ap.innerHTML = ({ today: todayView, sales: salesView, stock: stockView, changes: changesView, shop: shopView })[r]();
  bind();
  if (fid) { var n = $('#' + fid); if (n) { n.focus(); try { n.setSelectionRange(pos, pos); } catch (e) {} } }
  watchEnd(); fillPane();
}
function bind() {
  document.querySelectorAll('[data-lang]').forEach(function (b) { b.onclick = function () { lang = b.dataset.lang; try { localStorage.setItem('coduo-lang', lang); } catch (e) {} render(); }; });
  var tb = $('#themeBtn'); if (tb) tb.onclick = function () { var root = document.documentElement; root.dataset.theme = isDark() ? 'light' : 'dark'; render(); };
  ['mout', 'rout'].forEach(function (id) { var b = $('#' + id); if (b) b.onclick = function () { signOut(); }; });
  var lk = $('#rlock'); if (lk) lk.onclick = function () { lockData(); };
  var rf = $('#rfr'); if (rf) rf.onclick = function () { refresh(); };
  var rt = $('#retry'); if (rt) rt.onclick = function () { refresh(); };
  document.querySelectorAll('[data-sale]').forEach(function (b) { b.onclick = function () { openSale(+b.dataset.sale); }; });
  document.querySelectorAll('[data-sf]').forEach(function (b) { b.onclick = function () { state.salesFilter = b.dataset.sf; state.salesLook = 4; render(); }; });
  document.querySelectorAll('[data-kf]').forEach(function (b) { b.onclick = function () { state.stockFilter = b.dataset.kf; state.stockShow = 200; render(); }; });
  function wire(id, key) { var q = $('#' + id); if (q) q.oninput = function () { state[key] = q.value; state.salesLook = 4; state.stockShow = 200; render(); }; }
  wire('sq', 'salesQ'); wire('kq', 'stockQ');
  var ol = $('#older'); if (ol) ol.onclick = function () { state.salesLook = 4; loadOlder(); };
  document.querySelectorAll('[data-day]').forEach(function (b) { b.onclick = function () { var h = document.getElementById('d-' + b.dataset.day); if (h) h.scrollIntoView({ behavior: matchMedia('(prefers-reduced-motion:reduce)').matches ? 'auto' : 'smooth', block: 'start' }); }; });
  // A narrow screen shows the newest days first; where someone scrolled the bars to stays put when the page draws again.
  var ds = document.querySelector('.dstrip');
  if (ds) { ds.scrollLeft = state.stripLeft == null ? ds.scrollWidth : state.stripLeft; ds.onscroll = function () { state.stripLeft = ds.scrollLeft >= ds.scrollWidth - ds.clientWidth - 2 ? null : ds.scrollLeft; }; }
  var km = $('#kmore'); if (km) km.onclick = function () { state.stockShow += 200; render(); };
  document.querySelectorAll('[data-prod]').forEach(function (b) { b.onclick = function () { openProduct(+b.dataset.prod); }; });
  document.querySelectorAll('[data-new]').forEach(function (b) { b.onclick = function () { ({ product: newProduct, customer: newCustomer, supplier: newSupplier })[b.dataset.new](); }; });
  document.querySelectorAll('[data-withdraw]').forEach(function (b) { b.onclick = function () { withdraw(b.dataset.withdraw, b); }; });
  var cr = $('#cretry'); if (cr) cr.onclick = function () { loadChanges(); };
}
$('#dlg').addEventListener('click', function (e) { if ((e.target.hasAttribute && e.target.hasAttribute('data-close')) || e.target === this) this.close(); });
/* The Sales screen changes shape (side pane or sheet) at a width: draw again when the window crosses it. */
matchMedia('(min-width:1180px)').addEventListener('change', function () { if (route() === 'sales' && !$('#dlg').open) render(); });
window.addEventListener('hashchange', function () {
  var d = $('#dlg'); if (d.open) d.close();
  if (route() === 'changes') loadChanges(); else clearTimeout(cmds.timer);
  render(); window.scrollTo(0, 0);
});

/* ---------- start ---------- */
if (session) {
  // Whether the shop has a POS can change (a menu-only shop installs one later), and older sessions do not say: ask before choosing a page.
  var asked = session.hasTill === true ? Promise.resolve() : api('/me').then(function (m) {
    me = m; if (session) { session.hasTill = m.hasTill; save('coduo-owner', session); }
  }, function () {});
  Promise.all([keyGet(), asked]).then(function (r) {
    var k = r[0];
    if (k && session && session.hasTill !== false) { cryptoKey = k; refresh(); if (route() === 'changes') loadChanges(); }
    render();
  });
} else render();
})();
