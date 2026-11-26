// Motion for the console. One rule makes the cards reliable: a page's cards are children of <Stagger>, and motion plays them one
// after the other from a single timeline, every time the page mounts (no scroll observers, no reload races).
import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react'
import { motion, type Variants } from 'motion/react'

const calm = typeof matchMedia !== 'undefined' && matchMedia('(prefers-reduced-motion: reduce)').matches
export const reducedMotion = calm
export const EASE = [0.2, 0.8, 0.2, 1] as const

// ---------------------------------------------------------------- "go" gate: held while the opening plays
const Gate = createContext(true)
export const GateProvider = Gate.Provider
export const useGate = () => useContext(Gate)

// ---------------------------------------------------------------- staggered cards
const container: Variants = {
  hidden: {},
  show: { transition: { staggerChildren: calm ? 0 : 0.085, delayChildren: calm ? 0 : 0.05 } },
}
const child: Variants = calm
  ? { hidden: { opacity: 1 }, show: { opacity: 1 } }
  : {
    hidden: { opacity: 0, y: 26, scale: 0.985, filter: 'blur(6px)' },
    show: { opacity: 1, y: 0, scale: 1, filter: 'blur(0px)', transition: { duration: 0.75, ease: EASE }, transitionEnd: { filter: 'none', transform: 'none' } },
  }

/** Every direct <Item> inside enters in order. Nest a <Stagger as="div" className="g"> for a grid row: it counts as one step. */
export function Stagger({ children, className, as = 'div' }: { children: ReactNode; className?: string; as?: 'div' | 'section' }) {
  const go = useGate()
  const M = as === 'section' ? motion.section : motion.div
  return <M className={className} variants={container} initial="hidden" animate={go ? 'show' : 'hidden'}>{children}</M>
}

type ItemProps = {
  children: ReactNode; className?: string; as?: 'div' | 'section' | 'form'; id?: string; style?: React.CSSProperties
  onSubmit?: (e: React.FormEvent<HTMLFormElement>) => void; 'aria-labelledby'?: string
}
export function Item({ children, className, as = 'section', onSubmit, ...rest }: ItemProps) {
  if (as === 'form') return <motion.form className={className} variants={child} onSubmit={onSubmit} {...rest}>{children}</motion.form>
  const M = as === 'div' ? motion.div : motion.section
  return <M className={className} variants={child} {...rest}>{children}</M>
}

/** A row in a list: slides in from the start side, a little after the one before. */
export const rowMotion = (i: number) => calm ? {} : ({
  initial: { opacity: 0, x: -16 },
  animate: { opacity: 1, x: 0 },
  transition: { duration: 0.55, ease: EASE, delay: 0.15 + Math.min(i, 14) * 0.045 },
})

// ---------------------------------------------------------------- numbers that count up
/** Counts from 0 (first time) or from the old value (when it changes) to `value`, formatted by `format`. */
export function CountUp({ value, format = (n) => Math.round(n).toLocaleString('en-US'), delay = 0.25, duration = 1.5 }:
  { value: number; format?: (n: number) => string; delay?: number; duration?: number }) {
  const go = useGate()
  const [shown, setShown] = useState(calm ? value : 0)
  const from = useRef(calm ? value : 0)
  useEffect(() => {
    if (calm || !go) return
    const start = performance.now() + delay * 1000, a = from.current, b = value
    let raf = 0
    const tick = (now: number) => {
      const t = Math.max(0, Math.min(1, (now - start) / (duration * 1000)))
      const e = t >= 1 ? 1 : 1 - Math.pow(2, -10 * t)
      setShown(a + (b - a) * e)
      if (t < 1) raf = requestAnimationFrame(tick)
      else from.current = b
    }
    raf = requestAnimationFrame(tick)
    return () => { cancelAnimationFrame(raf); from.current = b }
  }, [value, go, delay, duration])
  return <>{format(calm ? value : shown)}</>
}

// ---------------------------------------------------------------- the pointer, shared
export const pointer = { x: -1e4, y: -1e4 }
const pointerSubs = new Set<() => void>()
if (typeof window !== 'undefined' && !calm) {
  let queued = false
  addEventListener('pointermove', (e) => {
    pointer.x = e.clientX; pointer.y = e.clientY
    if (!queued) { queued = true; requestAnimationFrame(() => { queued = false; pointerSubs.forEach((f) => f()) }) }
  }, { passive: true })
  document.documentElement.addEventListener('pointerleave', () => { pointer.x = pointer.y = -1e4; pointerSubs.forEach((f) => f()) })
}
export const onPointer = (f: () => void) => { pointerSubs.add(f); return () => { pointerSubs.delete(f) } }

