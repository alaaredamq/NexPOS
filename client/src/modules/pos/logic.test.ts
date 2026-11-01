import { describe, expect, it } from 'vitest'
import ar from '@/locales/ar/pos.json'
import en from '@/locales/en/pos.json'
import { columnsFor } from './catalog/useGridColumns'
import { stockLevel } from './catalog/stock'
import { SHORTCUT_HELP, shortcutFor } from './layout/shortcuts'
import { KeystrokeTimer } from './scan/rapidInput'
import { isTypingQuantityPrefix, parseScanInput, rejectBarcode } from './scan/scanLogic'

describe('parseScanInput (POS.js parity)', () => {
  it('reads plain codes, trimmed', () => {
    expect(parseScanInput('  6001234567890 ')).toEqual({ kind: 'barcode', barcode: '6001234567890', quantity: 1 })
    expect(parseScanInput('')).toEqual({ kind: 'empty' })
    expect(parseScanInput('   ')).toEqual({ kind: 'empty' })
  })

  it('reads "5*" and "5x" alone as a quantity for the next scan', () => {
    expect(parseScanInput('5*')).toEqual({ kind: 'quantity', quantity: 5 })
    expect(parseScanInput('12 X ')).toEqual({ kind: 'quantity', quantity: 12 })
  })

  it('reads a quantity written in front of a code', () => {
    expect(parseScanInput('5*123456')).toEqual({ kind: 'barcode', barcode: '123456', quantity: 5 })
    expect(parseScanInput('5 x 123456')).toEqual({ kind: 'barcode', barcode: '123456', quantity: 5 })
  })

  it('treats an out-of-range prefix as part of the code', () => {
    expect(parseScanInput('0*123')).toEqual({ kind: 'barcode', barcode: '0*123', quantity: 1 })
    expect(parseScanInput('1000*123')).toEqual({ kind: 'barcode', barcode: '1000*123', quantity: 1 })
    expect(parseScanInput('999*123')).toEqual({ kind: 'barcode', barcode: '123', quantity: 999 })
  })

  it('knows when the box holds the start of a prefix', () => {
    expect(isTypingQuantityPrefix('5')).toBe(true)
    expect(isTypingQuantityPrefix('5*')).toBe(true)
    expect(isTypingQuantityPrefix('5*12')).toBe(false)
    expect(isTypingQuantityPrefix('abc')).toBe(false)
  })
})

describe('rejectBarcode', () => {
  it.each([
    ['https://shop.example/p/1', 'url'],
    ['www.example.com', 'url'],
    ['mailto:a@b.co', 'contact'],
    ['sara@example.com', 'contact'],
    ['WIFI:S:home;T:WPA;P:secret;;', 'structured'],
    ['BEGIN:VCARD', 'structured'],
    ['{"id":1}', 'structured'],
    ['x'.repeat(51), 'too_long'],
    ['123\n456', 'multiline'],
    ['12<3>', 'special'],
  ])('refuses %s', (input, reason) => {
    expect(rejectBarcode(input)).toBe(reason)
  })

  it.each(['6001234567890', '012345678905', 'ABC-123.45', 'CODE 128 TEST', 'x'.repeat(50)])('lets %s through', (input) => {
    expect(rejectBarcode(input)).toBeNull()
  })
})

describe('KeystrokeTimer', () => {
  const burst = (gap: number, count: number) => {
    const timer = new KeystrokeTimer()
    for (let i = 0; i < count; i++) timer.record(1000 + i * gap)
    return timer
  }

  it('calls a fast, long burst a scan', () => {
    expect(burst(8, 13).looksLikeScan()).toBe(true)
  })

  it('does not call a person typing a scan', () => {
    expect(burst(150, 13).looksLikeScan()).toBe(false)
  })

  it('needs enough characters to judge', () => {
    expect(burst(5, 3).looksLikeScan()).toBe(false)
  })

  it('forgets the burst after a reset', () => {
    const timer = burst(8, 13)
    timer.reset()
    expect(timer.looksLikeScan()).toBe(false)
  })
})

describe('stockLevel (tile rule of the Razor view)', () => {
  it('is out at zero or below, low at or under the minimum (5 when unset), else ok', () => {
    expect(stockLevel({ stockQuantity: 0, minimumStock: null })).toBe('out')
    expect(stockLevel({ stockQuantity: -2, minimumStock: 10 })).toBe('out')
    expect(stockLevel({ stockQuantity: 5, minimumStock: null })).toBe('low')
    expect(stockLevel({ stockQuantity: 5.5, minimumStock: null })).toBe('ok')
    expect(stockLevel({ stockQuantity: 10, minimumStock: 10 })).toBe('low')
    expect(stockLevel({ stockQuantity: 11, minimumStock: 10 })).toBe('ok')
  })
})

describe('columnsFor', () => {
  it('fits as many minimum-width tiles as the width allows, at least one', () => {
    expect(columnsFor(0, 132, 8)).toBe(1)
    expect(columnsFor(100, 132, 8)).toBe(1)
    expect(columnsFor(800, 132, 8)).toBe(5) // 5 * 132 + 4 * 8 = 692 <= 800 < 6 * 132 + 5 * 8
    expect(columnsFor(1000, 132, 8)).toBe(7)
  })
})

describe('shortcutFor', () => {
  const key = (k: string, extra: Partial<KeyboardEvent> = {}) => shortcutFor({ key: k, ctrlKey: false, altKey: false, metaKey: false, shiftKey: false, ...extra })

  it('maps function keys', () => {
    expect(key('F2')).toEqual({ type: 'focusSearch' })
    expect(key('F3')).toEqual({ type: 'priceCheck' })
    expect(key('F4')).toEqual({ type: 'command', command: 'retrieve' })
    expect(key('F6')).toEqual({ type: 'command', command: 'hold' })
    expect(key('F9')).toEqual({ type: 'command', command: 'clear' })
  })

  it('maps Ctrl+Enter to checkout and Alt+letter to management pages', () => {
    expect(key('Enter', { ctrlKey: true })).toEqual({ type: 'command', command: 'checkout' })
    expect(key('p', { altKey: true })).toEqual({ type: 'navigate', navKey: 'Products' })
    expect(key('D', { altKey: true })).toEqual({ type: 'navigate', navKey: 'Dashboard' })
  })

  it('leaves everything else to the page: letters, digits, plain Enter, reload and dev-tools keys', () => {
    for (const k of ['a', '5', 'Enter', 'Escape', 'F5', 'F11', 'F12']) expect(key(k)).toBeNull()
    expect(key('F3', { ctrlKey: true })).toBeNull()
    expect(key('x', { altKey: true })).toBeNull()
  })

  it('has a translation for every line of the help dialog, in both languages', () => {
    const bundles: Record<string, Record<string, string>> = { en, ar }
    for (const [lang, messages] of Object.entries(bundles)) {
      for (const shortcut of SHORTCUT_HELP) expect(messages[shortcut.labelKey], `${lang}:${shortcut.labelKey}`).toBeTruthy()
    }
  })
})
