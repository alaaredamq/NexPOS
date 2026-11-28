// Shared pieces, drawn with the classes of cloud.css so the console and the old portal look the same.
import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react'
import { AnimatePresence, motion } from 'motion/react'
import { CountUp, EASE, reducedMotion, useGate } from './fx'
import type { Attention, PulseCell } from './api'
import { utc } from './format'

export const Icon = ({ id, className = 'icon' }: { id: string; className?: string }) =>
  <svg className={className} aria-hidden="true"><use href={'#i-' + id} /></svg>

/** The icon sprite, once per page. */
export function Sprite() {
  return (
    <svg width="0" height="0" style={{ position: 'absolute' }} aria-hidden="true">
      <symbol id="i-store" viewBox="0 0 24 24"><path d="M3 9l1.5-5h15L21 9" /><path d="M4 9v11h16V9" /><path d="M9 20v-6h6v6" /><path d="M3 9a3 3 0 006 0 3 3 0 006 0 3 3 0 006 0" /></symbol>
      <symbol id="i-plus" viewBox="0 0 24 24"><path d="M12 5v14M5 12h14" /></symbol>
      <symbol id="i-out" viewBox="0 0 24 24"><path d="M9 21H5a2 2 0 01-2-2V5a2 2 0 012-2h4" /><path d="M16 17l5-5-5-5" /><path d="M21 12H9" /></symbol>
      <symbol id="i-search" viewBox="0 0 24 24"><circle cx="11" cy="11" r="7" /><path d="M21 21l-4.3-4.3" /></symbol>
      <symbol id="i-copy" viewBox="0 0 24 24"><rect x="9" y="9" width="12" height="12" rx="2" /><path d="M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1" /></symbol>
      <symbol id="i-send" viewBox="0 0 24 24"><path d="M22 2L11 13" /><path d="M22 2l-7 20-4-9-9-4 20-7z" /></symbol>
      <symbol id="i-chev" viewBox="0 0 24 24"><path d="M9 6l6 6-6 6" /></symbol>
      <symbol id="i-back" viewBox="0 0 24 24"><path d="M15 6l-6 6 6 6" /></symbol>
      <symbol id="i-link" viewBox="0 0 24 24"><path d="M10 13a5 5 0 007.5.5l3-3a5 5 0 00-7-7l-1.7 1.7" /><path d="M14 11a5 5 0 00-7.5-.5l-3 3a5 5 0 007 7l1.7-1.7" /></symbol>
      <symbol id="i-alert" viewBox="0 0 24 24"><path d="M12 4L2.8 19.5h18.4z" /><path d="M12 10v4.5M12 17.2v.1" /></symbol>
      <symbol id="i-check" viewBox="0 0 24 24"><path d="M5 12l5 5L20 7" /></symbol>
      <symbol id="i-key" viewBox="0 0 24 24"><circle cx="7.5" cy="15.5" r="4.5" /><path d="M10.7 12.3L21 2M17 6l3 3M14 9l2.5 2.5" /></symbol>
      <symbol id="i-chart" viewBox="0 0 24 24"><path d="M4 20V10M10 20V4M16 20v-7M22 20H2" /></symbol>
      <symbol id="i-sun" viewBox="0 0 24 24"><circle cx="12" cy="12" r="4" /><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" /></symbol>
      <symbol id="i-moon" viewBox="0 0 24 24"><path d="M20.5 14.5A8.5 8.5 0 019.5 3.5a8.5 8.5 0 1011 11z" /></symbol>
      <symbol id="i-menu" viewBox="0 0 24 24"><path d="M4 6h16M4 12h16M4 18h10" /></symbol>
      <symbol id="i-book" viewBox="0 0 24 24"><path d="M12 6.5C10.2 5 7.6 4.5 4 4.5v14c3.6 0 6.2.5 8 2 1.8-1.5 4.4-2 8-2v-14c-3.6 0-6.2.5-8 2z" /><path d="M12 6.5v14" /></symbol>
      <symbol id="i-download" viewBox="0 0 24 24"><path d="M12 4v11M7 10l5 5 5-5" /><path d="M5 20h14" /></symbol>
      <symbol id="i-upload" viewBox="0 0 24 24"><path d="M12 15V4M7 9l5-5 5 5" /><path d="M5 20h14" /></symbol>
      <symbol id="i-eye" viewBox="0 0 24 24"><path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z" /><circle cx="12" cy="12" r="3" /></symbol>
      <symbol id="i-x" viewBox="0 0 24 24"><path d="M6 6l12 12M18 6L6 18" /></symbol>
      <symbol id="i-edit" viewBox="0 0 24 24"><path d="M4 20h4L19 9a2.8 2.8 0 00-4-4L4 16v4z" /><path d="M13.5 6.5l4 4" /></symbol>
    </svg>
  )
}

