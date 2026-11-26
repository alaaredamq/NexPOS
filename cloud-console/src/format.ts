// Money, days and times as the old portal showed them: "$1,250" or "$1,250.50", "-$300"; days "2026-11-26"; times "26 Nov, 14:05".

let currency = '$'
export const setCurrency = (c: string) => { currency = c || '$' }
export const getCurrency = () => currency

export function money(n: number | null | undefined): string {
  const v = n ?? 0
  const abs = Math.abs(v)
  const whole = abs === Math.trunc(abs)
  const text = abs.toLocaleString('en-US', { minimumFractionDigits: whole ? 0 : 2, maximumFractionDigits: whole ? 0 : 2 })
  return (v < 0 ? '-' : '') + currency + text
}

export function moneyShort(n: number): string {
  const abs = Math.abs(n)
  const text = abs >= 1_000_000 ? (abs / 1_000_000).toFixed(1).replace(/\.0$/, '') + 'M'
    : abs >= 1000 ? (abs / 1000).toFixed(1).replace(/\.0$/, '') + 'k' : Math.round(abs).toString()
  return (n < 0 ? '-' : '') + currency + text
}

/** A UTC time from the API; some arrive without the Z. */
export function utc(s: string | null | undefined): Date | null {
  if (!s) return null
  const d = new Date(/[zZ]|[+-]\d\d:\d\d$/.test(s) ? s : s + 'Z')
  return isNaN(+d) ? null : d
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']
const MONTHS_LONG = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December']
const two = (n: number) => String(n).padStart(2, '0')

export const day = (d: Date | null) => (d ? `${d.getFullYear()}-${two(d.getMonth() + 1)}-${two(d.getDate())}` : '-')
export const clock = (d: Date | null) => (d ? `${d.getDate()} ${MONTHS[d.getMonth()]}, ${two(d.getHours())}:${two(d.getMinutes())}` : '-')
export const isoToday = () => day(new Date())
/** "2026-11" → "Nov" (short) or "November 2026" (long). */
export const monthName = (ym: string, long = false) => {
  const [y, m] = ym.split('-').map(Number)
  return long ? `${MONTHS_LONG[m - 1]} ${y}` : MONTHS[m - 1]
}
/** "2026-11-22" → "22 Nov". */
export const shortDay = (iso: string) => { const [, m, d] = iso.split('-').map(Number); return `${d} ${MONTHS[m - 1]}` }

export function ago(d: Date | null, never = 'never'): string {
  if (!d) return never
  const s = (Date.now() - +d) / 1000
  if (s < 60) return 'just now'
  if (s < 3600) return `${Math.floor(s / 60)} min ago`
  if (s < 86400) { const h = Math.floor(s / 3600); return `${h} hour${h === 1 ? '' : 's'} ago` }
  const days = Math.floor(s / 86400)
  return days < 60 ? `${days} day${days === 1 ? '' : 's'} ago` : `on ${day(d)}`
}

export function until(d: Date | null): string {
  if (!d) return ''
  const days = Math.round((+d - Date.now()) / 86400000)
  if (days === 0) return 'today'
  if (days > 0) return `in ${days} day${days === 1 ? '' : 's'}`
  return `${-days} day${days === -1 ? '' : 's'} ago`
}

export const plural = (n: number, one: string, many = one + 's') => `${n} ${n === 1 ? one : many}`

/** Pill colour and words for an Online plan state, as the old portal had them. */
export function planPill(state: string | undefined | null): [tone: string, label: string] {
  switch (state) {
    case 'Active': return ['ok', 'Active']
    case 'Grace': return ['warn', 'Grace week']
    case 'Expired': return ['bad', 'Expired']
    case 'Suspended': return ['bad', 'Suspended']
    default: return ['none', 'No plan']
  }
}

export function connectionTone(c: string): string {
  return c === 'Connected' ? 'ok' : c === 'WaitingForShop' ? 'warn' : c === 'Disconnected' || c === 'LinkExpired' ? 'bad' : 'none'
}

export function healthPill(h: string): [string, string] {
  switch (h) {
    case 'Ok': return ['ok', 'In contact']
    case 'Stale': return ['warn', 'Quiet']
    case 'Down': return ['bad', 'No contact']
    default: return ['none', 'Never connected']
  }
}

export function licencePill(status: string, label: string): string {
  void label
  return status === 'Active' || status === 'Updates' ? 'ok'
    : status === 'EndingSoon' || status === 'Grace' || status === 'UpdatesEnding' ? 'warn'
    : status === 'Superseded' ? 'none' : 'bad'
}
