/* Coduo POS Online, owner site.
   Signs in with the owner account, asks for the shop's recovery code once, then reads the encrypted copy of the shop through
   /owner/v1 and decrypts it here in the browser (AES-256-GCM, same format as the phone app: "e1." + base64(nonce|ciphertext|tag),
   table and row id bound in as authenticated data). The key never leaves this browser and is kept as a non-extractable CryptoKey.
   Nothing here writes to the shop: it only reads. */
(function () {
'use strict';
var $ = function (s) { return document.querySelector(s); };
var API = '/owner/v1';
var BRAND = window.CODUO_BRAND;

/* ---------- copy ---------- */
var D = {
en: {
 today: 'Today', sales: 'Sales', stock: 'Stock', shop: 'Shop PC', signout: 'Sign out', lock: 'Lock data', dark: 'Dark mode', online: 'Online',
 greetM: 'Good morning', greetA: 'Good afternoon', greetE: 'Good evening',
 copyAt: 'Copy updated {0}', never: 'The shop PC has not sent anything yet.', refresh: 'Refresh', updating: 'Updating your shop data...',
 salesToday: 'Sales today', vsLast: '{0} vs last {1}, same hours', legToday: 'Today', legLast: 'Last {0}', soFar: 'so far',
 payments: 'Payments', receipts: 'Receipts', avgBasket: 'Average basket', noSalesYet: 'No sales yet today.',
 needs: 'Needs you', recent: 'Recent receipts', seeAll: 'See all sales', allGood: 'Nothing needs you right now.',
 nLow: '{0} items to reorder', nLowD: '{0} below their minimum and {1} out of stock.', seeStock: 'See stock',
 nDebt: 'Customers owe {0}', nDebtD: '{0} customers have an unpaid balance.', nDebtD1: '1 customer has an unpaid balance.', findSale: 'Search receipts',
 shopQuiet: 'The shop PC has been quiet for {0}', shopQuietD: 'Sales made since then appear here once it reconnects. The till keeps selling.', seeShop: 'See shop PC',
 salesSub: 'Receipts copied from your tills. Newest first.', all: 'All', cTime: 'Time', cId: 'Receipt', cCust: 'Customer', cItems: 'Items', cPay: 'Payment', cTot: 'Total',
 noMatch: 'Nothing matches.', rcpt: 'Receipt {0}', rMeta: '{0}, {1}', rTotal: 'Total', discount: 'Discount', voided: 'Void', close: 'Close', loadingItems: 'Loading the items...', noItems: 'No item lines were copied for this receipt.',
 stockSub: 'Quantities as of the last copy. They change when a till sells.', find: 'Search items', low: 'Low', out: 'Out of stock', cName: 'Item', cQty: 'In stock', cMin: 'Minimum', cPrice: 'Price',
 inStock: 'In stock', lowS: 'Low', outS: 'Out', min: 'min',
 shopSub: 'Whether your shop PC is talking to the cloud. A till keeps selling when it loses contact.', lastContact: 'Last contact', lastCopy: 'Last copy of your data', plan: 'Online plan', renews: 'Runs until {0}',
 sActive: 'Active', sGrace: 'Ended recently, still updating', sExpired: 'Ended', sSuspended: 'Paused by your provider', sNone: 'Not started',
 planNote: 'If the plan ends the tills keep selling. This site stops updating after 7 days.', contactOk: 'In contact', contactLate: 'Quiet', contactOff: 'Offline',
 signTitle: 'Sign in', signSub: 'See your shop from your phone.', email: 'Email', pass: 'Password', show: 'Show', hide: 'Hide', signBtn: 'Sign in', signing: 'Signing in',
 errEmail: 'Enter your email.', errPass: 'Enter your password.', noOnline: 'No account yet? Ask your provider to set one up for you.',
 lpH: 'See the till without standing at it.', lpP: 'Sales, stock and the shop PC\'s status, copied from the till every few minutes.',
 lp1: 'What you sold today and how it compares with last week', lp2: 'What is running low', lp3: 'Whether the shop PC is connected',
 trust: 'Your shop\'s data is locked with a key that only you hold. We cannot read it, and neither can this server.',
 unlockTitle: 'Unlock your shop data', unlockSub: 'Type the recovery code printed from the shop PC (Settings, then Online). It stays in this browser and is never sent to us.',
 code: 'Recovery code', unlockBtn: 'Unlock', unlocking: 'Checking the code', errInvalid: 'That is not a recovery code. Check for a typo; it has 56 letters and numbers.',
 errWrong: 'That code does not open this shop\'s data. It may belong to another shop or an older key.',
 hint: 'Example format: ABCD-EFGH-IJKL-... (dashes and capitals are optional)',
 errNoShop: 'No shop PC is connected to this account yet.', errPlan: 'The online plan is not active. Contact your provider.', errNet: 'Could not reach the cloud. Check your internet and try again.',
 errRate: 'Too many attempts. Try again in a few minutes.', errGeneric: 'Something went wrong. Try again.', errCreds: 'The email or password is not right.', errMayNot: 'Your login is not allowed to see this.',
 cash: 'Cash', ago: '{0} ago', min1: '{0} min', hr1: '{0} h', day1: '{0} d', justNow: 'just now', ready: 'Data decrypted in this browser', retry: 'Try again'
},
ar: {
 today: 'اليوم', sales: 'المبيعات', stock: 'المخزون', shop: 'كمبيوتر المحل', signout: 'تسجيل الخروج', lock: 'قفل البيانات', dark: 'الوضع الداكن', online: 'أونلاين',
 greetM: 'صباح الخير', greetA: 'نهارك سعيد', greetE: 'مساء الخير',
 copyAt: 'آخر تحديث للنسخة {0}', never: 'لم يرسل كمبيوتر المحل أي شيء بعد.', refresh: 'تحديث', updating: 'جارٍ تحديث بيانات محلك...',
 salesToday: 'مبيعات اليوم', vsLast: '{0} مقارنة بـ{1} الماضي في الساعات نفسها', legToday: 'اليوم', legLast: '{0} الماضي', soFar: 'حتى الآن',
 payments: 'المدفوعات', receipts: 'الإيصالات', avgBasket: 'متوسط السلة', noSalesYet: 'لا مبيعات اليوم بعد.',
 needs: 'يحتاج انتباهك', recent: 'آخر الإيصالات', seeAll: 'كل المبيعات', allGood: 'لا شيء يحتاج انتباهك الآن.',
 nLow: '{0} أصناف تحتاج إعادة طلب', nLowD: '{0} تحت الحد الأدنى و{1} نفدت.', seeStock: 'المخزون',
 nDebt: 'الزبائن مدينون بـ {0}', nDebtD: '{0} زبائن عليهم رصيد غير مدفوع.', nDebtD1: 'زبون واحد عليه رصيد غير مدفوع.', findSale: 'ابحث عن إيصال',
 shopQuiet: 'كمبيوتر المحل صامت منذ {0}', shopQuietD: 'المبيعات منذ ذلك الوقت تظهر هنا عند عودة الاتصال. الصندوق يتابع البيع.', seeShop: 'كمبيوتر المحل',
 salesSub: 'إيصالات منسوخة من صناديقك. الأحدث أولاً.', all: 'الكل', cTime: 'الوقت', cId: 'الإيصال', cCust: 'الزبون', cItems: 'الأصناف', cPay: 'الدفع', cTot: 'المجموع',
 noMatch: 'لا نتائج.', rcpt: 'إيصال {0}', rMeta: '{0}، {1}', rTotal: 'المجموع', discount: 'الخصم', voided: 'ملغى', close: 'إغلاق', loadingItems: 'جارٍ تحميل الأصناف...', noItems: 'لم تُنسخ أصناف هذا الإيصال.',
 stockSub: 'الكميات حتى آخر نسخة. تتغير عندما يبيع صندوق.', find: 'ابحث عن صنف', low: 'منخفض', out: 'نفد', cName: 'الصنف', cQty: 'المتوفر', cMin: 'الحد الأدنى', cPrice: 'السعر',
 inStock: 'متوفر', lowS: 'منخفض', outS: 'نفد', min: 'أدنى',
 shopSub: 'هل يتحدث كمبيوتر المحل مع السحابة. الصندوق يتابع البيع إذا انقطع اتصاله.', lastContact: 'آخر اتصال', lastCopy: 'آخر نسخة من بياناتك', plan: 'خطة أونلاين', renews: 'تنتهي في {0}',
 sActive: 'فعّالة', sGrace: 'انتهت مؤخراً وما زالت تُحدَّث', sExpired: 'منتهية', sSuspended: 'موقوفة من المزوّد', sNone: 'لم تبدأ',
 planNote: 'إذا انتهت الخطة تتابع الصناديق البيع. يتوقف هذا الموقع عن التحديث بعد 7 أيام.', contactOk: 'متصل', contactLate: 'صامت', contactOff: 'غير متصل',
 signTitle: 'تسجيل الدخول', signSub: 'شاهد محلك من هاتفك.', email: 'البريد الإلكتروني', pass: 'كلمة المرور', show: 'إظهار', hide: 'إخفاء', signBtn: 'تسجيل الدخول', signing: 'جارٍ تسجيل الدخول',
 errEmail: 'أدخل بريدك الإلكتروني.', errPass: 'أدخل كلمة المرور.', noOnline: 'ليس لديك حساب بعد؟ اطلب من المزوّد إنشاء واحد لك.',
 lpH: 'شاهد الصندوق دون أن تقف عنده.', lpP: 'المبيعات والمخزون وحالة كمبيوتر المحل، منسوخة من الصندوق كل بضع دقائق.',
 lp1: 'ما بعته اليوم ومقارنته بالأسبوع الماضي', lp2: 'ما يوشك على النفاد', lp3: 'هل كمبيوتر المحل متصل',
 trust: 'بيانات محلك مقفلة بمفتاح لا يملكه غيرك. لا نستطيع قراءتها ولا هذا الخادم.',
 unlockTitle: 'افتح بيانات محلك', unlockSub: 'اكتب رمز الاسترداد المطبوع من كمبيوتر المحل (الإعدادات ثم أونلاين). يبقى في هذا المتصفح ولا يُرسل إلينا.',
 code: 'رمز الاسترداد', unlockBtn: 'فتح', unlocking: 'جارٍ فحص الرمز', errInvalid: 'هذا ليس رمز استرداد. تحقق من الأخطاء؛ يتكون من 56 حرفاً ورقماً.',
 errWrong: 'هذا الرمز لا يفتح بيانات هذا المحل. ربما يخص محلاً آخر أو مفتاحاً قديماً.',
 hint: 'الشكل: ABCD-EFGH-IJKL-... (الشرطات والأحرف الكبيرة اختيارية)',
 errNoShop: 'لا يوجد كمبيوتر محل متصل بهذا الحساب بعد.', errPlan: 'خطة أونلاين غير فعّالة. تواصل مع المزوّد.', errNet: 'تعذر الوصول إلى السحابة. تحقق من الإنترنت وحاول مجدداً.',
 errRate: 'محاولات كثيرة. حاول بعد بضع دقائق.', errGeneric: 'حدث خطأ. حاول مجدداً.', errCreds: 'البريد أو كلمة المرور غير صحيحة.', errMayNot: 'حسابك غير مسموح له برؤية هذا.',
 cash: 'نقداً', ago: 'قبل {0}', min1: '{0} د', hr1: '{0} س', day1: '{0} ي', justNow: 'الآن', ready: 'البيانات مفتوحة في هذا المتصفح', retry: 'حاول مجدداً'
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
function importKey(bytes) { return crypto.subtle.importKey('raw', bytes, { name: 'AES-GCM' }, false, ['decrypt']); }
function b64(s) { var bin = atob(s), a = new Uint8Array(bin.length); for (var i = 0; i < bin.length; i++) a[i] = bin.charCodeAt(i); return a; }
function decryptRow(k, table, id, payload) {
  if (payload.indexOf('e1.') !== 0) return Promise.reject(new Error('format'));
  var packed;
  try { packed = b64(payload.slice(3)); } catch (e) { return Promise.reject(new Error('format')); }
  if (packed.length < 28) return Promise.reject(new Error('format'));
  return crypto.subtle.decrypt({ name: 'AES-GCM', iv: packed.slice(0, 12), additionalData: new TextEncoder().encode(table + '\n' + id), tagLength: 128 }, k, packed.slice(12))
    .then(function (plain) { return JSON.parse(new TextDecoder().decode(plain)); }, function () { throw new Error('wrongkey'); });
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
  return t('errGeneric');
}

/* ---------- the shop's data in memory ---------- */
var KEEP = {
  Currencies: ['Name', 'Code', 'Notation', 'IsDefault', 'IsActive', 'IsDeleted'],
  PaymentMethods: ['Name', 'IsDefault', 'IsDeleted'],
  Products: ['Name', 'Barcode', 'StockQuantity', 'MinimumStock', 'SellingPrice', 'CurrencyId', 'Unit', 'IsActive', 'IsDeleted'],
  Customers: ['FirstName', 'LastName', 'PhoneNumber', 'Balance', 'IsActive', 'IsDeleted'],
  Receipts: ['ReceiptNumber', 'ReceiptDate', 'TotalAmount', 'DiscountAmount', 'CurrencyId', 'CustomerName', 'PaymentMethodId', 'IsVoid', 'VoidReason', 'IsDeleted'],
  ReceiptItems: ['ReceiptId', 'ProductName', 'Quantity', 'Unit', 'UnitPrice', 'LineTotal', 'IsDeleted']
};
var ORDER = ['Currencies', 'PaymentMethods', 'Products', 'Customers', 'Receipts'];
var cryptoKey = null, rows = {}, cursors = {}, denied = {}, asOf = null, me = null, busy = false, loadError = null, itemsLoaded = false;
function resetData() { rows = {}; cursors = {}; denied = {}; asOf = null; busy = false; loadError = null; itemsLoaded = false; Object.keys(KEEP).forEach(function (k) { rows[k] = {}; }); }
resetData();

function pullTable(table) {
  function page() {
    var q = '/rows?table=' + encodeURIComponent(table) + '&limit=1000' + (cursors[table] ? '&cursor=' + encodeURIComponent(cursors[table]) : '');
    return api(q).then(function (p) {
      if (p.asOfUtc) asOf = new Date(p.asOfUtc);
      return Promise.all((p.rows || []).map(function (r) {
        return decryptRow(cryptoKey, table, r.id, r.payload).then(function (plain) {
          var o = {}; KEEP[table].forEach(function (c) { o[c] = plain[c]; }); rows[table][r.id] = o;
        });
      })).then(function () { cursors[table] = p.nextCursor; if (p.hasMore) return page(); });
    });
  }
  return page().catch(function (e) { if (e instanceof ApiError && e.status === 403) { denied[table] = true; return; } throw e; });
}
function refresh() {
  if (!cryptoKey || busy) return Promise.resolve();
  busy = true; loadError = null; render();
  var chain = api('/me').then(function (m) { me = m; render(); });
  ORDER.forEach(function (table) { chain = chain.then(function () { return pullTable(table); }).then(function () { render(); }); });
  return chain.catch(function (e) {
    if (e && e.message === 'wrongkey') { lockData(); return; }
    loadError = e;
  }).then(function () { busy = false; render(); });
}
function loadItems() {
  if (itemsLoaded) return Promise.resolve();
  return pullTable('ReceiptItems').then(function () { itemsLoaded = true; });
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
  var list = live('Receipts').map(function (e) {
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
var state = { salesFilter: 'all', stockFilter: 'all', stockQ: '', salesQ: '' };
function route() {
  var h = (location.hash || '').slice(1);
  if (!session) return 'signin';
  if (!cryptoKey) return 'unlock';
  return ['today', 'sales', 'stock', 'shop'].indexOf(h) > -1 ? h : 'today';
}
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
    '<div class="fld"><label for="em">' + t('email') + '</label><input class="inp" id="em" type="email" inputmode="email" autocomplete="username" autocapitalize="off" spellcheck="false" aria-describedby="emerr"><div id="emerr" class="err" role="alert" hidden>' + ic('alert', 'sm') + t('errEmail') + '</div></div>' +
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
      .then(function (k) { if (k) { cryptoKey = k; refresh(); go('today'); } else go('unlock'); })
      .catch(function (err) { fe.innerHTML = ic('alert', 'sm') + esc(explain(err)); fe.hidden = false; b.disabled = false; b.textContent = t('signBtn'); });
  };
}

/* ---------- unlock ---------- */
function unlockView() {
  return '<div class="login"><section class="lf"><div class="lgrow">' + BRAND.logo + '<span class="chip info">' + t('online') + '</span></div>' +
    '<form id="uform" novalidate><h1>' + t('unlockTitle') + '</h1><p class="muted" style="margin-bottom:22px;max-width:46ch">' + t('unlockSub') + '</p>' +
    '<div class="fld"><label for="code">' + t('code') + '</label><input class="inp mono" id="code" type="text" autocomplete="off" autocapitalize="characters" spellcheck="false" dir="ltr" aria-describedby="cerr chint"><p class="help" id="chint">' + t('hint') + '</p><div id="cerr" class="err" role="alert" hidden></div></div>' +
    '<button class="btn pri" id="ubtn" type="submit">' + t('unlockBtn') + '</button></form>' +
    '<p class="foot"><button type="button" class="linkbtn" id="uout">' + t('signout') + '</button></p></section>' +
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
var NAV = [['today', 'today'], ['sales', 'sales'], ['stock', 'stock'], ['shop', 'tills']];
function shell(r, inner) {
  var rail = NAV.map(function (n) { return '<a class="nav" href="#' + n[0] + '"' + (r === n[0] ? ' aria-current="page"' : '') + '>' + ic(n[1]) + t(n[0]) + '</a>'; }).join('');
  var tabs = NAV.map(function (n) { return '<a href="#' + n[0] + '"' + (r === n[0] ? ' aria-current="page"' : '') + '>' + ic(n[1]) + t(n[0]) + '</a>'; }).join('');
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
  if (low + outN > 0) att += '<div class="ai"><span class="ico warn">' + ic('stock') + '</span><div class="tx"><b>' + t('nLow', low + outN) + '</b><p>' + t('nLowD', low, outN) + '</p></div><a href="#stock">' + t('seeStock') + '</a></div>';
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
function salesView() {
  var all = sales(), pays = []; all.forEach(function (s) { if (s.pay && pays.indexOf(s.pay) < 0) pays.push(s.pay); });
  var counts = { all: all.length }; all.forEach(function (s) { counts[s.pay] = (counts[s.pay] || 0) + 1; });
  var fc = ['all'].concat(pays).map(function (k) { return '<button type="button" class="fc" data-sf="' + esc(k) + '" aria-pressed="' + (state.salesFilter === k) + '">' + (k === 'all' ? t('all') : esc(k)) + ' <em>' + (counts[k] || 0) + '</em></button>'; }).join('');
  var q = state.salesQ.toLowerCase();
  var list = all.filter(function (s) { return (state.salesFilter === 'all' || s.pay === state.salesFilter) && (!q || (s.number + ' ' + s.customer).toLowerCase().indexOf(q) > -1); }).slice(0, 300);
  var body = list.length ? list.map(function (s) {
    return '<button type="button" class="srow" data-sale="' + s.id + '"><span class="c-main"><span class="c-time num">' + (s.when ? s.when.toLocaleString(loc(), { day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' }) : '') + '</span><span class="c-id">' + esc(s.number) + '</span></span>' +
      '<span class="c-till">' + esc(s.customer) + '</span><span class="c-pay">' + (s.isVoid ? chip('bad', 'alert', t('voided')) : s.pay ? '<span class="chip mute">' + esc(s.pay) + '</span>' : '') + '</span><span class="c-tot num">' + money(s.total, s.cur) + '</span></button>';
  }).join('') : '<p class="empty">' + t('noMatch') + '</p>';
  var inner = '<header class="head"><div><h1>' + t('sales') + '</h1><p>' + t('salesSub') + '</p></div>' + copyChip() + '</header>' +
    '<div class="tools"><div class="srch"><label class="sr" for="sq">' + t('findSale') + '</label>' + ic('search', 'sm') + '<input id="sq" type="search" placeholder="' + t('findSale') + '" value="' + esc(state.salesQ) + '" autocomplete="off"></div>' + fc + '</div>' +
    '<section class="panel tbl"><div class="th s5c"><span>' + t('cTime') + '</span><span>' + t('cId') + '</span><span>' + t('cCust') + '</span><span>' + t('cPay') + '</span><span class="r">' + t('cTot') + '</span></div>' + body + '</section>';
  return shell('sales', inner);
}

/* Stock */
function stockView() {
  var all = stockItems(), low = all.filter(function (i) { return i.level === 1; }).length, out = all.filter(function (i) { return i.level === 0; }).length;
  var fc = [['all', t('all'), all.length], ['low', t('low'), low], ['out', t('out'), out]].map(function (a) { return '<button type="button" class="fc" data-kf="' + a[0] + '" aria-pressed="' + (state.stockFilter === a[0]) + '">' + a[1] + ' <em>' + a[2] + '</em></button>'; }).join('');
  var q = state.stockQ.toLowerCase();
  var list = all.filter(function (it) { return (state.stockFilter === 'all' || (state.stockFilter === 'low' && it.level === 1) || (state.stockFilter === 'out' && it.level === 0)) && (!q || (it.name + ' ' + it.barcode).toLowerCase().indexOf(q) > -1); }).slice(0, 300);
  var rowsHtml = list.length ? list.map(function (it) {
    var st = it.level === 0 ? ['bad', 'alert', t('outS')] : it.level === 1 ? ['warn', 'alert', t('lowS')] : ['good', 'check', t('inStock')];
    var q2 = it.qty.toLocaleString('en-US', { maximumFractionDigits: 3 }), m2 = it.min == null ? '-' : it.min.toLocaleString('en-US', { maximumFractionDigits: 3 });
    return '<div class="krow"><div class="nm"><b>' + esc(it.name) + '</b><small class="mono">' + esc(it.barcode) + '</small><small class="m-only"> ' + q2 + ' / ' + t('min') + ' ' + m2 + ' &nbsp;' + money(it.price, it.cur) + '</small></div>' +
      '<span class="num r d-only">' + q2 + (it.unit ? ' ' + esc(it.unit) : '') + '</span><span class="num r d-only muted">' + m2 + '</span><span class="num r d-only">' + money(it.price, it.cur) + '</span><span class="d-only">' + chip(st[0], st[1], st[2]) + '</span>' +
      '<span class="m-only" style="justify-self:end">' + chip(st[0], st[1], st[2]) + '</span></div>';
  }).join('') : '<p class="empty">' + t('noMatch') + '</p>';
  var inner = '<header class="head"><div><h1>' + t('stock') + '</h1><p>' + t('stockSub') + '</p></div>' + copyChip() + '</header>' +
    '<div class="tools"><div class="srch"><label class="sr" for="kq">' + t('find') + '</label>' + ic('search', 'sm') + '<input id="kq" type="search" placeholder="' + t('find') + '" value="' + esc(state.stockQ) + '" autocomplete="off"></div>' + fc + '</div>' +
    '<section class="panel tbl"><div class="kth k4"><span>' + t('cName') + '</span><span class="r">' + t('cQty') + '</span><span class="r">' + t('cMin') + '</span><span class="r">' + t('cPrice') + '</span><span></span></div>' + rowsHtml + '</section>';
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
    '<aside class="panel s4" style="align-self:start"><div class="ph"><h2>' + t('plan') + '</h2>' + planChip + '</div>' + (m.expiresAtUtc ? '<p>' + t('renews', esc(new Date(m.expiresAtUtc).toLocaleDateString(loc(), { day: 'numeric', month: 'long', year: 'numeric' }))) + '</p>' : '') + '<p class="muted" style="font-size:14px;margin-top:10px">' + t('planNote') + '</p></aside></div>';
  return shell('shop', inner);
}

/* receipt dialog */
function openSale(id) {
  var s = sales().filter(function (x) { return x.id === id; })[0]; if (!s) return;
  var d = $('#dlg');
  function body(items) {
    var lines = items === null ? '<p class="muted">' + t('loadingItems') + '</p>' : items.length ? items.map(function (x) { return '<div class="ln"><div class="a">' + esc(x.name) + '<small class="num">' + x.qty + (x.unit ? ' ' + esc(x.unit) : '') + ' x ' + money(x.price, s.cur) + '</small></div><b class="num" style="font-weight:500">' + money(x.total, s.cur) + '</b></div>'; }).join('') : '<p class="muted">' + t('noItems') + '</p>';
    d.innerHTML = '<div class="dh"><div><h2 id="dlg-t" style="font-size:19px">' + t('rcpt', esc(s.number)) + '</h2><p class="muted" style="font-size:14px;margin-top:2px">' + t('rMeta', s.when ? esc(s.when.toLocaleString(loc(), { day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' })) : '', esc(s.customer || s.pay)) + '</p></div>' + (s.isVoid ? chip('bad', 'alert', t('voided')) : s.pay ? '<span class="chip mute">' + esc(s.pay) + '</span>' : '') + '</div>' +
      '<div class="db">' + lines + (s.discount > 0 ? '<div class="ln"><div class="a muted">' + t('discount') + '</div><b class="num" style="font-weight:500">-' + money(s.discount, s.cur) + '</b></div>' : '') + '<div class="tt"><span>' + t('rTotal') + '</span><span class="num">' + money(s.total, s.cur) + '</span></div>' + (s.isVoid && s.voidReason ? '<p class="help" style="margin-top:8px">' + esc(s.voidReason) + '</p>' : '') + '</div>' +
      '<div class="df"><button type="button" class="btn pri" data-close>' + t('close') + '</button></div>';
  }
  body(itemsLoaded ? itemsOf(id) : null);
  if (!d.open) d.showModal();
  if (!itemsLoaded) loadItems().then(function () { if (d.open) body(itemsOf(id)); }, function () { if (d.open) body([]); });
}
function itemsOf(receiptId) {
  var out = [];
  live('ReceiptItems').forEach(function (e) { var r = e[1]; if (toInt(r.ReceiptId) === receiptId) out.push({ name: text(r.ProductName), qty: toNum(r.Quantity), unit: text(r.Unit), price: toNum(r.UnitPrice), total: toNum(r.LineTotal) }); });
  return out;
}

/* ---------- render ---------- */
function render() {
  var r = route();
  document.documentElement.lang = lang; document.documentElement.dir = lang === 'ar' ? 'rtl' : 'ltr';
  $('#topbar').innerHTML = topBar();
  var lg = $('#login'), ap = $('#app');
  var isApp = ['today', 'sales', 'stock', 'shop'].indexOf(r) > -1;
  lg.hidden = isApp; ap.hidden = !isApp;
  if (r === 'signin') { lg.innerHTML = signinView(); bindSignin(); }
  else if (r === 'unlock') { lg.innerHTML = unlockView(); bindUnlock(); }
  else ap.innerHTML = ({ today: todayView, sales: salesView, stock: stockView, shop: shopView })[r]();
  bind();
}
function bind() {
  document.querySelectorAll('[data-lang]').forEach(function (b) { b.onclick = function () { lang = b.dataset.lang; try { localStorage.setItem('coduo-lang', lang); } catch (e) {} render(); }; });
  var tb = $('#themeBtn'); if (tb) tb.onclick = function () { var root = document.documentElement; root.dataset.theme = isDark() ? 'light' : 'dark'; render(); };
  ['mout', 'rout'].forEach(function (id) { var b = $('#' + id); if (b) b.onclick = function () { signOut(); }; });
  var lk = $('#rlock'); if (lk) lk.onclick = function () { lockData(); };
  var rf = $('#rfr'); if (rf) rf.onclick = function () { refresh(); };
  var rt = $('#retry'); if (rt) rt.onclick = function () { refresh(); };
  document.querySelectorAll('[data-sale]').forEach(function (b) { b.onclick = function () { openSale(+b.dataset.sale); }; });
  document.querySelectorAll('[data-sf]').forEach(function (b) { b.onclick = function () { state.salesFilter = b.dataset.sf; render(); }; });
  document.querySelectorAll('[data-kf]').forEach(function (b) { b.onclick = function () { state.stockFilter = b.dataset.kf; render(); }; });
  function wire(id, key) { var q = $('#' + id); if (q) q.oninput = function () { state[key] = q.value; var pos = q.selectionStart; render(); var n = $('#' + id); n.focus(); try { n.setSelectionRange(pos, pos); } catch (e) {} }; }
  wire('sq', 'salesQ'); wire('kq', 'stockQ');
}
$('#dlg').addEventListener('click', function (e) { if ((e.target.hasAttribute && e.target.hasAttribute('data-close')) || e.target === this) this.close(); });
window.addEventListener('hashchange', function () { var d = $('#dlg'); if (d.open) d.close(); render(); window.scrollTo(0, 0); });

/* ---------- start ---------- */
if (session) {
  keyGet().then(function (k) {
    if (k) { cryptoKey = k; refresh(); }
    render();
  });
} else render();
})();
