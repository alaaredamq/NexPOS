/* Coduo POS online-menu editor (/owner/menu.html).
   Two ways in: the shop owner, signed in on the owner website (its session token is in localStorage), or the provider from the admin
   portal (/owner/menu.html?client=ID, riding on the admin cookie). The API binds every call to one client's single menu.
   No shop data is decrypted here: the menu is public by nature, and linked items come from the shop PC already readable. */
(function () {
  'use strict';

  var params = new URLSearchParams(location.search);
  var CLIENT = params.get('client');
  var ADMIN = !!CLIENT && /^\d+$/.test(CLIENT);
  var session = null;
  try { session = JSON.parse(localStorage.getItem('coduo-owner')); } catch (e) {}
  var lang = 'en';
  try { lang = localStorage.getItem('coduo-lang') || ((navigator.language || '').slice(0, 2) === 'ar' ? 'ar' : 'en'); } catch (e) {}

  var D = {
    en: {
      title: 'Online menu', sub: 'A page with your products, pictures and prices that anyone can open from a link or a QR code on the table.',
      back: 'My shop', signOut: 'Sign out', backAdmin: 'Client', loading: 'Loading…',
      createH: 'Make your online menu', createP: 'Give it a name. You can change everything later, and nobody sees it until you publish it.',
      createName: 'Menu name', create: 'Make the menu',
      shareH: 'Share', published: 'Published', notPublished: 'Not published', publishLbl: 'Anyone with the link can see it',
      copy: 'Copy', copied: 'Link copied', open: 'Open', preview: 'Preview', qrDl: 'Download QR', qrNote: 'Print it for tables, the door or the counter.',
      previewNote: 'Not published yet: only the Preview link opens it.',
      srcH: 'Where the products come from', srcPos: 'From my POS', srcPosP: 'Names, prices and sections follow the shop PC by themselves.',
      srcHand: 'Added here', srcHandP: 'You type each item and its price on this page.',
      branch: 'Branch', noBranch: 'No shop PC is connected to this account, so the menu can only be filled here.',
      posNote: 'Each ticked branch sends its active products to the cloud readable: names, selling prices, currencies and categories. Never cost prices, stock or anything else. Pictures, descriptions and what to hide are set here.',
      lastFrom: 'Last update from the shop PC: {0}', waiting: 'Waiting for the shop PC. It checks in every few minutes while it is on and connected to Online.',
      unlinkQ: 'Stop following the POS? The items stay on the menu and become ordinary items you edit here.',
      detailsH: 'Name, language and web address', mTitle: 'Menu name', mTag: 'One line under the name', mNote: 'A note at the bottom (delivery area, parking, anything else)',
      mLang: 'Menu language', mCur: 'Currency for items added here', mSlug: 'Address', slugHint: 'Small English letters, digits and dashes.',
      logo: 'Logo', chooseLogo: 'Choose logo', remove: 'Remove', save: 'Save', saved: 'Saved',
      lsH: 'How items are listed', lsP: 'Pick the look customers see. You can change it any time; nothing else changes.', lsOn: 'In use', lsSaved: 'Look changed',
      ls_compact: 'Compact rows', ls_compact_d: 'Many items on one screen. Fine with or without pictures.',
      ls_grid: 'Photo grid', ls_grid_d: 'Two columns of picture cards. Best when most items have a photo.',
      ls_cards: 'Big photo cards', ls_cards_d: 'One large picture per item, with its description.',
      ls_classic: 'Classic menu', ls_classic_d: 'No pictures: name, dotted line, price, like a printed menu.',
      ls_rails: 'Swipe rows', ls_rails_d: 'Each section is a row that customers swipe sideways.',
      itemsH: 'Menu', showMore: 'Show more ({0} left)', nItems: '{0} items', itemsP: 'Use the arrows to change the order. Hidden items stay here but not on the menu.', posOrder: 'Sections and their order come from the Online Menu screen in the POS. Change them there; pictures, descriptions and hiding are set here.',
      addSection: 'Section', addItem: 'Item', noSection: 'No section', emptySec: 'Nothing in this section yet.', emptyMenu: 'Nothing on the menu yet. Add a section, then items.',
      emptyLinked: 'Nothing yet. Products appear here a few minutes after the shop PC checks in.',
      hide: 'Hide', show: 'Show', hidden: 'Hidden', soldOut: 'Sold out', pos: 'POS', rename: 'Rename', del: 'Delete', edit: 'Edit',
      up: 'Move up', down: 'Move down', picture: 'Picture',
      secName: 'Section name', newSection: 'New section', renameSection: 'Rename section', delSecQ: 'Delete "{0}" and the items in it?',
      newItem: 'New item', editItem: 'Edit item', iName: 'Name', iDesc: 'Description', iPrice: 'Price', iCur: 'Currency', iSec: 'Section',
      iCurHint: 'Empty = the menu\'s currency ({0})', iHidden: 'Hide from the menu', iSold: 'Sold out today',
      fromPos: 'Name, price and section come from the POS. Change them there.', posDesc: 'From the POS: {0}',
      choosePic: 'Choose picture', cancel: 'Cancel', delItemQ: 'Delete "{0}"?',
      picBig: 'That picture could not be read. Try a JPEG or PNG.', signIn: 'Please sign in on the owner website first.', signInBtn: 'Sign in',
      errNet: 'No connection. Check the internet and try again.', errNoEdit: 'This login cannot change the menu. Ask the shop owner.',
      brH: 'Branches', brP: 'Tick the branches whose POS fills the menu. Customers pick their branch; each sees its own prices and what it sells. The first one is the main branch: it decides the names and where items sit.',
      brMain: 'Main', brMakeMain: 'Make main', brOffQ: 'Take "{0}" off the menu? Its prices go; items only it sells stay on the menu, and you edit them here.',
      qrFor: 'QR and link for', qrAll: 'All branches (customers pick)',
      otH: 'Order types', otP: 'Dine in, take out, delivery... Customers pick one and every price follows it. With none, each item has one price.',
      otAdd: 'Order type', otNew: 'New order type', otEdit: 'Edit order type', otName: 'Name', otRule: 'Prices', otValue: 'By',
      otNone: 'Same as the normal price', otPct: 'Normal price + a percentage', otAmt: 'Normal price + an amount',
      otNoneS: 'Normal price', otPctS: '{0}% on the normal price', otAmtS: '{0} on the normal price', otDelQ: 'Delete the order type "{0}"? Prices set for it on items go too.',
      otEmpty: 'No order types: every item has one price.', otValueHint: 'Use a minus to take off, e.g. -10.',
      tpH: 'Prices by order type', tpHint: 'Empty = by the rule ({0})', brPrices: 'At each branch',
      lookH: 'Look', lookP: 'Colours, pictures and how items are listed. Customers see the change at once.',
      thH: 'Colours and lettering', thSaved: 'Colours changed',
      th_basil: 'Basil', th_basil_d: 'Herb green. Fresh, suits any kitchen.', th_ember: 'Ember', th_ember_d: 'Grill orange, bold capitals. Burgers, grills, pizza.',
      th_saffron: 'Saffron', th_saffron_d: 'Warm gold, soft serif. Bakeries, breakfast, sweets.', th_harbor: 'Harbor', th_harbor_d: 'Sea blue, clean lettering. Seafood, cafés.',
      th_plum: 'Plum', th_plum_d: 'Deep plum, elegant serif. Bars, desserts, evenings.', th_charcoal: 'Charcoal', th_charcoal_d: 'Black and white, fine serif. Fine dining, coffee.',
      cover: 'Cover photo', coverP: 'A wide photo across the top: the shop front, the room, or what you are known for.', chooseCover: 'Choose cover', logoP: 'Square works best.',
      ctH: 'Contact and location', ctP: 'Shown as buttons at the top of the menu and in "Find us" at the bottom. Leave empty what you do not use.',
      cAddr: 'Address', cMap: 'Map link', cMapHint: 'Paste a Google Maps share link. Empty = a map search for the address.', cPhone: 'Phone',
      cWa: 'WhatsApp number', cWaHint: 'With the country code. Customers can also send their order there.',
      cIg: 'Instagram', cFb: 'Facebook', cTt: 'TikTok', cWeb: 'Website', cHandle: '@yourshop or a link',
      hrH: 'Opening hours', hrP: 'Customers see "Open now" or when you open next. Closed for a break? Add a time (09:00 to 12:00, then 13:00 to 16:00). Times after midnight are fine (18:00 to 02:00).',
      hrOpen: 'Open', hrClosed: 'Closed', hrFrom: 'From', hrTo: 'To', hrCopy: 'Same hours every open day', hrAdd: 'Add time', hrDel: 'Remove this time', hrNone: 'No hours given: the menu does not say whether you are open.', hrClear: 'Clear hours',
      hrClock: 'Show times as', hrHour: 'Hour', hrMin: 'Minutes', am: 'AM', pm: 'PM',
      days: ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'],
      brDetails: 'Address and hours', brDlg: 'Branch: {0}', brDlgP: 'Leave a field empty to use the shop\'s own. Hours with every day closed = the shop\'s hours.',
      brOwnHours: 'This branch has its own hours',
      secEdit: 'Edit section', secFromPos: 'The name comes from the POS. Change it there.', secDesc: 'Line under the heading (optional)', secDescHint: 'For example: served until noon, or all burgers come with fries.', secPic: 'Section picture', secPicHint: 'Also shown on the button for this section at the top of the menu.', picAs: 'Show the picture as', picBanner: 'Background', picBannerD: 'A wide photo behind the section name.', picIcon: 'Small logo', picIconD: 'A small square before the name, for a logo or an icon.',
      omH: 'Taking orders', omP: 'What happens when a customer taps the button on their order.',
      om_show: 'Show to staff', om_show_d: 'The order stays on the phone. The customer shows it to a waiter.',
      om_request: 'Send to me', om_request_d: 'The order comes here, to the list below. You accept it and mark it ready.',
      om_pos: 'Send to the POS', om_pos_d: 'The order goes to the till of the customer\'s branch, where the cashier rings it up.',
      om_pos_off: 'Needs the menu to follow a POS (From my POS, below).', omSaved: 'Saved',
      waShow: 'Show a WhatsApp button on the order', waShowD: 'Customers can also send the order to your WhatsApp.', waNone: 'Add a WhatsApp number under Contact and location first.',
      ordH: 'Orders', ordOpen: '{0} open', ordNone: 'No orders yet. They appear here as customers send them.', ordLive: 'Updates by itself',
      ordNew: 'New order', ordNewN: '{0} new orders', ordTill: 'Sent to the POS', ordMore: 'Show {0} more', ordLess: 'Show fewer', ordDone: 'Finished today ({0})', ordNoOpen: 'Nothing waiting. New orders appear here.',
      os_new: 'New', os_accepted: 'Accepted', os_ready: 'Ready', os_done: 'Done', os_rejected: 'Turned down',
      oAccept: 'Accept', oReady: 'Ready', oDone: 'Done', oReject: 'Turn down', oRejectQ: 'Turn down order #{0}? The customer sees that it was not taken.', oCall: 'Call',
      ago0: 'just now', agoM: '{0} min ago', agoH: '{0} h ago', agoD: '{0} days ago',
      tabHome: 'Home', tabOrders: 'Orders', tabDishes: 'Items', tabLook: 'Look', tabShop: 'Shop', pages: 'Menu editor', backTo: 'Back to {0}',
      needsH: 'Needs you', needOrder: '1 new order is waiting', needOrders: '{0} new orders are waiting', needPics: '{0} items have no picture',
      needHours: 'Opening hours are not set', needContact: 'No phone, WhatsApp or address yet', needEmpty: 'Nothing on the menu yet',
      needPos: 'Waiting for the shop PC to send its products', allSet: 'Nothing needs you right now.',
      search: 'Search items', searchNone: 'No item matches "{0}".', secsH: 'Sections', addDish: 'Add item', addSec: 'Add section', secShown: 'Shown on the menu',
      picsH: 'Cover and logo', shopSub: 'What customers see about you, and how the menu works.',
      grpSee: 'What customers see', grpOrders: 'Orders', grpSetup: 'Setup', prodH: 'Products and branches',
      notSet: 'Not set', hrEvery: 'Open every day', hrClosedOn: 'Closed {0}', hrRange: '{0} to {1}', otNoneShort: 'None: one price per item',
      srcHandShort: 'Added on this page', withWa: ', with a WhatsApp button', brSame: 'Same hours as the shop', brHoursH: 'Branch hours',
      ordWaiting: 'Waiting', ordFinished: 'Finished today', ordDoneNone: 'Nothing finished today yet.', change: 'Change',
      ordShowMode: 'Orders are not sent here. Customers show their order to a waiter.', ordChange: 'Change how orders arrive',
      ordHere: 'Orders come to this page.', ordViaPos: 'Orders go to the till. You can follow them here too.', liveH: 'What customers see',
    },
    ar: {
      title: 'القائمة على الإنترنت', sub: 'صفحة فيها منتجاتك وصورها وأسعارها، يفتحها أي شخص من رابط أو من رمز QR على الطاولة.',
      back: 'متجري', signOut: 'تسجيل الخروج', backAdmin: 'العميل', loading: 'جارٍ التحميل…',
      createH: 'أنشئ قائمتك على الإنترنت', createP: 'أعطها اسماً. يمكنك تغيير كل شيء لاحقاً، ولا يراها أحد قبل أن تنشرها.',
      createName: 'اسم القائمة', create: 'أنشئ القائمة',
      shareH: 'المشاركة', published: 'منشورة', notPublished: 'غير منشورة', publishLbl: 'يراها كل من لديه الرابط',
      copy: 'نسخ', copied: 'نُسخ الرابط', open: 'فتح', preview: 'معاينة', qrDl: 'تنزيل رمز QR', qrNote: 'اطبعه للطاولات أو الباب أو الصندوق.',
      previewNote: 'لم تُنشر بعد: يفتحها رابط المعاينة فقط.',
      srcH: 'من أين تأتي المنتجات', srcPos: 'من نقطة البيع', srcPosP: 'الأسماء والأسعار والأقسام تتبع كمبيوتر المتجر تلقائياً.',
      srcHand: 'تُضاف هنا', srcHandP: 'تكتب كل صنف وسعره في هذه الصفحة.',
      branch: 'الفرع', noBranch: 'لا يوجد كمبيوتر متجر متصل بهذا الحساب، لذا تُملأ القائمة من هنا فقط.',
      posNote: 'كل فرع مختار يرسل منتجاته النشطة إلى السحابة مقروءة: الأسماء وأسعار البيع والعملات والفئات. لا أسعار كلفة ولا مخزون ولا أي شيء آخر. الصور والأوصاف وما يُخفى تُحدَّد هنا.',
      lastFrom: 'آخر تحديث من كمبيوتر المتجر: {0}', waiting: 'بانتظار كمبيوتر المتجر. يتصل كل بضع دقائق ما دام يعمل ومتصلاً بـ Online.',
      unlinkQ: 'إيقاف المتابعة من نقطة البيع؟ تبقى الأصناف في القائمة وتصبح أصنافاً عادية تعدّلها هنا.',
      detailsH: 'الاسم واللغة ورابط القائمة', mTitle: 'اسم القائمة', mTag: 'سطر تحت الاسم', mNote: 'ملاحظة في الأسفل (منطقة التوصيل، الموقف، أي شيء آخر)',
      mLang: 'لغة القائمة', mCur: 'عملة الأصناف المضافة هنا', mSlug: 'العنوان', slugHint: 'أحرف إنجليزية صغيرة وأرقام وشرطات.',
      logo: 'الشعار', chooseLogo: 'اختر الشعار', remove: 'إزالة', save: 'حفظ', saved: 'تم الحفظ',
      lsH: 'طريقة عرض الأصناف', lsP: 'اختر الشكل الذي يراه الزبائن. يمكنك تغييره في أي وقت، ولا يتغير شيء آخر.', lsOn: 'مستخدم', lsSaved: 'تغيّر الشكل',
      ls_compact: 'صفوف مضغوطة', ls_compact_d: 'أصناف كثيرة في شاشة واحدة. مناسب مع الصور وبدونها.',
      ls_grid: 'شبكة صور', ls_grid_d: 'عمودان من بطاقات الصور. الأفضل حين يكون لمعظم الأصناف صورة.',
      ls_cards: 'بطاقات صور كبيرة', ls_cards_d: 'صورة كبيرة لكل صنف مع وصفه.',
      ls_classic: 'قائمة كلاسيكية', ls_classic_d: 'بدون صور: الاسم ونقاط ثم السعر، مثل القائمة المطبوعة.',
      ls_rails: 'صفوف تُسحب', ls_rails_d: 'كل قسم صف يسحبه الزبون جانبياً.',
      itemsH: 'القائمة', showMore: 'عرض المزيد (بقي {0})', nItems: '{0} أصناف', itemsP: 'استخدم الأسهم لتغيير الترتيب. الأصناف المخفية تبقى هنا ولا تظهر في القائمة.', posOrder: 'الأقسام وترتيبها تأتي من شاشة القائمة الإلكترونية في نقطة البيع. غيّرها هناك؛ الصور والأوصاف والإخفاء تُحدَّد هنا.',
      addSection: 'قسم', addItem: 'صنف', noSection: 'بلا قسم', emptySec: 'لا شيء في هذا القسم بعد.', emptyMenu: 'القائمة فارغة. أضف قسماً ثم أصنافاً.',
      emptyLinked: 'لا شيء بعد. تظهر المنتجات هنا بعد دقائق من اتصال كمبيوتر المتجر.',
      hide: 'إخفاء', show: 'إظهار', hidden: 'مخفي', soldOut: 'نفد', pos: 'نقطة البيع', rename: 'إعادة تسمية', del: 'حذف', edit: 'تعديل',
      up: 'إلى الأعلى', down: 'إلى الأسفل', picture: 'صورة',
      secName: 'اسم القسم', newSection: 'قسم جديد', renameSection: 'إعادة تسمية القسم', delSecQ: 'حذف "{0}" والأصناف التي فيه؟',
      newItem: 'صنف جديد', editItem: 'تعديل الصنف', iName: 'الاسم', iDesc: 'الوصف', iPrice: 'السعر', iCur: 'العملة', iSec: 'القسم',
      iCurHint: 'فارغ = عملة القائمة ({0})', iHidden: 'إخفاء من القائمة', iSold: 'نفد اليوم',
      fromPos: 'الاسم والسعر والقسم من نقطة البيع. غيّرها هناك.', posDesc: 'من نقطة البيع: {0}',
      choosePic: 'اختر صورة', cancel: 'إلغاء', delItemQ: 'حذف "{0}"؟',
      picBig: 'تعذّرت قراءة الصورة. جرّب JPEG أو PNG.', signIn: 'سجّل الدخول في موقع المالك أولاً.', signInBtn: 'تسجيل الدخول',
      errNet: 'لا يوجد اتصال. تحقق من الإنترنت وحاول مجدداً.', errNoEdit: 'هذا الحساب لا يستطيع تعديل القائمة. اطلب ذلك من صاحب المتجر.',
      brH: 'الفروع', brP: 'اختر الفروع التي تملأ نقطة البيع فيها القائمة. يختار الزبون فرعه ويرى أسعاره وما يبيعه. الفرع الأول هو الرئيسي: يحدد الأسماء ومكان الأصناف.',
      brMain: 'رئيسي', brMakeMain: 'اجعله رئيسياً', brOffQ: 'إزالة "{0}" من القائمة؟ تُحذف أسعاره، والأصناف التي يبيعها وحده تبقى أصنافاً عادية تعدّلها هنا.',
      qrFor: 'رمز QR والرابط لـ', qrAll: 'كل الفروع (يختار الزبون)',
      otH: 'طرق الطلب', otP: 'داخل المطعم، سفري، توصيل... يختار الزبون واحدة وتتبعها كل الأسعار. بدونها لكل صنف سعر واحد.',
      otAdd: 'طريقة طلب', otNew: 'طريقة طلب جديدة', otEdit: 'تعديل طريقة الطلب', otName: 'الاسم', otRule: 'الأسعار', otValue: 'بمقدار',
      otNone: 'مثل السعر العادي', otPct: 'السعر العادي + نسبة مئوية', otAmt: 'السعر العادي + مبلغ',
      otNoneS: 'السعر العادي', otPctS: '{0}% على السعر العادي', otAmtS: '{0} على السعر العادي', otDelQ: 'حذف طريقة الطلب "{0}"؟ تُحذف معها الأسعار المحددة لها في الأصناف.',
      otEmpty: 'لا طرق طلب: لكل صنف سعر واحد.', otValueHint: 'استخدم السالب للخصم، مثلاً -10.',
      tpH: 'الأسعار حسب طريقة الطلب', tpHint: 'فارغ = حسب القاعدة ({0})', brPrices: 'في كل فرع',
      lookH: 'الشكل', lookP: 'الألوان والصور وطريقة عرض الأصناف. يرى الزبائن التغيير فوراً.',
      thH: 'الألوان والخط', thSaved: 'تغيّرت الألوان',
      th_basil: 'ريحان', th_basil_d: 'أخضر الأعشاب. منعش ويناسب أي مطبخ.', th_ember: 'جمر', th_ember_d: 'برتقالي المشاوي وخط عريض. برغر ومشاوي وبيتزا.',
      th_saffron: 'زعفران', th_saffron_d: 'ذهبي دافئ وخط ناعم. مخابز وفطور وحلويات.', th_harbor: 'مرفأ', th_harbor_d: 'أزرق البحر وخط نظيف. مأكولات بحرية ومقاهٍ.',
      th_plum: 'برقوق', th_plum_d: 'بنفسجي داكن وخط أنيق. حانات وحلويات وسهرات.', th_charcoal: 'فحم', th_charcoal_d: 'أبيض وأسود وخط راقٍ. مطاعم فاخرة وقهوة.',
      cover: 'صورة الغلاف', coverP: 'صورة عريضة في أعلى الصفحة: واجهة المحل أو الصالة أو طبق مميز.', chooseCover: 'اختر الغلاف', logoP: 'المربع هو الأفضل.',
      ctH: 'التواصل والموقع', ctP: 'تظهر أزراراً في أعلى القائمة وفي "زورونا" في الأسفل. اترك فارغاً ما لا تستخدمه.',
      cAddr: 'العنوان', cMap: 'رابط الخريطة', cMapHint: 'الصق رابط مشاركة من خرائط Google. فارغ = بحث في الخريطة عن العنوان.', cPhone: 'الهاتف',
      cWa: 'رقم واتساب', cWaHint: 'مع رمز البلد. يمكن للزبائن أيضاً إرسال طلبهم إليه.',
      cIg: 'إنستغرام', cFb: 'فيسبوك', cTt: 'تيك توك', cWeb: 'الموقع الإلكتروني', cHandle: '@اسم_محلك أو رابط',
      hrH: 'ساعات العمل', hrP: 'يرى الزبائن "مفتوح الآن" أو موعد الفتح التالي. تغلق للاستراحة؟ أضف وقتاً (09:00 إلى 12:00، ثم 13:00 إلى 16:00). الأوقات بعد منتصف الليل مقبولة (18:00 إلى 02:00).',
      hrOpen: 'مفتوح', hrClosed: 'مغلق', hrFrom: 'من', hrTo: 'إلى', hrCopy: 'نفس الساعات لكل أيام الفتح', hrAdd: 'أضف وقتاً', hrDel: 'احذف هذا الوقت', hrNone: 'لا ساعات: لا تقول القائمة إن كنت مفتوحاً.', hrClear: 'امسح الساعات',
      hrClock: 'اعرض الوقت بصيغة', hrHour: 'الساعة', hrMin: 'الدقائق', am: 'ص', pm: 'م',
      days: ['الأحد', 'الاثنين', 'الثلاثاء', 'الأربعاء', 'الخميس', 'الجمعة', 'السبت'],
      brDetails: 'العنوان والساعات', brDlg: 'الفرع: {0}', brDlgP: 'اترك الحقل فارغاً لاستخدام بيانات المحل. الساعات مع كل الأيام مغلقة = ساعات المحل.',
      brOwnHours: 'لهذا الفرع ساعاته الخاصة',
      secEdit: 'تعديل القسم', secFromPos: 'الاسم من نقطة البيع. غيّره هناك.', secDesc: 'سطر تحت العنوان (اختياري)', secDescHint: 'مثلاً: يُقدَّم حتى الظهر، أو كل البرغر مع بطاطا.', secPic: 'صورة القسم', secPicHint: 'تظهر أيضاً على زر القسم في أعلى القائمة.', picAs: 'اعرض الصورة كـ', picBanner: 'خلفية', picBannerD: 'صورة عريضة خلف اسم القسم.', picIcon: 'شعار صغير', picIconD: 'مربع صغير قبل الاسم، لشعار أو أيقونة.',
      omH: 'استقبال الطلبات', omP: 'ماذا يحدث عندما يضغط الزبون الزر في طلبه.',
      om_show: 'عرض على العامل', om_show_d: 'يبقى الطلب على الهاتف. يعرضه الزبون على النادل.',
      om_request: 'أرسله إليّ', om_request_d: 'يصل الطلب هنا، إلى القائمة في الأسفل. تقبله وتعلّمه جاهزاً.',
      om_pos: 'أرسله إلى نقطة البيع', om_pos_d: 'يذهب الطلب إلى صندوق فرع الزبون، حيث يسجّله الكاشير.',
      om_pos_off: 'يحتاج أن تتبع القائمة نقطة بيع (من نقطة البيع، في الأسفل).', omSaved: 'تم الحفظ',
      waShow: 'إظهار زر واتساب في الطلب', waShowD: 'يمكن للزبائن أيضاً إرسال الطلب إلى واتساب الخاص بك.', waNone: 'أضف رقم واتساب في التواصل والموقع أولاً.',
      ordH: 'الطلبات', ordOpen: '{0} مفتوحة', ordNone: 'لا طلبات بعد. تظهر هنا عندما يرسلها الزبائن.', ordLive: 'تتحدث تلقائياً',
      ordNew: 'طلب جديد', ordNewN: 'طلبات جديدة: {0}', ordTill: 'أُرسل إلى نقطة البيع', ordMore: 'عرض {0} أخرى', ordLess: 'عرض أقل', ordDone: 'المنتهية اليوم ({0})', ordNoOpen: 'لا شيء بالانتظار. تظهر الطلبات الجديدة هنا.',
      os_new: 'جديد', os_accepted: 'مقبول', os_ready: 'جاهز', os_done: 'تم', os_rejected: 'مرفوض',
      oAccept: 'قبول', oReady: 'جاهز', oDone: 'تم', oReject: 'رفض', oRejectQ: 'رفض الطلب رقم {0}؟ سيرى الزبون أنه لم يُقبل.', oCall: 'اتصال',
      ago0: 'الآن', agoM: 'قبل {0} د', agoH: 'قبل {0} س', agoD: 'قبل {0} أيام',
      tabHome: 'الرئيسية', tabOrders: 'الطلبات', tabDishes: 'الأصناف', tabLook: 'الشكل', tabShop: 'المتجر', pages: 'محرر القائمة', backTo: 'العودة إلى {0}',
      needsH: 'بحاجة إليك', needOrder: 'طلب جديد بالانتظار', needOrders: 'طلبات جديدة بالانتظار: {0}', needPics: 'أصناف بلا صورة: {0}',
      needHours: 'ساعات العمل غير محددة', needContact: 'لا هاتف ولا واتساب ولا عنوان بعد', needEmpty: 'القائمة فارغة بعد',
      needPos: 'بانتظار كمبيوتر المتجر ليرسل المنتجات', allSet: 'لا شيء بحاجة إليك الآن.',
      search: 'ابحث في الأصناف', searchNone: 'لا صنف يطابق "{0}".', secsH: 'الأقسام', addDish: 'أضف صنفاً', addSec: 'أضف قسماً', secShown: 'ظاهر في القائمة',
      picsH: 'الغلاف والشعار', shopSub: 'ما يراه الزبائن عنك، وكيف تعمل القائمة.',
      grpSee: 'ما يراه الزبائن', grpOrders: 'الطلبات', grpSetup: 'الإعداد', prodH: 'المنتجات والفروع',
      notSet: 'غير محدد', hrEvery: 'مفتوح كل يوم', hrClosedOn: 'مغلق: {0}', hrRange: 'من {0} إلى {1}', otNoneShort: 'لا شيء: سعر واحد لكل صنف',
      srcHandShort: 'تُضاف في هذه الصفحة', withWa: '، مع زر واتساب', brSame: 'نفس ساعات المحل', brHoursH: 'ساعات الفروع',
      ordWaiting: 'بالانتظار', ordFinished: 'المنتهية اليوم', ordDoneNone: 'لا شيء انتهى اليوم بعد.', change: 'تغيير',
      ordShowMode: 'لا تصل الطلبات إلى هنا. يعرض الزبون طلبه على النادل.', ordChange: 'غيّر طريقة وصول الطلبات',
      ordHere: 'تصل الطلبات إلى هذه الصفحة.', ordViaPos: 'تذهب الطلبات إلى الصندوق، ويمكنك متابعتها هنا أيضاً.', liveH: 'ما يراه الزبائن',
    },
  };

  function t(k) { var s = D[lang][k] != null ? D[lang][k] : D.en[k]; for (var i = 1; i < arguments.length; i++) s = s.replace('{' + (i - 1) + '}', arguments[i]); return s; }
  function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  function ic(id, c) { return '<svg class="ic ' + (c || '') + '" aria-hidden="true"><use href="#i-' + id + '"/></svg>'; }
  function $(s) { return document.querySelector(s); }
  function toast(msg) { var el = $('#toast'); el.firstChild.textContent = msg; el.classList.add('on'); clearTimeout(toast.h); toast.h = setTimeout(function () { el.classList.remove('on'); }, 3200); }
  function ago(iso) {
    var m = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 60000));
    return m < 1 ? t('ago0') : m < 60 ? t('agoM', m) : m < 1440 ? t('agoH', Math.round(m / 60)) : t('agoD', Math.round(m / 1440));
  }

  var S = null; // what the editor shows (GET /menu/v1/editor)

  // ---- talking to the cloud ----
  function api(method, path, body, blob) {
    var url = '/menu/v1' + path + (ADMIN ? (path.indexOf('?') > -1 ? '&' : '?') + 'client=' + CLIENT : '');
    var headers = { Accept: 'application/json' };
    if (ADMIN) headers['X-Coduo-Menu'] = '1';
    else if (session && session.token) headers.Authorization = 'Bearer ' + session.token;
    var init = { method: method, headers: headers, credentials: 'same-origin' };
    if (blob) { headers['Content-Type'] = blob.type || 'application/octet-stream'; init.body = blob; }
    else if (body !== undefined) { headers['Content-Type'] = 'application/json'; init.body = JSON.stringify(body); }
    return fetch(url, init).then(function (r) {
      if (r.status === 204) return null;
      return r.json().catch(function () { return {}; }).then(function (j) {
        if (r.ok) { var created = r.headers.get('X-Created-Id'); if (created && j) j.createdId = +created; return j; }
        if (r.status === 401) { needSignIn(); throw new Error(t('signIn')); }
        if (r.status === 403) throw new Error(t('errNoEdit'));
        throw new Error(j.message || ('Error ' + r.status));
      });
    }, function () { throw new Error(t('errNet')); });
  }
  function apply(state) {
    if (state) {
      var changed = !!S;
      S = state;
      // Forget pages of sections that are gone.
      var keys = keysOf();
      Object.keys(L).forEach(function (k) { if (keys.indexOf(+k) < 0) delete L[k]; });
      render();
      if (changed) reloadPreview();
    }
    return state;
  }
  function act(p) { return p.then(apply).catch(function (e) { toast(e.message); throw e; }); }
  /** For changes to items: also reload the item pages already on screen, so they show the change. */
  function actItems(p) { return act(p).then(function (state) { return refreshLoaded().then(function () { return state; }); }); }

  // ---- items, a page at a time: a menu can hold thousands ----
  var PAGE = 40;
  var L = {}; // section id (0 = no section) -> { items, hasMore, busy }
  function keysOf() { return S && S.menu ? S.sections.map(function (s) { return s.id; }).concat(S.looseCount ? [0] : []) : []; }
  function loadPage(key, append) {
    var slot = L[key] || (L[key] = { items: [], hasMore: true, busy: false });
    if (slot.busy) return Promise.resolve();
    slot.busy = true;
    var skip = append ? slot.items.length : 0;
    return api('GET', '/items?section=' + key + '&skip=' + skip + '&take=' + PAGE).then(function (page) {
      slot.items = append ? slot.items.concat(page.items) : page.items;
      slot.hasMore = page.hasMore;
      slot.busy = false;
      render();
    }, function (e) { slot.busy = false; toast(e.message); });
  }
  function refreshLoaded() {
    if (Q.term) searchNow();
    return Promise.all(Object.keys(L).map(function (k) {
      var slot = L[k], take = Math.min(100, Math.max(PAGE, slot.items.length));
      return api('GET', '/items?section=' + k + '&skip=0&take=' + take).then(function (page) { slot.items = page.items; slot.hasMore = page.hasMore; });
    })).then(render, function (e) { toast(e.message); });
  }
  var watcher = 'IntersectionObserver' in window ? new IntersectionObserver(function (entries) {
    entries.forEach(function (e) { if (e.isIntersecting) { watcher.unobserve(e.target); loadPage(+e.target.dataset.load, false); } });
  }, { rootMargin: '600px 0px' }) : null;
  function findItem(id) {
    var byId = function (i) { return i.id === id; };
    for (var k in L) { var hit = L[k].items.filter(byId)[0]; if (hit) return hit; }
    return (Q.items || []).filter(byId)[0] || null;
  }

  // ---- searching dishes across every section (the server looks, so it works on menus of any size) ----
  var Q = { term: '', items: null, timer: 0 };
  function searchNow() {
    var term = Q.term;
    if (!term) { Q.items = null; drawSearch(); return; }
    api('GET', '/items?q=' + encodeURIComponent(term) + '&take=60').then(function (page) {
      if (term !== Q.term) return;
      Q.items = page.items; drawSearch();
    }, function (e) { toast(e.message); });
  }
  function drawSearch() {
    var box = document.getElementById('dres'), secs = document.getElementById('dsecs');
    if (!box) return;
    box.hidden = !Q.term; if (secs) secs.hidden = !!Q.term;
    box.innerHTML = !Q.term ? '' : Q.items === null ? '<p class="small muted empty">' + t('loading') + '</p>'
      : Q.items.length ? '<ul class="items list">' + Q.items.map(function (i) { return itemRow(i, Q.items, 0, false, true); }).join('') + '</ul>'
      : '<p class="small muted empty">' + esc(t('searchNone', Q.term)) + '</p>';
    bindItems(box);
  }

  function needSignIn() {
    $('#app').innerHTML = '<div class="wrap">' + bar() + '<div class="card"><h2>' + t('title') + '</h2><p class="sub">' + t('signIn') + '</p>' +
      '<p style="margin-top:14px"><a class="btn pri" href="' + (ADMIN ? '/admin/Account/Login?ReturnUrl=' + encodeURIComponent(location.pathname + location.search) : '/owner/index.html') + '">' + t('signInBtn') + '</a></p></div></div>';
    bindBar();
  }

  // ---- pictures: shrunk in the browser so uploads are small and fast on mobile data ----
  // Safari (every iPhone browser) cannot encode WebP: asking for it gives a PNG, so check once instead of encoding twice.
  var WEBP_OUT = (function () {
    try { var c = document.createElement('canvas'); c.width = c.height = 1; return c.toDataURL('image/webp').indexOf('data:image/webp') === 0; }
    catch (e) { return false; }
  })();
  function shrink(file, max, keepAlpha) {
    return new Promise(function (resolve, reject) {
      var url = URL.createObjectURL(file), img = new Image();
      img.onload = function () {
        URL.revokeObjectURL(url);
        var w = img.naturalWidth, h = img.naturalHeight;
        if (!w || !h) return reject(new Error(t('picBig')));
        var s = Math.min(1, max / Math.max(w, h));
        var c = document.createElement('canvas');
        c.width = Math.max(1, Math.round(w * s)); c.height = Math.max(1, Math.round(h * s));
        var g = c.getContext('2d');
        if (!keepAlpha) { g.fillStyle = '#fff'; g.fillRect(0, 0, c.width, c.height); }
        g.drawImage(img, 0, 0, c.width, c.height);
        img.src = '';
        // iOS caps the memory all canvases may hold together and frees a canvas late; zero it so repeated uploads keep working.
        var done = function (b) { c.width = c.height = 0; b ? resolve(b) : reject(new Error(t('picBig'))); };
        var fallback = function () { c.toBlob(done, keepAlpha ? 'image/png' : 'image/jpeg', 0.86); };
        if (!WEBP_OUT) return fallback();
        c.toBlob(function (b) { b && b.type === 'image/webp' ? done(b) : fallback(); }, 'image/webp', 0.84);
      };
      img.onerror = function () { URL.revokeObjectURL(url); reject(new Error(t('picBig'))); };
      img.src = url;
    });
  }
  // One file input kept in the page: iOS Safari can drop an input that is not in the page while the photo picker is open,
  // and then the picked photo never arrives.
  var picker;
  function pickFile(cb) {
    if (!picker) {
      picker = document.createElement('input');
      picker.type = 'file'; picker.accept = 'image/*';
      picker.setAttribute('aria-hidden', 'true'); picker.tabIndex = -1;
      picker.style.cssText = 'position:fixed;left:-9999px;top:0;width:1px;height:1px;opacity:0';
      document.body.appendChild(picker);
    }
    picker.value = ''; // so choosing the same photo again still counts as a change
    picker.onchange = function () { var f = picker.files && picker.files[0]; if (f) cb(f); };
    picker.click();
  }
  function uploadItemPic(id, file) {
    return shrink(file, 1000, false).then(function (b) { return actItems(api('PUT', '/items/' + id + '/image', undefined, b)); })
      .catch(function (e) { toast(e.message); });
  }

  // ---- views ----
  /** The way out of the editor. A shop with no POS has nothing else on the owner website, so it gets "Sign out" rather than "back". */
  function exitHtml(cls) {
    return ADMIN ? '<a class="' + cls + '" href="/admin/Clients/Details/' + esc(CLIENT) + '">' + ic('back', 'flip') + t('backAdmin') + '</a>'
      : session && session.hasTill === false ? '<button type="button" class="' + cls + '" data-signout>' + t('signOut') + '</button>'
      : '<a class="' + cls + '" href="/owner/index.html">' + ic('back', 'flip') + t('back') + '</a>';
  }
  function langHtml() {
    return '<div class="lang" role="group" aria-label="Language"><button type="button" data-lang="en" aria-pressed="' + (lang === 'en') + '">EN</button>' +
      '<button type="button" data-lang="ar" aria-pressed="' + (lang === 'ar') + '">ع</button></div>';
  }
  /** The top bar of the pages shown before there is a menu (signing in, making the menu). */
  function bar() { return '<div class="bar">' + exitHtml('back') + '<span class="sp"></span>' + langHtml() + '</div>'; }
  function bindBar() {
    document.querySelectorAll('[data-signout]').forEach(function (out) {
      out.onclick = function () {
        var token = session && session.token;
        try { localStorage.removeItem('coduo-owner'); } catch (e) {}
        var done = function () { location.href = '/owner/index.html'; };
        if (!token) return done();
        fetch('/owner/v1/auth/logout', { method: 'POST', headers: { Authorization: 'Bearer ' + token } }).then(done, done);
      };
    });
    document.querySelectorAll('[data-lang]').forEach(function (b) {
      b.onclick = function () { lang = b.dataset.lang; try { localStorage.setItem('coduo-lang', lang); } catch (e) {} setLang(); if (S) render(); else if (!ADMIN && !session) needSignIn(); };
    });
  }
  function setLang() { document.documentElement.lang = lang; document.documentElement.dir = lang === 'ar' ? 'rtl' : 'ltr'; document.title = t('title') + ' · Coduo POS'; }

  // ---- pages: five tabs (#home, #orders, #dishes, #look, #shop), a page per section (#dishes/12) and per shop setting (#shop/hours) ----
  var TABS = ['home', 'orders', 'dishes', 'look', 'shop'];
  var TAB_ICON = { home: 'home', orders: 'receipt', dishes: 'list', look: 'palette', shop: 'store' };
  var TAB_LABEL = { home: 'tabHome', orders: 'tabOrders', dishes: 'tabDishes', look: 'tabLook', shop: 'tabShop' };
  var SHOP_PAGES = ['contact', 'hours', 'details', 'taking', 'types', 'products'];
  var WIDE = window.matchMedia ? matchMedia('(min-width:1280px)') : { matches: false };

  function route() {
    var parts = (location.hash || '').replace(/^#\/?/, '').split('/');
    var tab = TABS.indexOf(parts[0]) > -1 ? parts[0] : 'home', sub = parts[1] || '';
    var ok = tab === 'dishes' ? sub === '' || (sub === '0' ? S.looseCount > 0 : !!secById(+sub))
      : tab === 'shop' ? sub === '' || SHOP_PAGES.indexOf(sub) > -1 : sub === '';
    // A page that is gone (a deleted section, an old link) opens its tab instead.
    if (!ok) { sub = ''; history.replaceState(null, '', '#' + tab); }
    return { tab: tab, sub: sub };
  }

  function render() {
    if (!S.menu) {
      document.body.classList.remove('sub', 'shell-on');
      $('#app').innerHTML = '<div class="wrap">' + bar() + '<header class="head"><h1>' + t('title') +
        (S.admin ? ' <span class="pill mute" style="vertical-align:middle">' + esc(S.clientName) + '</span>' : '') + '</h1><p>' + t('sub') + '</p></header>' +
        '<div class="card"><h2>' + t('createH') + '</h2><p class="sub">' + t('createP') + '</p>' +
        '<form id="fcreate" class="row" style="margin-top:14px"><div class="f" style="flex:1;min-width:220px"><label for="ct">' + t('createName') + '</label><input id="ct" maxlength="120" value="' + esc(S.clientName) + '" required></div>' +
        '<button class="btn pri" style="align-self:flex-end">' + t('create') + '</button></form></div></div>';
      bindBar(); bind();
      return;
    }
    var r = route();
    // The shell is built once, so the live menu beside the editor is not reloaded on every redraw.
    if (!document.getElementById('main')) {
      $('#app').innerHTML = '<div class="shell"><aside class="rail" id="rail"></aside><main class="main" id="main"></main><aside class="side" id="live" hidden></aside></div>' +
        '<nav class="tabs" id="tabs"></nav>';
    }
    document.body.classList.add('shell-on');
    document.body.classList.toggle('sub', !!r.sub);
    $('#rail').innerHTML = railHtml(r);
    $('#main').innerHTML = pageHtml(r);
    var tabs = $('#tabs');
    tabs.setAttribute('aria-label', t('pages'));
    tabs.innerHTML = TABS.map(function (k) { return navLink(k, r, 'tb'); }).join('');
    livePreview();
    bindBar();
    bind();
    drawSearch();
    drawOrders();
  }

  function navLink(k, r, cls) {
    return '<a href="#' + k + '"' + (r.tab === k ? ' aria-current="page"' : '') + '><span class="' + cls + '">' + ic(TAB_ICON[k]) +
      (k === 'orders' ? '<span class="badge" data-badge hidden></span>' : '') + '</span>' + t(TAB_LABEL[k]) + '</a>';
  }
  function railHtml(r) {
    var m = S.menu;
    return '<div class="rb"><span class="rt">' + t('title') + '</span><b dir="auto">' + esc(m.title) + '</b>' +
      (S.admin ? '<span class="rt" dir="auto">' + esc(S.clientName) + '</span>' : '') +
      '<span class="rs' + (m.isPublished ? ' on' : '') + '">' + (m.isPublished ? t('published') : t('notPublished')) + '</span></div>' +
      '<nav aria-label="' + esc(t('pages')) + '">' + TABS.map(function (k) { return navLink(k, r, 'ri'); }).join('') + '</nav>' +
      '<span class="sp"></span>' + langHtml() + exitHtml('rx');
  }
  /** A page's title row: a way back on a sub-page, the language on a tab's own page, and any buttons for the page. */
  function head(title, back, actions) {
    return '<header class="top">' + (back ? '<a class="up" href="#' + back[0] + '" aria-label="' + esc(t('backTo', back[1])) + '">' + ic('back', 'flip') + '</a>' : '') +
      '<h1 tabindex="-1" dir="auto">' + esc(title) + '</h1><span class="sp"></span>' + (actions || '') + (back ? '' : langHtml()) + '</header>';
  }
  function saveBar(form) { return '<div class="savebar"><button class="btn pri" form="' + form + '">' + t('save') + '</button></div>'; }
  function fab(section) { return '<button type="button" class="btn pri fab" data-additem="' + section + '">' + ic('plus') + t('addDish') + '</button>'; }
  function lrow(href, icon, title, value, tone) {
    return '<li><a class="lrow" href="#' + href + '"><span class="li">' + ic(icon) + '</span><span class="lt"><b>' + esc(title) + '</b>' +
      (value ? '<span' + (tone ? ' class="' + tone + '"' : '') + ' dir="auto">' + esc(value) + '</span>' : '') + '</span>' + ic('next', 'flip') + '</a></li>';
  }

  function pageHtml(r) {
    switch (r.tab) {
      case 'orders': return ordersPage();
      case 'dishes': return r.sub !== '' ? sectionPage(+r.sub) : dishesPage();
      case 'look': return lookPage();
      case 'shop': return r.sub ? shopPage(r.sub) : shopHome();
      default: return homePage();
    }
  }

  // ---- the live menu beside the editor, on a wide screen: the preview link, so it shows before publishing and is never cached ----
  var liveTimer = 0;
  function liveUrl() { var b = branchOf(qrBranch); return S.menu.previewUrl + (b ? '&b=' + encodeURIComponent(b.slug) : ''); }
  function livePreview() {
    var box = document.getElementById('live');
    if (!box) return;
    if (!WIDE.matches) { box.hidden = true; box.innerHTML = ''; return; }
    box.hidden = false;
    var f = box.querySelector('iframe');
    if (!f) {
      box.innerHTML = '<div class="lh"><b></b><a class="btn sm" target="_blank" rel="noopener">' + ic('open') + '<span></span></a></div><div class="phone"><iframe></iframe></div>';
      f = box.querySelector('iframe');
    }
    box.querySelector('.lh b').textContent = t('liveH');
    box.querySelector('.lh a span').textContent = S.menu.isPublished ? t('open') : t('preview');
    box.querySelector('.lh a').href = openUrl();
    f.title = t('liveH');
    if (f.dataset.src !== liveUrl()) { f.dataset.src = liveUrl(); f.src = liveUrl(); }
  }
  /** After a change is saved, shows it in the live menu (once, for a burst of changes). */
  function reloadPreview() {
    clearTimeout(liveTimer);
    liveTimer = setTimeout(function () {
      var f = document.querySelector('#live iframe');
      if (f && f.dataset.src) f.src = f.dataset.src + '&v=' + Date.now();
    }, 500);
  }

  // ---- Home: share the menu, and what needs the owner ----
  function homePage() {
    return head(S.menu.title) + shareCard() + '<section class="group"><h2>' + t('needsH') + '</h2><ul class="list" id="needs">' + needsItems() + '</ul></section>';
  }
  function needsItems() {
    var m = S.menu, rows = [], fresh = newCount();
    var row = function (href, icon, tone, text) {
      var tag = href ? 'a' : 'div';
      return '<li><' + tag + ' class="lrow"' + (href ? ' href="#' + href + '"' : '') + '><span class="li ' + tone + '">' + ic(icon) + '</span>' +
        '<span class="lt"><b>' + esc(text) + '</b></span>' + (href ? ic('next', 'flip') : '') + '</' + tag + '></li>';
    };
    if (fresh) rows.push(row('orders', 'receipt', 'bad', fresh === 1 ? t('needOrder') : t('needOrders', fresh)));
    if (!S.sections.length && !S.looseCount) rows.push(row('dishes', 'list', 'warn', t('needEmpty')));
    if (S.noPictureCount) rows.push(row('dishes', 'photo', 'warn', t('needPics', S.noPictureCount)));
    if (!m.hours) rows.push(row('shop/hours', 'clock', 'warn', t('needHours')));
    if (!m.phone && !m.whatsApp && !m.address) rows.push(row('shop/contact', 'pin', 'warn', t('needContact')));
    if ((S.branches || []).some(function (b) { return !b.catalogAtUtc; })) rows.push(row('shop/products', 'link', 'info', t('needPos')));
    if (!rows.length) rows.push(row('', 'check', 'good', t('allSet')));
    return rows.join('');
  }

  // With several branches, the link and QR can open at one branch (?b=), e.g. for that branch's tables, or ask the customer.
  var qrBranch = '';
  function branchOf(slug) { return (S.branches || []).filter(function (b) { return b.slug === slug; })[0] || null; }
  function shareUrl() { var b = branchOf(qrBranch); return b ? b.url : S.menu.url; }
  function openUrl() {
    var m = S.menu, b = branchOf(qrBranch);
    if (m.isPublished) return shareUrl();
    return m.previewUrl + (b ? '&b=' + encodeURIComponent(b.slug) : '');
  }
  function shareCard() {
    var m = S.menu, many = (S.branches || []).length > 1;
    if (!branchOf(qrBranch)) qrBranch = '';
    return '<section class="card"><div class="share"><div style="min-width:0">' +
      '<h2>' + t('shareH') + ' ' + (m.isPublished ? '<span class="pill good">' + t('published') + '</span>' : '<span class="pill warn">' + t('notPublished') + '</span>') + '</h2>' +
      '<label class="switch" style="margin-top:12px"><input type="checkbox" id="pub"' + (m.isPublished ? ' checked' : '') + '><span>' + t('publishLbl') + '</span></label>' +
      (many ? '<div class="f" style="margin-top:12px;max-width:320px"><label for="qrfor">' + t('qrFor') + '</label><select id="qrfor"><option value="">' + t('qrAll') + '</option>' +
        S.branches.map(function (b) { return '<option value="' + esc(b.slug) + '"' + (b.slug === qrBranch ? ' selected' : '') + '>' + esc(b.name) + '</option>'; }).join('') + '</select></div>' : '') +
      '<div class="url"><code dir="ltr" id="murl">' + esc(shareUrl()) + '</code>' +
      '<button type="button" class="btn sm" id="copy">' + ic('copy') + t('copy') + '</button>' +
      '<a class="btn sm" target="_blank" rel="noopener" href="' + esc(openUrl()) + '">' + ic('open') + (m.isPublished ? t('open') : t('preview')) + '</a></div>' +
      (m.isPublished ? '' : '<p class="small muted" style="margin-top:8px">' + t('previewNote') + '</p>') +
      '</div><div class="qr"><div class="box" id="qrbox" aria-label="QR"></div><div><button type="button" class="btn sm" id="qrdl">' + ic('qr') + t('qrDl') + '</button>' +
      '<p class="small muted" style="margin-top:6px">' + t('qrNote') + '</p></div></div></div></section>';
  }

  function sourceCard() {
    var m = S.menu, linked = !!m.linkedSiteId, hasSites = S.sites.length > 0;
    var h = '<section class="card"><h2>' + t('srcH') + '</h2><div class="src">' +
      '<button type="button" class="opt" id="src-pos" aria-pressed="' + linked + '"' + (hasSites ? '' : ' disabled') + '><b>' + ic('link') + t('srcPos') + '</b><span>' + t('srcPosP') + '</span></button>' +
      '<button type="button" class="opt" id="src-hand" aria-pressed="' + !linked + '"><b>' + ic('hand') + t('srcHand') + '</b><span>' + t('srcHandP') + '</span></button></div>';
    if (!hasSites) h += '<p class="note">' + t('noBranch') + '</p>';
    if (linked) {
      // Every branch of the shop, linked ones first in their order (the first is the main one), then the others.
      var linkedIds = S.branches.map(function (b) { return b.siteId; });
      var rows = S.branches.map(function (b) { return { id: b.siteId, name: b.name, on: true, at: b.catalogAtUtc }; })
        .concat(S.sites.filter(function (s) { return linkedIds.indexOf(s.id) < 0; }).map(function (s) { return { id: s.id, name: s.name, on: false }; }));
      h += '<fieldset class="branches"><legend><h3>' + t('brH') + '</h3></legend>' + (S.sites.length > 1 ? '<p class="sub">' + t('brP') + '</p>' : '') + '<ul>' +
        rows.map(function (r, n) {
          var status = !r.on ? '' : r.at ? '<span class="pill good">' + esc(t('lastFrom', ago(r.at))) + '</span>' : '<span class="pill info">' + t('waiting') + '</span>';
          return '<li><label class="switch"><input type="checkbox" data-br="' + esc(r.id) + '"' + (r.on ? ' checked' : '') + (r.on && linkedIds.length === 1 ? ' disabled' : '') + '><span>' + esc(r.name) + '</span></label>' +
            (r.on && n === 0 && linkedIds.length > 1 ? '<span class="pill mute">' + t('brMain') + '</span>' : '') +
            (r.on && n > 0 ? '<button type="button" class="btn sm" data-brmain="' + esc(r.id) + '">' + t('brMakeMain') + '</button>' : '') +
            (r.on && linkedIds.length > 1 ? '<button type="button" class="btn sm" data-brdet="' + esc(r.id) + '">' + t('brDetails') + '</button>' : '') +
            '<span class="sp"></span>' + status + '</li>';
        }).join('') + '</ul></fieldset>';
      h += '<p class="note">' + t('posNote') + '</p>';
    }
    return h + '</section>';
  }

  // ---- order types: dine in, take out, delivery... ----
  function ruleText(type) {
    var v = type.adjustValue, sign = v > 0 ? '+' : '';
    return type.adjust === 'percent' ? t('otPctS', sign + v) : type.adjust === 'amount' ? t('otAmtS', sign + v) : t('otNoneS');
  }
  function typesBody() {
    var types = S.orderTypes || [];
    return '<section class="card">' +
      (types.length ? '<ul class="types" style="margin-top:0">' + types.map(function (ty, n) {
        return '<li><b dir="auto">' + esc(ty.name) + '</b><span class="muted small">' + esc(ruleText(ty)) + '</span><span class="sp"></span>' +
          '<button type="button" class="btn icon" data-tmv="' + ty.id + '" data-dir="-1" aria-label="' + t('up') + '"' + (n === 0 ? ' disabled' : '') + '>' + ic('up') + '</button>' +
          '<button type="button" class="btn icon" data-tmv="' + ty.id + '" data-dir="1" aria-label="' + t('down') + '"' + (n === types.length - 1 ? ' disabled' : '') + '>' + ic('down') + '</button>' +
          '<button type="button" class="btn sm" data-tedit="' + ty.id + '">' + t('edit') + '</button>' +
          '<button type="button" class="btn sm danger" data-tdel="' + ty.id + '">' + t('del') + '</button></li>';
      }).join('') + '</ul>' : '<p class="small muted">' + t('otEmpty') + '</p>') + '</section>';
  }
  function typeById(id) { return (S.orderTypes || []).filter(function (x) { return x.id === id; })[0]; }
  function typeDialog(ty) {
    var rule = ty ? ty.adjust : 'none';
    var opt = function (v, k) { return '<option value="' + v + '"' + (rule === v ? ' selected' : '') + '>' + t(k) + '</option>'; };
    dialog('<form><h2 id="dlg-t">' + (ty ? t('otEdit') : t('otNew')) + '</h2><div class="fields">' +
      '<div class="f full"><label for="d-tn">' + t('otName') + '</label><input id="d-tn" maxlength="60" required dir="auto" value="' + esc(ty ? ty.name : '') + '"></div>' +
      '<div class="f"><label for="d-tr">' + t('otRule') + '</label><select id="d-tr">' + opt('none', 'otNone') + opt('percent', 'otPct') + opt('amount', 'otAmt') + '</select></div>' +
      '<div class="f" id="d-tvf"><label for="d-tv">' + t('otValue') + '</label><input id="d-tv" inputmode="decimal" dir="ltr" value="' + esc(ty && ty.adjust !== 'none' ? ty.adjustValue : '') + '"><span class="hint">' + t('otValueHint') + '</span></div>' +
      '</div><p class="err" hidden></p><div class="foot"><button type="button" class="btn" data-close>' + t('cancel') + '</button><button class="btn pri">' + t('save') + '</button></div></form>',
      function () {
        var adjust = $('#d-tr').value, value = parseFloat(String($('#d-tv').value).replace(/,/g, ''));
        if (adjust !== 'none' && isNaN(value)) return Promise.reject(new Error(t('otValue') + '?'));
        var body = { name: $('#d-tn').value, adjust: adjust, adjustValue: adjust === 'none' ? 0 : value };
        return (ty ? api('PUT', '/types/' + ty.id, body) : api('POST', '/types', body)).then(apply);
      },
      function (d) {
        var sel = d.querySelector('#d-tr'), box = d.querySelector('#d-tvf');
        var sync = function () { box.hidden = sel.value === 'none'; };
        sel.onchange = sync; sync();
      });
  }
  function moveType(id, dir) {
    var ids = S.orderTypes.map(function (x) { return x.id; }), i = ids.indexOf(id), j = i + dir;
    if (j < 0 || j >= ids.length) return;
    ids[i] = ids[j]; ids[j] = id;
    act(api('PUT', '/order', { types: ids }));
  }
  /** The price a type gives a dish by its rule (shown as the hint where the dish has no price of its own). */
  function byRule(price, ty) {
    var p = +price || 0;
    var v = ty.adjust === 'percent' ? Math.round(p * (1 + ty.adjustValue / 100) * 100) / 100 : ty.adjust === 'amount' ? p + ty.adjustValue : p;
    return Math.max(0, Math.round(v * 100) / 100);
  }

  // ---- how dishes are listed: picture cards that behave as radio buttons (real radios underneath, so keys and screen readers work) ----
  var STYLES = ['compact', 'grid', 'cards', 'classic', 'rails'];
  function mini(style) {
    var line = function (w) { return '<i class="ln" style="width:' + w + '%"></i>'; };
    var rows = function (n, fn) { var s = ''; for (var k = 0; k < n; k++) s += fn(k); return s; };
    switch (style) {
      case 'grid':
        return '<span class="mini m-grid">' + rows(4, function () { return '<span class="cell"><i class="ph"></i>' + line(70) + line(35) + '</span>'; }) + '</span>';
      case 'cards':
        return '<span class="mini m-cards">' + rows(2, function () { return '<span class="cell"><i class="ph"></i>' + line(60) + line(85) + '</span>'; }) + '</span>';
      case 'classic':
        return '<span class="mini m-classic"><i class="ln hd"></i>' + rows(5, function () { return '<span class="row">' + line(38) + '<i class="dots"></i><i class="ln pr"></i></span>'; }) + '</span>';
      case 'rails':
        return '<span class="mini m-rails">' + rows(2, function () { return line(30) + '<span class="rail"><i class="ph"></i><i class="ph"></i><i class="ph"></i></span>'; }) + '</span>';
      default:
        return '<span class="mini m-compact">' + rows(5, function () { return '<span class="row"><i class="dot"></i><span class="tx">' + line(70) + line(45) + '</span><i class="pill"></i></span>'; }) + '</span>';
    }
  }
  // ---- taking orders: what "Send order" does, the WhatsApp button, and the orders that came in ----
  var MODES = ['show', 'request', 'pos'];
  var O = { orders: null, open: 0, timer: 0, seen: null, view: 'open' };
  /** The Taking orders page: what "Send order" does, and the WhatsApp button. */
  function takingBody() {
    var m = S.menu, mode = m.orderMode || 'show', hasPos = (S.branches || []).length > 0;
    var opts = MODES.map(function (k) {
      var off = k === 'pos' && !hasPos;
      return '<label class="om' + (off ? ' off' : '') + '"><input type="radio" name="omode" value="' + k + '"' + (k === mode ? ' checked' : '') + (off ? ' disabled' : '') + '>' +
        '<b>' + ic(k === 'show' ? 'hand' : k === 'request' ? 'bell' : 'link') + t('om_' + k) + '</b><span>' + t(off ? 'om_pos_off' : 'om_' + k + '_d') + '</span></label>';
    }).join('');
    var wa = !!m.whatsApp;
    return '<section class="card"><fieldset class="bare"><legend class="sr">' + t('omH') + '</legend><div class="omgrid">' + opts + '</div></fieldset>' +
      '<label class="switch" style="margin-top:16px"><input type="checkbox" id="wa-show"' + (m.showWhatsApp && wa ? ' checked' : '') + (wa ? '' : ' disabled') + '><span>' + t('waShow') + '</span></label>' +
      '<p class="small muted" style="margin-block:4px 0;margin-inline-start:52px">' + (wa ? t('waShowD') : t('waNone') + ' <a href="#shop/contact">' + t('ctH') + '</a>') + '</p></section>';
  }
  function isOpen(o) { return o.status === 'new' || o.status === 'accepted' || o.status === 'ready'; }
  function newCount() {
    if (!S || !S.menu || (S.menu.orderMode || 'show') === 'show') return 0;
    return (O.orders || []).filter(function (o) { return o.status === 'new'; }).length;
  }
  function ordersPage() {
    var mode = S.menu.orderMode || 'show', h = head(t('ordH'));
    if (mode === 'show')
      return h + '<section class="card empty"><p>' + t('ordShowMode') + '</p><p style="margin-top:14px"><a class="btn pri" href="#shop/taking">' + t('ordChange') + '</a></p></section>';
    return h + '<p class="lead">' + t(mode === 'pos' ? 'ordViaPos' : 'ordHere') + ' <a href="#shop/taking">' + t('change') + '</a>' +
      '<span class="live"><i></i>' + t('ordLive') + '</span></p><div id="ord-wrap">' + ordersBlock() + '</div>';
  }
  /**
   * Waiting orders in full (they need an answer), oldest first so the one waiting longest is on top.
   * Finished ones (done, turned down, in the last day) are on their own tab, one short line each.
   */
  function ordersBlock() {
    if (O.orders === null) return '<p class="small muted empty">' + t('loading') + '</p>';
    var open = O.orders.filter(isOpen).reverse(), done = O.orders.filter(function (o) { return !isOpen(o); });
    var seg = function (k, label, n) { return '<button type="button" role="tab" aria-selected="' + (O.view === k) + '" data-oview="' + k + '">' + label + ' <span class="n">' + n + '</span></button>'; };
    var h = '<div class="seg" role="tablist">' + seg('open', t('ordWaiting'), open.length) + seg('done', t('ordFinished'), done.length) + '</div>';
    if (O.view === 'done')
      return h + (done.length ? '<ul class="list dlist">' + done.map(function (o) {
        return '<li><b class="on">#' + o.number + '</b><span class="dn"><span dir="auto">' + esc(o.name) + '</span><span class="small muted">' +
          esc(t('nItems', o.lines.reduce(function (n, l) { return n + l.quantity; }, 0))) + ' · ' + esc(ago(o.updatedAtUtc || o.createdAtUtc)) + '</span></span>' +
          '<span class="pill mute">' + t('os_' + o.status) + '</span><b class="pr" dir="ltr">' + esc(o.total) + '</b></li>';
      }).join('') + '</ul>' : '<p class="small muted empty">' + t('ordDoneNone') + '</p>');
    return h + (open.length ? '<ul class="orders">' + open.map(orderCard).join('') + '</ul>'
      : '<p class="small muted empty">' + t(O.orders.length ? 'ordNoOpen' : 'ordNone') + '</p>');
  }
  function orderCard(o) {
    var acts = o.status === 'new' ? ['accepted', 'rejected'] : o.status === 'accepted' ? ['ready', 'rejected'] : o.status === 'ready' ? ['done'] : [];
    var label = { accepted: 'oAccept', ready: 'oReady', done: 'oDone', rejected: 'oReject' };
    return '<li class="ord s-' + o.status + '"><div class="oh"><b class="on">#' + o.number + '</b><span class="pill ' + ({ 'new': 'warn', accepted: 'info', ready: 'good' })[o.status] + '">' + t('os_' + o.status) + '</span>' +
      (o.route === 'pos' ? '<span class="pill mute">' + t('ordTill') + '</span>' : '') + '<span class="sp"></span><span class="small muted">' + esc(ago(o.createdAtUtc)) + '</span></div>' +
      '<p class="who"><b dir="auto">' + esc(o.name) + '</b>' + (o.place ? ' · <span dir="auto">' + esc(o.place) + '</span>' : '') +
      [o.branch, o.orderType].filter(Boolean).map(function (x) { return ' · <span dir="auto">' + esc(x) + '</span>'; }).join('') + '</p>' +
      '<ul class="olines">' + o.lines.map(function (l) { return '<li><span class="q" dir="ltr">' + l.quantity + '×</span><span dir="auto">' + esc(l.name) + '</span><span class="pr" dir="ltr">' + esc(l.total) + '</span></li>'; }).join('') + '</ul>' +
      (o.note ? '<p class="onote" dir="auto">' + esc(o.note) + '</p>' : '') +
      '<div class="of"><b class="ot" dir="ltr">' + esc(o.total) + '</b><span class="sp"></span>' +
      (o.phone ? '<a class="btn sm" href="tel:' + esc(o.phone.replace(/[^0-9+]/g, '')) + '">' + t('oCall') + '</a>' : '') +
      acts.map(function (a) { return '<button type="button" class="btn sm' + (a === 'rejected' ? ' danger' : ' pri') + '" data-omove="' + o.id + '" data-to="' + a + '">' + t(label[a]) + '</button>'; }).join('') +
      '</div></li>';
  }
  /** Asks for the orders every few seconds while the list is shown; redraws only the list, so a form being typed in is left alone. */
  function pollOrders() {
    clearTimeout(O.timer);
    if (!S || !S.menu || (S.menu.orderMode || 'show') === 'show') return;
    api('GET', '/orders').then(function (r) {
      var newIds = r.orders.filter(function (o) { return o.status === 'new'; }).map(function (o) { return o.id; });
      if (O.seen) {
        var fresh = newIds.filter(function (id) { return O.seen.indexOf(id) < 0; });
        if (fresh.length) { toast(fresh.length === 1 ? t('ordNew') : t('ordNewN', fresh.length)); chime(); }
      }
      O.seen = newIds; O.orders = r.orders; O.open = r.open;
      document.title = (newIds.length ? '(' + newIds.length + ') ' : '') + t('title') + ' · Coduo POS';
      drawOrders();
    }, function () { }).then(function () { O.timer = setTimeout(pollOrders, document.hidden ? 30000 : 8000); });
  }
  /** Redraws only what the orders touch (the list, the badges, Home's list), so a form being typed in is left alone. */
  function drawOrders() {
    var wrap = document.getElementById('ord-wrap'), needs = document.getElementById('needs'), n = newCount();
    if (wrap) wrap.innerHTML = ordersBlock();
    if (needs) needs.innerHTML = needsItems();
    document.querySelectorAll('[data-badge]').forEach(function (b) { b.textContent = n; b.hidden = !n; });
  }
  /** A short two-note chime for a new order (the page has been clicked by then, so the browser lets it play). */
  function chime() {
    try {
      var A = window.AudioContext || window.webkitAudioContext; if (!A) return;
      var a = chime.ctx || (chime.ctx = new A()), now = a.currentTime;
      [880, 1320].forEach(function (f, k) {
        var o = a.createOscillator(), g = a.createGain();
        o.frequency.value = f; o.connect(g); g.connect(a.destination);
        g.gain.setValueAtTime(0.0001, now + k * 0.18); g.gain.exponentialRampToValueAtTime(0.2, now + k * 0.18 + 0.02); g.gain.exponentialRampToValueAtTime(0.0001, now + k * 0.18 + 0.3);
        o.start(now + k * 0.18); o.stop(now + k * 0.18 + 0.32);
      });
    } catch (e) { }
  }

  // ---- the look: colours and lettering, cover and logo, how dishes are listed ----
  var THEMES = ['basil', 'ember', 'saffron', 'harbor', 'plum', 'charcoal'];
  function lookPage() {
    var m = S.menu, current = m.listingStyle || 'compact', theme = m.theme || 'basil';
    var swatches = THEMES.map(function (k) {
      return '<label class="thm th-' + k + '"><input type="radio" name="mtheme" value="' + k + '"' + (k === theme ? ' checked' : '') + '>' +
        '<span class="sw"><i class="aa">Aa</i><i class="dot"></i></span><b>' + t('th_' + k) + '</b><span>' + t('th_' + k + '_d') + '</span></label>';
    }).join('');
    var styles = STYLES.map(function (s) {
      return '<label class="sty"><input type="radio" name="lstyle" value="' + s + '"' + (s === current ? ' checked' : '') + '>' +
        mini(s) + '<b>' + t('ls_' + s) + (s === current ? ' <span class="pill good">' + t('lsOn') + '</span>' : '') + '</b><span>' + t('ls_' + s + '_d') + '</span></label>';
    }).join('');
    return head(t('lookH')) + '<p class="lead">' + t('lookP') + '</p>' +
      '<section class="card"><fieldset class="themes"><legend><h2>' + t('thH') + '</h2></legend><div class="thgrid">' + swatches + '</div></fieldset></section>' +
      '<section class="card"><h2>' + t('picsH') + '</h2><div class="pics">' +
      '<div class="pk cover"><span class="lbl">' + t('cover') + '</span><div class="cv">' + (m.coverUrl ? '<img src="' + esc(m.coverUrl) + '" alt="">' : ic('photo')) + '</div>' +
      '<p class="small muted">' + t('coverP') + '</p><div class="row"><button type="button" class="btn sm" id="cover">' + t('chooseCover') + '</button>' +
      (m.coverUrl ? '<button type="button" class="btn sm danger" id="nocover">' + t('remove') + '</button>' : '') + '</div></div>' +
      '<div class="pk"><span class="lbl">' + t('logo') + '</span><div class="lg">' + (m.logoUrl ? '<img src="' + esc(m.logoUrl) + '" alt="">' : ic('photo')) + '</div>' +
      '<p class="small muted">' + t('logoP') + '</p><div class="row"><button type="button" class="btn sm" id="logo">' + t('chooseLogo') + '</button>' +
      (m.logoUrl ? '<button type="button" class="btn sm danger" id="nologo">' + t('remove') + '</button>' : '') + '</div></div></div></section>' +
      '<section class="card"><fieldset class="styles"><legend><h2>' + t('lsH') + '</h2></legend><p class="sub">' + t('lsP') + '</p><div class="stygrid">' + styles + '</div></fieldset></section>';
  }

  // ---- how to reach the shop ----
  var CONTACT = [['address', 'cAddr', 300, 'full'], ['mapUrl', 'cMap', 500, 'full', 'cMapHint'], ['phone', 'cPhone', 30, ''], ['whatsApp', 'cWa', 30, '', 'cWaHint'],
    ['instagram', 'cIg', 200, '', 'cHandle'], ['facebook', 'cFb', 200, '', 'cHandle'], ['tikTok', 'cTt', 200, '', 'cHandle'], ['website', 'cWeb', 200, '']];
  function contactForm() {
    var m = S.menu;
    return '<section class="card"><form id="fcontact"><div class="fields" style="margin-top:0">' +
      CONTACT.map(function (c) {
        var ltr = c[0] !== 'address';
        return '<div class="f ' + c[3] + '"><label for="c-' + c[0] + '">' + t(c[1]) + '</label><input id="c-' + c[0] + '" maxlength="' + c[2] + '"' + (ltr ? ' dir="ltr"' : ' dir="auto"') +
          (c[0] === 'phone' || c[0] === 'whatsApp' ? ' inputmode="tel"' : '') + (c[0] === 'mapUrl' || c[0] === 'website' ? ' inputmode="url"' : '') +
          ' value="' + esc(m[c[0]] || '') + '"' + (c[4] === 'cHandle' ? ' placeholder="' + esc(t('cHandle')) + '"' : '') + '>' +
          (c[4] && c[4] !== 'cHandle' ? '<span class="hint">' + t(c[4]) + '</span>' : '') + '</div>';
      }).join('') + '</div><p class="err" id="cerr" hidden></p></form></section>';
  }

  // ---- opening hours: Monday first on screen, Sunday first in the data. A day can open several times (09-12, 13-16, 18-21). ----
  var WEEK = [1, 2, 3, 4, 5, 6, 0], MAX_RANGES = 4;
  // The clock the hour pickers use: '24h' (13:00) or '12h' (1:00 PM). The owner picks it on the hours page; the data is always 24-hour.
  var CLOCK = '24h';
  function pad2(n) { return (n < 10 ? '0' : '') + n; }
  /** "13:30" as the menu writes it: "13:30" or "1:30 PM". */
  function fmtTime(v, clock) {
    if (!v || (clock || S.menu.clock) !== '12h') return v;
    var h = +v.slice(0, 2);
    return (h % 12 || 12) + ':' + v.slice(3, 5) + ' ' + t(h < 12 ? 'am' : 'pm');
  }
  /** A time picker (hour, minutes and, on the 12-hour clock, AM/PM) holding "HH:mm", or nothing picked for "". */
  function timePick(attr, v) {
    var h = v ? +v.slice(0, 2) : -1, m = v ? +v.slice(3, 5) : -1, twelve = CLOCK === '12h', i;
    var opt = function (val, label, sel) { return '<option value="' + val + '"' + (sel ? ' selected' : '') + '>' + label + '</option>'; };
    var hs = '<select data-th aria-label="' + esc(t('hrHour')) + '">' + (h < 0 ? opt('', '--', true) : '');
    if (twelve) for (i = 1; i <= 12; i++) hs += opt(i % 12, i, h >= 0 && h % 12 === i % 12);
    else for (i = 0; i < 24; i++) hs += opt(i, pad2(i), i === h);
    hs += '</select>';
    var ms = '<select data-tm aria-label="' + esc(t('hrMin')) + '">' + (m < 0 ? opt('', '--', true) : '');
    for (i = 0; i < 60; i += 5) {
      if (m > i - 5 && m < i && m % 5) ms += opt(pad2(m), pad2(m), true); // an odd minute saved earlier (09:07) stays
      ms += opt(pad2(i), pad2(i), i === m);
    }
    if (m > 55) ms += opt(pad2(m), pad2(m), true);
    var ap = twelve ? '<select data-tp aria-label="AM / PM">' + opt(0, t('am'), h < 12) + opt(12, t('pm'), h >= 12) + '</select>' : '';
    return '<span class="tp" ' + attr + ' dir="ltr">' + hs + '<span class="col" aria-hidden="true">:</span>' + ms + '</select>' + ap + '</span>';
  }
  /** What a picker holds, as "HH:mm" (24-hour), or "" when the hour or minutes are not picked. */
  function timeOf(el) {
    var h = el.querySelector('[data-th]').value, m = el.querySelector('[data-tm]').value, p = el.querySelector('[data-tp]');
    return h === '' || m === '' ? '' : pad2(+h + (p ? +p.value : 0)) + ':' + m;
  }
  function rangeField(from, to) {
    return '<div class="rg"><span class="tm" role="group" aria-label="' + esc(t('hrFrom')) + '">' + timePick('data-hfrom', from) + '</span>' +
      '<span class="dash" aria-hidden="true">–</span>' +
      '<span class="tm" role="group" aria-label="' + esc(t('hrTo')) + '">' + timePick('data-hto', to) + '</span>' +
      '<button type="button" class="btn icon ghost del" data-hdel title="' + esc(t('hrDel')) + '" aria-label="' + esc(t('hrDel')) + '">' + ic('x') + '</button></div>';
  }
  /** The 13:00 / 1:00 PM choice. Switching redraws the pickers with the same times. */
  function clockField(clock) {
    var o = function (k, label) { return '<label><input type="radio" name="hclock" value="' + k + '"' + (k === clock ? ' checked' : '') + '><span dir="ltr">' + label + '</span></label>'; };
    return '<fieldset class="clk"><legend>' + t('hrClock') + '</legend>' + o('24h', '13:00') + o('12h', '1:00 ' + t('pm')) + '</fieldset>';
  }
  function wireClock(form, root) {
    form.querySelectorAll('input[name=hclock]').forEach(function (r) {
      r.addEventListener('change', function () {
        if (!r.checked) return;
        CLOCK = r.value;
        root.querySelectorAll('.tp').forEach(function (tp) {
          tp.outerHTML = timePick(tp.hasAttribute('data-hfrom') ? 'data-hfrom' : 'data-hto', timeOf(tp));
        });
      });
    });
  }
  /** "09:00-12:00,13:00-16:00" as [["09:00","12:00"],["13:00","16:00"]]. */
  function rangesOf(raw) {
    return (raw || '').split(',').filter(Boolean).map(function (r) { return [r.slice(0, 5), r.slice(6, 11)]; });
  }
  function hoursFields(id, hours) {
    var days = t('days');
    return '<div class="hours" id="' + id + '">' + WEEK.map(function (d) {
      var raw = hours ? hours[d] || '' : '', on = !!raw, list = rangesOf(raw);
      if (!list.length) list = [['09:00', '23:00']];
      return '<div class="hr' + (on ? '' : ' off') + '" data-day="' + d + '"><label class="switch"><input type="checkbox" data-hon' + (on ? ' checked' : '') + '><span>' + esc(days[d]) + '</span></label>' +
        '<span class="hst muted small">' + t('hrClosed') + '</span>' +
        '<div class="rgs">' + list.map(function (r) { return rangeField(r[0], r[1]); }).join('') +
        '<button type="button" class="btn sm ghost add" data-hadd' + (list.length >= MAX_RANGES ? ' hidden' : '') + '>' + ic('plus') + '<span>' + t('hrAdd') + '</span></button></div></div>';
    }).join('') + '<p class="row" style="margin-top:10px"><button type="button" class="btn sm" data-hcopy="' + id + '">' + t('hrCopy') + '</button></p></div>';
  }
  function wireHours(root) {
    function sync(row) {
      var n = row.querySelectorAll('.rg').length;
      row.querySelector('[data-hadd]').hidden = n >= MAX_RANGES;
      row.classList.toggle('one', n === 1);
    }
    root.querySelectorAll('.hr').forEach(function (row) {
      var box = row.querySelector('[data-hon]');
      box.addEventListener('change', function () { row.classList.toggle('off', !box.checked); });
      sync(row);
    });
    // One listener for the add and remove buttons, so ranges added later work too.
    root.addEventListener('click', function (e) {
      var add = e.target.closest('[data-hadd]'), del = e.target.closest('[data-hdel]'), row = e.target.closest('.hr');
      if (add && row) {
        // The new range starts an hour after the last one closes (12:00 → 13:00-16:00); left empty when that would be late at night.
        var last = [].slice.call(row.querySelectorAll('[data-hto]')).pop(), lv = last ? timeOf(last) : '', h = lv ? +lv.slice(0, 2) + 1 : 18;
        var pad = function (n) { return (n < 10 ? '0' : '') + n + ':00'; };
        add.insertAdjacentHTML('beforebegin', h >= 6 && h <= 20 ? rangeField(pad(h), pad(h + 3)) : rangeField('', ''));
        sync(row);
        add.previousElementSibling.querySelector('[data-hfrom] select').focus();
      } else if (del && row && row.querySelectorAll('.rg').length > 1) {
        del.closest('.rg').remove();
        sync(row);
        row.querySelector('[data-hadd]').focus();
      }
    });
    root.querySelectorAll('[data-hcopy]').forEach(function (b) {
      b.onclick = function () {
        var rows = [].slice.call(root.querySelectorAll('.hr')), src = rows.filter(function (r) { return r.querySelector('[data-hon]').checked; })[0];
        if (!src) return;
        var list = [].slice.call(src.querySelectorAll('.rg')).map(function (g) { return [timeOf(g.querySelector('[data-hfrom]')), timeOf(g.querySelector('[data-hto]'))]; });
        rows.forEach(function (r) {
          if (r === src || !r.querySelector('[data-hon]').checked) return;
          r.querySelectorAll('.rg').forEach(function (g) { g.remove(); });
          r.querySelector('[data-hadd]').insertAdjacentHTML('beforebegin', list.map(function (x) { return rangeField(x[0], x[1]); }).join(''));
          sync(r);
        });
      };
    });
  }
  /** Seven strings, Sunday first: "09:00-12:00,13:00-16:00" in time order, or "" for closed. */
  function readHours(root) {
    var out = ['', '', '', '', '', '', ''];
    root.querySelectorAll('.hr').forEach(function (row) {
      if (!row.querySelector('[data-hon]').checked) return;
      out[+row.dataset.day] = [].slice.call(row.querySelectorAll('.rg')).map(function (g) {
        var f = timeOf(g.querySelector('[data-hfrom]')), to = timeOf(g.querySelector('[data-hto]'));
        return f && to ? f + '-' + to : '';
      }).filter(Boolean).sort().join(',');
    });
    return out;
  }
  function hoursForm() {
    var m = S.menu;
    CLOCK = m.clock || '24h';
    return '<section class="card"><form id="fhours">' + clockField(CLOCK) + hoursFields('h-menu', m.hours) +
      (m.hours ? '' : '<p class="small muted" style="margin-top:8px">' + t('hrNone') + '</p>') +
      '<p class="err" id="herr" hidden></p>' + (m.hours ? '<p style="margin-top:14px"><button type="button" class="btn sm" id="hclear">' + t('hrClear') + '</button></p>' : '') + '</form></section>' +
      // With several branches, each can keep its own address and hours.
      ((S.branches || []).length > 1 ? '<section class="group"><h2>' + t('brHoursH') + '</h2><ul class="list">' + S.branches.map(function (b) {
        var own = b.hours ? hoursSum(b.hours)[0] : t('brSame');
        return '<li><button type="button" class="lrow" data-brdet="' + esc(b.siteId) + '"><span class="li">' + ic('clock') + '</span><span class="lt"><b dir="auto">' + esc(b.name) + '</b>' +
          '<span dir="auto">' + esc(own) + '</span></span>' + ic('next', 'flip') + '</button></li>';
      }).join('') + '</ul></section>' : '');
  }
  /** One line for the Shop page: a typical day's hours and the days closed. [text, tone] */
  function hoursSum(hours) {
    if (!hours) return [t('notSet'), 'warn'];
    var days = t('days'), open = WEEK.filter(function (d) { return hours[d]; }), closed = WEEK.filter(function (d) { return !hours[d]; });
    if (!open.length) return [t('notSet'), 'warn'];
    var times = rangesOf(hours[open[0]]).map(function (r) { return t('hrRange', fmtTime(r[0]), fmtTime(r[1])); }).join(', ');
    return [times + ' · ' + (closed.length ? t('hrClosedOn', closed.map(function (d) { return days[d]; }).join(', ')) : t('hrEvery')), ''];
  }

  /** A branch's own address, map, phone and hours (each empty one falls back to the shop's). */
  function branchDialog(b) {
    var own = !!b.hours;
    if (!$('#fhours')) CLOCK = S.menu.clock || '24h'; // on the hours page, the clock picked there (saved or not)
    dialog('<form><h2 id="dlg-t">' + esc(t('brDlg', b.name)) + '</h2><p class="sub">' + t('brDlgP') + '</p><div class="fields">' +
      '<div class="f full"><label for="b-addr">' + t('cAddr') + '</label><input id="b-addr" maxlength="300" dir="auto" value="' + esc(b.address || '') + '" placeholder="' + esc(S.menu.address || '') + '"></div>' +
      '<div class="f full"><label for="b-map">' + t('cMap') + '</label><input id="b-map" maxlength="500" dir="ltr" inputmode="url" value="' + esc(b.mapUrl || '') + '"><span class="hint">' + t('cMapHint') + '</span></div>' +
      '<div class="f"><label for="b-phone">' + t('cPhone') + '</label><input id="b-phone" maxlength="30" dir="ltr" inputmode="tel" value="' + esc(b.phone || '') + '" placeholder="' + esc(S.menu.phone || '') + '"></div>' +
      '<label class="switch full"><input type="checkbox" id="b-own"' + (own ? ' checked' : '') + '><span>' + t('brOwnHours') + '</span></label>' +
      '<div class="full" id="b-hwrap"' + (own ? '' : ' hidden') + '>' + hoursFields('h-branch', b.hours || S.menu.hours) + '</div>' +
      '</div><p class="err" hidden></p><div class="foot"><button type="button" class="btn" data-close>' + t('cancel') + '</button><button class="btn pri">' + t('save') + '</button></div></form>',
      function () {
        var body = { address: $('#b-addr').value, mapUrl: $('#b-map').value, phone: $('#b-phone').value,
          hours: $('#b-own').checked ? readHours($('#h-branch')) : ['', '', '', '', '', '', ''] };
        return api('PUT', '/branches/' + b.siteId, body).then(apply);
      },
      function (d) {
        wireHours(d);
        d.querySelector('#b-own').onchange = function (e) { d.querySelector('#b-hwrap').hidden = !e.target.checked; };
      });
  }

  function detailsForm() {
    var m = S.menu, origin = m.url.slice(0, m.url.length - m.slug.length);
    return '<section class="card"><form id="fset"><div class="fields" style="margin-top:0">' +
      '<div class="f"><label for="s-title">' + t('mTitle') + '</label><input id="s-title" maxlength="120" required value="' + esc(m.title) + '"></div>' +
      '<div class="f"><label for="s-tag">' + t('mTag') + '</label><input id="s-tag" maxlength="200" value="' + esc(m.tagline || '') + '"></div>' +
      '<div class="f full"><label for="s-note">' + t('mNote') + '</label><textarea id="s-note" maxlength="1000">' + esc(m.note || '') + '</textarea></div>' +
      '<div class="f"><label for="s-lang">' + t('mLang') + '</label><select id="s-lang"><option value="en"' + (m.language === 'en' ? ' selected' : '') + '>English</option><option value="ar"' + (m.language === 'ar' ? ' selected' : '') + '>العربية</option></select></div>' +
      '<div class="f"><label for="s-cur">' + t('mCur') + '</label><input id="s-cur" maxlength="10" value="' + esc(m.currency || '') + '" placeholder="$"></div>' +
      '<div class="f full"><label for="s-slug">' + t('mSlug') + '</label><div class="slug"><span>' + esc(origin.replace(/^https?:\/\//, '')) + '</span><input id="s-slug" maxlength="60" pattern="[a-z0-9][a-z0-9\\-]{1,58}[a-z0-9]" value="' + esc(m.slug) + '" spellcheck="false" autocapitalize="off"></div><span class="hint">' + t('slugHint') + '</span></div>' +
      '</div></form></section>';
  }

  // ---- Shop: what customers see about the shop, how orders work, and setup; each row opens its own page ----
  function shopHome() {
    var m = S.menu, types = S.orderTypes || [];
    var contact = [m.address, m.phone || m.whatsApp].filter(Boolean).join(' · ');
    var hrs = hoursSum(m.hours);
    var mode = m.orderMode || 'show';
    var group = function (title, rows) { return '<section class="group"><h2>' + title + '</h2><ul class="list">' + rows.join('') + '</ul></section>'; };
    return head(t('tabShop')) + '<p class="lead">' + t('shopSub') + '</p>' +
      group(t('grpSee'), [
        lrow('shop/contact', 'pin', t('ctH'), contact || t('notSet'), contact ? '' : 'warn'),
        lrow('shop/hours', 'clock', t('hrH'), hrs[0], hrs[1]),
        lrow('shop/details', 'text', t('detailsH'), m.title + ' · ' + m.url.replace(/^https?:\/\//, '')),
      ]) +
      group(t('grpOrders'), [
        lrow('shop/taking', 'bell', t('omH'), t('om_' + mode) + (m.showWhatsApp && m.whatsApp ? t('withWa') : '')),
        lrow('shop/types', 'tag', t('otH'), types.length ? types.map(function (x) { return x.name; }).join(', ') : t('otNoneShort')),
      ]) +
      group(t('grpSetup'), [
        lrow('shop/products', 'link', t('prodH'), m.linkedSiteId ? t('srcPos') + ': ' + S.branches.map(function (b) { return b.name; }).join(', ') : t('srcHandShort')),
      ]) +
      '<p class="out">' + exitHtml('btn') + '</p>';
  }
  function shopPage(k) {
    var back = ['shop', t('tabShop')];
    switch (k) {
      case 'contact': return head(t('ctH'), back) + '<p class="lead">' + t('ctP') + '</p>' + contactForm() + saveBar('fcontact');
      case 'hours': return head(t('hrH'), back) + '<p class="lead">' + t('hrP') + '</p>' + hoursForm() + saveBar('fhours');
      case 'details': return head(t('detailsH'), back) + detailsForm() + saveBar('fset');
      case 'taking': return head(t('omH'), back) + '<p class="lead">' + t('omP') + '</p>' + takingBody();
      case 'types': return head(t('otH'), back, '<button type="button" class="btn sm" id="addtype">' + ic('plus') + t('otAdd') + '</button>') +
        '<p class="lead">' + t('otP') + '</p>' + typesBody();
      default: return head(t('prodH'), back) + sourceCard();
    }
  }

  function itemsOf(key) { return L[key || 0] ? L[key || 0].items : []; }
  /** A section's item rows: what is loaded, a placeholder that loads the first page when it comes near, and "Show more". */
  function itemsList(key, count) {
    var slot = L[key];
    if (!count) return '<li class="emptys">' + t('emptySec') + '</li>';
    // Sized like the rows it stands for, so only sections near the screen load (not every section at once).
    if (!slot) return '<li class="emptys" data-load="' + key + '" style="min-height:' + Math.min(count, PAGE) * 73 + 'px">' + t('loading') + '</li>';
    var list = slot.items;
    var rows = list.map(function (i, idx) { return itemRow(i, list, idx, slot.hasMore); }).join('');
    if (slot.hasMore) rows += '<li class="emptys"><button type="button" class="btn sm" data-more="' + key + '">' + t('showMore', count - list.length) + '</button></li>';
    return rows;
  }

  function itemRow(i, list, idx, more, noMove) {
    var tags = (i.linked ? '<span class="pill info">' + t('pos') + '</span>' : '') + (i.isHidden ? '<span class="pill mute">' + t('hidden') + '</span>' : '') + (i.isSoldOut ? '<span class="pill warn">' + t('soldOut') + '</span>' : '');
    return '<li class="it' + (i.isHidden ? ' hid' : '') + '"><button type="button" class="th" data-pic="' + i.id + '" aria-label="' + t('picture') + ': ' + esc(i.name) + '">' + (i.imageUrl ? '<img src="' + esc(i.imageUrl) + '" alt="" loading="lazy">' : ic('photo')) + '</button>' +
      '<div class="nm"><span class="t" dir="auto">' + esc(i.name) + '</span>' + (tags ? '<span class="tags">' + tags + '</span>' : '') + '</div>' +
      '<div class="acts"><span class="pr" dir="ltr">' + esc(i.priceText) + '</span>' +
      (noMove || fixedOrder(i.linked) ? '' : '<button type="button" class="btn icon mv" data-imv="' + i.id + '" data-dir="-1" aria-label="' + t('up') + '"' + (idx === 0 ? ' disabled' : '') + '>' + ic('up') + '</button>' +
      '<button type="button" class="btn icon mv" data-imv="' + i.id + '" data-dir="1" aria-label="' + t('down') + '"' + (idx === list.length - 1 && !more ? ' disabled' : '') + '>' + ic('down') + '</button>') +
      '<button type="button" class="btn sm" data-edit="' + i.id + '">' + t('edit') + '</button></div></li>';
  }

  function secThumb(s) {
    return '<span class="li sth">' + (s && s.imageUrl ? '<img src="' + esc(s.imageUrl) + '" alt="" loading="lazy">' : esc(((s ? s.name : t('noSection')) || '?').trim().charAt(0).toUpperCase())) + '</span>';
  }
  /** Dishes: a search over every dish, then the sections; each opens its own page. */
  function dishesPage() {
    var secs = S.sections;
    var h = head(t('tabDishes'), null, '<button type="button" class="btn pri addbtn" data-additem="">' + ic('plus') + t('addDish') + '</button>') +
      '<div class="search">' + ic('search') + '<input type="search" id="dq" enterkeyhint="search" autocomplete="off" placeholder="' + esc(t('search')) + '" aria-label="' + esc(t('search')) + '" value="' + esc(Q.term) + '"></div>' +
      '<div id="dres" hidden></div><section class="group" id="dsecs"><div class="gh"><h2>' + t('secsH') + '</h2>' +
      '<button type="button" class="btn sm" id="addsec">' + ic('plus') + t('addSec') + '</button></div>' +
      (S.menu.posArranged ? '<p class="note" style="margin:0 0 10px">' + t('posOrder') + '</p>' : '');
    if (!secs.length && !S.looseCount) h += '<section class="card empty"><p>' + (S.menu.linkedSiteId ? t('emptyLinked') : t('emptyMenu')) + '</p></section>';
    else h += '<ul class="list">' + secs.map(function (s, si) {
      // Only "Hidden" is marked here: nearly every section of a POS menu is from the POS, so that is said once, on the section's page.
      return '<li class="srow"><a class="lrow" href="#dishes/' + s.id + '">' + secThumb(s) + '<span class="lt"><b dir="auto">' + esc(s.name) + '</b>' +
        '<span>' + t('nItems', s.itemCount) + (s.isHidden ? ' · ' + t('hidden') : '') + '</span></span>' + ic('next', 'flip') + '</a>' +
        (fixedOrder(s.linked) ? '' : '<span class="mvs"><button type="button" class="btn icon" data-smv="' + s.id + '" data-dir="-1" aria-label="' + t('up') + ': ' + esc(s.name) + '"' + (si === 0 ? ' disabled' : '') + '>' + ic('up') + '</button>' +
          '<button type="button" class="btn icon" data-smv="' + s.id + '" data-dir="1" aria-label="' + t('down') + ': ' + esc(s.name) + '"' + (si === secs.length - 1 ? ' disabled' : '') + '>' + ic('down') + '</button></span>') + '</li>';
    }).join('') + (S.looseCount ? '<li class="srow"><a class="lrow" href="#dishes/0">' + secThumb(null) + '<span class="lt"><b>' + t('noSection') + '</b><span>' + t('nItems', S.looseCount) + '</span></span>' + ic('next', 'flip') + '</a></li>' : '') + '</ul>';
    return h + '</section>' + fab('');
  }
  /** One section: chips to jump to the others, its picture, line and switch, then its dishes a page at a time. */
  function sectionPage(id) {
    var s = id ? secById(id) : null, count = s ? s.itemCount : S.looseCount;
    var chip = function (key, name) { return '<a href="#dishes/' + key + '" dir="auto"' + (key === id ? ' aria-current="page"' : '') + '>' + esc(name) + '</a>'; };
    var h = head(s ? s.name : t('noSection'), ['dishes', t('tabDishes')], '<button type="button" class="btn pri addbtn" data-additem="' + id + '">' + ic('plus') + t('addDish') + '</button>') +
      '<nav class="chips" aria-label="' + esc(t('secsH')) + '">' + S.sections.map(function (x) { return chip(x.id, x.name); }).join('') + (S.looseCount ? chip(0, t('noSection')) : '') + '</nav>';
    if (s)
      h += '<section class="card secsum"><button type="button" class="th sth" data-spic="' + s.id + '" aria-label="' + esc(t('secPic') + ': ' + s.name) + '">' +
        (s.imageUrl ? '<img src="' + esc(s.imageUrl) + '" alt="">' : ic('photo')) + '</button>' +
        '<div class="ss"><p class="small muted">' + t('nItems', count) + (s.linked ? ' · ' + t('pos') : '') + '</p>' + (s.description ? '<p dir="auto">' + esc(s.description) + '</p>' : '') +
        '<label class="switch"><input type="checkbox" id="sec-shown"' + (s.isHidden ? '' : ' checked') + '><span>' + t('secShown') + '</span></label></div>' +
        '<div class="sa"><button type="button" class="btn sm" data-sren="' + s.id + '">' + t('secEdit') + '</button>' +
        (s.linked ? '' : '<button type="button" class="btn sm danger" data-sdel="' + s.id + '">' + t('del') + '</button>') + '</div></section>';
    return h + '<ul class="items list">' + itemsList(id, count) + '</ul>' + fab(id);
  }

  // ---- wiring ----
  function on(sel, ev, fn) { var el = $(sel); if (el) el.addEventListener(ev, fn); }
  function each(attr, fn) { document.querySelectorAll('[' + attr + ']').forEach(function (b) { b.addEventListener('click', function () { fn(+b.getAttribute(attr), b); }); }); }

  function bind() {
    on('#fcreate', 'submit', function (e) { e.preventDefault(); act(api('POST', '/editor', { title: $('#ct').value })); });
    if (!S.menu) return;
    var m = S.menu;

    on('#pub', 'change', function (e) { act(api('PUT', '/settings', { isPublished: e.target.checked })); });
    document.querySelectorAll('input[name=omode]').forEach(function (r) {
      r.addEventListener('change', function () { act(api('PUT', '/settings', { orderMode: r.value })).then(function () { toast(t('omSaved')); O.orders = null; pollOrders(); }, function () {}); });
    });
    on('#wa-show', 'change', function (e) { act(api('PUT', '/settings', { showWhatsApp: e.target.checked })); });
    // The list is redrawn by itself every few seconds, so its clicks are caught on the box around it.
    on('#ord-wrap', 'click', function (e) {
      var v = e.target.closest('[data-oview]');
      if (v) { O.view = v.dataset.oview; drawOrders(); var sel = document.querySelector('[data-oview="' + O.view + '"]'); if (sel) sel.focus(); return; }
      var b = e.target.closest('[data-omove]'); if (!b) return;
      var id = +b.dataset.omove, to = b.dataset.to, o = (O.orders || []).filter(function (x) { return x.id === id; })[0];
      if (to === 'rejected' && !confirm(t('oRejectQ', o ? o.number : ''))) return;
      b.disabled = true;
      api('PUT', '/orders/' + id, { status: to }).then(function () { pollOrders(); }, function (x) { b.disabled = false; toast(x.message); });
    });
    on('#copy', 'click', function () {
      (navigator.clipboard ? navigator.clipboard.writeText(shareUrl()) : Promise.reject()).then(function () { toast(t('copied')); }, function () {
        var r = document.createRange(); r.selectNodeContents($('#murl')); var s = getSelection(); s.removeAllRanges(); s.addRange(r);
      });
    });
    drawQr();
    on('#qrdl', 'click', downloadQr);

    // From my POS: every branch of the shop fills the menu (any can be taken off below). Added here: none.
    on('#src-pos', 'click', function () { if (!m.linkedSiteId && S.sites.length) act(api('PUT', '/settings', { branchIds: S.sites.map(function (s) { return s.id; }) })); });
    on('#src-hand', 'click', function () { if (m.linkedSiteId && confirm(t('unlinkQ'))) act(api('PUT', '/settings', { branchIds: [] })); });
    var linkedIds = function () { return S.branches.map(function (b) { return b.siteId; }); };
    document.querySelectorAll('[data-br]').forEach(function (box) {
      box.addEventListener('change', function () {
        var id = box.dataset.br, ids = linkedIds();
        if (box.checked) { ids.push(id); act(api('PUT', '/settings', { branchIds: ids })); return; }
        var b = S.branches.filter(function (x) { return x.siteId === id; })[0];
        if (!confirm(t('brOffQ', b ? b.name : ''))) { box.checked = true; return; }
        act(api('PUT', '/settings', { branchIds: ids.filter(function (x) { return x !== id; }) }));
      });
    });
    document.querySelectorAll('[data-brmain]').forEach(function (b) {
      b.onclick = function () { var id = b.dataset.brmain; act(api('PUT', '/settings', { branchIds: [id].concat(linkedIds().filter(function (x) { return x !== id; })) })); };
    });
    on('#qrfor', 'change', function (e) { qrBranch = e.target.value; render(); });

    on('#addtype', 'click', function () { typeDialog(null); });
    each('data-tedit', function (id) { typeDialog(typeById(id)); });
    each('data-tdel', function (id) { var ty = typeById(id); if (confirm(t('otDelQ', ty.name))) actItems(api('DELETE', '/types/' + id)); });
    each('data-tmv', function (id, b) { moveType(id, +b.dataset.dir); });

    document.querySelectorAll('input[name=lstyle]').forEach(function (r) {
      r.addEventListener('change', function () { act(api('PUT', '/settings', { listingStyle: r.value })).then(function () { toast(t('lsSaved')); }, function () {}); });
    });
    on('#fset', 'submit', function (e) {
      e.preventDefault();
      act(api('PUT', '/settings', {
        title: $('#s-title').value, tagline: $('#s-tag').value, note: $('#s-note').value, language: $('#s-lang').value,
        currency: $('#s-cur').value, slug: $('#s-slug').value.trim().toLowerCase(),
      })).then(function () { toast(t('saved')); }, function () {});
    });
    document.querySelectorAll('input[name=mtheme]').forEach(function (r) {
      r.addEventListener('change', function () { act(api('PUT', '/settings', { theme: r.value })).then(function () { toast(t('thSaved')); }, function () {}); });
    });
    on('#cover', 'click', function () { pickFile(function (f) { shrink(f, 1800, false).then(function (b) { return act(api('PUT', '/cover', undefined, b)); }).catch(function (e) { toast(e.message); }); }); });
    on('#nocover', 'click', function () { act(api('DELETE', '/cover')); });
    on('#fcontact', 'submit', function (e) {
      e.preventDefault();
      var body = {}, err = $('#cerr');
      CONTACT.forEach(function (c) { body[c[0]] = $('#c-' + c[0]).value; });
      err.hidden = true;
      api('PUT', '/settings', body).then(function (st) { apply(st); toast(t('saved')); }, function (x) { err.textContent = x.message; err.hidden = false; });
    });
    var hroot = $('#h-menu');
    if (hroot) { wireHours(hroot); wireClock($('#fhours'), hroot); }
    on('#fhours', 'submit', function (e) {
      e.preventDefault();
      var err = $('#herr'); err.hidden = true;
      var ck = $('#fhours input[name=hclock]:checked');
      api('PUT', '/settings', { hours: readHours($('#h-menu')), clock: ck ? ck.value : CLOCK }).then(function (st) { apply(st); toast(t('saved')); }, function (x) { err.textContent = x.message; err.hidden = false; });
    });
    on('#hclear', 'click', function () { act(api('PUT', '/settings', { hours: ['', '', '', '', '', '', ''] })); });
    document.querySelectorAll('[data-brdet]').forEach(function (b) {
      b.onclick = function () { var br = S.branches.filter(function (x) { return x.siteId === b.dataset.brdet; })[0]; if (br) branchDialog(br); };
    });
    each('data-spic', function (id) { pickFile(function (f) { uploadSectionPic(id, f); }); });
    on('#logo', 'click', function () { pickFile(function (f) { shrink(f, 512, true).then(function (b) { return act(api('PUT', '/logo', undefined, b)); }).catch(function (e) { toast(e.message); }); }); });
    on('#nologo', 'click', function () { act(api('DELETE', '/logo')); });

    bindItems($('#main'));
    each('data-smv', function (id, b) { moveSection(id, +b.dataset.dir); });
    each('data-sren', function (id) { sectionDialog(secById(id)); });
    each('data-sdel', function (id) {
      var s = secById(id);
      if (confirm(t('delSecQ', s.name))) actItems(api('DELETE', '/sections/' + id)).then(function () { if (location.hash === '#dishes/' + id) location.hash = '#dishes'; }, function () {});
    });
    on('#sec-shown', 'change', function (e) { var id = +location.hash.split('/')[1]; act(api('PUT', '/sections/' + id, { isHidden: !e.target.checked })); });
    on('#addsec', 'click', function () { sectionDialog(null); });
    document.querySelectorAll('[data-additem]').forEach(function (b) {
      b.onclick = function () { itemDialog(null, b.dataset.additem === '' ? null : +b.dataset.additem); };
    });
    on('#dq', 'input', function (e) {
      Q.term = e.target.value.trim(); Q.items = null;
      clearTimeout(Q.timer);
      if (!Q.term) { drawSearch(); return; }
      Q.timer = setTimeout(function () { drawSearch(); searchNow(); }, 300);
    });
  }
  /** On opening a page: start at its top, put the focus on its title, and bring the open section's chip into view. */
  function opened() {
    window.scrollTo(0, 0);
    var h = document.querySelector('#main h1');
    if (h) h.focus({ preventScroll: true });
    var chip = document.querySelector('.chips [aria-current]');
    if (chip && chip.scrollIntoView) chip.scrollIntoView({ block: 'nearest', inline: 'center' });
    window.scrollTo(0, 0);
  }
  /** Dish rows: picture, edit, order, and loading more. Used for a section's page and for search results. */
  function bindItems(root) {
    if (!root) return;
    var all = function (attr, fn) { root.querySelectorAll('[' + attr + ']').forEach(function (b) { b.addEventListener('click', function () { fn(+b.getAttribute(attr), b); }); }); };
    all('data-pic', function (id) { pickFile(function (f) { uploadItemPic(id, f); }); });
    all('data-edit', function (id) { itemDialog(findItem(id)); });
    all('data-more', function (key) { loadPage(key, true); });
    all('data-imv', function (id, b) { moveItem(id, +b.dataset.dir); });
    root.querySelectorAll('[data-load]').forEach(function (el) { if (watcher) watcher.observe(el); else loadPage(+el.dataset.load, false); });
  }

  function fixedOrder(linked) { return !!(linked && S.menu && S.menu.posArranged); }
  function secById(id) { return S.sections.filter(function (s) { return s.id === id; })[0]; }

  function moveSection(id, dir) {
    var ids = S.sections.map(function (s) { return s.id; }), i = ids.indexOf(id), j = i + dir;
    if (j < 0 || j >= ids.length) return;
    ids[i] = ids[j]; ids[j] = id;
    act(api('PUT', '/order', { sections: ids }));
  }
  function moveItem(id, dir) {
    // The server swaps it with its neighbour, so this works while only part of a long section is loaded.
    actItems(api('POST', '/items/' + id + '/move', { direction: dir }));
  }

  // ---- dialogs ----
  function dialog(html, onSubmit, after) {
    var d = $('#dlg');
    d.classList.remove('wide');
    d.innerHTML = html;
    var f = d.querySelector('form');
    f.addEventListener('submit', function (e) {
      e.preventDefault();
      var err = d.querySelector('.err'); err.hidden = true;
      onSubmit().then(function () { d.close(); }, function (x) { err.textContent = x.message; err.hidden = false; });
    });
    d.querySelector('[data-close]').onclick = function () { d.close(); };
    if (after) after(d);
    d.showModal();
  }

  function uploadSectionPic(id, file) {
    return shrink(file, 1400, false).then(function (b) { return act(api('PUT', '/sections/' + id + '/image', undefined, b)); }).catch(function (e) { toast(e.message); });
  }
  /** A section's name (not for one from the POS), the line under it, and its picture. */
  function sectionDialog(s) {
    var linked = s && s.linked, pending = null;
    dialog('<form><h2 id="dlg-t">' + (s ? t('secEdit') : t('newSection')) + '</h2>' + (linked ? '<p class="note">' + t('secFromPos') + '</p>' : '') + '<div class="fields">' +
      '<div class="f full"><label for="d-sn">' + t('secName') + '</label><input id="d-sn" maxlength="120" required dir="auto"' + (linked ? ' readonly' : '') + ' value="' + esc(s ? s.name : '') + '"></div>' +
      '<div class="f full"><label for="d-sd">' + t('secDesc') + '</label><input id="d-sd" maxlength="200" dir="auto" value="' + esc(s ? s.description || '' : '') + '"><span class="hint">' + t('secDescHint') + '</span></div>' +
      '<div class="f full"><label>' + t('secPic') + '</label><div class="pic"><span class="th wide" id="d-sth">' + (s && s.imageUrl ? '<img src="' + esc(s.imageUrl) + '" alt="">' : ic('photo')) + '</span>' +
      '<button type="button" class="btn sm" id="d-spick">' + t('choosePic') + '</button>' + (s && s.imageUrl ? '<button type="button" class="btn sm danger" id="d-snopic">' + t('remove') + '</button>' : '') +
      '</div><span class="hint">' + t('secPicHint') + '</span></div>' +
      '<fieldset class="f full picas"><legend>' + t('picAs') + '</legend><div class="pa">' +
        ['banner', 'icon'].map(function (k) {
          var on = (s && s.pictureAs || 'banner') === k;
          return '<label class="opt pa-' + k + '"><input type="radio" name="d-pa" value="' + k + '"' + (on ? ' checked' : '') + '><span class="pv"><i class="pi"></i><i class="pl"></i></span><b>' + t(k === 'banner' ? 'picBanner' : 'picIcon') + '</b><span>' + t(k === 'banner' ? 'picBannerD' : 'picIconD') + '</span></label>';
        }).join('') + '</div></fieldset>' +
      '</div><p class="err" hidden></p>' +
      '<div class="foot"><button type="button" class="btn" data-close>' + t('cancel') + '</button><button class="btn pri">' + t('save') + '</button></div></form>',
      function () {
        var pa = document.querySelector('#dlg input[name=d-pa]:checked');
        var body = { description: $('#d-sd').value, pictureAs: pa ? pa.value : 'banner' };
        if (!linked) body.name = $('#d-sn').value;
        return (s ? api('PUT', '/sections/' + s.id, body) : api('POST', '/sections', body)).then(function (state) {
          apply(state);
          var id = s ? s.id : state && state.createdId;
          if (pending && id) return uploadSectionPic(id, pending);
        });
      },
      function (d) {
        d.querySelector('#d-spick').onclick = function () {
          pickFile(function (f) { pending = f; d.querySelector('#d-sth').innerHTML = '<img src="' + URL.createObjectURL(f) + '" alt="">'; });
        };
        var np = d.querySelector('#d-snopic');
        if (np) np.onclick = function () { act(api('DELETE', '/sections/' + s.id + '/image')).then(function () { d.close(); }, function () {}); };
      });
  }

  /** A dish. A new one goes in <paramref name="inSection"/> (0 = no section) when given, else in the last section. */
  function itemDialog(i, inSection) {
    var linked = i && i.linked, pending = null, m = S.menu;
    var want = i ? i.sectionId || 0 : inSection != null ? inSection : S.sections.length ? S.sections[S.sections.length - 1].id : 0;
    var secOpts = '<option value="0"' + (want === 0 ? ' selected' : '') + '>' + t('noSection') + '</option>' +
      S.sections.map(function (s) { return '<option value="' + s.id + '"' + (want === s.id ? ' selected' : '') + '>' + esc(s.name) + '</option>'; }).join('');
    var html = '<form><h2 id="dlg-t">' + (i ? t('editItem') : t('newItem')) + '</h2>' + (linked ? '<p class="note">' + t('fromPos') + '</p>' : '') +
      '<div class="fields">' +
      '<div class="f full"><label for="d-n">' + t('iName') + '</label><input id="d-n" maxlength="200" required dir="auto"' + (linked ? ' readonly' : '') + ' value="' + esc(i ? i.name : '') + '"></div>' +
      '<div class="f full"><label for="d-d">' + t('iDesc') + '</label><textarea id="d-d" maxlength="500" dir="auto" placeholder="' + esc(i && i.posDescription ? i.posDescription : '') + '">' + esc(i ? i.description || '' : '') + '</textarea>' +
      (i && i.posDescription ? '<span class="hint">' + esc(t('posDesc', i.posDescription)) + '</span>' : '') + '</div>' +
      '<div class="f"><label for="d-p">' + t('iPrice') + '</label><input id="d-p" inputmode="decimal" dir="ltr" required' + (linked ? ' readonly' : '') + ' value="' + esc(i ? i.price : '') + '"></div>' +
      '<div class="f"><label for="d-c">' + t('iCur') + '</label><input id="d-c" maxlength="10" dir="ltr"' + (linked ? ' readonly' : '') + ' value="' + esc(i ? i.currency || '' : '') + '" placeholder="' + esc(m.currency) + '"><span class="hint">' + esc(t('iCurHint', m.currency || '-')) + '</span></div>' +
      (linked ? '' : '<div class="f full"><label for="d-s">' + t('iSec') + '</label><select id="d-s">' + secOpts + '</select></div>') +
      // Where a POS dish is sold and at what price (read-only: the branches' POS set them).
      (i && i.branches && i.branches.length > 1 ? '<div class="f full"><label>' + t('brPrices') + '</label><ul class="brprices">' +
        i.branches.map(function (b) { return '<li><span>' + esc(b.name) + '</span><b dir="ltr">' + esc(b.priceText) + '</b></li>'; }).join('') + '</ul></div>' : '') +
      // Its own price per order type; empty = the type's rule.
      ((S.orderTypes || []).length ? '<fieldset class="f full tprices"><legend>' + t('tpH') + '</legend><div class="fields">' +
        S.orderTypes.map(function (ty) {
          var own = i && i.typePrices ? i.typePrices[ty.id] : null;
          return '<div class="f"><label for="d-tp' + ty.id + '" dir="auto">' + esc(ty.name) + '</label><input id="d-tp' + ty.id + '" data-tp="' + ty.id + '" inputmode="decimal" dir="ltr" value="' + (own == null ? '' : esc(own)) + '"' +
            ' placeholder="' + esc(i ? byRule(i.price, ty) : '') + '"><span class="hint">' + esc(t('tpHint', ruleText(ty))) + '</span></div>';
        }).join('') + '</div></fieldset>' : '') +
      '<div class="f full"><label>' + t('picture') + '</label><div class="pic"><span class="th" id="d-th">' + (i && i.imageUrl ? '<img src="' + esc(i.imageUrl) + '" alt="">' : ic('photo')) + '</span>' +
      '<button type="button" class="btn sm" id="d-pick">' + t('choosePic') + '</button>' + (i && i.imageUrl ? '<button type="button" class="btn sm danger" id="d-nopic">' + t('remove') + '</button>' : '') + '</div></div>' +
      '<label class="switch full"><input type="checkbox" id="d-h"' + (i && i.isHidden ? ' checked' : '') + '><span>' + t('iHidden') + '</span></label>' +
      '<label class="switch full"><input type="checkbox" id="d-o"' + (i && i.isSoldOut ? ' checked' : '') + '><span>' + t('iSold') + '</span></label>' +
      '</div><p class="err" hidden></p><div class="foot">' + (i && !linked ? '<button type="button" class="btn danger" id="d-del">' + t('del') + '</button><span class="sp"></span>' : '') +
      '<button type="button" class="btn" data-close>' + t('cancel') + '</button><button class="btn pri">' + t('save') + '</button></div></form>';

    dialog(html, function () {
      var body = { description: $('#d-d').value, isHidden: $('#d-h').checked, isSoldOut: $('#d-o').checked };
      if (!linked) {
        var price = parseFloat(String($('#d-p').value).replace(/,/g, ''));
        if (isNaN(price) || price < 0) return Promise.reject(new Error(t('iPrice') + '?'));
        body.name = $('#d-n').value; body.price = price; body.currency = $('#d-c').value; body.sectionId = +$('#d-s').value;
      }
      var tps = document.querySelectorAll('#dlg [data-tp]');
      if (tps.length) {
        body.typePrices = {};
        for (var k = 0; k < tps.length; k++) {
          var raw = String(tps[k].value).replace(/,/g, '').trim();
          if (raw === '') { body.typePrices[tps[k].dataset.tp] = null; continue; }
          var v = parseFloat(raw);
          if (isNaN(v) || v < 0) { tps[k].focus(); return Promise.reject(new Error(t('tpH') + '?')); }
          body.typePrices[tps[k].dataset.tp] = v;
        }
      }
      var saving = i ? api('PUT', '/items/' + i.id, body) : api('POST', '/items', body);
      return saving.then(function (state) {
        apply(state);
        var id = i ? i.id : state && state.createdId;
        if (pending && id) return uploadItemPic(id, pending);
        return refreshLoaded();
      });
    }, function (d) {
      d.classList.add('wide');
      d.querySelector('#d-pick').onclick = function () {
        pickFile(function (f) { pending = f; var u = URL.createObjectURL(f); d.querySelector('#d-th').innerHTML = '<img src="' + u + '" alt="">'; });
      };
      var np = d.querySelector('#d-nopic');
      if (np) np.onclick = function () { actItems(api('DELETE', '/items/' + i.id + '/image')).then(function () { d.close(); }, function () {}); };
      var del = d.querySelector('#d-del');
      if (del) del.onclick = function () { if (confirm(t('delItemQ', i.name))) actItems(api('DELETE', '/items/' + i.id)).then(function () { d.close(); }, function () {}); };
    });
  }

  // ---- QR ----
  function drawQr() {
    var box = $('#qrbox');
    if (!box || typeof QRCode === 'undefined') return;
    box.innerHTML = '';
    new QRCode(box, { text: shareUrl(), width: 256, height: 256, colorDark: '#0B1633', colorLight: '#ffffff', correctLevel: QRCode.CorrectLevel.M });
  }
  function downloadQr() {
    if (typeof QRCode === 'undefined') return;
    // A large one, with a quiet margin, so it prints sharp on a table card.
    var holder = document.createElement('div');
    new QRCode(holder, { text: shareUrl(), width: 1024, height: 1024, colorDark: '#000000', colorLight: '#ffffff', correctLevel: QRCode.CorrectLevel.M });
    var src = holder.querySelector('canvas');
    if (!src) return;
    var pad = 96, c = document.createElement('canvas');
    c.width = c.height = 1024 + pad * 2;
    var g = c.getContext('2d'); g.fillStyle = '#fff'; g.fillRect(0, 0, c.width, c.height); g.drawImage(src, pad, pad);
    var a = document.createElement('a');
    a.href = c.toDataURL('image/png'); a.download = 'menu-qr-' + S.menu.slug + (qrBranch ? '-' + qrBranch : '') + '.png';
    document.body.appendChild(a); a.click(); a.remove();
  }

  // ---- start ----
  setLang();
  if (!ADMIN && !(session && session.token)) { needSignIn(); return; }
  $('#app').innerHTML = '<div class="wrap">' + bar() + '<p class="loading">' + t('loading') + '</p></div>';
  bindBar();
  api('GET', '/editor').then(apply).then(function () { pollOrders(); }).catch(function (e) { if (S === null && $('#app').querySelector('.loading')) $('#app').querySelector('.loading').textContent = e.message; });
  window.addEventListener('hashchange', function () { if (S && S.menu) { render(); opened(); } });
  var onWide = function () { if (S && S.menu) livePreview(); };
  if (WIDE.addEventListener) WIDE.addEventListener('change', onWide); else if (WIDE.addListener) WIDE.addListener(onWide);
})();
