/**
 * Records the Coduo POS client demo video.
 *
 * Drives a running Coduo POS instance (seeded with the demo data - see README.md) through a
 * typical day: sign in, open the till, ring up sales, look after customers, review the
 * numbers, close the shift. An overlay adds a visible cursor and on-screen captions, since
 * the recording has no narration.
 *
 * Usage:  node record-demo.js            (env: BASE_URL, OUT_DIR, USERNAME, PIN, ONLY=scene,scene)
 * Output: <OUT_DIR>/raw.webm  (record.ps1 converts it to an mp4)
 */
const path = require('path');
const fs = require('fs');
const { chromium } = require('playwright');

const BASE = process.env.BASE_URL || 'http://localhost:5055';
const OUT_DIR = path.resolve(process.env.OUT_DIR || path.join(__dirname, 'out'));
const USERNAME = process.env.DEMO_USERNAME || 'layla';
const PIN = process.env.DEMO_PIN || '1234';
const ONLY = (process.env.ONLY || '').split(',').map(s => s.trim()).filter(Boolean);
const SIZE = { width: 1600, height: 900 };

// Text shown on the closing card - edit to taste.
const OUTRO_TITLE = 'Run your whole store from one screen';
const OUTRO_LINES = ['Fast checkout  ·  Live inventory  ·  Customer accounts  ·  Clear reports', 'English & Arabic  ·  Works offline on your own PC'];

// ------------------------------------------------------------------ overlay (cursor + captions)

const OVERLAY = `(() => {
  if (window.__ovl) return; window.__ovl = true;
  const css = \`
    #__cur{position:fixed;left:0;top:0;width:30px;height:30px;z-index:2147483647;pointer-events:none;
      transform:translate(-100px,-100px);transition:none;filter:drop-shadow(0 2px 3px rgba(0,0,0,.45))}
    #__cur svg{width:30px;height:30px;transition:transform .12s}
    #__cur.down svg{transform:scale(.82)}
    .__rip{position:fixed;z-index:2147483646;pointer-events:none;width:18px;height:18px;margin:-9px 0 0 -9px;border-radius:50%;
      background:rgba(14,165,164,.35);border:2px solid rgba(14,165,164,.9);animation:__rip .55s ease-out forwards}
    @keyframes __rip{to{transform:scale(3.6);opacity:0}}
    #__cap{position:fixed;left:50%;z-index:2147483645;pointer-events:none;display:flex;align-items:center;gap:14px;
      width:max-content;max-width:min(1180px,92vw);padding:13px 26px 13px 14px;border-radius:999px;background:rgba(10,25,35,.90);
      color:#fff;font:600 24px/1.25 'Segoe UI',Inter,system-ui,sans-serif;letter-spacing:.1px;
      box-shadow:0 10px 30px rgba(0,0,0,.35);opacity:0;transform:translate(-50%,14px);transition:opacity .35s,transform .35s}
    #__cap.show{opacity:1;transform:translate(-50%,0)}
    #__cap.bottom{bottom:30px}
    #__cap.top{top:8px;padding:8px 22px 8px 10px;font-size:21px;gap:12px}
    #__cap .b{flex:none;background:#0ea5a4;color:#fff;border-radius:999px;min-width:34px;height:34px;padding:0 12px;
      display:flex;align-items:center;justify-content:center;font-size:.72em;font-weight:800;letter-spacing:.4px;text-transform:uppercase}
    #__cap.top .b{height:30px;min-width:30px}
  \`;
  const mount = () => {
    if (!document.documentElement) return requestAnimationFrame(mount);
    const st = document.createElement('style'); st.textContent = css; document.documentElement.appendChild(st);
    const cur = document.createElement('div'); cur.id = '__cur';
    cur.innerHTML = '<svg viewBox="0 0 24 24"><path d="M4 2l15 9.2-6.4 1.5 3.9 6.9-2.7 1.5-3.9-6.9L5 18.5z" fill="#fff" stroke="#0b1b26" stroke-width="1.6" stroke-linejoin="round"/></svg>';
    document.documentElement.appendChild(cur);
    const cap = document.createElement('div'); cap.id = '__cap'; document.documentElement.appendChild(cap);
    const move = (x, y) => { cur.style.transform = 'translate(' + x + 'px,' + y + 'px)'; try { sessionStorage.__pos = x + ',' + y; } catch (e) {} };
    try { const p = (sessionStorage.__pos || '').split(','); if (p.length === 2) move(+p[0], +p[1]); } catch (e) {}
    addEventListener('mousemove', e => move(e.clientX, e.clientY), true);
    addEventListener('mousedown', e => {
      cur.classList.add('down');
      const r = document.createElement('div'); r.className = '__rip'; r.style.left = e.clientX + 'px'; r.style.top = e.clientY + 'px';
      document.documentElement.appendChild(r); setTimeout(() => r.remove(), 650);
    }, true);
    addEventListener('mouseup', () => cur.classList.remove('down'), true);
    const render = (c, animate) => {
      if (!c) { cap.classList.remove('show'); return; }
      cap.className = c.pos === 'top' ? 'top' : 'bottom';
      cap.innerHTML = (c.chapter ? '<span class="b">' + c.chapter + '</span>' : '') + '<span>' + c.text + '</span>';
      if (animate) requestAnimationFrame(() => requestAnimationFrame(() => cap.classList.add('show')));
      else { cap.style.transition = 'none'; cap.classList.add('show'); requestAnimationFrame(() => cap.style.transition = ''); }
    };
    window.__caption = c => { try { if (c) sessionStorage.__cap = JSON.stringify(c); else sessionStorage.removeItem('__cap'); } catch (e) {} render(c, true); };
    try { const s = sessionStorage.__cap; if (s) render(JSON.parse(s), false); } catch (e) {}
  };
  mount();
})();`;