export const Pill = ({ tone = 'none', plain, children, style }: { tone?: string; plain?: boolean; children: ReactNode; style?: React.CSSProperties }) =>
  <span className={`pill ${tone}${plain ? ' plain' : ''}`} style={style}>{children}</span>

export const Count = ({ n }: { n: number }) => <span className="count"><CountUp value={n} /></span>

// ---------------------------------------------------------------- the 24-hour contact strip
export function Pulse({ cells, large, row = 0 }: { cells: PulseCell[]; large?: boolean; row?: number }) {
  const go = useGate()
  const lit = cells.filter((c) => c.contacts > 0).length
  const last = cells.length - 1
  return (
    <div className="pulse-wrap">
      <ol className={'pulse' + (large ? ' lg' : '')} aria-label={`Contact in each of the last 24 hours: ${lit} of 24 hours`}>
        {cells.map((c, i) => {
          const d = utc(c.hourUtc)
          return (
            <motion.li key={i} className={(c.contacts > 0 ? 'on' : 'off') + (c.contacts >= 6 ? ' busy' : '') + (i === last ? ' now' : '')}
              title={`${d ? d.toLocaleString('en-GB', { weekday: 'short', hour: '2-digit' }) + ':00' : ''} · ${c.contacts === 0 ? 'no contact' : c.contacts + ' contact' + (c.contacts === 1 ? '' : 's')}`}
              initial={reducedMotion ? false : { scaleY: 0.15, opacity: 0 }} animate={go ? { scaleY: 1, opacity: 1 } : undefined}
              transition={{ type: 'spring', stiffness: 380, damping: 22, delay: 0.25 + row * 0.045 + i * 0.022 }} />
          )
        })}
      </ol>
      {large && <>
        <div className="pulse-axis" aria-hidden="true"><span>24 h ago</span><span>12 h</span><span>now</span></div>
        <p className="pulse-sum">
          {lit === 0 ? 'No contact in the last 24 hours.' : lit === 24 ? 'In contact every hour of the last 24.'
            : <>In contact in <b>{lit} of the last 24 hours</b>. Grey hours are when the shop's PC could not be reached: switched off, no power or no internet.</>}
        </p>
      </>}
    </div>
  )
}

// ---------------------------------------------------------------- gauges
/** SVG dash lengths animate with a CSS transition: true one frame after mount (and once the opening lets the page in). */
function useShown() {
  const go = useGate()
  const [shown, setShown] = useState(reducedMotion)
  useEffect(() => {
    if (!go || shown) return
    const raf = requestAnimationFrame(() => requestAnimationFrame(() => setShown(true)))
    return () => cancelAnimationFrame(raf)
  }, [go, shown])
  return shown
}

export function Ring({ share }: { share: number }) {
  const shown = useShown()
  const C = 326.73
  return (
    <div className="ring-wrap" aria-hidden="true">
      <svg className="ring" viewBox="0 0 120 120">
        <defs>
          <linearGradient id="ring-grad" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stopColor="#8aa5ff" /><stop offset="1" stopColor="#3ddbcb" /></linearGradient>
          <filter id="ring-glow" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="2.4" result="b" /><feMerge><feMergeNode in="b" /><feMergeNode in="SourceGraphic" /></feMerge></filter>
        </defs>
        <circle className="ring-track" cx="60" cy="60" r="52" />
        <circle className="ring-ticks" cx="60" cy="60" r="41" />
        <circle cx="60" cy="60" r="52" filter="url(#ring-glow)"
          style={{ fill: 'none', stroke: 'url(#ring-grad)', strokeWidth: 9, strokeLinecap: 'round', strokeDasharray: C,
            strokeDashoffset: shown ? C * (1 - share) : C, transition: 'stroke-dashoffset 1.8s cubic-bezier(.2,.8,.2,1) .3s' }} />
      </svg>
      <span className="ring-val"><b><CountUp value={Math.round(share * 100)} /></b><small>% online</small></span>
    </div>
  )
}

export function Donut({ parts, total, center, label }: { parts: { value: number; color: string }[]; total: number; center: ReactNode; label: string }) {
  const drawn = useShown()
  const D = 263.89
  const slices = parts.filter((p) => p.value > 0)
  const gap = slices.length > 1 ? 3 : 0
  let start = 0
  return (
    <div className="donut-wrap">
      <svg className="donut" viewBox="0 0 120 120" role="img" aria-label={label}>
        <defs><filter id="donut-glow" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="2.2" result="b" /><feMerge><feMergeNode in="b" /><feMergeNode in="SourceGraphic" /></feMerge></filter></defs>
        <circle className="donut-track" cx="60" cy="60" r="42" />
        <g filter="url(#donut-glow)">
          {slices.map((p, i) => {
            const full = (D * p.value) / Math.max(1, total)
            const len = Math.max(0.5, full - gap)
            const off = -start
            start += full
            return <circle key={i} cx="60" cy="60" r="42" style={{ fill: 'none', strokeWidth: 12, stroke: p.color, strokeDashoffset: off,
              strokeDasharray: drawn ? `${len} 999` : '0 999', transition: `stroke-dasharray 1.3s cubic-bezier(.2,.8,.2,1) ${0.35 + i * 0.22}s` }} />
          })}
        </g>
      </svg>
      <span className="donut-val">{center}</span>
    </div>
  )
}

/** A horizontal fill (budget meter, share bar). */
export function Fill({ percent, color, i = 0, thin }: { percent: number; color: string; i?: number; thin?: boolean }) {
  const go = useGate()
  return (
    <div className={thin ? 'tr' : 'meter'}>
      <motion.i style={{ width: Math.max(0, Math.min(100, percent)) + '%', background: color, transformOrigin: 'left' }}
        initial={reducedMotion ? false : { scaleX: 0 }} animate={go ? { scaleX: 1 } : undefined}
        transition={{ duration: 1.3, ease: EASE, delay: 0.3 + i * 0.07 }} />
    </div>
  )
}

// ---------------------------------------------------------------- bar charts
const pct = (v: number) => Math.round(v * 100) / 100 + '%'

/** The 24-hour contact chart: bars, plus a glowing line over their tops that draws itself, and a pulsing "now" dot. */
export function ContactChart({ cells, hourLabel }: { cells: PulseCell[]; hourLabel: (d: Date) => string }) {
  const go = useGate()
  const max = Math.max(0, ...cells.map((c) => c.contacts))
  const top = max <= 4 ? 4 : Math.ceil(max / 4) * 4
  const n = cells.length
  const vals = cells.map((c) => (c.contacts * 100) / top)
  const pts = vals.map((v, i) => [((i + 0.5) / n) * 1000, 100 - v] as const)
  const line = smooth(pts)
  return (
    <div className="chart" dir="ltr" role="img" aria-label="Contacts from all shops in each of the last 24 hours">
      <div className="yax">{[0, top / 2, top].map((v) => <span key={v} style={{ bottom: pct((v * 100) / top) }}>{v}</span>)}</div>
      <div className="plot">
        {[0, top / 2, top].map((v) => <div key={v} className="gl" style={{ bottom: pct((v * 100) / top) }} />)}
        <div className="cols">
          {cells.map((c, i) => (
            <div key={i} className="col">
              {c.contacts > 0 && <motion.div className={'bar' + (i === n - 1 ? ' now' : '')} style={{ height: pct(vals[i]), transformOrigin: 'bottom' }}
                initial={reducedMotion ? false : { scaleY: 0 }} animate={go ? { scaleY: 1 } : undefined}
                transition={{ duration: 1.1, ease: EASE, delay: 0.3 + i * 0.038 }} />}
            </div>
          ))}
        </div>
        {max > 0 && n > 1 && <>
          <motion.svg className="chart-line" viewBox="0 0 1000 100" preserveAspectRatio="none" aria-hidden="true"
            initial={reducedMotion ? false : { clipPath: 'inset(-20px 100% -20px -20px)' }} animate={go ? { clipPath: 'inset(-20px -20px -20px -20px)' } : undefined}
            transition={{ duration: 1.8, ease: EASE, delay: 0.6 }}>
            <defs><linearGradient id="cl-g" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stopColor="#3ddbcb" stopOpacity=".32" /><stop offset="1" stopColor="#3ddbcb" stopOpacity="0" /></linearGradient></defs>
            <path className="ar" fill="url(#cl-g)" d={`${line} L${pts[n - 1][0]},100 L${pts[0][0]},100 Z`} />
            <path className="ln" vectorEffect="non-scaling-stroke" d={line} />
          </motion.svg>
          <motion.span className="chart-dot" style={{ left: pct(((n - 0.5) / n) * 100), top: pct(100 - vals[n - 1]) }}
            initial={reducedMotion ? false : { opacity: 0, scale: 0 }} animate={go ? { opacity: 1, scale: 1 } : undefined}
            transition={{ type: 'spring', stiffness: 300, damping: 15, delay: 2.2 }} />
        </>}
      </div>
      <div className="xl">{cells.map((c, i) => { const d = utc(c.hourUtc); return <span key={i}>{d && d.getHours() % 3 === 0 ? hourLabel(d) : ''}</span> })}</div>
    </div>
  )
}

function smooth(p: readonly (readonly [number, number])[]) {
  if (!p.length) return ''
  const y = (v: number) => Math.max(0, Math.min(100, v))
  const r = (v: number) => Math.round(v * 10) / 10
  let d = `M${r(p[0][0])},${r(p[0][1])}`
  for (let i = 0; i < p.length - 1; i++) {
    const p0 = p[i - 1] ?? p[i], p1 = p[i], p2 = p[i + 1], p3 = p[i + 2] ?? p2
    d += ` C${r(p1[0] + (p2[0] - p0[0]) / 6)},${r(y(p1[1] + (p2[1] - p0[1]) / 6))} ${r(p2[0] - (p3[0] - p1[0]) / 6)},${r(y(p2[1] - (p3[1] - p1[1]) / 6))} ${r(p2[0])},${r(p2[1])}`
  }
  return d
}

/** Two bars per month (money in and out), each month a link to that month. */
export function PairChart({ rows, top, selected, label, onPick, tip }:
  { rows: { key: string; a: number; b: number; x: string }[]; top: number; selected?: string; label: (v: number) => string; onPick: (key: string) => void; tip: (key: string) => string }) {
  const go = useGate()
  return (
    <div className="chart fchart" dir="ltr" role="img" aria-label="Collected and spent in each of the last 12 months; the table below has the figures">
      <div className="yax">{[0, top / 2, top].map((v) => <span key={v} style={{ bottom: pct((v * 100) / top) }}>{label(v)}</span>)}</div>
      <div className="plot">
        {[0, top / 2, top].map((v) => <div key={v} className="gl" style={{ bottom: pct((v * 100) / top) }} />)}
        <div className="cols m12">
          {rows.map((r, i) => (
            <button type="button" key={r.key} className={'col pair' + (r.key === selected ? ' sel' : '')} onClick={() => onPick(r.key)} title={tip(r.key)}
              style={{ border: 0, cursor: 'pointer', font: 'inherit' }} aria-label={tip(r.key)}>
              {(['a', 'b'] as const).map((k, j) => (
                <motion.i key={k} className={'bar ' + (k === 'a' ? 'in' : 'out')} style={{ height: pct((r[k] * 100) / top), transformOrigin: 'bottom' }}
                  initial={reducedMotion ? false : { scaleY: 0 }} animate={go ? { scaleY: 1 } : undefined}
                  transition={{ duration: 1.1, ease: EASE, delay: 0.3 + i * 0.06 + j * 0.05 }} />
              ))}
            </button>
          ))}
        </div>
      </div>
      <div className="xl m12">{rows.map((r) => <span key={r.key}>{r.x}</span>)}</div>
    </div>
  )
}

// ---------------------------------------------------------------- "needs you" items
export function AttentionList({ items, onOpen }: { items: Attention[]; onOpen: (a: Attention) => void }) {
  return <>
    {items.map((a, i) => (
      <motion.div key={i} className="ai" initial={reducedMotion ? false : { opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.25 + i * 0.07, duration: 0.6, ease: EASE }}>
        <span className={'ico ' + (a.severity === 'bad' ? 'bad' : 'warn')}><Icon id="alert" /></span>
        <div className="tx"><b>{a.title}</b><p>{a.reason}</p></div>
        {a.action && <button type="button" className="btn sm secondary" onClick={() => onOpen(a)}>{a.action}</button>}
      </motion.div>
    ))}
  </>
}

// ---------------------------------------------------------------- copy, confirm, toasts
export function CopyButton({ text, label = 'Copy', className = 'btn' }: { text: string; label?: string; className?: string }) {
  const [done, setDone] = useState(false)
  return (
    <button type="button" className={className} onClick={async () => {
      try { await navigator.clipboard.writeText(text) } catch { /* the text is selectable too */ }
      setDone(true); setTimeout(() => setDone(false), 1600)
    }}><Icon id={done ? 'check' : 'copy'} /><span className="label">{done ? 'Copied' : label}</span></button>
  )
}

type ConfirmAsk = { title: string; text: string; yes: string; danger?: boolean }
const ConfirmCtx = createContext<(a: ConfirmAsk) => Promise<boolean>>(async () => false)
export const useConfirm = () => useContext(ConfirmCtx)
type ToastItem = { id: number; text: string; bad?: boolean }
const ToastCtx = createContext<(text: string, bad?: boolean) => void>(() => { })
export const useToast = () => useContext(ToastCtx)

export function Overlays({ children }: { children: ReactNode }) {
  const [ask, setAsk] = useState<ConfirmAsk | null>(null)
  const resolver = useRef<(v: boolean) => void>(() => { })
  const confirm = useCallback((a: ConfirmAsk) => new Promise<boolean>((res) => { resolver.current = res; setAsk(a) }), [])
  const answer = (v: boolean) => { resolver.current(v); setAsk(null) }
  const [toasts, setToasts] = useState<ToastItem[]>([])
  const toast = useCallback((text: string, bad?: boolean) => {
    const id = Date.now() + Math.random()
    setToasts((t) => [...t, { id, text, bad }])
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), 6000)
  }, [])
  return (
    <ConfirmCtx.Provider value={confirm}>
      <ToastCtx.Provider value={toast}>
        {children}
        <AnimatePresence>
          {ask && (
            <motion.div key="backdrop" className="c-backdrop" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} onClick={() => answer(false)}>
              <motion.div className="confirm c-dialog" role="dialog" aria-modal="true" aria-labelledby="c-title" onClick={(e) => e.stopPropagation()}
                initial={{ opacity: 0, y: 14, scale: 0.94 }} animate={{ opacity: 1, y: 0, scale: 1 }} exit={{ opacity: 0, y: 8, scale: 0.97 }}
                transition={{ type: 'spring', stiffness: 420, damping: 30 }}
                onKeyDown={(e) => { if (e.key === 'Escape') answer(false) }}>
                <h3 id="c-title">{ask.title}</h3>
                <p className="muted">{ask.text}</p>
                <div className="row">
                  <button type="button" className="btn secondary" onClick={() => answer(false)}>Cancel</button>
                  <button type="button" className={'btn ' + (ask.danger === false ? '' : 'danger')} autoFocus onClick={() => answer(true)}>{ask.yes}</button>
                </div>
              </motion.div>
            </motion.div>
          )}
        </AnimatePresence>
        <div className="toasts" role="status" aria-live="polite">
          <AnimatePresence>
            {toasts.map((t) => (
              <motion.div key={t.id} className="toast c-toast" layout initial={{ opacity: 0, x: 40, scale: 0.96 }} animate={{ opacity: 1, x: 0, scale: 1 }}
                exit={{ opacity: 0, x: 40 }} transition={{ type: 'spring', stiffness: 380, damping: 28 }} style={t.bad ? { borderColor: 'var(--bad)' } : undefined}>
                <Icon id={t.bad ? 'alert' : 'check'} /><span>{t.text}</span>
              </motion.div>
            ))}
          </AnimatePresence>
        </div>
      </ToastCtx.Provider>
    </ConfirmCtx.Provider>
  )
}

