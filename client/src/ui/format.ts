/**
 * Money / quantity / date formatting — the only place the SPA turns numbers into text (no ad-hoc `toFixed`).
 *
 * Decisions mirrored from the Razor app:
 *  - Digits are always Latin (0-9), also in Arabic. `localization.js` only swaps strings, and the old
 *    pages print `currency.notation + amount.toFixed(2)` — there is no Arabic-Indic numeral setting.
 *  - Money is `<notation> <amount>` with 2 decimals by default (POS.js prints `notation + toFixed(2)`).
 *  - Rounding is banker's rounding (half to even), because C# `Math.Round(decimal, n)` defaults to
 *    `MidpointRounding.ToEven` and the server owns the real totals (see `roundHalfEven`).
 */

const LRI = '⁦'
const PDI = '⁩'

/**
 * Unicode isolates so "−USD 1,500.00" keeps its internal LTR order when embedded in Arabic (RTL) text;
 * without them the bidi algorithm can push the sign to the wrong end of the number.
 */
export function ltrIsolate(s: string): string {
  return LRI + s + PDI
}

/** Round half to even at `decimals` places — the same rule as C# `Math.Round(decimal, decimals)`. */
export function roundHalfEven(value: number, decimals = 2): number {
  const f = 10 ** decimals
  const scaled = Math.abs(value) * f
  const floor = Math.floor(scaled)
  const diff = scaled - floor
  // Treat values within float noise of .5 as an exact tie (e.g. 1.005 * 100 = 100.49999999999999).
  const isTie = Math.abs(diff - 0.5) < 1e-8
  const rounded = isTie ? (floor % 2 === 0 ? floor : floor + 1) : Math.round(scaled)
  return (Math.sign(value) * rounded) / f
}

const numberFormats = new Map<string, Intl.NumberFormat>()
function numberFormat(min: number, max: number): Intl.NumberFormat {
  const key = `${min}:${max}`
  let nf = numberFormats.get(key)
  if (!nf) {
    nf = new Intl.NumberFormat('en-US', { minimumFractionDigits: min, maximumFractionDigits: max, useGrouping: true })
    numberFormats.set(key, nf)
  }
  return nf
}

/** Plain grouped number with fixed decimals: `1,234.50`. Latin digits regardless of UI language. */
export function formatNumber(value: number, decimals = 2): string {
  return numberFormat(decimals, decimals).format(roundHalfEven(value, decimals))
}

/** `USD 1,234.50` / `−USD 1,234.50`, isolated for RTL. `notation` is the currency's short code/symbol. */
export function formatMoney(amount: number, notation = '', decimals = 2): string {
  const body = formatNumber(Math.abs(roundHalfEven(amount, decimals)), decimals)
  const sign = roundHalfEven(amount, decimals) < 0 ? '−' : ''
  return ltrIsolate(`${sign}${notation ? `${notation} ` : ''}${body}`)
}

/** Compact for stat tiles / chart ticks: `1.2K`, `3.4M`; never more than one decimal. */
export function formatMoneyCompact(amount: number, notation = ''): string {
  const abs = Math.abs(amount)
  const sign = amount < 0 ? '−' : ''
  let body: string
  if (abs >= 1_000_000) body = `${trim1(abs / 1_000_000)}M`
  else if (abs >= 1_000) body = `${trim1(abs / 1_000)}K`
  else body = String(Math.round(abs))
  return ltrIsolate(`${sign}${notation ? `${notation} ` : ''}${body}`)
}
const trim1 = (n: number) => (n >= 10 ? String(Math.round(n)) : n.toFixed(1).replace(/\.0$/, ''))

/** Quantity with up to `maxDecimals` places and no trailing zeros: `3`, `2.5`, `0.125`. */
export function formatQty(qty: number, maxDecimals = 3): string {
  return numberFormat(0, maxDecimals).format(roundHalfEven(qty, maxDecimals))
}

/** Percentage from a 0–100 value: `12.5%`. */
export function formatPercent(value: number, maxDecimals = 1): string {
  return ltrIsolate(`${numberFormat(0, maxDecimals).format(roundHalfEven(value, maxDecimals))}%`)
}

function currentLocale(): string {
  const lang = typeof document !== 'undefined' ? document.documentElement.lang || 'en' : 'en'
  // `-u-nu-latn` keeps Latin digits in Arabic (see top of file).
  return `${lang}-u-nu-latn`
}

type DateInput = string | number | Date

const toDate = (v: DateInput): Date => (v instanceof Date ? v : new Date(v))

/** `31 Oct 2026` in the UI language. Date-only ISO strings (`2026-10-31`) are read as local dates, not UTC. */
export function formatDate(value: DateInput): string {
  const d = typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value) ? fromDateInputValue(value) : toDate(value)
  return new Intl.DateTimeFormat(currentLocale(), { day: 'numeric', month: 'short', year: 'numeric' }).format(d)
}

/** `31 Oct 2026, 14:05`. */
export function formatDateTime(value: DateInput): string {
  return new Intl.DateTimeFormat(currentLocale(), {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(toDate(value))
}

const pad = (n: number) => String(n).padStart(2, '0')

/** Date → `yyyy-mm-dd` in local time (the value format of `<input type="date">`). */
export function toDateInputValue(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

/** `yyyy-mm-dd` → local-midnight Date. */
export function fromDateInputValue(s: string): Date {
  const [y, m, d] = s.split('-').map(Number)
  return new Date(y ?? 1970, (m ?? 1) - 1, d ?? 1)
}

/**
 * Parses what a person types into a number: accepts `,` as a thousands separator, Arabic-Indic digits
 * (٠-٩ / ۰-۹) and the Arabic decimal separator (٫). Returns `null` for empty or unparseable input.
 */
export function parseNumberInput(raw: string): number | null {
  const normalised = raw
    .replace(/[٠-٩]/g, (c) => String(c.charCodeAt(0) - 0x0660))
    .replace(/[۰-۹]/g, (c) => String(c.charCodeAt(0) - 0x06f0))
    .replace(/٫/g, '.')
    .replace(/[,٬\s]/g, '')
  if (normalised === '' || normalised === '-' || normalised === '.') return null
  if (!/^-?\d*\.?\d*$/.test(normalised)) return null
  const n = Number(normalised)
  return Number.isFinite(n) ? n : null
}
