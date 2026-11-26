// Coduo Cloud portal: motion. No dependencies.
//  - the opening (once a session, after sign-in): the cloud draws itself, its eyes open, blink and wink, then the cloud becomes a
//    window and the camera flies in through it onto the page
//  - blocks glide in as they come into view; numbers count up; bars, rings, meters and the contact line draw themselves
//  - the room: a slow star field that reaches for the pointer; glass edges light up near the pointer
//  - the rail's highlight slides between links; the logo's eyes follow the pointer (and close while a password is typed)
// <head> sets html.fx / html.fx-wait / html.fx-intro before the first paint. With reduced motion (html.fx-calm) nothing moves.
(() => {
  const root = document.documentElement
  window.__fx = true
  const calm = root.classList.contains('fx-calm') || !!window.__fxGaveUp
  const clamp01 = (v) => (v < 0 ? 0 : v > 1 ? 1 : v)
  const span = (t, a, b) => clamp01((t - a) / (b - a)) // how far t is through [a, b]
  const ease = {
    out: (t) => 1 - Math.pow(1 - t, 3),
    expo: (t) => (t >= 1 ? 1 : 1 - Math.pow(2, -10 * t)),
    inOut: (t) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2),
    back: (t) => 1 + 2.9 * Math.pow(t - 1, 3) + 1.9 * Math.pow(t - 1, 2),
  }
  const pointer = { x: -1e4, y: -1e4 }

  // ================================================================== blocks that glide in
  const blocks = [...document.querySelectorAll(
    'main > :not(script):not(template):not(.g), main .g > *, .auth .form .box, .auth .side > *')]
  const COUNT = '.big, .kpi, .kv b, .ledger b, .pr .am, .chip .n, .hbars .am, .count, .mh b, .ring-val b, .donut-val b, .item .data b, .ph .num'

  // Stagger indexes: each bar, cell, row and line item waits a little longer than the one before.
  const stagger = (scope, sel, prop, cap) => scope.querySelectorAll(sel).forEach((el, n) => el.style.setProperty(prop, Math.min(n, cap)))
  if (!calm) {
    for (const b of blocks) {
      b.classList.add('fx-r')
      if (b.matches('.page-head, .hud, .auth .side > *')) b.classList.add('fx-text')
      b.querySelectorAll('.cols, .pulse').forEach((g) => [...g.children].forEach((el, n) => el.style.setProperty('--i', n)))
      stagger(b, '.item', '--ri', 14)
      for (const sel of ['.ai', '.pr', '.hbars li', '.site-card', 'table.t tbody tr', '.meter i', '.stack i']) stagger(b, sel, '--i', 12)
    }
  }

  // ================================================================== the contact line over a single-series bar chart
  const smooth = (p) => {
    const clampY = (y) => Math.max(0, Math.min(100, y))
    const r = (v) => Math.round(v * 10) / 10
    let d = `M${r(p[0][0])},${r(p[0][1])}`
    for (let i = 0; i < p.length - 1; i++) {
      const p0 = p[i - 1] || p[i], p1 = p[i], p2 = p[i + 1], p3 = p[i + 2] || p2
      const c1 = [p1[0] + (p2[0] - p0[0]) / 6, clampY(p1[1] + (p2[1] - p0[1]) / 6)]
      const c2 = [p2[0] - (p3[0] - p1[0]) / 6, clampY(p2[1] - (p3[1] - p1[1]) / 6)]
      d += ` C${r(c1[0])},${r(c1[1])} ${r(c2[0])},${r(c2[1])} ${r(p2[0])},${r(p2[1])}`
    }
    return d
  }
  document.querySelectorAll('.chart').forEach((chart, k) => {
    const cols = chart.querySelector('.cols')
    const plot = chart.querySelector('.plot')
    if (!cols || !plot || cols.querySelector('.pair')) return
    const vals = [...cols.children].map((col) => parseFloat(col.querySelector('.bar')?.style.height) || 0)
    if (vals.length < 2 || vals.every((v) => v === 0)) return
    const n = vals.length
    const pts = vals.map((v, i) => [((i + 0.5) / n) * 1000, 100 - v])
    const line = smooth(pts)
    const id = 'cl-g' + k
    plot.insertAdjacentHTML('beforeend',
      `<svg class="chart-line" viewBox="0 0 1000 100" preserveAspectRatio="none" aria-hidden="true">` +
      `<defs><linearGradient id="${id}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3ddbcb" stop-opacity=".32"/>` +
      `<stop offset="1" stop-color="#3ddbcb" stop-opacity="0"/></linearGradient></defs>` +
      `<path class="ar" fill="url(#${id})" d="${line} L${pts[n - 1][0]},100 L${pts[0][0]},100 Z"/>` +
      `<path class="ln" vector-effect="non-scaling-stroke" d="${line}"/></svg>` +
      `<span class="chart-dot" style="left:${((n - 0.5) / n) * 100}%;top:${100 - vals[n - 1]}%"></span>`)
  })

  // ================================================================== numbers count up
  const NUM = /(\d{1,3}(?:,\d{3})+|\d+)(?:\.(\d+))?/
  const countUp = (el, delay) => {
    if (el.dataset.fxCount) return
    const node = [...el.childNodes].find((c) => c.nodeType === 3 && /\d/.test(c.nodeValue))
    if (!node) return
    const text = node.nodeValue
    if (/\d\s*[-/:]\s*\d/.test(text)) return // dates and times stay as they are
    const m = text.match(NUM)
    if (!m) return
    const decimals = (m[2] || '').length
    const target = parseFloat(m[1].replace(/,/g, '') + (decimals ? '.' + m[2] : ''))
    if (!isFinite(target) || target === 0) return
    el.dataset.fxCount = '1'
    const pre = text.slice(0, m.index), post = text.slice(m.index + m[0].length)
    const fmt = new Intl.NumberFormat('en-US', { minimumFractionDigits: decimals, maximumFractionDigits: decimals, useGrouping: m[1].includes(',') })
    node.nodeValue = pre + fmt.format(0) + post
    const start = performance.now() + delay
    const dur = 1500 + Math.min(500, Math.log10(target + 1) * 120)
    const tick = (now) => {
      const t = span(now, start, start + dur)
      node.nodeValue = t >= 1 ? text : pre + fmt.format(target * ease.expo(t)) + post
      if (t < 1) requestAnimationFrame(tick)
    }
    requestAnimationFrame(tick)
  }

  // ================================================================== reveal as blocks come into view
  const byOrder = (a, b) => (a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1)
  const reveal = (els) => {
    els.sort(byOrder).forEach((el, n) => {
      const d = Math.min(n * 85, 680)
      el.style.setProperty('--d', d + 'ms')
      el.classList.add('in')
      el.querySelectorAll(COUNT).forEach((c) => countUp(c, d + 260))
    })
  }
  const startReveal = () => {
    if (!('IntersectionObserver' in window)) { reveal(blocks); return }
    const io = new IntersectionObserver((entries) => {
      const seen = entries.filter((e) => e.isIntersecting).map((e) => e.target)
      seen.forEach((el) => io.unobserve(el))
      if (seen.length) reveal(seen)
    }, { rootMargin: '0px 0px -4% 0px', threshold: 0.01 })
    blocks.forEach((b) => io.observe(b))
  }
  root.classList.remove('fx-wait')

  // ================================================================== the opening
  const intro = document.getElementById('intro')
  const shell = document.querySelector('.shell')
  const playIntro = !calm && root.classList.contains('fx-intro') && intro && shell
  if (!playIntro) {
    root.classList.remove('fx-intro')
    intro?.remove()
    if (!calm) requestAnimationFrame(() => requestAnimationFrame(startReveal))
  } else {
    try { sessionStorage.setItem('coduo.intro', '1') } catch { }
    const $ = (s) => intro.querySelector(s)
    const zooms = [...intro.querySelectorAll('[data-zoom]')]
    const cloud = $('[data-cloud]'), glow = $('[data-glow]'), hole = $('[data-hole]'), tilt = $('[data-tilt]')
    const eyeL = $('[data-eye="l"]'), eyeR = $('[data-eye="r"]'), eyes = $('[data-eyes]')
    const rings = [...intro.querySelectorAll('[data-ring]')]
    const reticle = $('[data-reticle]'), arc = $('[data-arc]')
    const OY = -12 + 1.64 * 0.95 // the point between the eyes, in the intro's own units: the camera flies in there
    const RINGS = [300, 1000, 2300]
    const eyeT = (cx, sy) => `translate(${cx} 1.64) scale(${Math.max(0.0001, Math.min(sy[0], 9))} ${Math.max(0.0001, sy[1])}) translate(${-cx} -1.64)`
    let revealed = false, done = false, t = 0, speed = 1, last = performance.now()

    const place = () => {
      const unit = Math.min(innerWidth, innerHeight) / 200
      shell.style.transformOrigin = `${innerWidth / 2}px ${scrollY + innerHeight / 2 + OY * unit}px`
    }
    place()
    addEventListener('resize', place)

    const frame = (t) => {
      // 1. the cloud draws itself
      const drawn = ease.inOut(span(t, 150, 1250))
      cloud.style.strokeDashoffset = 100 - 100 * drawn
      glow.style.strokeDashoffset = 100 - 100 * drawn
      glow.setAttribute('opacity', (0.95 * span(t, 150, 700) * (1 - span(t, 3100, 3350))).toFixed(3))
      // 2. the eyes pop open, blink, then the right one winks while the cloud tilts
      const pop = span(t, 900, 1320)
      const open = pop === 0 ? 0 : ease.back(pop)
      const blink = span(t, 1650, 1840)
      const blinkY = blink > 0 && blink < 1 ? 1 - Math.sin(blink * Math.PI) * 0.92 : 1
      const wink = ease.out(span(t, 2250, 2340)) * (1 - ease.out(span(t, 2500, 2660)))
      const shut = ease.out(span(t, 3120, 3290))
      eyeL.setAttribute('transform', eyeT(-9.5, [open, open * blinkY * (1 - 0.16 * wink) * (1 - 0.95 * shut)]))
      eyeR.setAttribute('transform', eyeT(9.5, [open, open * blinkY * (1 - 0.92 * wink) * (1 - 0.95 * shut)]))
      eyes.setAttribute('opacity', (1 - span(t, 3200, 3380)).toFixed(3))
      const lean = Math.sin(Math.PI * span(t, 2180, 2760))
      tilt.setAttribute('transform', `translate(0 ${(-2.2 * lean).toFixed(2)}) rotate(${(-7 * lean).toFixed(2)} 0 -10)`)
      // 3. sonar rings and a turning reticle while it wakes up
      rings.forEach((ring, n) => {
        const p = span(t, RINGS[n], RINGS[n] + 1800)
        ring.setAttribute('r', (30 + 115 * ease.out(p)).toFixed(2))
        ring.setAttribute('opacity', (p > 0 && p < 1 ? 0.55 * (1 - p) : 0).toFixed(3))
      })
      const ret = 0.85 * span(t, 450, 1200) * (1 - span(t, 2700, 3000))
      reticle.setAttribute('opacity', ret.toFixed(3))
      reticle.setAttribute('transform', `rotate(${(t * 0.018).toFixed(2)} 0 -10)`)
      arc.setAttribute('transform', `rotate(${(-t * 0.09).toFixed(2)} 0 -10)`)
      // 4. the cloud becomes a window onto the page, winds up, and the camera flies in through it
      const win = ease.inOut(span(t, 2750, 3150))
      const g = Math.round(255 * (1 - win))
      hole.setAttribute('fill', `rgb(${g},${g},${g})`)
      const fly = span(t, 3150, 4450)
      const z = fly > 0 ? 0.93 * Math.pow(60, ease.inOut(fly)) : 1 - 0.07 * ease.out(span(t, 2850, 3150))
      const tf = `translate(0 ${OY}) scale(${z.toFixed(4)}) translate(0 ${-OY})`
      zooms.forEach((el) => el.setAttribute('transform', tf))
      shell.style.transform = `scale(${(0.86 + 0.14 * ease.out(fly)).toFixed(4)})`
      // the words
      intro.classList.toggle('s-word', t > 1050)
      intro.classList.toggle('s-out', t > 2650)
    }

    const finish = () => {
      if (done) return
      done = true
      removeEventListener('resize', place)
      shell.style.transform = ''
      shell.style.transformOrigin = ''
      root.classList.remove('fx-intro')
      intro.remove()
      if (!revealed) { revealed = true; startReveal() }
    }
    const skip = () => { if (t < 2700) t = 2700; speed = 1.7 }
    intro.addEventListener('pointerdown', skip)
    addEventListener('keydown', skip, { once: true })
    addEventListener('wheel', skip, { once: true, passive: true })

    intro.classList.add('s-scan')
    const loop = (now) => {
      if (done) return
      if (!root.classList.contains('fx-intro')) { finish(); return } // the safety net in <head> gave up on us
      t += Math.min(64, now - last) * speed
      last = now
      try { frame(t) } catch { finish(); return }
      if (!revealed && t >= 2850) { revealed = true; startReveal() }
      if (t >= 4480) { finish(); return }
      requestAnimationFrame(loop)
    }
    requestAnimationFrame((now) => { last = now; loop(now) })
  }

  // ================================================================== the star field
  let redrawStars = () => { }
  const canvas = document.querySelector('.fx-stars')
  if (canvas?.getContext) {
    const ctx = canvas.getContext('2d')
    let w = 0, h = 0, pts = [], lastT = 0, running = !document.hidden
    const make = () => ({
      x: Math.random() * w, y: Math.random() * h, vx: (Math.random() - 0.5) * 0.14, vy: (Math.random() - 0.5) * 0.14,
      r: Math.random() * 1.2 + 0.35, ph: Math.random() * 6.283, sp: 0.5 + Math.random() * 1.5, teal: Math.random() < 0.42,
    })
    const LINK = 135, REACH = 190
    // Pale stars on the night sky; navy and deep teal ones, a little stronger, on the light one.
    const DARK = { link: '125,155,255', reach: '61,219,203', teal: '61,219,203', star: '185,200,255', k: 1 }
    const LIGHT = { link: '30,58,138', reach: '14,154,141', teal: '14,154,141', star: '30,58,138', k: 0.85 }
    const draw = (now) => {
      const c = root.dataset.theme === 'light' ? LIGHT : DARK
      ctx.clearRect(0, 0, w, h)
      ctx.lineWidth = 0.6
      for (let i = 0; i < pts.length; i++) {
        const a = pts[i]
        for (let j = i + 1; j < pts.length; j++) {
          const b = pts[j], dx = a.x - b.x, dy = a.y - b.y
          if (dx > LINK || dx < -LINK || dy > LINK || dy < -LINK) continue
          const d = Math.sqrt(dx * dx + dy * dy)
          if (d >= LINK) continue
          ctx.strokeStyle = `rgba(${c.link},${((1 - d / LINK) * 0.2 * c.k).toFixed(3)})`
          ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); ctx.stroke()
        }
        const mx = a.x - pointer.x, my = a.y - pointer.y, md = Math.sqrt(mx * mx + my * my)
        if (md < REACH) {
          ctx.strokeStyle = `rgba(${c.reach},${((1 - md / REACH) * 0.45 * c.k).toFixed(3)})`
          ctx.beginPath(); ctx.moveTo(a.x, a.y); ctx.lineTo(pointer.x, pointer.y); ctx.stroke()
        }
      }
      for (const p of pts) {
        const tw = 0.5 + 0.5 * Math.sin((now / 1000) * p.sp + p.ph)
        ctx.fillStyle = p.teal ? `rgba(${c.teal},${((0.3 + 0.55 * tw) * c.k).toFixed(3)})` : `rgba(${c.star},${((0.25 + 0.5 * tw) * c.k).toFixed(3)})`
        ctx.beginPath(); ctx.arc(p.x, p.y, p.r, 0, 6.283); ctx.fill()
      }
    }
    const size = () => {
      const dpr = Math.min(window.devicePixelRatio || 1, 1.5)
      w = innerWidth; h = innerHeight
      canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr)
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
      const n = Math.round(Math.max(34, Math.min(95, (w * h) / 19000)))
      while (pts.length < n) pts.push(make())
      pts.length = n
      if (calm) draw(0)
    }
    const step = (now) => {
      if (!running) return
      requestAnimationFrame(step)
      if (now - lastT < 33) return // 30 frames a second is plenty for drifting stars
      const dt = Math.min(3, (now - lastT) / 16.7)
      lastT = now
      for (const p of pts) {
        const mx = pointer.x - p.x, my = pointer.y - p.y
        if (mx * mx + my * my < REACH * REACH) { p.x += mx * 0.0016 * dt; p.y += my * 0.0016 * dt }
        p.x += p.vx * dt; p.y += p.vy * dt
        if (p.x < -20) p.x = w + 20; else if (p.x > w + 20) p.x = -20
        if (p.y < -20) p.y = h + 20; else if (p.y > h + 20) p.y = -20
      }
      draw(now)
    }
    size()
    addEventListener('resize', size)
    redrawStars = () => { if (calm) draw(0) }
    if (!calm) {
      requestAnimationFrame(step)
      document.addEventListener('visibilitychange', () => {
        running = !document.hidden
        if (running) requestAnimationFrame(step)
      })
    }
  }

  // ================================================================== pointer: lit glass edges, a face that follows
  const glass = [...document.querySelectorAll('.card, .list, .auth .form .box')]
  // Each cloud face: the eyes look toward the pointer (up to 8 units sideways, 6 up/down: most of the room inside the frame), and the
  // whole cloud leans and turns a little the same way. Units are the logo's own (the eyes are 11 across). Eased, so it moves like a head.
  const faces = [...document.querySelectorAll('[data-look]')].map((eyes) => ({
    eyes, face: eyes.closest('[data-face]'), x: 0, y: 0, tx: 0, ty: 0,
  }))
  let queued = false, easing = false
  const ease2 = () => {
    let moving = false
    for (const f of faces) {
      f.x += (f.tx - f.x) * 0.14
      f.y += (f.ty - f.y) * 0.14
      if (Math.abs(f.tx - f.x) > 0.005 || Math.abs(f.ty - f.y) > 0.005) moving = true
      f.eyes.style.transform = `translate(${(f.x * 8).toFixed(2)}px, ${(f.y * 6).toFixed(2)}px)`
      if (f.face) f.face.style.transform = `translate(${(f.x * 2.4).toFixed(2)}px, ${(f.y * 1.8).toFixed(2)}px) rotate(${(f.x * 7).toFixed(2)}deg)`
    }
    easing = moving
    if (moving) requestAnimationFrame(ease2)
  }
  const aim = () => {
    for (const f of faces) {
      if (f.eyes.closest('.shut') || pointer.x < -1e3) { f.tx = f.ty = 0; continue }
      const r = (f.face || f.eyes).getBoundingClientRect()
      const dx = pointer.x - (r.left + r.width / 2), dy = pointer.y - (r.top + r.height / 2)
      const dist = Math.hypot(dx, dy) || 1
      const k = Math.min(1, dist / 220)
      f.tx = (dx / dist) * k
      f.ty = (dy / dist) * k
    }
    if (!easing && faces.length) { easing = true; requestAnimationFrame(ease2) }
  }
  const onPointer = () => {
    queued = false
    for (const el of glass) {
      const r = el.getBoundingClientRect()
      if (r.bottom < -300 || r.top > innerHeight + 300) continue
      el.style.setProperty('--mx', (pointer.x - r.left).toFixed(0) + 'px')
      el.style.setProperty('--my', (pointer.y - r.top).toFixed(0) + 'px')
    }
    aim()
  }
  if (!calm) {
    addEventListener('pointermove', (e) => {
      pointer.x = e.clientX; pointer.y = e.clientY
      if (!queued) { queued = true; requestAnimationFrame(onPointer) }
    }, { passive: true })
    document.documentElement.addEventListener('pointerleave', () => {
      pointer.x = pointer.y = -1e4
      if (!queued) { queued = true; requestAnimationFrame(onPointer) }
    })
  }

  // ================================================================== light or dark (remembered on this device)
  const toggles = [...document.querySelectorAll('[data-theme-toggle]')]
  const labelToggles = () => toggles.forEach((b) => b.setAttribute('aria-label', root.dataset.theme === 'light' ? 'Switch to dark mode' : 'Switch to light mode'))
  const setTheme = (theme) => {
    root.dataset.theme = theme
    try { localStorage.setItem('coduo.theme', theme) } catch { }
    document.querySelector('meta[name=theme-color]')?.setAttribute('content', theme === 'light' ? '#eef2fb' : '#050a1c')
    labelToggles()
    redrawStars()
  }
  labelToggles()
  toggles.forEach((button) => button.addEventListener('click', () => {
    const next = root.dataset.theme === 'light' ? 'dark' : 'light'
    if (calm || !document.startViewTransition) { setTheme(next); return }
    // The new theme spreads out from the switch in a circle.
    const r = button.getBoundingClientRect()
    const x = r.left + r.width / 2, y = r.top + r.height / 2
    const end = Math.hypot(Math.max(x, innerWidth - x), Math.max(y, innerHeight - y))
    root.classList.add('theme-switching')
    const vt = document.startViewTransition(() => setTheme(next))
    vt.ready.then(() => root.animate(
      { clipPath: [`circle(0px at ${x}px ${y}px)`, `circle(${end}px at ${x}px ${y}px)`] },
      { duration: 800, easing: 'cubic-bezier(.65, 0, .35, 1)', pseudoElement: '::view-transition-new(root)' })).catch(() => { })
    vt.finished.finally(() => root.classList.remove('theme-switching'))
  }))

  // ================================================================== the rail: a highlight that slides between links
  const nav = document.querySelector('.rail nav')
  if (nav) {
    const links = [...nav.querySelectorAll('a')]
    const current = nav.querySelector('a[aria-current="page"]')
    const glowEl = document.createElement('span')
    glowEl.className = 'nav-glow'
    glowEl.setAttribute('aria-hidden', 'true')
    nav.prepend(glowEl)
    nav.classList.add('has-glow')
    const moveTo = (a, instant) => {
      if (!a) { glowEl.style.opacity = '0'; return }
      if (instant) glowEl.style.transition = 'none'
      glowEl.style.opacity = '1'
      glowEl.style.width = a.offsetWidth + 'px'
      glowEl.style.height = a.offsetHeight + 'px'
      glowEl.style.transform = `translate(${a.offsetLeft}px, ${a.offsetTop}px)`
      if (instant) { void glowEl.offsetWidth; glowEl.style.transition = '' }
    }
    // Start where the last page left it, then slide to this page's link.
    let from = -1
    try { from = parseInt(sessionStorage.getItem('coduo.nav') ?? '-1', 10) } catch { }
    const here = links.indexOf(current)
    if (!calm && from >= 0 && links[from] && from !== here) {
      moveTo(links[from], true)
      requestAnimationFrame(() => requestAnimationFrame(() => moveTo(current)))
    } else moveTo(current, true)
    try { sessionStorage.setItem('coduo.nav', String(here)) } catch { }
    nav.addEventListener('pointerover', (e) => { const a = e.target.closest('a'); if (a) moveTo(a) })
    nav.addEventListener('pointerleave', () => moveTo(current))
    nav.addEventListener('focusin', (e) => { const a = e.target.closest('a'); if (a) moveTo(a) })
    nav.addEventListener('focusout', () => moveTo(current))
    addEventListener('resize', () => moveTo(current, true))
  }

  // ================================================================== the console clock
  const clock = document.querySelector('[data-clock]')
  if (clock) {
    const two = (n) => String(n).padStart(2, '0')
    const tickClock = () => {
      const d = new Date()
      clock.textContent = d.toLocaleDateString('en-GB', { weekday: 'short', day: '2-digit', month: 'short' }) +
        '  ·  ' + two(d.getHours()) + ':' + two(d.getMinutes()) + ':' + two(d.getSeconds())
    }
    tickClock()
    setInterval(tickClock, 1000)
  }

  // ================================================================== sign in: the logo looks away while a password is typed
  const password = document.getElementById('password')
  const logo = document.querySelector('.auth .logo')
  if (password && logo) {
    try { sessionStorage.removeItem('coduo.intro') } catch { } // signing in again plays the opening again
    const sync = () => {
      const shut = document.activeElement === password && password.type === 'password'
      logo.classList.toggle('shut', shut)
      aim() // eyes back to the middle, then shut
    }
    password.addEventListener('focus', sync)
    password.addEventListener('blur', sync)
    document.getElementById('pw-toggle')?.addEventListener('click', () => setTimeout(sync))
  }
})()