/** Runs an action; shows the cloud's message on failure, and `done` on success. Returns whether it worked. */
export function useAction() {
  const toast = useToast()
  const [busy, setBusy] = useState(false)
  const run = useCallback(async <T,>(f: () => Promise<T>, done?: string | ((r: T) => string | undefined)): Promise<T | undefined> => {
    setBusy(true)
    try {
      const r = await f()
      const msg = typeof done === 'function' ? done(r) : done
      if (msg) toast(msg)
      return r
    } catch (e) {
      toast(e instanceof Error ? e.message : 'That did not work.', true)
      return undefined
    } finally { setBusy(false) }
  }, [toast])
  return { run, busy }
}

// ---------------------------------------------------------------- the link the owner pastes, as a till receipt (shown once)
export function LinkSlip({ store, url, expires, whatsApp }: { store: string; url: string; expires: string; whatsApp?: string | null }) {
  return (
    <motion.div className="slip-area" initial={{ opacity: 0, y: -12 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.7, ease: EASE }}>
      <motion.div className="slip" initial={{ rotate: -6, y: -30 }} animate={{ rotate: -0.8, y: 0 }} transition={{ type: 'spring', stiffness: 160, damping: 14 }}>
        <div className="head">Coduo POS Online</div>
        <div className="store">{store}</div>
        <hr />
        <div className="label">Connection link</div>
        <div className="link">{url}</div>
        <hr />
        <div className="terms">SINGLE USE<br />VALID UNTIL {expires}</div>
      </motion.div>
      <div className="slip-copy">
        <h2>Send this link to the shop owner</h2>
        <p>It works once and expires on {expires}. It is shown only now; if you lose it, make a new one.</p>
        <ol>
          <li>The owner opens Coduo POS on the shop PC.</li>
          <li>Settings, then the Online tab: paste the link.</li>
          <li>Press Connect. That is all.</li>
        </ol>
        <div className="row">
          <CopyButton text={url} label="Copy link" />
          <a className="btn secondary" target="_blank" rel="noopener" href={whatsApp ?? 'https://wa.me/?text=' + encodeURIComponent(url)}><Icon id="send" />Send on WhatsApp</a>
        </div>
      </div>
    </motion.div>
  )
}

// ---------------------------------------------------------------- loading and errors
export function Loading() {
  return (
    <div className="g" aria-busy="true" aria-label="Loading">
      {[8, 4, 12].map((s, i) => <div key={i} className={'card s' + s + ' skel'} style={{ height: i === 2 ? 220 : 300 }} />)}
    </div>
  )
}

export function Problem({ error, retry }: { error: unknown; retry?: () => void }) {
  return (
    <div className="list"><div className="empty">
      <h3>That did not load</h3>
      <p>{error instanceof Error ? error.message : 'Something went wrong.'}</p>
      {retry && <button type="button" className="btn secondary" onClick={retry}>Try again</button>}
    </div></div>
  )
}

export const Fold = ({ title, children, open }: { title: string; children: ReactNode; open?: boolean }) =>
  <details className="fold" style={{ marginTop: 14 }} open={open}><summary>{title}</summary>{children}</details>
