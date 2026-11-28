// Coduo POS landing page: the receipt that prints as you scroll, the live logo, the 3D modules beside the paper,
// the little things you can try (rate, PIN, coupons) and the one-field contact. The printer itself is printer3d.js.
(() => {
  'use strict';

  // EDIT HERE: how people reach you. Leave a value empty to hide it.
  // whatsapp = international number, digits only (e.g. 9613123456). phone as you want it dialled. email = address.
  // instagram = the handle without @; used to send the message when no WhatsApp or email is set.
  const CONTACT = { whatsapp: '', phone: '', email: '', instagram: 'coduo.am' };

  const root = document.documentElement;
  root.classList.add('js');
  const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const wait = ms => new Promise(r => setTimeout(r, ms));
  const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
  const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const cssPx = name => parseFloat(getComputedStyle(root).getPropertyValue(name)) || 0;
  const two = n => String(n).padStart(2, '0');

  $$('[data-year]').forEach(n => { n.textContent = new Date().getFullYear(); });
  const d = new Date();
  $$('[data-today]').forEach(n => { n.textContent = `${two(d.getDate())}/${two(d.getMonth() + 1)}/${d.getFullYear()} ${two(d.getHours())}:${two(d.getMinutes())}`; });

  /* ---------------- header ---------------- */
  const top = $('[data-top]');
  let lastY = scrollY;
  const onScrollTop = () => {
    top.classList.toggle('scrolled', scrollY > 30);
    if (scrollY > 60) root.classList.add('scrolled-once');
  };
  onScrollTop();

  /* ---------------- the first bit of paper feeds out by itself ---------------- */
  const hero = $('.hero'), heroIn = $('.hero-in');
  const feedFirst = () => {
    const room = innerHeight - cssPx('--slot') - heroIn.getBoundingClientRect().bottom - 28 - scrollY;
    const want = innerWidth < 640 ? 230 : 170;
    root.style.setProperty('--fed', `${Math.round(clamp(room, 0, want))}px`);
  };
  if (reduce) feedFirst();
  else (document.fonts ? document.fonts.ready : Promise.resolve()).then(() => setTimeout(feedFirst, 450));
  addEventListener('resize', () => { if (!reduce) hero.style.transition = 'none'; feedFirst(); requestAnimationFrame(() => { hero.style.transition = ''; }); }, { passive: true });

  /* ---------------- printing: a line is blank until it clears the printer ---------------- */
  const feedSel = ['.r-head > *', '.r > .rev', '.r > .mute', '.r > .rule', '.r > .kv', '.item > *', '.steps > li', '.pl > *',
    '.r > .cut-l', '.coupons > *', '.r > p', '.qr-row', '.qa > details', '.r-end > *'];
  const feedables = [...new Set(feedSel.flatMap(s => $$(s)))];
  feedables.forEach(el => el.classList.add('feedable'));
  let feedIO = null;
  const watchFeed = () => {
    if (feedIO) feedIO.disconnect();
    if (reduce || !('IntersectionObserver' in window)) { feedables.forEach(el => el.classList.add('inked')); return; }
    feedIO = new IntersectionObserver(es => es.forEach(e => {
      if (e.isIntersecting) { e.target.classList.add('inked'); feedIO.unobserve(e.target); }
    }), { rootMargin: `0px 0px -${Math.round(cssPx('--slot') + 4)}px 0px` });
    feedables.forEach(el => { if (!el.classList.contains('inked')) feedIO.observe(el); });
  };
  watchFeed();
  // jumping straight to a section prints everything above it at once
  addEventListener('hashchange', () => setTimeout(() => {
    const lim = innerHeight - cssPx('--slot');
    feedables.forEach(el => { if (el.getBoundingClientRect().top < lim) el.classList.add('inked'); });
  }, 700));

  /* ---------------- when the end of the receipt passes the slot, the printer cuts and steps away ---------------- */
  const roll = $('[data-roll]');
  const checkCut = () => {
    const end = roll.getBoundingClientRect().bottom - 40;
    root.classList.toggle('cut', end < innerHeight - cssPx('--slot') - 30);
  };

  /* ---------------- the modules beside the paper turn as they pass ---------------- */
  const mods = $$('[data-m3d]');
  const wide = matchMedia('(min-width: 1181px)');
  if ('IntersectionObserver' in window && !reduce) {
    const io = new IntersectionObserver(es => es.forEach(e => { if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); } }), { rootMargin: '0px 0px -12% 0px' });
    mods.forEach(m => io.observe(m));
  } else mods.forEach(m => m.classList.add('in'));
  let px = 0, py = 0; // pointer, -1..1
  const turn = () => {
    if (!wide.matches || reduce) return;
    const vh = innerHeight;
    for (const m of mods) {
      const r = m.getBoundingClientRect();
      if (r.bottom < -200 || r.top > vh + 200) continue;
      const t = clamp(((r.top + r.height / 2) - vh * .42) / (vh * .62), -1, 1);
      const card = m.firstElementChild;
      if (m.hasAttribute('data-drawer')) continue;
      card.style.setProperty('--ry', `${(7 + Math.abs(t) * 20 + px * 5 * (m.classList.contains('left') ? 1 : -1)).toFixed(2)}deg`);
      card.style.setProperty('--rx', `${(5 - t * 9 - py * 4).toFixed(2)}deg`);
      card.style.setProperty('--ty', `${(t * 26).toFixed(1)}px`);
    }
  };

  /* ---------------- the setup window fills as you scroll through the steps ---------------- */
  const setup = $('[data-setup]');
  const suBar = $('[data-su-bar]'), suPct = $('[data-su-pct]'), suWhat = $('[data-su-what]'), suStep = $('[data-su-step]');
  const SU = [[0, 'Installing on this PC', 'Copying files'], [.35, 'Activating', 'Checking the key on this PC'], [.62, 'Importing products', 'products.xlsx, 1,248 rows'], [.9, 'Ready to sell', 'Printer found on USB001']];
  const setupProgress = () => {
    if (!setup) return;
    const r = setup.closest('.rrow').getBoundingClientRect();
    const p = clamp((innerHeight * .85 - r.top) / (r.height + innerHeight * .25), 0, 1);
    suBar.style.width = `${Math.round(p * 100)}%`;
    suPct.textContent = `${Math.round(p * 100)}%`;
    const s = SU.filter(x => p >= x[0]).pop();
    suStep.textContent = s[1]; suWhat.textContent = p >= 1 ? 'Done' : s[2];
  };

  /* ---------------- one scroll handler for all of it ---------------- */
  let ticking = false;
  const frame = () => {
    ticking = false;
    onScrollTop(); checkCut(); turn(); setupProgress();
    const dy = scrollY - lastY; lastY = scrollY;
    if (dy > 2) brandLook(0, 1, 650);  // the logo glances down at the printer while the paper feeds
  };
  addEventListener('scroll', () => { if (!ticking) { ticking = true; requestAnimationFrame(frame); } }, { passive: true });
  addEventListener('resize', () => { watchFeed(); frame(); }, { passive: true });

  /* ---------------- the live logo (top right): eyes follow the pointer, blink, glance, close while you type ---------------- */
  const brand = $('[data-brand]');
  const look = $('[data-look]', brand), face = $('[data-face]', brand);
  let ex = 0, ey = 0, tx = 0, ty = 0, easing = false, glanceUntil = 0, gx = 0, gy = 0;
  const pointer = { x: -1e4, y: -1e4 };
  const ease = () => {
    ex += (tx - ex) * .14; ey += (ty - ey) * .14;
    look.style.transform = `translate(${(ex * 9).toFixed(2)}px, ${(ey * 8).toFixed(2)}px)`;
    face.style.transform = `translate(${(ex * 2).toFixed(2)}px, ${(ey * 1.2).toFixed(2)}px) rotate(${(ex * 6).toFixed(2)}deg)`;
    easing = Math.abs(tx - ex) > .004 || Math.abs(ty - ey) > .004;
    if (easing) requestAnimationFrame(ease);
  };
  const aim = () => {
    if (brand.classList.contains('shut')) { tx = ty = 0; }
    else if (performance.now() < glanceUntil) { tx = gx; ty = gy; }
    else if (pointer.x < -1e3) { tx = ty = 0; }
    else {
      const r = brand.querySelector('.mark').getBoundingClientRect();
      const dx = pointer.x - (r.left + r.width / 2), dy = pointer.y - (r.top + r.height / 2);
      const dist = Math.hypot(dx, dy) || 1, k = Math.min(1, dist / 200);
      tx = dx / dist * k; ty = dy / dist * k;
    }
    if (!easing && !reduce) { easing = true; requestAnimationFrame(ease); }
  };
  let glanceT = 0;
  function brandLook(x, y, ms) {
    gx = x; gy = y; glanceUntil = performance.now() + ms; aim();
    clearTimeout(glanceT); glanceT = setTimeout(aim, ms + 20);
  }
  const mood = (cls, ms) => { brand.classList.remove(cls); void brand.offsetWidth; brand.classList.add(cls); setTimeout(() => brand.classList.remove(cls), ms); };
  if (!reduce) {
    let q = false;
    addEventListener('pointermove', e => { pointer.x = e.clientX; pointer.y = e.clientY; if (!q) { q = true; requestAnimationFrame(() => { q = false; aim(); }); } }, { passive: true });
    root.addEventListener('pointerleave', () => { pointer.x = pointer.y = -1e4; aim(); });
    brand.addEventListener('pointerenter', () => mood('wink', 520));
  }

  /* ---------------- try the rate ---------------- */
  const rate = $('[data-rate]');
  if (rate) {
    const lbpOut = $('[data-fx-lbp]'), chOut = $('[data-fx-ch]');
    const fmt = n => (Math.round(n / 1000) * 1000).toLocaleString('en-US');
    const upd = () => {
      const R = clamp(+rate.value || 0, 1000, 500000);
      const total = 17.95, paid = 10 + 800000 / R;
      lbpOut.textContent = fmt(total * R);
      const ch = paid - total;
      chOut.textContent = ch >= 0 ? `LBP ${fmt(ch * R)}` : `Short LBP ${fmt(-ch * R)}`;
      chOut.style.color = ch >= 0 ? '' : '#B4372B';
    };
    rate.addEventListener('input', upd); upd();
  }

  /* ---------------- the cash drawer opens on a cash sale, then shuts ---------------- */
  const drawer = $('[data-drawer] .drawer3d');
  if (drawer && !reduce && 'IntersectionObserver' in window) {
    let on = false, timer = 0;
    const cycle = async () => {
      if (!on) return;
      drawer.style.setProperty('--open', '1'); await wait(2600);
      drawer.style.setProperty('--open', '0'); timer = setTimeout(cycle, 2600);
    };
    new IntersectionObserver(([e]) => { on = e.isIntersecting; clearTimeout(timer); if (on) timer = setTimeout(cycle, 500); else drawer.style.setProperty('--open', '0'); }, { threshold: .4 }).observe(drawer);
  } else if (drawer) drawer.style.setProperty('--open', '.7');

  /* ---------------- offline: sales keep landing on the PC ---------------- */
  const toasts = $('[data-toasts]');
  if (toasts && !reduce && 'IntersectionObserver' in window) {
    let n = 1042, on = false, timer = 0;
    const amounts = [4.65, 9.30, 22.10, 3.40, 15.75, 7.20, 31.05];
    const add = () => {
      if (!on || document.hidden) { timer = setTimeout(add, 1500); return; }
      const li = document.createElement('li');
      li.innerHTML = `<svg class="ic" aria-hidden="true"><use href="#i-check"/></svg><span><b>Sale #${++n} &middot; $${amounts[n % amounts.length].toFixed(2)}</b>Saved on this PC</span>`;
      toasts.append(li);
      while (toasts.children.length > 3) toasts.firstElementChild.remove();
      timer = setTimeout(add, 2400);
    };
    new IntersectionObserver(([e]) => { on = e.isIntersecting; }).observe(toasts);
    timer = setTimeout(add, 2400);
  }

  /* ---------------- PIN pad: any four digits ---------------- */
  const pin = $('[data-pin]');
  if (pin) {
    const dots = $$('.pin-dots i', pin), head = $('[data-pin-h]', pin);
    let code = '';
    const show = () => dots.forEach((dot, i) => dot.classList.toggle('on', i < code.length));
    pin.addEventListener('click', async e => {
      const b = e.target.closest('button');
      if (!b || pin.classList.contains('ok')) return;
      if (b.hasAttribute('data-clear')) code = '';
      else if (b.hasAttribute('data-back')) code = code.slice(0, -1);
      else if (code.length < 4) code += b.textContent.trim();
      show();
      if (code.length === 4) {
        if (code === '0000') { pin.classList.add('shake'); head.textContent = 'Not that one. Try another.'; await wait(500); pin.classList.remove('shake'); code = ''; show(); return; }
        pin.classList.add('ok');
        const role = +code[3] % 2 ? ['Sara', 'Cashier: selling and the tab'] : ['Omar', 'Manager: stock and reports'];
        head.textContent = `Welcome, ${role[0]}. ${role[1]}`;
        mood('happy', 720);
        await wait(2600);
        pin.classList.remove('ok'); code = ''; show(); head.textContent = 'Enter your PIN';
      }
    });
  }

  /* ---------------- QR to the owner sign-in ---------------- */
  const drawQR = () => {
    const c = $('[data-qr]');
    if (!c || typeof window.qrcode !== 'function') return;
    const qr = window.qrcode(0, 'M');
    qr.addData(`${location.origin}/owner`); qr.make();
    const n = qr.getModuleCount(), s = c.width / (n + 2), g = c.getContext('2d');
    g.fillStyle = '#FBFBF8'; g.fillRect(0, 0, c.width, c.height); g.fillStyle = '#17191F';
    for (let y = 0; y < n; y++) for (let x = 0; x < n; x++) if (qr.isDark(y, x)) g.fillRect(Math.floor((x + 1) * s), Math.floor((y + 1) * s), Math.ceil(s), Math.ceil(s));
  };
  if (document.readyState === 'complete') drawQR(); else addEventListener('load', drawQR);

  /* ---------------- contact ---------------- */
  const form = $('[data-ask]'), input = $('[data-ask-in]'), note = $('[data-ask-note]'), reach = $('[data-reach]');
  const row = (label, href, text) => `<li><span>${esc(label)}</span><a href="${esc(href)}" rel="noopener">${esc(text)}</a></li>`;
  const rows = [];
  if (CONTACT.whatsapp) rows.push(row('WhatsApp', `https://wa.me/${CONTACT.whatsapp.replace(/\D/g, '')}`, `+${CONTACT.whatsapp.replace(/\D/g, '')}`));
  if (CONTACT.phone) rows.push(row('Phone', `tel:${CONTACT.phone.replace(/[^\d+]/g, '')}`, CONTACT.phone));
  if (CONTACT.email) rows.push(row('Email', `mailto:${CONTACT.email}`, CONTACT.email));
  if (CONTACT.instagram) rows.push(row('Instagram', `https://www.instagram.com/${CONTACT.instagram}/`, `@${CONTACT.instagram}`));
  rows.push(row('Already a client', '/owner', 'Owner sign-in'));
  reach.innerHTML = rows.join('');
  const via = CONTACT.whatsapp ? 'WhatsApp' : CONTACT.email ? 'your email' : 'Instagram';
  note.textContent = `Sending opens ${via} with your message ready.`;

  // the logo shuts its eyes while you type, like the Cloud logo does for a password
  input.addEventListener('focus', () => { brand.classList.add('shut'); aim(); });
  input.addEventListener('blur', () => { brand.classList.remove('shut'); aim(); });

  let plan = '';
  $$('[data-plan]').forEach(a => a.addEventListener('click', async e => {
    e.preventDefault();
    plan = a.dataset.plan;
    if (!reduce) { a.classList.add('torn'); mood('happy', 720); await wait(560); }
    $('#contact').scrollIntoView({ behavior: reduce ? 'auto' : 'smooth' });
    input.placeholder = `${plan}: what do you sell, and where?`;
    setTimeout(() => { input.focus({ preventScroll: true }); a.classList.remove('torn'); }, reduce ? 0 : 900);
  }));

  form.addEventListener('submit', async e => {
    e.preventDefault();
    const what = input.value.trim();
    if (!what) { input.focus(); return; }
    const text = `Hello Coduo POS. ${plan ? `I'm asking about ${plan}. ` : ''}We are ${what}. Can you show us a demo?`;
    if (CONTACT.whatsapp) { open(`https://wa.me/${CONTACT.whatsapp.replace(/\D/g, '')}?text=${encodeURIComponent(text)}`, '_blank', 'noopener'); return; }
    if (CONTACT.email) { location.href = `mailto:${CONTACT.email}?subject=${encodeURIComponent(`Coduo POS ${plan || 'demo'}`)}&body=${encodeURIComponent(text)}`; return; }
    try { await navigator.clipboard.writeText(text); note.textContent = 'Your message is copied. Paste it in the Instagram chat that just opened.'; }
    catch { note.textContent = `Send us this on Instagram: "${text}"`; }
    open(`https://ig.me/m/${CONTACT.instagram}`, '_blank', 'noopener');
  });

  // pointer for the modules' parallax
  if (!reduce) addEventListener('pointermove', e => { px = e.clientX / innerWidth * 2 - 1; py = e.clientY / innerHeight * 2 - 1; }, { passive: true });
  frame();
})();
