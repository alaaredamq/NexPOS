/* The public menu. The page arrives with its first items only; the rest load as their section nears the screen, a page at a time
   (GET /m/{slug}/items?section=&skip=). Search asks the server, so it finds items that are not loaded yet. Works without IntersectionObserver
   too: everything then loads straight away. */
(function () {
  var body = document.body;
  var base = body.dataset.items, preview = body.dataset.preview || '';
  var menu = document.getElementById('menu');
  if (!menu || !base) return;
  var PAGE = 40;

  function url(params) {
    var q = new URLSearchParams(params);
    if (preview) q.set('preview', preview);
    return base + '?' + q.toString();
  }

  /* ---- one item, built with textContent (never HTML from the server) ---- */
  function el(tag, cls, text) { var e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }
  function itemNode(i) {
    var li = el('li', 'item' + (i.soldOut ? ' out' : ''));
    var txt = el('div', 'txt');
    var h = el('h3', null, i.name); h.dir = 'auto'; txt.appendChild(h);
    if (i.description) { var p = el('p', null, i.description); p.dir = 'auto'; txt.appendChild(p); }
    var price = el('div', 'price');
    var s = el('span', null, i.priceText); s.dir = 'ltr'; price.appendChild(s);
    if (i.soldOut) price.appendChild(el('em', null, body.dataset.soldOut));
    txt.appendChild(price);
    li.appendChild(txt);
    if (i.imageUrl) {
      var img = el('img'); img.src = i.imageUrl; img.alt = ''; img.loading = 'lazy'; img.decoding = 'async'; img.width = 112; img.height = 112;
      li.appendChild(img);
    }
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
        slot.parentNode.insertBefore(frag, slot);
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
        slot.textContent = body.dataset.error;
        slot.removeAttribute('aria-hidden');
        slot.onclick = function () { slot.onclick = null; slot.textContent = ''; load(slot); };
      });
  }
  function near(slot) { var r = slot.getBoundingClientRect(); return r.top < innerHeight + 900 && r.bottom > -900; }

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
    if (!text) { showMenu(true); return; }
    timer = setTimeout(function () {
      var mine = ++asked;
      fetch(url({ q: text, take: 50 }), { headers: { Accept: 'application/json' } })
        .then(function (r) { return r.ok ? r.json() : { items: [] }; })
        .then(function (page) {
          if (mine !== asked) return; // an older answer arriving late
          resultsList.textContent = '';
          page.items.forEach(function (i) { resultsList.appendChild(itemNode(i)); });
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

  /* ---- highlight the chip of the section on screen ---- */
  if (!chips || !('IntersectionObserver' in window)) return;
  var links = {};
  [].forEach.call(chips.querySelectorAll('a'), function (a) { links[a.getAttribute('href').slice(1)] = a; });
  var current = null;
  function mark(id) {
    if (id === current) return; current = id;
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
