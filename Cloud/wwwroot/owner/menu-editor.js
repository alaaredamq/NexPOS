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
      detailsH: 'Name, address and look', mTitle: 'Menu name', mTag: 'One line under the name', mNote: 'At the bottom (address, opening hours, phone)',
      mLang: 'Menu language', mCur: 'Currency for items added here', mSlug: 'Address', slugHint: 'Small English letters, digits and dashes.',
      logo: 'Logo', chooseLogo: 'Choose logo', remove: 'Remove', save: 'Save', saved: 'Saved',
      lsH: 'How dishes are listed', lsP: 'Pick the look customers see. You can change it any time; nothing else changes.', lsOn: 'In use', lsSaved: 'Look changed',
      ls_compact: 'Compact rows', ls_compact_d: 'Many dishes on one screen. Fine with or without pictures.',
      ls_grid: 'Photo grid', ls_grid_d: 'Two columns of picture cards. Best when most dishes have a photo.',
      ls_cards: 'Big photo cards', ls_cards_d: 'One large picture per dish, with its description.',
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
      brH: 'Branches', brP: 'Tick the branches whose POS fills the menu. Customers pick their branch; each sees its own prices and what it sells. The first one is the main branch: it decides the names and where dishes sit.',
      brMain: 'Main', brMakeMain: 'Make main', brOffQ: 'Take "{0}" off the menu? Its prices go; dishes only it sells stay as ordinary items you edit here.',
      qrFor: 'QR and link for', qrAll: 'All branches (customers pick)',
      otH: 'Order types', otP: 'Dine in, take out, delivery... Customers pick one and every price follows it. With none, each dish has one price.',
      otAdd: 'Order type', otNew: 'New order type', otEdit: 'Edit order type', otName: 'Name', otRule: 'Prices', otValue: 'By',
      otNone: 'Same as the normal price', otPct: 'Normal price + a percentage', otAmt: 'Normal price + an amount',
      otNoneS: 'Normal price', otPctS: '{0}% on the normal price', otAmtS: '{0} on the normal price', otDelQ: 'Delete the order type "{0}"? Prices set for it on dishes go too.',
      otEmpty: 'No order types: every dish has one price.', otValueHint: 'Use a minus to take off, e.g. -10.',
      tpH: 'Prices by order type', tpHint: 'Empty = by the rule ({0})', brPrices: 'At each branch',
      ago0: 'just now', agoM: '{0} min ago', agoH: '{0} h ago', agoD: '{0} days ago',
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
      detailsH: 'الاسم والعنوان والشكل', mTitle: 'اسم القائمة', mTag: 'سطر تحت الاسم', mNote: 'في الأسفل (العنوان، ساعات العمل، الهاتف)',
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
      ago0: 'الآن', agoM: 'قبل {0} د', agoH: 'قبل {0} س', agoD: 'قبل {0} أيام',
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
  var openDetails = false;

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
      S = state;
      // Forget pages of sections that are gone.
      var keys = keysOf();
      Object.keys(L).forEach(function (k) { if (keys.indexOf(+k) < 0) delete L[k]; });
      render();
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
    return Promise.all(Object.keys(L).map(function (k) {
      var slot = L[k], take = Math.min(100, Math.max(PAGE, slot.items.length));
      return api('GET', '/items?section=' + k + '&skip=0&take=' + take).then(function (page) { slot.items = page.items; slot.hasMore = page.hasMore; });
    })).then(render, function (e) { toast(e.message); });
  }
  var watcher = 'IntersectionObserver' in window ? new IntersectionObserver(function (entries) {
    entries.forEach(function (e) { if (e.isIntersecting) { watcher.unobserve(e.target); loadPage(+e.target.dataset.load, false); } });
  }, { rootMargin: '600px 0px' }) : null;
  function findItem(id) {
    for (var k in L) { var hit = L[k].items.filter(function (i) { return i.id === id; })[0]; if (hit) return hit; }
    return null;
  }

  function needSignIn() {
    $('#app').innerHTML = '<div class="wrap">' + bar() + '<div class="card"><h2>' + t('title') + '</h2><p class="sub">' + t('signIn') + '</p>' +
      '<p style="margin-top:14px"><a class="btn pri" href="' + (ADMIN ? '/admin/Account/Login?ReturnUrl=' + encodeURIComponent(location.pathname + location.search) : '/owner/index.html') + '">' + t('signInBtn') + '</a></p></div></div>';
    bindBar();
  }

  // ---- pictures: shrunk in the browser so uploads are small and fast on mobile data ----
  function shrink(file, max, keepAlpha) {
    return new Promise(function (resolve, reject) {
      var url = URL.createObjectURL(file), img = new Image();
      img.onload = function () {
        var s = Math.min(1, max / Math.max(img.naturalWidth, img.naturalHeight));
        var c = document.createElement('canvas');
        c.width = Math.max(1, Math.round(img.naturalWidth * s)); c.height = Math.max(1, Math.round(img.naturalHeight * s));
        var g = c.getContext('2d');
        if (!keepAlpha) { g.fillStyle = '#fff'; g.fillRect(0, 0, c.width, c.height); }
        g.drawImage(img, 0, 0, c.width, c.height);
        URL.revokeObjectURL(url);
        c.toBlob(function (b) {
          if (b && b.type === 'image/webp') return resolve(b);
          c.toBlob(function (b2) { b2 ? resolve(b2) : reject(new Error(t('picBig'))); }, keepAlpha ? 'image/png' : 'image/jpeg', 0.86);
        }, 'image/webp', 0.84);
      };
      img.onerror = function () { URL.revokeObjectURL(url); reject(new Error(t('picBig'))); };
      img.src = url;
    });
  }
  function pickFile(cb) {
    var input = document.createElement('input');
    input.type = 'file'; input.accept = 'image/*';
    input.onchange = function () { if (input.files && input.files[0]) cb(input.files[0]); };
    input.click();
  }
  function uploadItemPic(id, file) {
    return shrink(file, 1000, false).then(function (b) { return actItems(api('PUT', '/items/' + id + '/image', undefined, b)); })
      .catch(function (e) { toast(e.message); });
  }

  // ---- views ----
  function bar() {
    // A shop with no POS has nothing else on the owner website, so it gets a way out rather than a way "back".
    var back = ADMIN ? '<a class="back" href="/admin/Clients/Details/' + esc(CLIENT) + '">' + ic('back', 'flip') + t('backAdmin') + '</a>'
      : session && session.hasTill === false ? '<button type="button" class="back" id="signout">' + t('signOut') + '</button>'
      : '<a class="back" href="/owner/index.html">' + ic('back', 'flip') + t('back') + '</a>';
    return '<div class="bar">' + back + '<span class="sp"></span><div class="lang" role="group" aria-label="Language">' +
      '<button type="button" data-lang="en" aria-pressed="' + (lang === 'en') + '">EN</button><button type="button" data-lang="ar" aria-pressed="' + (lang === 'ar') + '">ع</button></div></div>';
  }
  function bindBar() {
    var out = document.getElementById('signout');
    if (out) out.onclick = function () {
      var token = session && session.token;
      try { localStorage.removeItem('coduo-owner'); } catch (e) {}
      var done = function () { location.href = '/owner/index.html'; };
      if (!token) return done();
      fetch('/owner/v1/auth/logout', { method: 'POST', headers: { Authorization: 'Bearer ' + token } }).then(done, done);
    };
    document.querySelectorAll('[data-lang]').forEach(function (b) {
      b.onclick = function () { lang = b.dataset.lang; try { localStorage.setItem('coduo-lang', lang); } catch (e) {} setLang(); if (S) render(); else if (!ADMIN && !session) needSignIn(); };
    });
  }
  function setLang() { document.documentElement.lang = lang; document.documentElement.dir = lang === 'ar' ? 'rtl' : 'ltr'; document.title = t('title') + ' · Coduo POS'; }

  function render() {
    var h = '<div class="wrap">' + bar() + '<header class="head"><h1>' + t('title') + (S.admin ? ' <span class="pill mute" style="vertical-align:middle">' + esc(S.clientName) + '</span>' : '') + '</h1><p>' + t('sub') + '</p></header>';
    if (!S.menu) {
      h += '<div class="card"><h2>' + t('createH') + '</h2><p class="sub">' + t('createP') + '</p>' +
        '<form id="fcreate" class="row" style="margin-top:14px"><div class="f" style="flex:1;min-width:220px"><label for="ct">' + t('createName') + '</label><input id="ct" maxlength="120" value="' + esc(S.clientName) + '" required></div>' +
        '<button class="btn pri" style="align-self:flex-end">' + t('create') + '</button></form></div>';
    } else {
      h += shareCard() + sourceCard() + typesCard() + styleCard() + detailsCard() + itemsCard();
    }
    $('#app').innerHTML = h + '</div>';
    bindBar();
    bind();
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
  function typesCard() {
    var types = S.orderTypes || [];
    return '<section class="card"><div class="cardhead"><div><h2>' + t('otH') + '</h2><p class="sub">' + t('otP') + '</p></div>' +
      '<button type="button" class="btn sm" id="addtype">' + ic('plus') + t('otAdd') + '</button></div>' +
      (types.length ? '<ul class="types">' + types.map(function (ty, n) {
        return '<li><b dir="auto">' + esc(ty.name) + '</b><span class="muted small">' + esc(ruleText(ty)) + '</span><span class="sp"></span>' +
          '<button type="button" class="btn icon" data-tmv="' + ty.id + '" data-dir="-1" aria-label="' + t('up') + '"' + (n === 0 ? ' disabled' : '') + '>' + ic('up') + '</button>' +
          '<button type="button" class="btn icon" data-tmv="' + ty.id + '" data-dir="1" aria-label="' + t('down') + '"' + (n === types.length - 1 ? ' disabled' : '') + '>' + ic('down') + '</button>' +
          '<button type="button" class="btn sm" data-tedit="' + ty.id + '">' + t('edit') + '</button>' +
          '<button type="button" class="btn sm danger" data-tdel="' + ty.id + '">' + t('del') + '</button></li>';
      }).join('') + '</ul>' : '<p class="small muted" style="margin-top:10px">' + t('otEmpty') + '</p>') + '</section>';
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
  function styleCard() {
    var current = S.menu.listingStyle || 'compact';
    return '<section class="card"><fieldset class="styles"><legend><h2>' + t('lsH') + '</h2></legend><p class="sub">' + t('lsP') + '</p><div class="stygrid">' +
      STYLES.map(function (s) {
        return '<label class="sty"><input type="radio" name="lstyle" value="' + s + '"' + (s === current ? ' checked' : '') + '>' +
          mini(s) + '<b>' + t('ls_' + s) + (s === current ? ' <span class="pill good">' + t('lsOn') + '</span>' : '') + '</b><span>' + t('ls_' + s + '_d') + '</span></label>';
      }).join('') + '</div></fieldset></section>';
  }

  function detailsCard() {
    var m = S.menu, origin = m.url.slice(0, m.url.length - m.slug.length);
    return '<section class="card"><details class="more" id="det"' + (openDetails ? ' open' : '') + '><summary>' + t('detailsH') + '</summary>' +
      '<form id="fset"><div class="fields">' +
      '<div class="f"><label for="s-title">' + t('mTitle') + '</label><input id="s-title" maxlength="120" required value="' + esc(m.title) + '"></div>' +
      '<div class="f"><label for="s-tag">' + t('mTag') + '</label><input id="s-tag" maxlength="200" value="' + esc(m.tagline || '') + '"></div>' +
      '<div class="f full"><label for="s-note">' + t('mNote') + '</label><textarea id="s-note" maxlength="1000">' + esc(m.note || '') + '</textarea></div>' +
      '<div class="f"><label for="s-lang">' + t('mLang') + '</label><select id="s-lang"><option value="en"' + (m.language === 'en' ? ' selected' : '') + '>English</option><option value="ar"' + (m.language === 'ar' ? ' selected' : '') + '>العربية</option></select></div>' +
      '<div class="f"><label for="s-cur">' + t('mCur') + '</label><input id="s-cur" maxlength="10" value="' + esc(m.currency || '') + '" placeholder="$"></div>' +
      '<div class="f full"><label for="s-slug">' + t('mSlug') + '</label><div class="slug"><span>' + esc(origin.replace(/^https?:\/\//, '')) + '</span><input id="s-slug" maxlength="60" pattern="[a-z0-9][a-z0-9\\-]{1,58}[a-z0-9]" value="' + esc(m.slug) + '" spellcheck="false" autocapitalize="off"></div><span class="hint">' + t('slugHint') + '</span></div>' +
      '<div class="f full"><label>' + t('logo') + '</label><div class="logo">' + (m.logoUrl ? '<img src="' + esc(m.logoUrl) + '" alt="">' : '<span class="ph">' + ic('photo') + '</span>') +
      '<button type="button" class="btn sm" id="logo">' + t('chooseLogo') + '</button>' + (m.logoUrl ? '<button type="button" class="btn sm danger" id="nologo">' + t('remove') + '</button>' : '') + '</div></div>' +
      '</div><p style="margin-top:16px"><button class="btn pri">' + t('save') + '</button></p></form></details></section>';
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

  function itemRow(i, list, idx, more) {
    var tags = (i.linked ? '<span class="pill info">' + t('pos') + '</span>' : '') + (i.isHidden ? '<span class="pill mute">' + t('hidden') + '</span>' : '') + (i.isSoldOut ? '<span class="pill warn">' + t('soldOut') + '</span>' : '');
    return '<li class="it' + (i.isHidden ? ' hid' : '') + '"><button type="button" class="th" data-pic="' + i.id + '" aria-label="' + t('picture') + ': ' + esc(i.name) + '">' + (i.imageUrl ? '<img src="' + esc(i.imageUrl) + '" alt="">' : ic('photo')) + '</button>' +
      '<div class="nm"><span class="t" dir="auto">' + esc(i.name) + '</span>' + (tags ? '<span class="tags">' + tags + '</span>' : '') + '</div>' +
      '<div class="acts"><span class="pr" dir="ltr">' + esc(i.priceText) + '</span>' +
      (fixedOrder(i.linked) ? '' : '<button type="button" class="btn icon mv" data-imv="' + i.id + '" data-dir="-1" aria-label="' + t('up') + '"' + (idx === 0 ? ' disabled' : '') + '>' + ic('up') + '</button>' +
      '<button type="button" class="btn icon mv" data-imv="' + i.id + '" data-dir="1" aria-label="' + t('down') + '"' + (idx === list.length - 1 && !more ? ' disabled' : '') + '>' + ic('down') + '</button>') +
      '<button type="button" class="btn sm" data-edit="' + i.id + '">' + t('edit') + '</button></div></li>';
  }

  function itemsCard() {
    var h = '<section class="card"><h2>' + t('itemsH') + '</h2><p class="sub">' + t('itemsP') + '</p>' + (S.menu.posArranged ? '<p class="note">' + t('posOrder') + '</p>' : '');
    var secs = S.sections;
    secs.forEach(function (s, si) {
      h += '<div class="sec' + (s.isHidden ? ' hid' : '') + '"><div class="sh"><h3 dir="auto">' + esc(s.name) + '</h3>' +
        (s.linked ? '<span class="pill info">' + t('pos') + '</span>' : '') + (s.isHidden ? '<span class="pill mute">' + t('hidden') + '</span>' : '') +
        (fixedOrder(s.linked) ? '' : '<button type="button" class="btn icon" data-smv="' + s.id + '" data-dir="-1" aria-label="' + t('up') + '"' + (si === 0 ? ' disabled' : '') + '>' + ic('up') + '</button>' +
        '<button type="button" class="btn icon" data-smv="' + s.id + '" data-dir="1" aria-label="' + t('down') + '"' + (si === secs.length - 1 ? ' disabled' : '') + '>' + ic('down') + '</button>') +
        '<span class="small muted">' + t('nItems', s.itemCount) + '</span>' +
        '<button type="button" class="btn sm" data-shide="' + s.id + '">' + (s.isHidden ? t('show') : t('hide')) + '</button>' +
        (s.linked ? '' : '<button type="button" class="btn sm" data-sren="' + s.id + '">' + t('rename') + '</button><button type="button" class="btn sm danger" data-sdel="' + s.id + '">' + t('del') + '</button>') +
        '</div><ul class="items">' + itemsList(s.id, s.itemCount) + '</ul></div>';
    });
    if (S.looseCount)
      h += '<div class="sec"><div class="sh"><h3>' + t('noSection') + '</h3><span class="small muted">' + t('nItems', S.looseCount) + '</span></div><ul class="items">' + itemsList(0, S.looseCount) + '</ul></div>';
    if (!secs.length && !S.looseCount) h += '<p class="note">' + (S.menu.linkedSiteId ? t('emptyLinked') : t('emptyMenu')) + '</p>';
    h += '<div class="adds"><button type="button" class="btn" id="addsec">' + ic('plus') + t('addSection') + '</button><button type="button" class="btn pri" id="additem">' + ic('plus') + t('addItem') + '</button></div>';
    return h + '</section>';
  }

  // ---- wiring ----
  function on(sel, ev, fn) { var el = $(sel); if (el) el.addEventListener(ev, fn); }
  function each(attr, fn) { document.querySelectorAll('[' + attr + ']').forEach(function (b) { b.addEventListener('click', function () { fn(+b.getAttribute(attr), b); }); }); }

  function bind() {
    on('#fcreate', 'submit', function (e) { e.preventDefault(); act(api('POST', '/editor', { title: $('#ct').value })); });
    if (!S.menu) return;
    var m = S.menu;

    on('#pub', 'change', function (e) { act(api('PUT', '/settings', { isPublished: e.target.checked })); });
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

    on('#det', 'toggle', function (e) { openDetails = e.target.open; });
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
    on('#logo', 'click', function () { pickFile(function (f) { shrink(f, 512, true).then(function (b) { return act(api('PUT', '/logo', undefined, b)); }).catch(function (e) { toast(e.message); }); }); });
    on('#nologo', 'click', function () { act(api('DELETE', '/logo')); });

    each('data-pic', function (id) { pickFile(function (f) { uploadItemPic(id, f); }); });
    each('data-edit', function (id) { itemDialog(findItem(id)); });
    each('data-more', function (key) { loadPage(key, true); });
    document.querySelectorAll('[data-load]').forEach(function (el) { if (watcher) watcher.observe(el); else loadPage(+el.dataset.load, false); });
    each('data-imv', function (id, b) { moveItem(id, +b.dataset.dir); });
    each('data-smv', function (id, b) { moveSection(id, +b.dataset.dir); });
    each('data-shide', function (id) { var s = secById(id); act(api('PUT', '/sections/' + id, { isHidden: !s.isHidden })); });
    each('data-sren', function (id) { sectionDialog(secById(id)); });
    each('data-sdel', function (id) { var s = secById(id); if (confirm(t('delSecQ', s.name))) actItems(api('DELETE', '/sections/' + id)); });
    on('#addsec', 'click', function () { sectionDialog(null); });
    on('#additem', 'click', function () { itemDialog(null); });
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

  function sectionDialog(s) {
    dialog('<form><h2 id="dlg-t">' + (s ? t('renameSection') : t('newSection')) + '</h2><div class="fields"><div class="f full"><label for="d-sn">' + t('secName') + '</label>' +
      '<input id="d-sn" maxlength="120" required dir="auto" value="' + esc(s ? s.name : '') + '"></div></div><p class="err" hidden></p>' +
      '<div class="foot"><button type="button" class="btn" data-close>' + t('cancel') + '</button><button class="btn pri">' + t('save') + '</button></div></form>',
      function () { var name = $('#d-sn').value; return s ? api('PUT', '/sections/' + s.id, { name: name }).then(apply) : api('POST', '/sections', { name: name }).then(apply); });
  }

  function itemDialog(i) {
    var linked = i && i.linked, pending = null, m = S.menu;
    var secOpts = '<option value="0">' + t('noSection') + '</option>' + S.sections.map(function (s) { return '<option value="' + s.id + '"' + (i && i.sectionId === s.id ? ' selected' : (!i && S.sections[S.sections.length - 1] === s ? ' selected' : '')) + '>' + esc(s.name) + '</option>'; }).join('');
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
  api('GET', '/editor').then(apply).catch(function (e) { if (S === null && $('#app').querySelector('.loading')) $('#app').querySelector('.loading').textContent = e.message; });
})();