// ------------------------------------------------------------------ cards

const cardHtml = (inner) => `<!doctype html><html><head><meta charset="utf-8"><style>
  html,body{margin:0;height:100%;overflow:hidden;font-family:'Segoe UI',Inter,system-ui,sans-serif;color:#fff}
  body{background:radial-gradient(1200px 700px at 20% 15%,#12b3b0 0%,#08807f 45%,#04494a 100%);
       display:flex;align-items:center;justify-content:center;text-align:center}
  .wrap{animation:in 1s ease-out both}
  @keyframes in{from{opacity:0;transform:translateY(24px)}to{opacity:1;transform:none}}
  img{width:520px;display:block;margin:0 auto 18px}
  h1{font-size:54px;font-weight:700;margin:0 0 16px;letter-spacing:-.5px}
  p{font-size:27px;margin:6px 0;opacity:.92}
  .chips{margin-top:34px;display:flex;gap:14px;justify-content:center;flex-wrap:wrap}
  .chips span{background:rgba(255,255,255,.14);border:1px solid rgba(255,255,255,.28);border-radius:999px;padding:10px 24px;font-size:23px}
  .sub{font-size:30px;font-weight:400;opacity:.95;margin-top:4px}
</style></head><body><div class="wrap">${inner}</div></body></html>`;

const introHtml = () => cardHtml(`
  <img src="${BASE}/images/logo-light.svg" alt="Coduo POS">
  <h1>Your store, running smoothly</h1>
  <p class="sub">Point of sale, inventory and customer accounts in one simple app</p>
  <div class="chips"><span>Fast checkout</span><span>Live inventory</span><span>Customer accounts</span><span>Sales reports</span><span>English &amp; العربية</span></div>
  <p style="margin-top:38px;font-size:22px;opacity:.75">A quick tour of a normal day</p>`);

