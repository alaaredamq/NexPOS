import { beforeAll, describe, expect, it } from 'vitest'
import { i18n, loadNamespaces, setLanguage } from '@/core/i18n'
import { describeEvent } from './historyText'
import type { ProductHistoryItem } from './api'

const line = (over: Partial<ProductHistoryItem>): ProductHistoryItem => ({
  id: 1,
  createdAt: '2026-11-05T10:00:00Z',
  type: 'stockAdded',
  fields: [],
  fromImport: false,
  ...over,
})

let t: (key: string, options?: Record<string, unknown>) => string

beforeAll(async () => {
  await loadNamespaces('products', 'common')
  await setLanguage('en')
  t = (key, options) => i18n.t(key, { ns: 'products', ...options })
})

const plain = (text: string) => text.replace(/[⁦-⁩]/g, '') // money is wrapped in bidi isolates
const describeIt = (item: ProductHistoryItem, unit: string | null = 'piece') => {
  const event = describeEvent(item, { t, unit, currencyNotation: '$' })
  return { ...event, text: plain(event.text) }
}

describe('describeEvent', () => {
  it('says how many units were added, and carries the stock before and after', () => {
    const event = describeIt(line({ type: 'stockAdded', quantityChange: 15, stockBefore: 10, stockAfter: 25 }))

    expect(event.text).toBe('15 units added to stock')
    expect(event.tone).toBe('success')
    expect(event.stock).toEqual({ before: 10, after: 25 })
  })

  it('words a removal with the positive number', () => {
    expect(describeIt(line({ type: 'stockRemoved', quantityChange: -3, stockBefore: 10, stockAfter: 7 })).text).toBe('3 units removed from stock')
  })

  it('names the sale a sale line belongs to, and a return the sale it undid', () => {
    expect(describeIt(line({ type: 'sold', quantityChange: -2, orderNumber: 'ORD-000042', stockBefore: 15, stockAfter: 13 })).text).toBe(
      '2 units sold in sale ORD-000042',
    )
    expect(describeIt(line({ type: 'saleReturned', quantityChange: 2, orderNumber: 'ORD-000042' })).text).toBe(
      '2 units put back in stock: sale ORD-000042 was cancelled',
    )
  })

  it('uses the unit for weighed products', () => {
    expect(describeIt(line({ type: 'sold', quantityChange: -0.5, orderNumber: 'ORD-000007' }), 'kg').text).toBe('0.5 kg sold in sale ORD-000007')
  })

  it('says a typed-in or imported stock figure was corrected, not added', () => {
    expect(describeIt(line({ type: 'stockSet', quantityChange: -6, stockBefore: 10, stockAfter: 4 })).text).toBe('Stock corrected by hand from 10 to 4')
    expect(describeIt(line({ type: 'stockSet', stockBefore: 10, stockAfter: 4, fromImport: true })).text).toBe('Stock updated by file import from 10 to 4')
    expect(describeIt(line({ type: 'stockSet', stockBefore: 10, stockAfter: 4 })).stock).toBeNull() // the sentence already says it
  })

  it('describes the opening stock, by hand or by import', () => {
    expect(describeIt(line({ type: 'created', stockAfter: 25 })).text).toBe('Product added with 25 units in stock')
    expect(describeIt(line({ type: 'created', stockAfter: 25, fromImport: true })).text).toBe('Product added by file import with 25 units in stock')
  })

  it('formats price changes in the product currency', () => {
    expect(describeIt(line({ type: 'sellingPriceChanged', oldValue: 6, newValue: 7.5 })).text).toBe('Selling price changed from $ 6.00 to $ 7.50')
    expect(describeIt(line({ type: 'costPriceChanged', oldValue: null, newValue: 4 })).text).toBe('Purchase price set to $ 4.00')
    expect(describeIt(line({ type: 'costPriceChanged', oldValue: 4, newValue: null })).text).toBe('Purchase price removed (was $ 4.00)')
  })

  it('lists the other fields that changed', () => {
    expect(describeIt(line({ type: 'updated', fields: ['name', 'category', 'deactivated'] })).text).toBe(
      'Details changed: name, category, and set to inactive',
    )
  })

  it('does not break on a kind a newer server sends', () => {
    expect(describeIt(line({ type: 'somethingNew' })).text).toBe('Product changed')
  })
})
