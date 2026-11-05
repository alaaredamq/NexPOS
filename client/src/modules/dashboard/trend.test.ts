import { describe, expect, it } from 'vitest'
import { fillTrend } from './trend'

const row = (date: string, sales: number, profit = 0) => ({ date, sales, profit })

describe('fillTrend', () => {
  const today = new Date(2026, 10, 5) // 5 Nov 2026

  it('lists every day of the month so far, with zero for the quiet ones', () => {
    const out = fillTrend([row('2026-11-02', 40, 10), row('2026-11-05', 15, 5)], 'month', today)

    expect(out.map((r) => r.date)).toEqual(['2026-11-01', '2026-11-02', '2026-11-03', '2026-11-04', '2026-11-05'])
    expect(out.map((r) => r.sales)).toEqual([0, 40, 0, 0, 15])
    expect(out[1]).toEqual(row('2026-11-02', 40, 10))
  })

  it('covers the last seven days and today for the week', () => {
    const out = fillTrend([], 'week', today)
    expect(out[0]?.date).toBe('2026-10-29')
    expect(out.at(-1)?.date).toBe('2026-11-05')
    expect(out).toHaveLength(8)
  })

  it('uses one bucket per month from the start of the quarter', () => {
    const out = fillTrend([row('2026-10-01', 500)], 'quarter', today)
    expect(out.map((r) => r.date)).toEqual(['2026-10-01', '2026-11-01'])
    expect(out[0]?.sales).toBe(500)
  })

  it('uses one bucket per month from January for the year', () => {
    const out = fillTrend([row('2026-03-01', 90)], 'year', today)
    expect(out).toHaveLength(11)
    expect(out[2]).toEqual(row('2026-03-01', 90))
    expect(out[0]?.sales).toBe(0)
  })

  it('runs one bucket per month from the first sale to now for all time', () => {
    const out = fillTrend([row('2026-09-01', 70), row('2026-07-01', 30)], 'all', today)
    expect(out.map((r) => r.date)).toEqual(['2026-07-01', '2026-08-01', '2026-09-01', '2026-10-01', '2026-11-01'])
    expect(out.map((r) => r.sales)).toEqual([30, 0, 70, 0, 0])
  })

  it('shows just this month for all time when nothing was sold', () => {
    expect(fillTrend([], 'all', today).map((r) => r.date)).toEqual(['2026-11-01'])
  })

  it('keeps a bucket outside the window instead of dropping it', () => {
    const out = fillTrend([row('2026-10-31', 7)], 'month', today)
    expect(out[0]).toEqual(row('2026-10-31', 7))
    expect(out).toHaveLength(6)
  })
})
