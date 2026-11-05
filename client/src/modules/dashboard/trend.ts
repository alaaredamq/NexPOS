import { toDateInputValue } from '@/ui/format'

export type Period = 'week' | 'month' | 'quarter' | 'year' | 'all'
export const PERIODS: Period[] = ['month', 'quarter', 'year', 'all']

export interface TrendRow {
  /** `yyyy-MM-dd` - the day, or the 1st of the month for quarter / year. */
  date: string
  sales: number
  profit: number
}

const monthly = (period: Period) => period === 'quarter' || period === 'year' || period === 'all'

/**
 * First bucket of the period, the same windows `DashboardService.ResolvePeriodWindow` uses (local calendar). "All" starts at the month of the
 * first sale the server sent (`firstDate`), or this month when there is none.
 */
function periodStart(period: Period, today: Date, firstDate?: string): Date {
  if (period === 'all') {
    const [y, m] = (firstDate ?? '').split('-').map(Number)
    return y && m ? new Date(y, m - 1, 1) : new Date(today.getFullYear(), today.getMonth(), 1)
  }
  if (period === 'week') return new Date(today.getFullYear(), today.getMonth(), today.getDate() - 7)
  if (period === 'quarter') return new Date(today.getFullYear(), Math.floor(today.getMonth() / 3) * 3, 1)
  if (period === 'year') return new Date(today.getFullYear(), 0, 1)
  return new Date(today.getFullYear(), today.getMonth(), 1)
}

/**
 * The server sends only the buckets that have sales. A chart that skips quiet days draws a slump as a smooth line, so the quiet buckets are
 * filled in with zero from the start of the period to now, and the axis is a true timeline.
 */
export function fillTrend(rows: readonly TrendRow[], period: Period, today: Date = new Date()): TrendRow[] {
  const byDate = new Map(rows.map((r) => [r.date, r]))
  const start = periodStart(period, today, rows.map((r) => r.date).sort()[0])
  const out: TrendRow[] = []

  for (let cursor = new Date(start); cursor <= today; ) {
    const key = toDateInputValue(cursor)
    out.push(byDate.get(key) ?? { date: key, sales: 0, profit: 0 })
    byDate.delete(key)
    cursor = monthly(period) ? new Date(cursor.getFullYear(), cursor.getMonth() + 1, 1) : new Date(cursor.getFullYear(), cursor.getMonth(), cursor.getDate() + 1)
  }

  // A bucket outside that window (clock or time-zone edge) is kept rather than silently dropped.
  return [...out, ...byDate.values()].sort((a, b) => a.date.localeCompare(b.date))
}

const locale = () => `${typeof document === 'undefined' ? 'en' : document.documentElement.lang || 'en'}-u-nu-latn`

/** `5 Nov` for a day, `Nov 2026` for a month - the label of a chart point, in the UI language with Latin digits. */
export function pointLabel(date: string, period: Period): string {
  const [y, m, d] = date.split('-').map(Number)
  const day = new Date(y ?? 1970, (m ?? 1) - 1, d ?? 1)
  return new Intl.DateTimeFormat(locale(), monthly(period) ? { month: 'short', year: 'numeric' } : { day: 'numeric', month: 'short' }).format(day)
}
