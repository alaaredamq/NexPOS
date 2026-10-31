import { computeRowWindow } from './hooks/useVirtualRows'
import { pageWindow } from './Pagination'
import { formatMoney, formatMoneyCompact, formatNumber, formatPercent, formatQty, fromDateInputValue, parseNumberInput, roundHalfEven, toDateInputValue } from './format'

const strip = (s: string) => s.replace(/[⁦⁩]/g, '')

describe('roundHalfEven (mirrors C# Math.Round(decimal))', () => {
  it.each([
    [0.125, 2, 0.12],
    [0.135, 2, 0.14],
    [1.005, 2, 1],
    [2.5, 0, 2],
    [3.5, 0, 4],
    [-2.5, 0, -2],
    [-0.135, 2, -0.14],
    [10.994, 2, 10.99],
    [10.996, 2, 11],
  ])('rounds %d to %d places → %d', (value, places, expected) => {
    expect(roundHalfEven(value, places)).toBe(expected)
  })
})

describe('money / quantity formatting', () => {
  it('groups thousands, pads decimals and prefixes the notation', () => {
    expect(strip(formatMoney(1234.5, 'USD'))).toBe('USD 1,234.50')
    expect(strip(formatMoney(0, 'LBP', 0))).toBe('LBP 0')
  })

  it('uses a proper minus and isolates the run so RTL text cannot reorder it', () => {
    const s = formatMoney(-12.5, 'USD')
    expect(strip(s)).toBe('−USD 12.50')
    expect(s.startsWith('⁦') && s.endsWith('⁩')).toBe(true)
  })

  it('never shows a negative zero', () => {
    expect(strip(formatMoney(-0.001, 'USD'))).toBe('USD 0.00')
  })

  it('formats compact figures', () => {
    expect(strip(formatMoneyCompact(1_250_000, 'LBP'))).toBe('LBP 1.3M')
    expect(strip(formatMoneyCompact(84))).toBe('84')
    expect(strip(formatMoneyCompact(12_400))).toBe('12K')
  })

  it('trims trailing zeros on quantities', () => {
    expect(formatQty(3)).toBe('3')
    expect(formatQty(2.5)).toBe('2.5')
    expect(formatQty(0.12345)).toBe('0.123')
    expect(formatNumber(1234.5, 2)).toBe('1,234.50')
    expect(strip(formatPercent(12.54))).toBe('12.5%')
  })
})

describe('parseNumberInput', () => {
  it('accepts separators, Arabic-Indic digits and the Arabic decimal mark', () => {
    expect(parseNumberInput('1,234.5')).toBe(1234.5)
    expect(parseNumberInput('١٢٫٥')).toBe(12.5)
    expect(parseNumberInput('۳۰')).toBe(30)
    expect(parseNumberInput('-4')).toBe(-4)
  })

  it('returns null for empty or non-numeric input', () => {
    expect(parseNumberInput('')).toBeNull()
    expect(parseNumberInput('-')).toBeNull()
    expect(parseNumberInput('abc')).toBeNull()
    expect(parseNumberInput('1.2.3')).toBeNull()
  })
})

describe('date helpers', () => {
  it('round-trips yyyy-mm-dd as a local date (no UTC shift)', () => {
    const d = fromDateInputValue('2026-10-31')
    expect([d.getFullYear(), d.getMonth(), d.getDate()]).toEqual([2026, 9, 31])
    expect(toDateInputValue(d)).toBe('2026-10-31')
  })
})

describe('pageWindow', () => {
  it('shows everything for a few pages', () => {
    expect(pageWindow(1, 3)).toEqual([1, 2, 3])
  })
  it('collapses far pages into a gap but keeps first, last and neighbours', () => {
    expect(pageWindow(10, 20)).toEqual([1, null, 9, 10, 11, null, 20])
    expect(pageWindow(1, 20)).toEqual([1, 2, null, 20])
    expect(pageWindow(3, 20)).toEqual([1, 2, 3, 4, null, 20])
  })
})

describe('computeRowWindow', () => {
  it('renders only the visible rows plus overscan and pads the rest', () => {
    const w = computeRowWindow(1000, 40, 4000, 400, 5)
    expect(w).toEqual({ start: 95, end: 115, padBefore: 95 * 40, padAfter: (1000 - 115) * 40 })
  })
  it('clamps at both ends and handles empty lists', () => {
    expect(computeRowWindow(10, 40, 0, 400, 5)).toEqual({ start: 0, end: 10, padBefore: 0, padAfter: 0 })
    expect(computeRowWindow(0, 40, 0, 400)).toEqual({ start: 0, end: 0, padBefore: 0, padAfter: 0 })
  })
})