/** Glass edges light up near the pointer (cloud.css reads --mx/--my). */
export function useGlassLight() {
  useEffect(() => onPointer(() => {
    for (const el of document.querySelectorAll<HTMLElement>('.card, .list, .auth .form .box')) {
      const r = el.getBoundingClientRect()
      if (r.bottom < -300 || r.top > innerHeight + 300) continue
      el.style.setProperty('--mx', (pointer.x - r.left).toFixed(0) + 'px')
      el.style.setProperty('--my', (pointer.y - r.top).toFixed(0) + 'px')
    }
  }), [])
}

// ---------------------------------------------------------------- the cloud mark, with a face that follows the pointer
const CLOUD = 'M-20 18A14 14 0 0 1 -20.97 -9.97A19 19 0 0 1 16.43 -13.6A16 16 0 0 1 20 18Z'
export const CLOUD_PATH = CLOUD

/** The eyes look toward the pointer (up to 8 units sideways, 6 up and down) and the cloud leans and turns with them, eased. */
export function useFace(shut = false) {
  const face = useRef<SVGGElement>(null)
  const eyes = useRef<SVGGElement>(null)
  useEffect(() => {
    if (calm) return
    let x = 0, y = 0, tx = 0, ty = 0, raf = 0, running = false
    const step = () => {
      x += (tx - x) * 0.14; y += (ty - y) * 0.14
      if (eyes.current) eyes.current.style.transform = `translate(${(x * 8).toFixed(2)}px, ${(y * 6).toFixed(2)}px)`
      if (face.current) face.current.style.transform = `translate(${(x * 2.4).toFixed(2)}px, ${(y * 1.8).toFixed(2)}px) rotate(${(x * 7).toFixed(2)}deg)`
      running = Math.abs(tx - x) > 0.005 || Math.abs(ty - y) > 0.005
      if (running) raf = requestAnimationFrame(step)
    }
    const aim = () => {
      const el = face.current
      if (!el || shut || pointer.x < -1e3) { tx = ty = 0 } else {
        const r = el.getBoundingClientRect()
        const dx = pointer.x - (r.left + r.width / 2), dy = pointer.y - (r.top + r.height / 2)
        const d = Math.hypot(dx, dy) || 1, k = Math.min(1, d / 220)
        tx = (dx / d) * k; ty = (dy / d) * k
      }
      if (!running) { running = true; raf = requestAnimationFrame(step) }
    }
    aim()
    const off = onPointer(aim)
    return () => { off(); cancelAnimationFrame(raf) }
  }, [shut])
  return { face, eyes }
}

/** The mark alone (rail, sign-in). `frame`/`eye` colours come from CSS so both themes work. */
export function CloudMark({ className, shut = false }: { className?: string; shut?: boolean }) {
  const { face, eyes } = useFace(shut)
  return (
    <svg className={className} viewBox="-138 -146 276 276" aria-hidden="true">
      <g transform="scale(3)">
        <g data-face ref={face}>
          <g transform="translate(-1.12 5) scale(1.12)"><path className="mk-frame" d={CLOUD} fill="none" strokeWidth="7.143" strokeLinejoin="round" /></g>
          <g data-look ref={eyes} className={shut ? 'shut' : undefined}>
            <circle className="eye l mk-eye" cx="-9.5" cy="1.64" r="5.5" />
            <circle className="eye r mk-eye" cx="9.5" cy="1.64" r="5.5" />
          </g>
        </g>
      </g>
    </svg>
  )
}

