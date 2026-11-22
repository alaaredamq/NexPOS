// Coduo POS landing page: header state, scroll reveal, the printing receipt, and the contact block.
(() => {
  'use strict';

  // EDIT HERE: how people reach you. Leave a value empty to hide that button.
  // whatsapp = international number, digits only (e.g. 9613123456). phone as you want it dialled. email = address.
  const CONTACT = { whatsapp: '', phone: '', email: '' };

  const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  document.documentElement.classList.add('js');

  // Header: transparent over the dark hero, solid once you scroll.
  const top = document.querySelector('[data-top]');
  const onScroll = () => top.classList.toggle('scrolled', window.scrollY > 24);
  onScroll();
  window.addEventListener('scroll', onScroll, { passive: true });

  // Phone menu: under 960px the section links and owner sign-in fold behind a button.
  const burger = top.querySelector('[data-burger]');
  const setMenu = open => {
    top.classList.toggle('open', open);
    burger.setAttribute('aria-expanded', String(open));
    burger.setAttribute('aria-label', open ? 'Close menu' : 'Menu');
  };
  burger.addEventListener('click', () => setMenu(!top.classList.contains('open')));
  top.querySelector('[data-menu]').addEventListener('click', e => { if (e.target.closest('a')) setMenu(false); });
  document.addEventListener('click', e => { if (!top.contains(e.target)) setMenu(false); });
  document.addEventListener('keydown', e => {
    if (e.key === 'Escape' && top.classList.contains('open')) { setMenu(false); burger.focus(); }
  });
  window.matchMedia('(min-width:961px)').addEventListener('change', e => { if (e.matches) setMenu(false); });

  document.querySelectorAll('[data-year]').forEach(n => { n.textContent = new Date().getFullYear(); });

  // Scroll reveal for cards and steps, staggered a little within each group.
  const groups = ['.timeline > li', '.steps > li', '.plan', '.flow > *', '.qa details', '.seal'];
  const items = groups.flatMap(sel => [...document.querySelectorAll(sel)].map((el, i) => {
    el.classList.add('rv');
    el.style.transitionDelay = `${Math.min(i, 5) * 50}ms`;
    return el;
  }));
  if (!reduce && 'IntersectionObserver' in window) {
    const io = new IntersectionObserver(entries => entries.forEach(e => {
      if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); }
    }), { rootMargin: '0px 0px -8% 0px' });
    items.forEach(el => io.observe(el));
  } else {
    items.forEach(el => el.classList.add('in'));
  }

  // Contact block.
  const reach = document.querySelector('[data-reach]');
  const icon = id => `<svg class="ic lg" aria-hidden="true"><use href="#${id}"/></svg>`;
  const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
  const rows = [];
  if (CONTACT.whatsapp) rows.push(`<a href="https://wa.me/${esc(CONTACT.whatsapp.replace(/\D/g, ''))}" rel="noopener">${icon('i-chat')}<span><small>Message us on</small><b>WhatsApp</b></span></a>`);
  if (CONTACT.phone) rows.push(`<a href="tel:${esc(CONTACT.phone.replace(/[^\d+]/g, ''))}">${icon('i-call')}<span><small>Call</small><b dir="ltr">${esc(CONTACT.phone)}</b></span></a>`);
  if (CONTACT.email) rows.push(`<a href="mailto:${esc(CONTACT.email)}?subject=${encodeURIComponent('Coduo POS demo')}">${icon('i-mail')}<span><small>Email</small><b>${esc(CONTACT.email)}</b></span></a>`);
  reach.innerHTML = rows.length ? rows.join('') : '<p class="none">Ask the person who told you about Coduo POS, or the shop that showed it to you, for our number.</p>';
  // Plan buttons remember which plan was asked about, for the email subject.
  document.querySelectorAll('[data-plan]').forEach(b => b.addEventListener('click', () => {
    const mail = reach.querySelector('a[href^="mailto:"]');
    if (mail) mail.href = `mailto:${CONTACT.email}?subject=${encodeURIComponent(`Coduo POS ${b.dataset.plan}`)}`;
  }));

  // The receipt. Prints one basket line by line, totals it in dollars and lira, tears off, starts the next.
  const paper = document.querySelector('[data-paper]');
  if (!paper) return;
  const lines = paper.querySelector('[data-lines]');
  const total = paper.querySelector('[data-total]');
  const meta = paper.querySelector('[data-r-meta]');
  const RATE = 89500;
  const baskets = [
    { who: 'Till 1 · Sara · 08:14', paid: 'Cash · USD', items: [
      ['Arabic bread', 1, 0.85], ['Milk 1L', 2, 1.40], ['Labneh 500g', 1, 3.25], ['Eggs, tray of 30', 1, 6.50], ['Za\'atar 250g', 1, 2.10] ] },
    { who: 'Till 1 · Omar · 13:10', paid: 'On account · Hassan K.', items: [
      ['Olive oil 1L', 1, 9.75], ['Rice 5kg', 1, 7.20], ['Tomato paste', 3, 0.95], ['Sugar 1kg', 2, 1.10] ] },
    { who: 'Till 1 · Sara · 19:42', paid: 'Cash · USD + LBP', items: [
      ['Water 6 x 1.5L', 1, 2.40], ['Halloumi 250g', 1, 4.30], ['Cucumbers 1kg', 1, 1.15], ['Ka\'ak', 4, 0.50], ['Coffee 200g', 1, 5.60], ['Tissues', 2, 1.25] ] },
  ];
  const money = n => n.toFixed(2);
  const lbp = n => Math.round(n * RATE / 1000) * 1000;
  const lineEl = ([name, qty, price]) => {
    const li = document.createElement('li');
    li.innerHTML = `<span>${esc(name)}</span><span>${money(qty * price)}</span>${qty > 1 ? `<small>${qty} x ${money(price)}</small>` : ''}`;
    return li;
  };
  const fill = b => {
    const sum = b.items.reduce((s, [, q, p]) => s + q * p, 0);
    meta.textContent = b.who;
    total.querySelector('[data-usd]').textContent = `$${money(sum)}`;
    total.querySelector('[data-lbp]').textContent = lbp(sum).toLocaleString('en-US');
    total.querySelector('[data-paid]').textContent = b.paid;
  };

  if (reduce) {
    const b = baskets[0];
    lines.replaceChildren(...b.items.map(lineEl));
    fill(b);
    total.hidden = false;
    return;
  }

  let visible = true;
  if ('IntersectionObserver' in window) {
    new IntersectionObserver(([e]) => { visible = e.isIntersecting; }).observe(paper);
  }
  const wait = ms => new Promise(r => setTimeout(r, ms));
  const whenVisible = async () => { while (!visible || document.hidden) await wait(400); };

  (async () => {
    for (let n = 0; ; n++) {
      const b = baskets[n % baskets.length];
      // Put the fresh paper back under the slot without animating it up from below.
      paper.style.transition = 'none';
      paper.classList.remove('torn');
      void paper.offsetHeight;
      paper.style.transition = '';
      lines.replaceChildren();
      total.hidden = true;
      meta.textContent = b.who;
      await wait(500);
      for (const item of b.items) {
        await whenVisible();
        lines.append(lineEl(item));
        await wait(520);
      }
      fill(b);
      total.hidden = false;
      await wait(4200);
      await whenVisible();
      paper.classList.add('torn');
      await wait(800);
    }
  })();
})();
