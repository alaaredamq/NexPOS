/* The public menu. The page arrives with its first items only; the rest load as their section nears the screen, a page at a time
   (GET /m/{slug}/items?section=&skip=). Search asks the server, so it finds items that are not loaded yet. Works without IntersectionObserver
   too: everything then loads straight away.
   Tapping a dish opens it in a sheet (bigger picture, the whole description, how many, add). The guest's order is kept in this browser only
   (12 hours), drawn as a ticket they can show to staff or send: there is no online ordering, so nothing is sent to the shop by us. */
(function () {
  var body = document.body, root = document.documentElement;
  var base = body.dataset.items, preview = body.dataset.preview || '';
  // The branch (?b=) and order type (?t=) this page is priced for; the feed and the guest's order follow them.
  var branch = body.dataset.b || '', type = body.dataset.t || '';
  var BRANCH_KEY = 'coduo-branch:' + base;
  var menu = document.getElementById('menu');

  // The branch list (several branches, none picked yet): mark the one this guest used last time. Nothing is chosen for them.
  var picks = document.querySelectorAll('[data-branch]');
  if (picks.length) {
    var last = null;
    try { last = localStorage.getItem(BRANCH_KEY); } catch (e) { }
    [].forEach.call(picks, function (a) { if (a.dataset.branch === last) { a.classList.add('last'); a.querySelector('small').hidden = false; } });
  }
  if (branch) try { localStorage.setItem(BRANCH_KEY, branch); } catch (e) { }

  if (!menu || !base) return;
  var PAGE = 40;
  var W = {};
  try { W = JSON.parse(document.getElementById('words').textContent) || {}; } catch (e) { }
  var reduced = window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches;

  function url(params) {
    var q = new URLSearchParams(params);
    if (preview) q.set('preview', preview);
    if (branch) q.set('b', branch);
    if (type) q.set('t', type);
    return base + '?' + q.toString();
  }

  /* ---- money: the same text as the server's MenuRules.Price ("$12.50", "$8", "450,000 LBP"), from whole cents ---- */
  function money(cents, cur) {
    var whole = cents % 100 === 0, d = whole ? 0 : 2;
    var s = (cents / 100).toLocaleString('en-US', { minimumFractionDigits: d, maximumFractionDigits: d });
    cur = (cur || '').trim();
    if (!cur) return s;
    return cur === '$' || cur === '€' || cur === '£' ? cur + s : s + ' ' + cur;
  }
  function centsOf(text) { var n = parseFloat(text); return isFinite(n) ? Math.round(n * 100) : 0; }

  /* ---- one item, built with textContent (never HTML from the server) ---- */
  function el(tag, cls, text) { var e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }
  function svg(d) {
    var ns = 'http://www.w3.org/2000/svg', s = document.createElementNS(ns, 'svg'), p = document.createElementNS(ns, 'path');
    s.setAttribute('viewBox', '0 0 24 24'); s.setAttribute('aria-hidden', 'true'); p.setAttribute('d', d); s.appendChild(p); return s;
  }
  var PLUS = 'M12 5v14M5 12h14', MINUS = 'M5 12h14';
  function itemNode(i) {
    var li = el('li', 'item' + (i.soldOut ? ' out' : ''));
    li.dataset.id = i.id; li.dataset.price = i.price; li.dataset.cur = i.currency || '';
    // Same markup as the server renders: picture (or a letter where there is none) first, then the words. menu.css lays it out per style.
    if (i.imageUrl) {
      var img = el('img', 'pic'); img.src = i.imageUrl; img.alt = ''; img.loading = 'lazy'; img.decoding = 'async'; img.width = 300; img.height = 300;
      li.appendChild(img);
    } else {
      var none = el('span', 'pic blank', (i.name || '').trim().charAt(0).toUpperCase()); none.setAttribute('aria-hidden', 'true');
      li.appendChild(none);
    }
    var txt = el('div', 'txt');
    var h = el('h3'); h.dir = 'auto';
    var open = el('button', 'open', i.name); open.type = 'button'; h.appendChild(open); txt.appendChild(h);
    if (i.description) { var p = el('p', null, i.description); p.dir = 'auto'; txt.appendChild(p); }
    var price = el('div', 'price');
    var s = el('span', null, i.priceText); s.dir = 'ltr'; price.appendChild(s);
    if (i.soldOut) price.appendChild(el('em', null, W.soldOut));
    else {
      var add = el('button', 'add'); add.type = 'button'; add.setAttribute('aria-label', W.addName + ' ' + i.name);
      add.appendChild(svg(PLUS)); add.appendChild(el('b'));
      price.appendChild(add);
    }
    txt.appendChild(price);
    li.appendChild(txt);
    return li;
  }

  /* ---- loading the rest of a section ---- */
  var busy = new WeakSet();
  function load(slot) {
    if (busy.has(slot)) return;
    busy.add(slot);
    slot.classList.remove('err');
    var skip = +slot.dataset.skip;
    fetch(url({ section: slot.dataset.section, skip: skip, take: PAGE }), { headers: { Accept: 'application/json' } })
      .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
      .then(function (page) {
        var frag = document.createDocumentFragment();
        page.items.forEach(function (i) { frag.appendChild(itemNode(i)); });
        var list = slot.parentNode;
        list.insertBefore(frag, slot);
        badges(list);
        if (page.hasMore) {
          slot.dataset.skip = skip + page.items.length;
          slot.style.setProperty('--n', Math.max(1, +getComputedStyle(slot).getPropertyValue('--n') - page.items.length));
          busy.delete(slot);
          if (near(slot)) load(slot);
        } else {
          if (observer) observer.unobserve(slot);
          slot.remove();
        }
      })
      .catch(function () {
        busy.delete(slot);
        slot.classList.add('err');
        slot.textContent = W.error;
        slot.removeAttribute('aria-hidden');
        slot.onclick = function () { slot.onclick = null; slot.textContent = ''; load(slot); };
      });
  }
  // Near in both directions: in the swipe-rails style a section scrolls sideways, and must not load whole the moment it is in view.
  function near(slot) {
    var r = slot.getBoundingClientRect();
    return r.top < innerHeight + 900 && r.bottom > -900 && r.left < innerWidth + 400 && r.right > -400;
  }

  var slots = [].slice.call(document.querySelectorAll('[data-more]'));
  var observer = 'IntersectionObserver' in window
    ? new IntersectionObserver(function (entries) { entries.forEach(function (e) { if (e.isIntersecting) load(e.target); }); }, { rootMargin: '900px 0px' })
    : null;
  slots.forEach(function (s) { observer ? observer.observe(s) : load(s); });

  /* ---- search, on the server ---- */
  var q = document.getElementById('q'), results = document.getElementById('results');
  var resultsList = results && results.querySelector('ul'), none = document.getElementById('none');
  var chips = document.getElementById('chips');
  var secs = [].slice.call(menu.querySelectorAll('.sec')).filter(function (s) { return s !== results; });
  var timer = 0, asked = 0;
  if (q && results) q.addEventListener('input', function () {
    clearTimeout(timer);
    var text = q.value.trim();
    if (!text) { asked++; showMenu(true); return; }
    timer = setTimeout(function () {
      var mine = ++asked;
      fetch(url({ q: text, take: 50 }), { headers: { Accept: 'application/json' } })
        .then(function (r) { return r.ok ? r.json() : { items: [] }; })
        .then(function (page) {
          if (mine !== asked) return; // an older answer arriving late
          resultsList.textContent = '';
          page.items.forEach(function (i) { resultsList.appendChild(itemNode(i)); });
          badges(resultsList);
          none.hidden = page.items.length > 0;
          showMenu(false);
        })
        .catch(function () {});
    }, 250);
  });
  function showMenu(on) {
    secs.forEach(function (s) { s.hidden = !on; });
    results.hidden = on;
    if (chips) chips.hidden = !on;
  }

  /* ---- the guest's order, in this browser ---- */
  // One order per branch and order type: prices differ between them, so an order is never silently repriced.
  var KEY = 'coduo-order:' + base + '|' + branch + '|' + type, TTL = 12 * 3600 * 1000;
  var order = (function () {
    try {
      var o = JSON.parse(localStorage.getItem(KEY));
      if (o && Array.isArray(o.lines) && o.at > Date.now() - TTL) return o;
    } catch (e) { }
    return { lines: [], at: Date.now() };
  })();
  function save() {
    order.at = Date.now();
    try { order.lines.length ? localStorage.setItem(KEY, JSON.stringify(order)) : localStorage.removeItem(KEY); } catch (e) { }
  }
  function lineOf(id) { for (var k = 0; k < order.lines.length; k++) if (order.lines[k].id === id) return order.lines[k]; return null; }
  function setQty(d, n) {
    var l = lineOf(d.id);
    n = Math.max(0, Math.min(99, n));
    if (!n) { if (l) order.lines.splice(order.lines.indexOf(l), 1); }
    else if (l) { l.n = n; l.name = d.name; l.cents = d.cents; l.cur = d.cur; }
    else order.lines.push({ id: d.id, name: d.name, cents: d.cents, cur: d.cur, n: n });
    save();
    paint();
  }
  function count() { return order.lines.reduce(function (a, l) { return a + l.n; }, 0); }
  // One total per currency: a shop that prices some things in $ and some in LBP gets "$12 + 450,000 LBP", never a wrong sum.
  function total(lines) {
    var by = {}, keys = [];
    lines.forEach(function (l) { if (!(l.cur in by)) { by[l.cur] = 0; keys.push(l.cur); } by[l.cur] += l.cents * l.n; });
    return keys.map(function (c) { return money(by[c], c); }).join(' + ');
  }

  // What the page shows of a dish, read from its item.
  function dishOf(li) {
    var img = li.querySelector('img.pic'), blank = li.querySelector('.pic.blank'), p = li.querySelector('.txt p');
    return {
      id: +li.dataset.id, name: li.querySelector('h3').textContent.trim(), desc: p ? p.textContent : '',
      img: img ? img.currentSrc || img.src : null, letter: blank ? blank.textContent : '',
      priceText: li.querySelector('.price span').textContent, cents: centsOf(li.dataset.price), cur: li.dataset.cur || '',
      out: li.classList.contains('out')
    };
  }

  /* ---- the order bar and the counts on each add button ---- */
  var bag = document.getElementById('bag'), bagN = document.getElementById('bag-n'), bagT = document.getElementById('bag-t');
  var say = document.getElementById('say');
  function badges(scope) {
    [].forEach.call((scope || menu).querySelectorAll('.item[data-id]'), function (li) {
      var add = li.querySelector('.add'); if (!add) return;
      var l = lineOf(+li.dataset.id);
      if (l) { add.dataset.n = l.n; add.querySelector('b').textContent = l.n; }
      else { delete add.dataset.n; add.querySelector('b').textContent = ''; }
    });
  }
  function paint() {
    var n = count();
    bag.classList.toggle('on', n > 0);
    root.classList.toggle('ordering', n > 0);
    bag.setAttribute('aria-label', (W.view || '') + ', ' + n + ', ' + total(order.lines));
    if (bagN.textContent !== String(n)) { bagN.textContent = n; pop(bagN); }
    bagT.textContent = total(order.lines);
    badges();
    if (orderSheet.open) drawOrder();
  }
  function pop(e) { if (reduced) return; e.classList.remove('pop'); void e.offsetWidth; e.classList.add('pop'); }

  /* ---- sheets: a real <dialog> (focus stays inside, Esc closes); Back on the phone closes it too ---- */
  var current = null, lastFocus = null;
  function show(d) {
    if (current) shut(current, true);
    lastFocus = document.activeElement;
    d.classList.remove('closing');
    d.style.removeProperty('--dy'); d.style.transform = '';
    d.showModal();
    d.querySelector('.sheet-scroll').scrollTop = 0;
    current = d;
    try { history.pushState({ menuSheet: d.id }, ''); } catch (e) { }
  }
  function close(d) {
    if (history.state && history.state.menuSheet === d.id) history.back(); // popstate below does the closing
    else shut(d);
  }
  function shut(d, now) {
    if (!d.open || d.classList.contains('closing')) return;
    if (current === d) current = null;
    var done = function () {
      d.classList.remove('closing'); d.close(); d.style.removeProperty('--dy'); d.style.transform = '';
      if (lastFocus && document.contains(lastFocus)) lastFocus.focus({ preventScroll: true });
    };
    if (now || reduced) return done();
    d.classList.add('closing');
    var t = setTimeout(done, 300);
    d.addEventListener('animationend', function end(e) { if (e.target !== d) return; d.removeEventListener('animationend', end); clearTimeout(t); done(); });
  }
  addEventListener('popstate', function () { if (current) shut(current); });
  if (history.state && history.state.menuSheet) try { history.replaceState(null, ''); } catch (e) { }

  function sheet(d) {
    d.addEventListener('cancel', function (e) { e.preventDefault(); close(d); });
    d.addEventListener('click', function (e) {
      if (e.target === d) { close(d); return; } // the dimmed backdrop
      if (e.target.closest('[data-close]')) close(d);
    });
    // Pull down to close, on a phone: from the picture / the ticket's head.
    var zone = d.querySelector('[data-drag]'), y0 = null, dy = 0;
    zone.addEventListener('pointerdown', function (e) {
      if (e.pointerType === 'mouse' || matchMedia('(min-width: 700px)').matches) return;
      y0 = e.clientY; dy = 0; d.classList.add('dragging');
      try { zone.setPointerCapture(e.pointerId); } catch (x) { }
    });
    zone.addEventListener('pointermove', function (e) {
      if (y0 == null) return;
      dy = Math.max(0, e.clientY - y0);
      d.style.transform = 'translateY(' + dy + 'px)';
    });
    function up() {
      if (y0 == null) return;
      y0 = null; d.classList.remove('dragging');
      if (dy > 110) { d.style.setProperty('--dy', dy + 'px'); d.style.transform = ''; close(d); }
      else d.style.transform = '';
    }
    zone.addEventListener('pointerup', up);
    zone.addEventListener('pointercancel', up);
  }

  /* ---- one dish ---- */
  var dishSheet = document.getElementById('dish'), dish = null, qty = 1;
  var $ = function (id) { return document.getElementById(id); };
  sheet(dishSheet);
  function openDish(li) {
    dish = dishOf(li);
    var l = lineOf(dish.id);
    if (l) { l.name = dish.name; l.cents = dish.cents; l.cur = dish.cur; }
    qty = l ? l.n : 1;
    var pic = $('dish-pic'); pic.textContent = '';
    if (dish.img) { var img = el('img'); img.src = dish.img; img.alt = ''; pic.appendChild(img); }
    else pic.appendChild(el('div', 'blank', dish.letter));
    $('dish-name').textContent = dish.name;
    $('dish-desc').textContent = dish.desc;
    $('dish-price').textContent = dish.priceText;
    $('dish-out').hidden = !dish.out;
    dishSheet.classList.toggle('out', dish.out);
    drawDish();
    show(dishSheet);
  }
  function drawDish() {
    var inOrder = !!lineOf(dish.id), go = $('dish-go');
    $('dish-step').hidden = dish.out;
    $('dish-act').classList.toggle('solo', dish.out);
    $('dish-q').textContent = qty;
    dishSheet.querySelector('[data-q="-1"]').disabled = qty <= (inOrder ? 0 : 1);
    dishSheet.querySelector('[data-q="1"]').disabled = qty >= 99;
    go.disabled = dish.out;
    go.querySelector('.go-l').textContent = dish.out ? W.soldOut : !inOrder ? W.add : qty === 0 ? W.remove : W.update;
    go.querySelector('.go-p').textContent = dish.out || qty === 0 ? '' : money(dish.cents * qty, dish.cur);
  }
  dishSheet.addEventListener('click', function (e) {
    var b = e.target.closest('[data-q]');
    if (b && !b.disabled) { qty += +b.dataset.q; drawDish(); }
  });
  $('dish-go').addEventListener('click', function () {
    if (!dish || dish.out) return;
    var was = lineOf(dish.id);
    setQty(dish, qty);
    if (qty && !was) say.textContent = W.added + ': ' + dish.name;
    close(dishSheet);
  });

  /* ---- taps on the menu: the add button adds one, anywhere else on a dish opens it ---- */
  menu.addEventListener('click', function (e) {
    var add = e.target.closest('.add');
    if (add) {
      var d = dishOf(add.closest('.item')), l = lineOf(d.id);
      setQty(d, (l ? l.n : 0) + 1);
      pop(add);
      say.textContent = W.added + ': ' + d.name + ' (' + lineOf(d.id).n + ')';
      return;
    }
    var open = e.target.closest('.open');
    if (open) openDish(open.closest('.item'));
  });

  /* ---- the order, as a ticket ---- */
  var orderSheet = document.getElementById('order');
  sheet(orderSheet);
  bag.addEventListener('click', function () { orderSheet.classList.remove('staff'); drawOrder(); show(orderSheet); });
  function drawOrder() {
    var list = $('order-lines'), empty = !order.lines.length;
    list.textContent = '';
    order.lines.forEach(function (l) {
      var li = el('li', 'ln'); li.dataset.id = l.id;
      var name = el('div');
      var b = el('b', null, l.name); b.dir = 'auto'; name.appendChild(b);
      var unit = el('small', null, money(l.cents, l.cur)); unit.dir = 'ltr'; name.appendChild(unit);
      var q = el('span', 'q', l.n + '×'); q.dir = 'ltr';
      var step = el('div', 'step sm');
      var minus = el('button'); minus.type = 'button'; minus.dataset.d = '-1'; minus.setAttribute('aria-label', '−1 ' + l.name); minus.appendChild(svg(MINUS));
      var out = el('output', null, l.n);
      var plus = el('button'); plus.type = 'button'; plus.dataset.d = '1'; plus.setAttribute('aria-label', '+1 ' + l.name); plus.appendChild(svg(PLUS));
      plus.disabled = l.n >= 99;
      step.appendChild(minus); step.appendChild(out); step.appendChild(plus);
      var sum = el('span', 'sum', money(l.cents * l.n, l.cur)); sum.dir = 'ltr';
      li.appendChild(q); li.appendChild(name); li.appendChild(step); li.appendChild(sum);
      list.appendChild(li);
    });
    var n = count();
    var at = new Date(order.at);
    $('order-meta').textContent = (n === 1 ? W.one : (W.many || '').replace('{n}', n)) + (body.dataset.typeName ? ' · ' + body.dataset.typeName : '') + ' · ' + at.toLocaleTimeString(document.documentElement.lang, { hour: '2-digit', minute: '2-digit' });
    $('order-sum').textContent = total(order.lines);
    $('order-total').hidden = $('order-hint').hidden = $('order-act').hidden = list.hidden = empty;
    $('order-empty').hidden = !empty;
    if (empty) orderSheet.classList.remove('staff');
  }
  orderSheet.addEventListener('click', function (e) {
    var step = e.target.closest('.ln [data-d]');
    if (step) {
      var l = lineOf(+step.closest('.ln').dataset.id);
      if (l) {
        setQty(l, l.n + +step.dataset.d);
        // Keep focus on the line's stepper after it is redrawn, or where the line was.
        var again = orderSheet.querySelector('.ln[data-id="' + l.id + '"] [data-d="' + step.dataset.d + '"]');
        (again || orderSheet.querySelector('.ln [data-d]') || orderSheet.querySelector('[data-close]')).focus({ preventScroll: true });
      }
      return;
    }
    if (e.target.closest('[data-staff]')) { orderSheet.classList.add('staff'); orderSheet.querySelector('.sheet-scroll').scrollTop = 0; orderSheet.querySelector('[data-unstaff]').focus(); return; }
    if (e.target.closest('[data-unstaff]')) { orderSheet.classList.remove('staff'); orderSheet.querySelector('[data-staff]').focus(); return; }
    if (e.target.closest('[data-share]')) { share(e.target.closest('[data-share]')); return; }
    var clear = e.target.closest('[data-clear]');
    if (clear) {
      // Two taps, so a stray one does not lose the order.
      if (!clear.classList.contains('sure')) {
        clear.classList.add('sure'); clear.textContent = W.clearSure;
        setTimeout(function () { clear.classList.remove('sure'); clear.textContent = W.clear; }, 3000);
        return;
      }
      clear.classList.remove('sure'); clear.textContent = W.clear;
      order.lines = []; save(); paint();
    }
  });
  function share(btn) {
    var shop = document.querySelector('.top h1').textContent.trim();
    var where = [body.dataset.branchName, body.dataset.typeName].filter(Boolean).join(' · ');
    var link = location.origin + location.pathname + (branch ? '?b=' + encodeURIComponent(branch) : '');
    var text = shop + ' · ' + (W.order || '') + (where ? '\n' + where : '') + '\n\n' +
      order.lines.map(function (l) { return l.n + ' × ' + l.name + ' — ' + money(l.cents * l.n, l.cur); }).join('\n') +
      '\n\n' + (W.total || '') + ': ' + total(order.lines) + '\n' + link;
    var label = btn.querySelector('span'), was = label.textContent;
    function copied() { label.textContent = W.copied; setTimeout(function () { label.textContent = was; }, 2000); }
    if (navigator.share) { navigator.share({ title: shop, text: text }).catch(function () { }); return; }
    if (navigator.clipboard) navigator.clipboard.writeText(text).then(copied, function () { });
  }

  paint();

  /* ---- highlight the chip of the section on screen ---- */
  if (!chips || !('IntersectionObserver' in window)) return;
  var links = {};
  [].forEach.call(chips.querySelectorAll('a'), function (a) { links[a.getAttribute('href').slice(1)] = a; });
  var marked = null;
  function mark(id) {
    if (id === marked) return; marked = id;
    for (var k in links) links[k].removeAttribute('aria-current');
    var a = links[id]; if (!a) return;
    a.setAttribute('aria-current', 'true');
    chips.scrollTo({ left: a.offsetLeft - chips.clientWidth / 2 + a.clientWidth / 2, behavior: 'smooth' });
  }
  var spy = new IntersectionObserver(function (entries) {
    entries.forEach(function (e) { if (e.isIntersecting) mark(e.target.id); });
  }, { rootMargin: '-140px 0px -60% 0px' });
  secs.forEach(function (s) { spy.observe(s); });
})();