const outroHtml = () => cardHtml(`
  <img src="${BASE}/images/logo-light.svg" alt="Coduo POS" style="width:420px">
  <h1>${OUTRO_TITLE}</h1>
  ${OUTRO_LINES.map(l => `<p>${l}</p>`).join('')}
  <p style="margin-top:40px;font-size:24px;opacity:.85">Ask us for a live demo on your own products.</p>`);

// ------------------------------------------------------------------ helpers

const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const ease = (t) => (t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2);

async function main() {
  const VIDEO_DIR = path.join(OUT_DIR, 'video');
  fs.rmSync(VIDEO_DIR, { recursive: true, force: true });
  fs.mkdirSync(VIDEO_DIR, { recursive: true });
  const browser = await chromium.launch({ args: ['--force-device-scale-factor=1', '--hide-scrollbars'] });
  const context = await browser.newContext({
    viewport: SIZE,
    recordVideo: { dir: VIDEO_DIR, size: SIZE },
    locale: 'en-US',
    timezoneId: 'Asia/Beirut',
    colorScheme: 'light',
    acceptDownloads: true
  });
  await context.addInitScript(OVERLAY);
  const page = await context.newPage();
  page.on('dialog', d => d.accept().catch(() => {}));
  page.on('pageerror', e => console.log('  [page error]', e.message.slice(0, 160)));

  let mouse = { x: 800, y: 460 };

  // ---- interaction primitives -------------------------------------------------------------
  async function glide(x, y, ms = 650) {
    const from = { ...mouse };
    const dist = Math.hypot(x - from.x, y - from.y);
    const dur = Math.min(Math.max(ms * (0.45 + dist / 1400), 260), 1100);
    const steps = Math.max(10, Math.round(dur / 18));
    for (let i = 1; i <= steps; i++) {
      const t = ease(i / steps);
      await page.mouse.move(from.x + (x - from.x) * t, from.y + (y - from.y) * t);
      await sleep(dur / steps);
    }
    mouse = { x, y };
  }

  async function centerOf(target) {
    const loc = typeof target === 'string' ? page.locator(target).first() : target.first();
    await loc.waitFor({ state: 'visible', timeout: 15000 });
    await loc.scrollIntoViewIfNeeded().catch(() => {});
    const box = await loc.boundingBox();
    if (!box) throw new Error('no box for ' + target);
    return { loc, x: box.x + box.width / 2, y: box.y + box.height / 2, box };
  }

  async function hover(target, ms) {
    const { x, y } = await centerOf(target);
    await glide(x, y, ms);
  }

  async function click(target, { after = 350, offsetY = 0 } = {}) {
    const { loc, x, y } = await centerOf(target);
    await glide(x, y + offsetY);
    await sleep(160);
    await page.mouse.down(); await sleep(70); await page.mouse.up();
    await sleep(after);
    return loc;
  }

  async function type(target, text, { delay = 85, clear = true } = {}) {
    await click(target, { after: 150 });
    if (clear) await page.keyboard.press('Control+A').then(() => page.keyboard.press('Backspace'));
    for (const ch of text) { await page.keyboard.type(ch); await sleep(delay + Math.random() * 45); }
    await sleep(250);
  }

  async function scroll(total, { step = 38, gap = 16 } = {}) {
    const dir = Math.sign(total);
    let done = 0;
    while (Math.abs(done) < Math.abs(total)) {
      await page.mouse.wheel(0, dir * step);
      done += dir * step;
      await sleep(gap);
    }
    await sleep(300);
  }

  async function settle(extra = 700) {
    await page.waitForLoadState('domcontentloaded').catch(() => {});
    await page.waitForLoadState('networkidle', { timeout: 5000 }).catch(() => {});
    await sleep(extra);
  }

  async function nav(href, opts) {
    await click(`a[href="${href}"]`, { after: 150, ...opts });
    await settle();
  }

  async function say(text, { chapter, pos = 'bottom' } = {}) {
    await page.evaluate(c => window.__caption && window.__caption(c), { text, chapter, pos }).catch(() => {});
  }
  async function unsay() { await page.evaluate(() => window.__caption && window.__caption(null)).catch(() => {}); }

  async function card(html, ms) {
    await unsay();
    await page.setContent(html, { waitUntil: 'load' });
    await sleep(ms);
  }

  async function goto(url) {
    await page.goto(BASE + url, { waitUntil: 'load' });
    await settle();
  }

  // ---- POS helpers ------------------------------------------------------------------------
  const productCard = (name) => page.locator('#product-grid, .product-grid, body').locator(`text="${name}"`).first();

  async function addByTap(name) {
    await click(page.getByText(name, { exact: true }).first(), { after: 450 });
  }

  async function addByWeight(name, grams) {
    await click(page.getByText(name, { exact: true }).first(), { after: 700 });
    await type('#quantity-input', String(grams));
    await sleep(1400);
    await click('.swal2-confirm', { after: 700 });
  }

  async function checkout({ customerId, paymentId, onAccount = false, hold = 1400 }) {
    await click('#checkoutButton', { after: 900 });
    if (customerId) {
      const { x, y } = await centerOf('#checkout-customer-select');
      await glide(x, y); await sleep(200);
      await page.selectOption('#checkout-customer-select', String(customerId));
      await sleep(900);
    }
    if (paymentId) {
      const { x, y } = await centerOf('#checkout-payment-method');
      await glide(x, y); await sleep(200);
      await page.selectOption('#checkout-payment-method', String(paymentId));
      await sleep(700);
    }
    await sleep(hold);
    await click(onAccount ? '.swal2-deny' : '.swal2-confirm', { after: 500 });
  }

  // ------------------------------------------------------------------ scenes
  const scenes = [];
  const scene = (name, fn) => scenes.push({ name, fn });

  scene('intro', async () => {
    await page.setContent(cardHtml(''), { waitUntil: 'load' }); // dark frame while the card image loads
    await card(introHtml(), 6500);
  });

  scene('login', async () => {
    await goto('/en/Account/Login');
    // Hide the built-in "Demo Credentials" hint - not something clients should see.
    await page.addStyleTag({ content: '.demo-credentials{display:none!important}' });
    await say('Staff sign in with a quick 4-digit PIN - no passwords to forget', { chapter: '1 · Sign in' });
    await sleep(1800);
    await type('input[type=text]', USERNAME);
    for (const d of PIN) await click(`button[data-digit="${d}"]`, { after: 300 });
    await sleep(700);
    await click('button[type=submit]', { after: 300 });
    await page.waitForURL(/\/POS/i, { timeout: 15000 }).catch(() => {});
    await settle(1500);
  });

  scene('drawer-open', async () => {
    await say('Every shift starts by counting the cash in the drawer', { chapter: '2 · Open the till', pos: 'top' });
    await sleep(1800);
    await click('a[href="/en/CashDrawer"]', { after: 200 });
    await settle(900);
    await say('Every shift starts by counting the cash in the drawer', { chapter: '2 · Open the till', pos: 'bottom' });
    await click('#currentSessionCard a, #currentSessionCard button, .btn:has-text("Open Cash Drawer") >> nth=-1', { after: 200 });
    await page.waitForURL(/CashDrawer\/Open/i, { timeout: 10000 }).catch(() => {});
    await settle(700);
    await type('#openingCash', '100', { delay: 140 });
    await type('#openingNotes', 'Morning shift', { delay: 70 });
    await sleep(600);
    await click('#openDrawerForm button[type=submit]', { after: 300 });
    await page.waitForURL(u => /CashDrawer\/?$/i.test(u.pathname), { timeout: 15000 }).catch(() => {});
    await settle(1200);
    await say('Sales are tracked against your shift, so the count at closing time always adds up', { chapter: '2 · Open the till' });
    await sleep(3200);
  });

  scene('pos', async () => {
    await click('a[href="/en/POS"]', { after: 200 });
    await settle(1200);

    await say('Tap a product to add it to the cart', { chapter: '3 · Ring up a sale', pos: 'top' });
    await sleep(1200);
    await addByTap('Arabic Bread (Khubz) Pack');
    await addByTap('Cola 330ml Can');
    await addByTap('Cola 330ml Can');

    await say('Scanner ready? Scan a barcode and the item is added instantly', { chapter: '3 · Ring up a sale', pos: 'top' });
    await type('#barcode-input', '6001000000135', { delay: 45 });
    await page.keyboard.press('Enter');
    await sleep(1600);

    await say('Or search by name', { chapter: '3 · Ring up a sale', pos: 'top' });
    await type('#product-search', 'milk', { delay: 130 });
    await sleep(900);
    await click('.ui-menu-item >> nth=0', { after: 900 });

    await say('Sold by weight? Type the amount - the price works itself out', { chapter: '3 · Ring up a sale', pos: 'top' });
    await page.fill('#product-search', '').catch(() => {});
    await addByWeight('Tomatoes (per kg)', 750);
    await sleep(1200);

    await say('Pick the customer and how they pay - cash or card', { chapter: '3 · Ring up a sale', pos: 'top' });
    await checkout({ customerId: 2, hold: 1800 });
    await say('Sale complete - receipt printed and stock updated instantly', { chapter: '3 · Ring up a sale', pos: 'top' });
    await sleep(2400);

    // a quick second sale, by card
    await say('Card payments are just as quick', { chapter: '3 · Ring up a sale', pos: 'top' });
    await addByTap('Orange Juice 1L');
    await addByTap('Butter Croissant');
    await addByTap('Butter Croissant');
    await checkout({ paymentId: 2, hold: 600 });
    await sleep(1500);

    // sale on account
    await say('Regular customers can buy on account - the balance is tracked for you', { chapter: '3 · Ring up a sale', pos: 'top' });
    await addByTap('Eggs Tray (30)');
    await addByTap('Labneh 500g');
    await addByTap('Arabic Coffee 250g');
    await checkout({ customerId: 3, onAccount: true, hold: 1300 });
    await sleep(600);
    // confirm dialog for "Add to Debt", if the app asks
    const confirmBtn = page.locator('.swal2-confirm:visible');
    if (await confirmBtn.count()) await click(confirmBtn, { after: 500 });
    await sleep(2000);
    await unsay();
  });

  scene('customers', async () => {
    await say('Customer accounts: order history, spending and balances at a glance', { chapter: '4 · Customers' });
    await click('a[href="/en/Dashboard"]', { after: 200 });
    await settle(1000);
    await nav('/en/Customers');
    await sleep(2600);
    const row = page.locator('tbody tr', { hasText: 'Karim Mansour' }).first();
    await click(row.locator('.fa-eye').first(), { after: 200 });
    await settle(1200);
    await say('Each account keeps a full ledger of what the customer owes', { chapter: '4 · Customers' });
    await sleep(2200);
    await scroll(430);
    await sleep(2600);
    await scroll(-430);
    await say('Customer paid something? Record the payment in two clicks', { chapter: '4 · Customers' });
    await click('button:has-text("Add Transaction")', { after: 900 });
    { const { x, y } = await centerOf('#debt-direction'); await glide(x, y); await sleep(200);
      await page.selectOption('#debt-direction', 'false'); await sleep(700); }
    await type('#debt-amount', '20', { delay: 140 });
    await type('#debt-note', 'Cash payment received', { delay: 60 });
    await sleep(500);
    await click('.swal2-confirm', { after: 1200 });
    await settle(1800);
    await sleep(1800);
    await unsay();
  });

  scene('orders', async () => {
    await say('Every order and receipt is one click away', { chapter: '5 · Orders & receipts' });
    await nav('/en/Orders');
    await sleep(2800);
    await click('tbody tr:first-child .fa-eye', { after: 200 });
    await settle(1200);
    await sleep(3000);
    await nav('/en/Receipts');
    await sleep(3000);
    await unsay();
  });

  scene('dashboard', async () => {
    await say('The dashboard shows how the business is doing - live', { chapter: '6 · Dashboard' });
    await nav('/en/Dashboard');
    await sleep(3500);
    await scroll(520, { step: 26, gap: 18 });
    await sleep(2500);
    await scroll(560, { step: 26, gap: 18 });
    await sleep(2500);
    await scroll(-1100, { step: 60, gap: 12 });
    await unsay();
  });

  scene('inventory', async () => {
    await say('Products and stock levels, with low-stock warnings before you run out', { chapter: '7 · Inventory' });
    await nav('/en/Products');
    await sleep(2500);
    await nav('/en/Inventory');
    await sleep(2200);
    await scroll(300, { step: 30, gap: 16 });
    await sleep(2200);
    await scroll(-300, { step: 60, gap: 12 });
    await unsay();
  });

  scene('reports', async () => {
    await say('Sales figures for today, this month and this year - always up to date', { chapter: '8 · Reports' });
    await nav('/en/Reports');
    await sleep(3800);
    await say('Trends over time, category and payment analysis - ready when your accountant asks', { chapter: '8 · Reports' });
    await click(page.getByText('Sales Trend', { exact: true }).first(), { after: 300 });
    await sleep(4500);
    await click('.modal.show button:has-text("Close")', { after: 600 });
    await unsay();
  });

  scene('drawer-close', async () => {
    await say('At closing time, count the drawer and see any difference immediately', { chapter: '9 · End of day' });
    await nav('/en/CashDrawer');
    await sleep(2500);
    await click('a:has-text("Close Drawer")', { after: 200 });
    await settle(1200);
    await sleep(2400);
    await click('a:has-text("Cancel"), button:has-text("Cancel")', { after: 200 });
    await settle(800);
    await click('a:has-text("History")', { after: 200 });
    await settle(1500);
    await say('Past shifts are kept, with who worked them and how the count compared', { chapter: '9 · End of day' });
    await sleep(3000);
    await unsay();
  });

  scene('team', async () => {
    await say('Roles and permissions: cashiers only see what they need', { chapter: '10 · Your team' });
    await nav('/en/Users');
    await sleep(3800);
    await nav('/en/Currency');
    await say('Multiple currencies (USD / LBP) with your own exchange rates', { chapter: '10 · Your team' });
    await sleep(3200);
    await unsay();
  });

  scene('arabic', async () => {
    await say('Full Arabic support - the whole app switches to right-to-left in one click', { chapter: '11 · Arabic' });
    await click('a[href="/en/POS"]', { after: 200 });
    await settle(1200);
    await click('header a:has-text("EN"), a.dropdown-toggle:has-text("EN"), .dropdown-toggle:has-text("EN")', { after: 700 });
    await click('a[href="/ar/POS"]', { after: 200 });
    await settle(1500);
    await sleep(4200);
    await unsay();
  });

  scene('outro', async () => {
    await card(outroHtml(), 7000);
  });

  await run(scenes);

  // ---- finish ---------------------------------------------------------------------------------
  const video = page.video();
  await context.close();
  const file = await video.path();
  fs.renameSync(file, path.join(OUT_DIR, 'raw.webm'));
  await browser.close();
  console.log('Recorded ->', path.join(OUT_DIR, 'raw.webm'));

  async function run(list) {
    for (const s of list) {
      if (ONLY.length && !ONLY.includes(s.name)) continue;
      console.log('scene:', s.name);
      const t0 = Date.now();
      try { await s.fn(); }
      catch (e) {
        console.log('  !! scene failed:', e.message.split('\n')[0]);
        await page.screenshot({ path: path.join(OUT_DIR, `fail-${s.name}.png`) }).catch(() => {});
      }
      console.log(`  (${((Date.now() - t0) / 1000).toFixed(1)}s)`);
    }
  }
}

main().catch(e => { console.error(e); process.exit(1); });