// ---------------------------------------------------------------- the star field (lives in the shell, so it never restarts)
export function Stars() {
  const ref = useRef<HTMLCanvasElement>(null)
  useEffect(() => {
    const canvas = ref.current
    const ctx = canvas?.getContext('2d')
    if (!canvas || !ctx) return
    const DARK = { link: '125,155,255', reach: '61,219,203', teal: '61,219,203', star: '185,200,255', k: 1 }
    const LIGHT = { link: '30,58,138', reach: '14,154,141', teal: '14,154,141', star: '30,58,138', k: 0.85 }
    type P = { x: number; y: number; vx: number; vy: number; r: number; ph: number; sp: number; teal: boolean }
    let w = 0, h = 0, pts: P[] = [], last = 0, raf = 0, running = !document.hidden
    const make = (): P => ({ x: Math.random() * w, y: Math.random() * h, vx: (Math.random() - 0.5) * 0.14, vy: (Math.random() - 0.5) * 0.14,
      r: Math.random() * 1.2 + 0.35, ph: Math.random() * 6.283, sp: 0.5 + Math.random() * 1.5, teal: Math.random() < 0.42 })
    const LINK = 135, REACH = 190
    const draw = (now: number) => {
      const c = document.documentElement.dataset.theme === 'light' ? LIGHT : DARK
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
      const dpr = Math.min(devicePixelRatio || 1, 1.5)
      w = innerWidth; h = innerHeight
      canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr)
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
      const n = Math.round(Math.max(34, Math.min(95, (w * h) / 19000)))
      while (pts.length < n) pts.push(make())
      pts = pts.slice(0, n)
      draw(performance.now())
    }
    const step = (now: number) => {
      if (!running) return
      raf = requestAnimationFrame(step)
      if (now - last < 33) return
      const dt = Math.min(3, (now - last) / 16.7)
      last = now
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
    const vis = () => { running = !document.hidden; if (running && !calm) raf = requestAnimationFrame(step) }
    document.addEventListener('visibilitychange', vis)
    const themeWatch = new MutationObserver(() => draw(performance.now()))
    themeWatch.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })
    if (!calm) raf = requestAnimationFrame(step)
    return () => { cancelAnimationFrame(raf); removeEventListener('resize', size); document.removeEventListener('visibilitychange', vis); themeWatch.disconnect() }
  }, [])
  return <canvas ref={ref} className="fx-stars" />
}

export function Background() {
  return (
    <div className="fx-bg" aria-hidden="true">
      <div className="fx-orb o1" /><div className="fx-orb o2" /><div className="fx-orb o3" />
      <div className="fx-grid" />
      <Stars />
    </div>
  )
}

// ---------------------------------------------------------------- light or dark
export function useTheme() {
  const [theme, setThemeState] = useState(() => document.documentElement.dataset.theme === 'light' ? 'light' : 'dark')
  const apply = (t: string) => {
    document.documentElement.dataset.theme = t
    try { localStorage.setItem('coduo.theme', t) } catch { /* private mode */ }
    document.querySelector('meta[name=theme-color]')?.setAttribute('content', t === 'light' ? '#eef2fb' : '#050a1c')
    setThemeState(t)
  }
  /** The new theme spreads out in a circle from the button that was pressed. */
  const toggle = (from: HTMLElement) => {
    const next = theme === 'light' ? 'dark' : 'light'
    const doc = document as Document & { startViewTransition?: (cb: () => void) => { ready: Promise<void>; finished: Promise<void> } }
    if (calm) { apply(next); return }
    const r = from.getBoundingClientRect()
    const x = r.left + r.width / 2, y = r.top + r.height / 2
    const end = Math.hypot(Math.max(x, innerWidth - x), Math.max(y, innerHeight - y))
    // Phones: no page snapshots (their sliding toolbars made the page jump). The page stays in view and its colours fade.
    const touchy = navigator.maxTouchPoints > 0 || matchMedia('(pointer: coarse)').matches || matchMedia('(hover: none)').matches || innerWidth < 860
    if (touchy || !doc.startViewTransition) {
      const html = document.documentElement
      html.classList.add('theme-fade')
      requestAnimationFrame(() => {
        apply(next)
        setTimeout(() => html.classList.remove('theme-fade'), 700)
      })
      return
    }
    document.documentElement.classList.add('theme-switching')
    const vt = doc.startViewTransition(() => apply(next))
    vt.ready.then(() => document.documentElement.animate(
      { clipPath: [`circle(0px at ${x}px ${y}px)`, `circle(${end}px at ${x}px ${y}px)`] },
      { duration: 800, easing: 'cubic-bezier(.65, 0, .35, 1)', pseudoElement: '::view-transition-new(root)' })).catch(() => { })
    vt.finished.finally(() => document.documentElement.classList.remove('theme-switching'))
  }
  return { theme, toggle }
}

export function ThemeToggle({ float = false }: { float?: boolean }) {
  const { theme, toggle } = useTheme()
  return (
    <button type="button" className={'theme-toggle' + (float ? ' float' : '')} onClick={(e) => toggle(e.currentTarget)}
      aria-label={theme === 'light' ? 'Switch to dark mode' : 'Switch to light mode'} title="Light or dark">
      <svg className="icon t-sun" aria-hidden="true"><use href="#i-sun" /></svg>
      <svg className="icon t-moon" aria-hidden="true"><use href="#i-moon" /></svg>
    </button>
  )
}
